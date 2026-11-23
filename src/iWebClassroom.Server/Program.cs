using System.Reflection;
using System.Text.Json;
using System.Threading.RateLimiting;
using IWebClassroom.Contracts;
using IWebClassroom.Server.Configuration;
using IWebClassroom.Server.Hubs;
using IWebClassroom.Server.Localization;
using IWebClassroom.Server.Security;
using IWebClassroom.Server.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;

var builder = WindowsServiceHelpers.IsWindowsService()
    ? WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory })
    : WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(options => options.ServiceName = "iWebClassroom.Server");
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.Configuration.AddJsonFile(
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "iWebClassroom", "Server", "appsettings.Local.json"),
    optional: true,
    reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();
BootstrapSecrets.AddOrCreate(builder);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.Configure<ClassroomOptions>(builder.Configuration.GetSection("Classroom"));
builder.Services.Configure<ServerUpdateOptions>(builder.Configuration.GetSection("Update"));
builder.Services.AddSingleton<AgentRegistry>();
builder.Services.AddSingleton<LessonRegistry>();
builder.Services.AddSingleton<CommandAuditStore>();
builder.Services.AddSingleton<UpdateCatalog>();
builder.Services.AddHttpClient(nameof(ServerUpdateWorker), client => client.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddHostedService<LessonExpirationWorker>();
builder.Services.AddHostedService<ServerUpdateWorker>();
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = false;
    options.MaximumReceiveMessageSize = 2_000_000;
});
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("enrollment", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("teacher", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();
var registry = app.Services.GetRequiredService<AgentRegistry>();
await registry.InitializeAsync(app.Lifetime.ApplicationStopping);
var lessons = app.Services.GetRequiredService<LessonRegistry>();
await lessons.InitializeAsync(app.Lifetime.ApplicationStopping);

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

if (builder.Configuration.GetValue("Security:RequireHttps", true))
{
    app.UseHttpsRedirection();
}
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0",
    utc = DateTimeOffset.UtcNow
}));

