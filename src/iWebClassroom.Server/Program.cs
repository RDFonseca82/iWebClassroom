using System.Reflection;
using System.Text.Json;
using System.Threading.RateLimiting;
using IWebClassroom.Contracts;
using IWebClassroom.Server.Configuration;
using IWebClassroom.Server.Hubs;
using IWebClassroom.Server.Security;
using IWebClassroom.Server.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.Configure<ClassroomOptions>(builder.Configuration.GetSection("Classroom"));
builder.Services.Configure<ServerUpdateOptions>(builder.Configuration.GetSection("Update"));
builder.Services.AddSingleton<AgentRegistry>();
builder.Services.AddSingleton<CommandAuditStore>();
builder.Services.AddSingleton<UpdateCatalog>();
builder.Services.AddHttpClient(nameof(ServerUpdateWorker), client => client.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddHostedService<ServerUpdateWorker>();
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = false;
    options.MaximumReceiveMessageSize = 2_000_000;
});
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("enrollment", limiter =>
    {
        limiter.PermitLimit = 10;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.AutoReplenishment = true;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();
var registry = app.Services.GetRequiredService<AgentRegistry>();
await registry.InitializeAsync(app.Lifetime.ApplicationStopping);

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
    EnrollmentRequest request,
    AgentRegistry agentRegistry,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    var expectedCode = Environment.GetEnvironmentVariable("IWEBCLASSROOM_ENROLLMENT_CODE")
                       ?? configuration["Security:EnrollmentCode"];
    if (string.IsNullOrWhiteSpace(expectedCode))
    {
        return Results.Problem("Enrollment is not configured on this server.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    if (!SecretComparer.Equals(request.EnrollmentCode, expectedCode))
    {
        return Results.Unauthorized();
    }

    var errors = ValidateEnrollment(request);
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

    var validationError = ValidateCommand(request, options.Value);
    if (validationError is not null)
    {
        return Results.BadRequest(new { error = validationError });
    }

    var knownIds = agentRegistry.List().Select(agent => agent.AgentId).ToHashSet();
    var targets = request.TargetAgentIds.Distinct().Where(knownIds.Contains).ToArray();
    if (targets.Length == 0)
    {
        return Results.BadRequest(new { error = "No valid target agent was selected." });
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

static bool TryAuthenticateAgent(HttpRequest request, AgentRegistry registry)
{
    return Guid.TryParse(request.Headers[HubProtocol.AgentIdHeader].ToString(), out var agentId) &&
           registry.TryAuthenticate(agentId, request.Headers[HubProtocol.AgentTokenHeader].ToString(), out _);
}

static Dictionary<string, string[]> ValidateEnrollment(EnrollmentRequest request)
{
    var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
    ValidateText(request.MachineId, nameof(request.MachineId), 1, 200, errors);
    ValidateText(request.DisplayName, nameof(request.DisplayName), 1, 100, errors);
    ValidateText(request.ClassroomId, nameof(request.ClassroomId), 1, 50, errors);
    if (!Enum.IsDefined(request.Role))
    {
        errors[nameof(request.Role)] = ["The role is invalid."];
    }

    return errors;
}

static void ValidateText(string? value, string field, int minimum, int maximum, IDictionary<string, string[]> errors)
{
    if (value is null || value.Length < minimum || value.Length > maximum || value.Any(char.IsControl))
    {
        errors[field] = [$"The field must contain between {minimum} and {maximum} printable characters."];
    }
}

static string? ValidateCommand(CommandRequest request, ClassroomOptions options)
{
    if (request.TargetAgentIds.Count is 0 or > 200)
    {
        return "Select between 1 and 200 agents.";
    }

    if (!Enum.IsDefined(request.Kind))
    {
        return "The command type is invalid.";
    }

    if (request.Payload.ValueKind != JsonValueKind.Object)
    {
        return "The command payload must be a JSON object.";
    }

    switch (request.Kind)
    {
        case ClassroomCommandKind.ShowMessage:
            if (!request.Payload.TryGetProperty("message", out var message) ||
                message.ValueKind != JsonValueKind.String ||
                message.GetString() is not { Length: > 0 and <= 500 })
            {
                return "The message must contain between 1 and 500 characters.";
            }

            break;
        case ClassroomCommandKind.LaunchApplication:
            if (!request.Payload.TryGetProperty("applicationId", out var applicationId) ||
                applicationId.ValueKind != JsonValueKind.String ||
                !options.AllowedApplicationIds.Contains(applicationId.GetString()!, StringComparer.OrdinalIgnoreCase))
            {
                return "The application is not allowed by the server.";
            }

            break;
        case ClassroomCommandKind.StartScreenShare:
        case ClassroomCommandKind.StopScreenShare:
        case ClassroomCommandKind.CheckForUpdate:
            break;
        default:
            return "The command type is not supported.";
    }

    return null;
}

public partial class Program;