app.MapPost("/api/v1/enrollment", async (
    HttpRequest httpRequest,
    EnrollmentRequest request,
    AgentRegistry agentRegistry,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    var expectedCode = Environment.GetEnvironmentVariable("IWEBCLASSROOM_ENROLLMENT_CODE")
                       ?? configuration["Security:EnrollmentCode"];
    if (string.IsNullOrWhiteSpace(expectedCode))
    {
        return Results.Problem(ApiText.Get(httpRequest, "EnrollmentNotConfigured"), statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    if (!SecretComparer.Equals(request.EnrollmentCode, expectedCode))
    {
        return Results.Unauthorized();
    }

    var errors = ValidateEnrollment(httpRequest, request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var response = await agentRegistry.EnrollAsync(request, cancellationToken);
    return Results.Ok(response);
}).RequireRateLimiting("enrollment");

app.MapGet("/api/v1/agents", (HttpRequest request, AgentRegistry agentRegistry, IConfiguration configuration) =>
    IsAdmin(request, configuration)
        ? Results.Ok(agentRegistry.List())
        : Results.Unauthorized());

app.MapGet("/api/v1/teacher/overview", (
    HttpRequest request,
    AgentRegistry agentRegistry,
    LessonRegistry lessonRegistry,
    IConfiguration configuration) =>
    IsTeacher(request, configuration)
        ? Results.Ok(new TeacherOverview(agentRegistry.List(), lessonRegistry.List()))
        : Results.Unauthorized()).RequireRateLimiting("teacher");

app.MapPost("/api/v1/lessons", async (
    HttpRequest httpRequest,
    StartLessonRequest request,
    AgentRegistry agentRegistry,
    LessonRegistry lessonRegistry,
    IHubContext<ClassroomHub> hub,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    if (!IsTeacher(httpRequest, configuration))
    {
        return Results.Unauthorized();
    }

    if (request.RoomId is not { Length: >= 1 and <= 50 } || request.RoomId.Any(char.IsControl))
    {
        return Results.BadRequest(new { error = ApiText.Get(httpRequest, "InvalidRoom") });
    }

    if (request.DurationMinutes is < 15 or > 480)
    {
        return Results.BadRequest(new { error = ApiText.Get(httpRequest, "InvalidDuration") });
    }

    var teacher = agentRegistry.List().FirstOrDefault(item => item.AgentId == request.TeacherAgentId);
    if (teacher is null || teacher.Role != AgentRole.Teacher)
    {
        return Results.BadRequest(new { error = ApiText.Get(httpRequest, "InvalidTeacher") });
    }

    var lesson = await lessonRegistry.TryStartAsync(
        request.RoomId.Trim(),
        teacher,
        TimeSpan.FromMinutes(request.DurationMinutes),
        cancellationToken);
    if (lesson is null)
    {
        return Results.Conflict(new { error = ApiText.Get(httpRequest, "RoomInUse") });
    }

    foreach (var agentId in lesson.MemberAgentIds)
    {
        foreach (var connectionId in agentRegistry.GetConnectionIds(agentId))
        {
            await hub.Groups.AddToGroupAsync(connectionId, ClassroomHub.LessonGroup(lesson.LessonId), cancellationToken);
        }
    }

    return Results.Ok(lesson);
}).RequireRateLimiting("teacher");

app.MapDelete("/api/v1/lessons/{lessonId:guid}", async (
    Guid lessonId,
    HttpRequest httpRequest,
    AgentRegistry agentRegistry,
    LessonRegistry lessonRegistry,
    IHubContext<ClassroomHub> hub,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    if (!IsTeacher(httpRequest, configuration))
    {
        return Results.Unauthorized();
    }

    var lesson = await lessonRegistry.EndAsync(lessonId, cancellationToken);
    if (lesson is null)
    {
        return Results.NotFound();
    }

    await hub.Clients.Group(ClassroomHub.LessonGroup(lessonId))
        .SendAsync(HubProtocol.ScreenStopped, cancellationToken);
    foreach (var agentId in lesson.MemberAgentIds)
    {
        foreach (var connectionId in agentRegistry.GetConnectionIds(agentId))
        {
            await hub.Groups.RemoveFromGroupAsync(connectionId, ClassroomHub.LessonGroup(lessonId), cancellationToken);
        }
    }

    return Results.NoContent();
}).RequireRateLimiting("teacher");

app.MapPost("/api/v1/lessons/{lessonId:guid}/commands", async (
    Guid lessonId,
    HttpRequest httpRequest,
    CommandRequest request,
    AgentRegistry agentRegistry,
    LessonRegistry lessonRegistry,
    IHubContext<ClassroomHub> hub,
    CommandAuditStore audit,
    IOptions<ClassroomOptions> options,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    if (!IsTeacher(httpRequest, configuration))
    {
        return Results.Unauthorized();
    }

    if (!lessonRegistry.TryGet(lessonId, out var lesson))
    {
        return Results.NotFound(new { error = ApiText.Get(httpRequest, "LessonNotFound") });
    }

    var validationError = ValidateCommand(httpRequest, request, options.Value);
    if (validationError is not null)
    {
        return Results.BadRequest(new { error = validationError });
    }

    var allowedTargets = lesson!.MemberAgentIds.ToHashSet();
    var targets = request.TargetAgentIds.Distinct().Where(allowedTargets.Contains).ToArray();
    if (request.Kind is ClassroomCommandKind.StartScreenShare or ClassroomCommandKind.StopScreenShare)
    {
        targets = targets.Where(id => id == lesson.TeacherAgentId).ToArray();
    }

    if (targets.Length == 0)
    {
        return Results.BadRequest(new { error = ApiText.Get(httpRequest, "NoLessonTarget") });
    }

    var command = new ClassroomCommand(Guid.NewGuid(), request.Kind, request.Payload.Clone(), DateTimeOffset.UtcNow);
    foreach (var target in targets)
    {
        await hub.Clients.Group(ClassroomHub.AgentGroup(target))
            .SendAsync(HubProtocol.ReceiveCommand, command, cancellationToken);
    }

    await audit.AppendAsync(command, targets, cancellationToken);
    return Results.Accepted(value: new { command.CommandId, targetCount = targets.Length });
}).RequireRateLimiting("teacher");

app.MapPost("/api/v1/commands", async (
    HttpRequest httpRequest,
    CommandRequest request,
    AgentRegistry agentRegistry,
    IHubContext<ClassroomHub> hub,
    CommandAuditStore audit,
    IOptions<ClassroomOptions> options,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    if (!IsAdmin(httpRequest, configuration))
    {
        return Results.Unauthorized();
    }

    var validationError = ValidateCommand(httpRequest, request, options.Value);
    if (validationError is not null)
    {
        return Results.BadRequest(new { error = validationError });
    }

    var knownIds = agentRegistry.List().Select(agent => agent.AgentId).ToHashSet();
    var targets = request.TargetAgentIds.Distinct().Where(knownIds.Contains).ToArray();
    if (targets.Length == 0)
    {
        return Results.BadRequest(new { error = ApiText.Get(httpRequest, "NoTarget") });
    }

    var command = new ClassroomCommand(Guid.NewGuid(), request.Kind, request.Payload.Clone(), DateTimeOffset.UtcNow);
    foreach (var target in targets)
    {
        await hub.Clients.Group(ClassroomHub.AgentGroup(target))
            .SendAsync(HubProtocol.ReceiveCommand, command, cancellationToken);
    }

    await audit.AppendAsync(command, targets, cancellationToken);
    return Results.Accepted(value: new { command.CommandId, targetCount = targets.Length });
});

app.MapGet("/api/v1/updates/{component}/{channel}", async (
    string component,
    string channel,
    HttpRequest request,
    AgentRegistry agentRegistry,
    UpdateCatalog catalog,
    CancellationToken cancellationToken) =>
{
    if (!TryAuthenticateAgent(request, agentRegistry))
    {
        return Results.Unauthorized();
    }

    var manifest = await catalog.FindAsync(component, channel, cancellationToken);
    return manifest is null ? Results.NotFound() : Results.Ok(manifest);
});

app.MapHub<ClassroomHub>(HubProtocol.HubPath);
app.MapFallbackToFile("index.html");
app.Run();

static bool IsAdmin(HttpRequest request, IConfiguration configuration)
{
    var expectedToken = Environment.GetEnvironmentVariable("IWEBCLASSROOM_ADMIN_TOKEN")
                        ?? configuration["Security:AdminToken"];
    return SecretComparer.Equals(request.Headers[HubProtocol.AdminTokenHeader].ToString(), expectedToken);
}

static bool IsTeacher(HttpRequest request, IConfiguration configuration)
{
    var expectedPin = Environment.GetEnvironmentVariable("IWEBCLASSROOM_TEACHER_PIN")
                      ?? configuration["Security:TeacherPin"];
    return SecretComparer.Equals(request.Headers[HubProtocol.TeacherPinHeader].ToString(), expectedPin) ||
           IsAdmin(request, configuration);
}

static bool TryAuthenticateAgent(HttpRequest request, AgentRegistry registry)
{
    return Guid.TryParse(request.Headers[HubProtocol.AgentIdHeader].ToString(), out var agentId) &&
           registry.TryAuthenticate(agentId, request.Headers[HubProtocol.AgentTokenHeader].ToString(), out _);
}

static Dictionary<string, string[]> ValidateEnrollment(HttpRequest httpRequest, EnrollmentRequest request)
{
    var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
    ValidateText(httpRequest, request.MachineId, nameof(request.MachineId), 1, 200, errors);
    ValidateText(httpRequest, request.DisplayName, nameof(request.DisplayName), 1, 100, errors);
    ValidateText(httpRequest, request.ClassroomId, nameof(request.ClassroomId), 1, 50, errors);
    if (!Enum.IsDefined(request.Role))
    {
        errors[nameof(request.Role)] = [ApiText.Get(httpRequest, "InvalidRole")];
    }

    return errors;
}

static void ValidateText(HttpRequest httpRequest, string? value, string field, int minimum, int maximum, IDictionary<string, string[]> errors)
{
    if (value is null || value.Length < minimum || value.Length > maximum || value.Any(char.IsControl))
    {
        errors[field] = [ApiText.Get(httpRequest, "InvalidText", minimum, maximum)];
    }
}

static string? ValidateCommand(HttpRequest httpRequest, CommandRequest request, ClassroomOptions options)
{
    if (request.TargetAgentIds.Count is 0 or > 200)
    {
        return ApiText.Get(httpRequest, "SelectAgents");
    }

    if (!Enum.IsDefined(request.Kind))
    {
        return ApiText.Get(httpRequest, "InvalidCommand");
    }

    if (request.Payload.ValueKind != JsonValueKind.Object)
    {
        return ApiText.Get(httpRequest, "ObjectPayload");
    }

    switch (request.Kind)
    {
        case ClassroomCommandKind.ShowMessage:
            if (!request.Payload.TryGetProperty("message", out var message) ||
                message.ValueKind != JsonValueKind.String ||
                message.GetString() is not { Length: > 0 and <= 500 })
            {
                return ApiText.Get(httpRequest, "MessageLength");
            }

            break;
        case ClassroomCommandKind.LaunchApplication:
            if (!request.Payload.TryGetProperty("applicationId", out var applicationId) ||
                applicationId.ValueKind != JsonValueKind.String ||
                !options.AllowedApplicationIds.Contains(applicationId.GetString()!, StringComparer.OrdinalIgnoreCase))
            {
                return ApiText.Get(httpRequest, "AppNotAllowed");
            }

            break;
        case ClassroomCommandKind.StartScreenShare:
        case ClassroomCommandKind.StopScreenShare:
        case ClassroomCommandKind.CheckForUpdate:
            break;
        default:
            return ApiText.Get(httpRequest, "UnsupportedCommand");
    }

    return null;
}

public partial class Program;
