using System.Reflection;
using System.Text.Json;
using System.Threading.RateLimiting;
using IWebClassroom.Contracts;
using IWebClassroom.Server.Configuration;
using IWebClassroom.Server.Hubs;
using IWebClassroom.Server.Localization;
using IWebClassroom.Server.Security;
using IWebClassroom.Server.Services;
using Microsoft.AspNetCore.Diagnostics;
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
var configuredDataDirectory = builder.Configuration["Classroom:DataDirectory"] ?? "data";
builder.Configuration.AddJsonFile(
    CloudConfigurationStore.ResolvePath(builder.Environment.ContentRootPath, configuredDataDirectory),
    optional: true,
    reloadOnChange: true);
// Environment variables remain the highest-priority local override, including in Windows service deployments.
builder.Configuration.AddEnvironmentVariables();
BootstrapSecrets.AddOrCreate(builder);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
var resolvedDataDirectory = Path.IsPathRooted(configuredDataDirectory)
    ? configuredDataDirectory
    : Path.Combine(builder.Environment.ContentRootPath, configuredDataDirectory);
var localLogStore = new LocalServerLogStore(resolvedDataDirectory);
builder.Logging.AddProvider(localLogStore);

// Explicit --urls remains available for development and smoke tests. Installed services use the
// managed endpoints below, which listen on every active IPv4/IPv6 interface.
ManagedHttpsCertificateProvider? managedHttps = null;
if (!args.Contains("--urls", StringComparer.OrdinalIgnoreCase))
{
    managedHttps = new ManagedHttpsCertificateProvider(builder.Environment.ContentRootPath, configuredDataDirectory);
    if (WindowsServiceHelpers.IsWindowsService())
    {
        try
        {
            managedHttps.InstallRootForLocalMachine();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"The local HTTPS authority could not be installed in the machine trust store: {exception.Message}");
        }
    }
    builder.Services.AddSingleton(managedHttps);
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenAnyIP(7080);
        options.ListenAnyIP(7443, listen => listen.UseHttps(managedHttps.Current));
    });
    builder.Services.AddHostedService<HttpsCertificateRenewalWorker>();
}

builder.Services.Configure<ClassroomOptions>(builder.Configuration.GetSection("Classroom"));
builder.Services.Configure<ServerUpdateOptions>(builder.Configuration.GetSection("Update"));
builder.Services.Configure<CloudSyncOptions>(builder.Configuration.GetSection("CloudSync"));
builder.Services.AddSingleton<AgentRegistry>();
builder.Services.AddSingleton<LessonRegistry>();
builder.Services.AddSingleton<StudentScreenStore>();
builder.Services.AddSingleton<LessonMediaStore>();
builder.Services.AddSingleton<CloudSyncStateStore>();
builder.Services.AddSingleton<CloudLicenseStore>();
builder.Services.AddSingleton<CloudApprovedUpdateStore>();
builder.Services.AddSingleton<CloudDirectoryStore>();
builder.Services.AddSingleton<CloudConfigurationStore>();
builder.Services.AddSingleton<CloudSyncClient>();
builder.Services.AddSingleton<CloudSyncService>();
builder.Services.AddSingleton(localLogStore);
builder.Services.AddSingleton<LocalServerEndpointProvider>();
builder.Services.AddSingleton<CloudRoomService>();
builder.Services.AddSingleton<PeerCoordinationService>();
builder.Services.AddSingleton<AdminSessionService>();
builder.Services.AddSingleton<TeacherSessionService>();
builder.Services.AddSingleton<CommandAuditStore>();
builder.Services.AddSingleton<ServerMaintenanceState>();
builder.Services.AddSingleton<UpdateCatalog>();
builder.Services.AddHttpClient(nameof(ServerUpdateWorker), client => client.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddHttpClient(nameof(CloudSyncClient));
builder.Services.AddHttpClient(nameof(PeerCoordinationService))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        // Peers use different school-local certificate authorities. HMAC authenticates every request;
        // TLS remains responsible for confidentiality while the signature prevents impersonation.
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });
builder.Services.AddHostedService<CloudSyncWorker>();
builder.Services.AddHostedService<CloudDirectorySyncWorker>();
builder.Services.AddHostedService<CloudRoomHeartbeatWorker>();
builder.Services.AddHostedService<CloudLogUploadWorker>();
builder.Services.AddHostedService<PeerCoordinationWorker>();
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
    options.AddPolicy("admin-login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("peer", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 300,
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
var cloudState = app.Services.GetRequiredService<CloudSyncStateStore>();
await cloudState.InitializeAsync(app.Lifetime.ApplicationStopping);
var cloudLicense = app.Services.GetRequiredService<CloudLicenseStore>();
await cloudLicense.InitializeAsync(app.Lifetime.ApplicationStopping);
var cloudApprovedUpdate = app.Services.GetRequiredService<CloudApprovedUpdateStore>();
await cloudApprovedUpdate.InitializeAsync(app.Lifetime.ApplicationStopping);
var cloudDirectory = app.Services.GetRequiredService<CloudDirectoryStore>();
await cloudDirectory.InitializeAsync(app.Lifetime.ApplicationStopping);

app.UseExceptionHandler(errorApplication => errorApplication.Run(async context =>
{
    var failure = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var requestId = context.TraceIdentifier;
    app.Logger.LogError(failure, "Unhandled request error {RequestId} on {Method} {Path}.",
        requestId, context.Request.Method, context.Request.Path);
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    context.Response.ContentType = "application/json";
    await context.Response.WriteAsJsonAsync(new
    {
        error = ApiText.Get(context.Request, "UnexpectedError", requestId),
        requestId
    });
}));

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

if (builder.Configuration.GetValue("Security:RequireHttps", true))
{
    app.UseWhen(
        context => !context.Request.Path.StartsWithSegments("/api/v1/certificate/root"),
        branch => branch.UseHttpsRedirection());
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

app.MapGet("/api/v1/version", () => Results.Ok(new
{
    version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0"
}));

app.MapGet("/api/v1/certificate/root", async (
    HttpRequest request,
    IConfiguration configuration,
    AgentRegistry agentRegistry,
    CloudRoomService cloudRooms,
    CancellationToken cancellationToken) =>
{
    if (managedHttps is null)
    {
        return Results.NotFound();
    }

    // A root CA certificate is public material. Publishing it on the bootstrap
    // port allows a freshly installed agent to register as an unassigned device.
    request.HttpContext.Response.Headers["X-Certificate-Thumbprint"] = managedHttps.RootThumbprint;
    return Results.File(managedHttps.ExportRootCertificate(), "application/pkix-cert", "iwebclassroom-root.cer");
}).RequireRateLimiting("enrollment");

app.MapGet("/api/v1/localization", () => Results.Ok(new
{
    language = InstallerPreferences.Language()
}));

app.MapPost("/api/v1/peer/heartbeat", (
    HttpRequest httpRequest,
    PeerHeartbeatRequest request,
    PeerCoordinationService peers) =>
{
    if (!peers.Authenticate(httpRequest, request))
    {
        return Results.Unauthorized();
    }
    peers.Observe(request);
    return Results.Ok(new { accepted = true, masterServerId = peers.MasterServerId() });
}).RequireRateLimiting("peer");

app.MapPost("/api/v1/peer/rooms/allocate", (
    HttpRequest httpRequest,
    PeerRoomAllocationRequest request,
    PeerCoordinationService peers) =>
{
    if (!peers.Authenticate(httpRequest, request))
    {
        return Results.Unauthorized();
    }
    return peers.MasterServerId() is { } master && master == cloudState.Snapshot().ServerId
        ? Results.Ok(peers.Allocate(request))
        : Results.Conflict(new { error = "This server is not the elected temporary master." });
}).RequireRateLimiting("peer");

app.MapPost("/api/v1/peer/rooms/resolve", (
    HttpRequest httpRequest,
    PeerRoomResolveRequest request,
    PeerCoordinationService peers) =>
{
    if (!peers.Authenticate(httpRequest, request))
    {
        return Results.Unauthorized();
    }
    if (peers.MasterServerId() is not { } master || master != cloudState.Snapshot().ServerId)
    {
        return Results.Conflict(new { error = "This server is not the elected temporary master." });
    }
    var result = peers.Resolve(request);
    return result is null ? Results.NotFound() : Results.Ok(result);
}).RequireRateLimiting("peer");

app.MapPost("/api/v1/enrollment", async (
    HttpRequest httpRequest,
    EnrollmentRequest request,
    AgentRegistry agentRegistry,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    var expectedCode = Environment.GetEnvironmentVariable("IWEBCLASSROOM_ENROLLMENT_CODE")
                       ?? configuration["Security:EnrollmentCode"];
    var legacyEnrollment = !string.IsNullOrWhiteSpace(expectedCode) &&
                           SecretComparer.Equals(request.EnrollmentCode, expectedCode);
    var normalizedRequest = legacyEnrollment
        ? request
        : request with
        {
            DisplayName = Environment.MachineName,
            Role = AgentRole.Student,
            ClassroomId = "PENDENTE",
            IsShared = false,
            EnrollmentCode = string.Empty
        };

    var errors = ValidateEnrollment(httpRequest, normalizedRequest);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var response = await agentRegistry.EnrollAsync(normalizedRequest, cancellationToken);
    return Results.Ok(response);
}).RequireRateLimiting("enrollment");

app.MapGet("/api/v1/agent/profile", (HttpRequest request, AgentRegistry agentRegistry) =>
    TryAuthenticateAgentWithSummary(request, agentRegistry, out var agent) && agent is not null
        ? Results.Ok(agent)
        : Results.Unauthorized());

app.MapPut("/api/v1/agent/capabilities", async (
    HttpRequest request,
    AgentCapabilitiesRequest capabilities,
    AgentRegistry agentRegistry,
    CancellationToken cancellationToken) =>
{
    if (!TryAuthenticateAgentWithSummary(request, agentRegistry, out var agent) || agent is null ||
        capabilities.Displays.Count is < 1 or > 16)
    {
        return Results.Unauthorized();
    }

    return await agentRegistry.UpdateCapabilitiesAsync(agent.AgentId, capabilities.Displays, cancellationToken)
        ? Results.NoContent()
        : Results.NotFound();
}).RequireRateLimiting("teacher");

app.MapPost("/api/v1/agent/logs", (
    HttpRequest request,
    AgentLogUploadRequest upload,
    AgentRegistry agentRegistry,
    LocalServerLogStore logs) =>
{
    if (!TryAuthenticateAgentWithSummary(request, agentRegistry, out var agent) || agent is null)
    {
        return Results.Unauthorized();
    }
    if (upload.Entries.Count is < 1 or > 250 || upload.Entries.Any(entry =>
            entry.EventId == Guid.Empty || string.IsNullOrWhiteSpace(entry.Message) || entry.Message.Length > 4000 ||
            string.IsNullOrWhiteSpace(entry.Category) || entry.Category.Length > 200 ||
            entry.Exception?.Length > 12000 ||
            entry.Level is not ("Information" or "Warning" or "Error" or "Critical")))
    {
        return Results.BadRequest(new { error = "Invalid agent log batch." });
    }
    logs.AppendAgentEntries(agent, upload.Entries);
    return Results.Accepted(value: new { accepted = upload.Entries.Count });
}).RequireRateLimiting("teacher");

app.MapGet("/api/v1/agents", (HttpRequest request, AgentRegistry agentRegistry, IConfiguration configuration) =>
    IsAdmin(request, configuration)
        ? Results.Ok(agentRegistry.List())
        : Results.Unauthorized());

app.MapPost("/api/v1/teacher/access/request", async (
    TeacherAccessRequest credentials,
    HttpRequest httpRequest,
    CloudSyncService cloud,
    CancellationToken cancellationToken) =>
{
    var email = credentials.Email?.Trim() ?? string.Empty;
    if (email.Length > 254 || !System.Net.Mail.MailAddress.TryCreate(email, out _))
    {
        return Results.BadRequest(new { code = "invalid_email" });
    }

    try
    {
        var challenge = await cloud.RequestTeacherAccessAsync(
            new CloudTeacherAccessRequest(email, TeacherAccessLocale(httpRequest)), cancellationToken);
        return Results.Ok(new TeacherAccessChallengeResponse(challenge.ExpiresAtUtc));
    }
    catch (CloudApiException exception)
    {
        var status = exception.StatusCode == System.Net.HttpStatusCode.TooManyRequests
            ? StatusCodes.Status429TooManyRequests
            : StatusCodes.Status503ServiceUnavailable;
        return Results.Json(new { code = exception.Code ?? "teacher_access_unavailable" }, statusCode: status);
    }
    catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
    {
        return Results.Json(new { code = "teacher_access_unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).RequireRateLimiting("teacher");

app.MapPost("/api/v1/teacher/access/verify", async (
    TeacherAccessVerifyRequest credentials,
    CloudSyncService cloud,
    TeacherSessionService sessions,
    CancellationToken cancellationToken) =>
{
    var email = credentials.Email?.Trim() ?? string.Empty;
    var code = credentials.Code?.Trim() ?? string.Empty;
    if (email.Length > 254 || !System.Net.Mail.MailAddress.TryCreate(email, out _) ||
        code.Length != 6 || code.Any(character => !char.IsAsciiDigit(character)))
    {
        return Results.BadRequest(new { code = "invalid_access_code" });
    }

    try
    {
        var teacher = await cloud.VerifyTeacherAccessAsync(
            new CloudTeacherAccessVerifyRequest(email, code), cancellationToken);
        return Results.Ok(sessions.Create(teacher));
    }
    catch (CloudApiException exception) when (exception.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.UnprocessableEntity)
    {
        return Results.Json(new { code = exception.Code ?? "invalid_access_code" }, statusCode: StatusCodes.Status401Unauthorized);
    }
    catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
    {
        return Results.Json(new { code = "teacher_access_unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).RequireRateLimiting("teacher");

app.MapGet("/api/v1/teacher/overview", (
    HttpRequest request,
    AgentRegistry agentRegistry,
    LessonRegistry lessonRegistry,
    CloudDirectoryStore directory,
    IOptionsMonitor<ClassroomOptions> classroomOptions,
    IConfiguration configuration) =>
{
    if (!TryGetTeacher(request, configuration, out var teacher) || teacher is null)
    {
        return Results.Unauthorized();
    }

    var lessons = lessonRegistry.List()
        .Select(lesson => string.Equals(lesson.TeacherId, teacher.TeacherId, StringComparison.Ordinal)
            ? lesson
            : lesson with { JoinCode = null, Participants = [] })
        .ToArray();
    return Results.Ok(new TeacherOverview(
        agentRegistry.List(),
        lessons,
        directory.Current.Classrooms
            .Select(room => new ClassroomDefinition(room.Id, room.Code, room.Name))
            .OrderBy(room => room.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray(),
        teacher,
        Math.Clamp(classroomOptions.CurrentValue.DefaultLessonDurationMinutes, 15, 480),
        AvailableApplications(directory)));
}).RequireRateLimiting("teacher");

app.MapGet("/api/v1/rooms/available", (
    HttpRequest request,
    AgentRegistry agentRegistry,
    LessonRegistry lessonRegistry) =>
{
    if (!TryAuthenticateAgentWithSummary(request, agentRegistry, out var agent) || agent is null || !agent.IsShared || agent.Role != AgentRole.Student)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(lessonRegistry.List()
        .Where(lesson => lesson.IsTemporary)
        .Select(lesson => new AvailableRoomSummary(
            lesson.LessonId,
            lesson.RoomId,
            lesson.RoomName,
            lesson.TeacherDisplayName,
            lesson.ExpiresAtUtc))
        .ToArray());
});

app.MapPost("/api/v1/rooms/join", async (
    HttpRequest request,
    JoinRoomRequest join,
    AgentRegistry agentRegistry,
    LessonRegistry lessonRegistry,
    IHubContext<ClassroomHub> hub,
    CancellationToken cancellationToken) =>
{
    if (!TryAuthenticateAgentWithSummary(request, agentRegistry, out var agent) || agent is null || !agent.IsShared || agent.Role != AgentRole.Student)
    {
        return Results.Unauthorized();
    }

    if (string.IsNullOrWhiteSpace(join.StudentName) || join.StudentName.Trim().Length is > 100)
    {
        return Results.BadRequest(new { error = ApiText.Get(request, "StudentNameRequired") });
    }

    var lesson = await lessonRegistry.TryJoinAsync(join.LessonId, agent, join.Pin.Trim(), join.StudentName.Trim(), cancellationToken);
    if (lesson is null)
    {
        return Results.BadRequest(new { error = ApiText.Get(request, "InvalidRoomPin") });
    }

    foreach (var connectionId in agentRegistry.GetConnectionIds(agent.AgentId))
    {
        await hub.Groups.AddToGroupAsync(connectionId, ClassroomHub.LessonGroup(lesson.LessonId), cancellationToken);
    }

    return Results.Ok(lesson);
}).RequireRateLimiting("teacher");

app.MapPost("/api/v1/rooms/resolve", async (
    HttpRequest request,
    ResolveRoomRequest resolve,
    AgentRegistry agentRegistry,
    LessonRegistry lessonRegistry,
    CloudRoomService cloudRooms,
    IHubContext<ClassroomHub> hub,
    CancellationToken cancellationToken) =>
{
    if (!TryAuthenticateAgentWithSummary(request, agentRegistry, out var agent) || agent is null ||
        !agent.IsShared || agent.Role != AgentRole.Student)
    {
        return Results.Unauthorized();
    }

    var code = resolve.Code.Trim();
    if (code.Length != 6 || code.Any(character => !char.IsAsciiDigit(character)))
    {
        return Results.BadRequest(new { error = ApiText.Get(request, "InvalidRoomCode") });
    }

    if (string.IsNullOrWhiteSpace(resolve.StudentName) || resolve.StudentName.Trim().Length > 100)
    {
        return Results.BadRequest(new { error = ApiText.Get(request, "StudentNameRequired") });
    }

    if (lessonRegistry.TryGetByJoinCode(code, out var localRoom))
    {
        var joined = await lessonRegistry.TryJoinAsync(
            localRoom!.LessonId, agent, code, resolve.StudentName.Trim(), cancellationToken);
        if (joined is null)
        {
            return Results.BadRequest(new { error = ApiText.Get(request, "InvalidRoomPin") });
        }

        foreach (var connectionId in agentRegistry.GetConnectionIds(agent.AgentId))
        {
            await hub.Groups.AddToGroupAsync(connectionId, ClassroomHub.LessonGroup(joined.LessonId), cancellationToken);
        }

        return Results.Ok(new ResolveRoomResponse(true, joined.RoomName, [], null, joined));
    }

    try
    {
        var remote = await cloudRooms.ResolveAsync(code, agent, resolve.StudentName.Trim(), cancellationToken);
        return Results.Ok(new ResolveRoomResponse(false, remote.RoomName, remote.HostUrls, remote.HandoffToken, null));
    }
    catch (CloudApiException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return Results.BadRequest(new { error = ApiText.Get(request, "InvalidRoomPin") });
    }
    catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
    {
        return Results.Problem(ApiText.Get(request, "CloudRoomUnavailable"), statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).RequireRateLimiting("teacher");

app.MapPost("/api/v1/rooms/roam", async (
    HttpRequest request,
    RoamingJoinRequest roaming,
    CloudRoomService cloudRooms,
    AgentRegistry agentRegistry,
    LessonRegistry lessonRegistry,
    IHubContext<ClassroomHub> hub,
    CancellationToken cancellationToken) =>
{
    var claims = await cloudRooms.ValidateTicketAsync(roaming.HandoffToken, cancellationToken);
    if (claims is null || !lessonRegistry.TryGet(claims.LessonUid, out var localRoom) || localRoom is null)
    {
        return Results.Unauthorized();
    }

    var enrollment = await agentRegistry.EnrollRoamingAsync(new EnrollmentRequest(
        claims.MachineId,
        claims.ComputerName,
        AgentRole.Student,
        "PARTILHADO",
        string.Empty,
        true), cancellationToken);
    if (!agentRegistry.TryAuthenticate(enrollment.AgentId, enrollment.AgentToken, out var roamingAgent) || roamingAgent is null)
    {
        return Results.Problem(ApiText.Get(request, "RoamingUnavailable"), statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var joined = await lessonRegistry.TryJoinAsync(
        claims.LessonUid, roamingAgent, claims.RoomCode, claims.StudentName, cancellationToken);
    if (joined is null)
    {
        return Results.Unauthorized();
    }

    foreach (var connectionId in agentRegistry.GetConnectionIds(roamingAgent.AgentId))
    {
        await hub.Groups.AddToGroupAsync(connectionId, ClassroomHub.LessonGroup(joined.LessonId), cancellationToken);
    }

    var serverUrl = $"https://{request.Host.Host}:{(request.Host.Port ?? 7443)}";
    return Results.Ok(new RoamingJoinResponse(serverUrl, enrollment, joined));
}).RequireRateLimiting("teacher");

app.MapGet("/api/v1/settings", (
    HttpRequest request,
    CloudSyncService cloud,
    CloudApprovedUpdateStore approvedUpdate,
    IConfiguration configuration) =>
    IsAdmin(request, configuration)
        ? Results.Ok(new { configuration = cloud.Configuration(), cloud = cloud.Status(), license = cloud.License(), approvedUpdate = approvedUpdate.Snapshot() })
        : Results.Unauthorized());

app.MapPost("/api/v1/admin/cloud-login", async (
    CloudConnectRequest credentials,
    CloudSyncService cloud,
    AdminSessionService sessions,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(credentials.Username) || credentials.Username.Length > 160 ||
        string.IsNullOrEmpty(credentials.Password) || credentials.Password.Length > 512)
    {
        return Results.BadRequest(new { error = "As credenciais de sincronização não têm o formato esperado." });
    }

    try
    {
        await cloud.ConnectAsync(credentials, cancellationToken);
        var session = sessions.Create();
        return Results.Ok(new
        {
            sessionToken = session.Token,
            session.ExpiresAtUtc,
            configuration = cloud.Configuration(),
            cloud = cloud.Status(),
            license = cloud.License()
        });
    }
    catch (CloudApiException exception)
    {
        var code = exception.Code ?? exception.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => "invalid_credentials",
            System.Net.HttpStatusCode.Forbidden => "inactive_server",
            _ => "cloud_request_failed"
        };
        return Results.BadRequest(new { error = exception.Message, code });
    }
    catch (HttpRequestException exception)
    {
        return Results.BadRequest(new { error = exception.Message, code = "cloud_unavailable" });
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message, code = "cloud_configuration_error" });
    }
}).RequireRateLimiting("admin-login");

app.MapGet("/api/v1/admin/bootstrap-status", (CloudSyncService cloud) => Results.Ok(new
{
    connected = cloud.Status().Connected,
    schoolName = cloud.Status().SchoolName,
    version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0"
}));

app.MapPost("/api/v1/admin/login", async (
    CloudAdministratorLoginRequest credentials,
    CloudSyncService cloud,
    AdminSessionService sessions,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(credentials.Email) || credentials.Email.Length > 255 ||
        string.IsNullOrEmpty(credentials.Password) || credentials.Password.Length > 512)
    {
        return Results.BadRequest(new { error = "As credenciais de administrador não têm o formato esperado.", code = "invalid_admin_credentials" });
    }

    try
    {
        var administrator = await cloud.AuthenticateAdministratorAsync(
            credentials with { Email = credentials.Email.Trim() }, cancellationToken);
        var session = sessions.Create();
        return Results.Ok(new
        {
            sessionToken = session.Token,
            session.ExpiresAtUtc,
            administrator,
            configuration = cloud.Configuration(),
            cloud = cloud.Status(),
            license = cloud.License()
        });
    }
    catch (CloudApiException exception)
    {
        return Results.Json(new
        {
            error = exception.Message,
            code = exception.Code ?? "invalid_admin_credentials"
        }, statusCode: exception.StatusCode == System.Net.HttpStatusCode.Unauthorized
            ? StatusCodes.Status401Unauthorized
            : StatusCodes.Status400BadRequest);
    }
    catch (HttpRequestException exception)
    {
        return Results.BadRequest(new { error = exception.Message, code = "cloud_unavailable" });
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message, code = "server_not_connected" });
    }
}).RequireRateLimiting("admin-login");

app.MapPut("/api/v1/settings", async (
    HttpRequest request,
    CloudServerSettings settings,
    CloudSyncService cloud,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    if (!IsAdmin(request, configuration))
    {
        return Results.Unauthorized();
    }

    var error = ValidateCloudSettings(settings);
    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    try
    {
        var status = await cloud.UpdateLocalConfigurationAsync(settings, cancellationToken);
        return Results.Ok(new { configuration = cloud.Configuration(), cloud = status, license = cloud.License() });
    }
    catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapPost("/api/v1/cloud/connect", async (
    HttpRequest request,
    CloudConnectRequest credentials,
    CloudSyncService cloud,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    if (!IsAdmin(request, configuration))
    {
        return Results.Unauthorized();
    }

    if (credentials.Username is not { Length: >= 4 and <= 160 } ||
        credentials.Password is not { Length: >= 16 and <= 512 })
    {
        return Results.BadRequest(new { error = "As credenciais de sincronização não têm o formato esperado." });
    }

    try
    {
        return Results.Ok(await cloud.ConnectAsync(credentials, cancellationToken));
    }
    catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapPost("/api/v1/cloud/sync", async (
    HttpRequest request,
    CloudSyncService cloud,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    if (!IsAdmin(request, configuration))
    {
        return Results.Unauthorized();
    }

    try
    {
        return Results.Ok(await cloud.SynchronizeAsync(cancellationToken));
    }
    catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapDelete("/api/v1/cloud/connection", async (
    HttpRequest request,
    CloudSyncService cloud,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
    IsAdmin(request, configuration)
        ? Results.Ok(await cloud.DisconnectAsync(cancellationToken))
        : Results.Unauthorized());

app.MapPost("/api/v1/lessons", async (
    HttpRequest httpRequest,
    StartLessonRequest request,
    AgentRegistry agentRegistry,
    LessonRegistry lessonRegistry,
    CloudDirectoryStore directory,
    CloudRoomService cloudRooms,
    IHubContext<ClassroomHub> hub,
    ILoggerFactory loggerFactory,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    if (!TryGetTeacher(httpRequest, configuration, out var teacherIdentity))
    {
        return Results.Unauthorized();
    }

    if (request.RoomId is not { Length: >= 1 and <= 50 } || request.RoomId.Any(char.IsControl))
    {
        return Results.BadRequest(new { error = ApiText.Get(httpRequest, "InvalidRoom") });
    }

    var fixedRoom = directory.Current.Classrooms.FirstOrDefault(room =>
        string.Equals(room.Code, request.RoomId.Trim(), StringComparison.OrdinalIgnoreCase));
    if (request.IsTemporary)
    {
        if (request.RoomName is not { Length: >= 1 and <= 80 } || request.RoomName.Any(char.IsControl))
        {
            return Results.BadRequest(new { error = ApiText.Get(httpRequest, "InvalidRoom") });
        }
    }
    else if (directory.Current.Classrooms.Count > 0 && fixedRoom is null)
    {
        return Results.BadRequest(new { error = ApiText.Get(httpRequest, "InvalidRoom") });
    }

    if (request.DurationMinutes is < 15 or > 480)
    {
        return Results.BadRequest(new { error = ApiText.Get(httpRequest, "InvalidDuration") });
    }

    var teacher = agentRegistry.List().FirstOrDefault(item => item.AgentId == request.TeacherAgentId);
    var validFixedTeacher = teacher is not null &&
                            teacher.IsAssigned &&
                            !teacher.IsShared &&
                            teacher.Role == AgentRole.Teacher &&
                            string.Equals(teacher.ClassroomId, request.RoomId.Trim(), StringComparison.OrdinalIgnoreCase);
    var validTemporaryTeacher = teacher is not null &&
                                teacher.IsAssigned &&
                                request.IsTemporary &&
                                (teacher.Role == AgentRole.Teacher || teacher.IsShared);
    if (!validFixedTeacher && !validTemporaryTeacher)
    {
        return Results.BadRequest(new { error = ApiText.Get(httpRequest, "InvalidTeacher") });
    }

    var lessonUid = Guid.NewGuid();
    CloudRoomAllocationResponse? cloudAllocation = null;
    if (request.IsTemporary)
    {
        try
        {
            cloudAllocation = await cloudRooms.TryAllocateAsync(
                lessonUid,
                request.RoomName!.Trim(),
                teacherIdentity!,
                DateTimeOffset.UtcNow.AddMinutes(request.DurationMinutes),
                cancellationToken);
        }
        catch (CloudApiException exception) when (exception.Code is "server_capacity_reached" or "server_not_accepting_rooms")
        {
            return Results.Conflict(new { error = ApiText.Get(httpRequest, "ServerAtCapacity") });
        }
    }

    var lesson = await lessonRegistry.TryStartAsync(
        request.RoomId.Trim(),
        teacher!,
        teacherIdentity!,
        request.IsTemporary,
        request.IsTemporary ? request.RoomName : fixedRoom?.Name,
        TimeSpan.FromMinutes(request.DurationMinutes),
        cancellationToken,
        lessonUid,
        cloudAllocation?.RoomCode,
        cloudAllocation is not null);
    if (lesson is null)
    {
        if (cloudAllocation is not null)
        {
            await cloudRooms.EndAsync(lessonUid, cancellationToken);
        }
        return Results.Conflict(new { error = ApiText.Get(httpRequest, "RoomInUse") });
    }

    foreach (var agentId in lesson.MemberAgentIds)
    {
        foreach (var connectionId in agentRegistry.GetConnectionIds(agentId))
        {
            try
            {
                await hub.Groups.AddToGroupAsync(connectionId, ClassroomHub.LessonGroup(lesson.LessonId), CancellationToken.None);
            }
            catch (Exception exception)
            {
                loggerFactory.CreateLogger("LessonEndpoint").LogWarning(exception,
                    "Lesson {LessonId} was created, but connection {ConnectionId} could not be added to its SignalR group.",
                    lesson.LessonId, connectionId);
            }
        }
    }

    return Results.Ok(lesson);
}).RequireRateLimiting("teacher");

app.MapDelete("/api/v1/lessons/{lessonId:guid}", async (
    Guid lessonId,
    HttpRequest httpRequest,
    AgentRegistry agentRegistry,
    LessonRegistry lessonRegistry,
    CloudRoomService cloudRooms,
    StudentScreenStore studentScreens,
    LessonMediaStore media,
    IHubContext<ClassroomHub> hub,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    if (!TryGetTeacher(httpRequest, configuration, out var teacherIdentity))
    {
        return Results.Unauthorized();
    }

    if (!lessonRegistry.TryGet(lessonId, out var existing) ||
        !string.Equals(existing!.TeacherId, teacherIdentity!.TeacherId, StringComparison.Ordinal))
    {
        return Results.NotFound();
    }

    var lesson = await lessonRegistry.EndAsync(lessonId, cancellationToken);
    if (lesson is null)
    {
        return Results.NotFound();
    }

    if (lesson.IsTemporary)
    {
        await cloudRooms.EndAsync(lesson.LessonId, cancellationToken);
    }

    studentScreens.RemoveLesson(lessonId);
    media.RemoveLesson(lessonId);

    await hub.Clients.Group(ClassroomHub.LessonGroup(lessonId))
        .SendAsync(HubProtocol.ScreenStopped, cancellationToken);
    foreach (var kind in new[] { ClassroomCommandKind.StopBlackout, ClassroomCommandKind.StopStudentView, ClassroomCommandKind.StopMedia })
    {
        var cleanup = new ClassroomCommand(Guid.NewGuid(), kind, JsonSerializer.SerializeToElement(new { }), DateTimeOffset.UtcNow);
        await hub.Clients.Group(ClassroomHub.LessonGroup(lessonId))
            .SendAsync(HubProtocol.ReceiveCommand, cleanup, cancellationToken);
    }
    var stopShare = new ClassroomCommand(Guid.NewGuid(), ClassroomCommandKind.StopScreenShare, JsonSerializer.SerializeToElement(new { }), DateTimeOffset.UtcNow);
    await hub.Clients.Group(ClassroomHub.AgentGroup(lesson.TeacherAgentId))
        .SendAsync(HubProtocol.ReceiveCommand, stopShare, cancellationToken);
    foreach (var agentId in lesson.MemberAgentIds)
    {
        foreach (var connectionId in agentRegistry.GetConnectionIds(agentId))
        {
            await hub.Groups.RemoveFromGroupAsync(connectionId, ClassroomHub.LessonGroup(lessonId), cancellationToken);
        }
    }
    await agentRegistry.RemoveRoamingAsync(lesson.MemberAgentIds, cancellationToken);

    return Results.NoContent();
}).RequireRateLimiting("teacher");

app.MapPost("/api/v1/lessons/{lessonId:guid}/extend", async (
    Guid lessonId,
    HttpRequest request,
    ExtendLessonRequest extension,
    LessonRegistry lessonRegistry,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    if (!TryGetTeacher(request, configuration, out var teacher) || teacher is null)
    {
        return Results.Unauthorized();
    }
    if (!lessonRegistry.TryGet(lessonId, out var current) || current is null ||
        !string.Equals(current.TeacherId, teacher.TeacherId, StringComparison.Ordinal))
    {
        return Results.NotFound(new { error = ApiText.Get(request, "LessonNotFound") });
    }
    if (extension.Minutes is < 5 or > 240)
    {
        return Results.BadRequest(new { error = ApiText.Get(request, "InvalidExtension") });
    }

    var updated = await lessonRegistry.ExtendAsync(lessonId, TimeSpan.FromMinutes(extension.Minutes), cancellationToken);
    return updated is null
        ? Results.NotFound(new { error = ApiText.Get(request, "LessonNotFound") })
        : Results.Ok(updated);
}).RequireRateLimiting("teacher");

app.MapGet("/api/v1/lessons/{lessonId:guid}/screens/{agentId:guid}", (
    Guid lessonId,
    Guid agentId,
    HttpRequest request,
    LessonRegistry lessonRegistry,
    StudentScreenStore screens,
    IConfiguration configuration) =>
{
    if (!TryGetTeacher(request, configuration, out var teacher) || teacher is null ||
        !lessonRegistry.TryGet(lessonId, out var lesson) || lesson is null ||
        !string.Equals(lesson.TeacherId, teacher.TeacherId, StringComparison.Ordinal) ||
        !lesson.MemberAgentIds.Contains(agentId))
    {
        return Results.Unauthorized();
    }

    if (!screens.Refresh(lessonId, agentId))
    {
        return Results.NoContent();
    }

    return screens.TryGet(lessonId, agentId, out var frame) && frame is not null
        ? Results.File(frame.Jpeg, "image/jpeg", enableRangeProcessing: false)
        : Results.NoContent();
}).RequireRateLimiting("teacher");

app.MapPost("/api/v1/lessons/{lessonId:guid}/media", async (
    Guid lessonId,
    HttpRequest request,
    LessonRegistry lessonRegistry,
    LessonMediaStore media,
    IHubContext<ClassroomHub> hub,
    CommandAuditStore audit,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    if (!TryGetTeacher(request, configuration, out var teacher) || teacher is null ||
        !lessonRegistry.TryGet(lessonId, out var lesson) || lesson is null ||
        !string.Equals(lesson.TeacherId, teacher.TeacherId, StringComparison.Ordinal))
    {
        return Results.Unauthorized();
    }
    if (!request.HasFormContentType || request.ContentLength is > 26_000_000)
    {
        return Results.BadRequest(new { error = ApiText.Get(request, "InvalidMedia") });
    }

    var form = await request.ReadFormAsync(cancellationToken);
    var file = form.Files.GetFile("media");
    Guid[] requestedTargets;
    try
    {
        requestedTargets = JsonSerializer.Deserialize<Guid[]>(form["targetAgentIds"].ToString(), JsonSerializerOptions.Web) ?? [];
    }
    catch (JsonException)
    {
        requestedTargets = [];
    }
    var allowed = lesson.MemberAgentIds.Where(id => id != lesson.TeacherAgentId).ToHashSet();
    var targets = requestedTargets.Distinct().Where(allowed.Contains).Take(200).ToArray();
    if (file is null || file.Length is < 1 or > 25_000_000 || targets.Length == 0)
    {
        return Results.BadRequest(new { error = ApiText.Get(request, file is null ? "InvalidMedia" : "NoLessonTarget") });
    }

    LessonMediaEntry stored;
    try
    {
        await using var input = file.OpenReadStream();
        stored = await media.CreateAsync(lessonId, file.FileName, file.ContentType, input, targets, cancellationToken);
    }
    catch (InvalidOperationException)
    {
        return Results.BadRequest(new { error = ApiText.Get(request, "InvalidMedia") });
    }

    var payload = JsonSerializer.SerializeToElement(new
    {
        mediaId = stored.MediaId,
        fileName = stored.FileName,
        contentType = stored.ContentType,
        sha256 = stored.Sha256,
        downloadUrl = $"/api/v1/agent/media/{lessonId:D}/{stored.MediaId:D}"
    });
    var command = new ClassroomCommand(Guid.NewGuid(), ClassroomCommandKind.ShowMedia, payload, DateTimeOffset.UtcNow);
    foreach (var target in targets)
    {
        await hub.Clients.Group(ClassroomHub.AgentGroup(target)).SendAsync(HubProtocol.ReceiveCommand, command, cancellationToken);
    }
    await audit.AppendAsync(command, targets, cancellationToken);
    return Results.Accepted(value: new
    {
        command.CommandId,
        targetCount = targets.Length,
        stored.MediaId,
        downloadUrl = $"/api/v1/agent/media/{lessonId:D}/{stored.MediaId:D}"
    });
}).RequireRateLimiting("teacher");

app.MapGet("/api/v1/agent/media/{lessonId:guid}/{mediaId:guid}", (
    Guid lessonId,
    Guid mediaId,
    HttpRequest request,
    AgentRegistry agentRegistry,
    LessonMediaStore media) =>
{
    if (!TryAuthenticateAgentWithSummary(request, agentRegistry, out var agent) || agent is null ||
        !media.TryOpen(mediaId, lessonId, agent.AgentId, out var entry) || entry is null)
    {
        return Results.Unauthorized();
    }
    return Results.File(entry.Path, entry.ContentType, entry.FileName, enableRangeProcessing: true);
}).RequireRateLimiting("teacher");

app.MapPost("/api/v1/lessons/{lessonId:guid}/commands", async (
    Guid lessonId,
    HttpRequest httpRequest,
    CommandRequest request,
    AgentRegistry agentRegistry,
    LessonRegistry lessonRegistry,
    IHubContext<ClassroomHub> hub,
    CommandAuditStore audit,
    StudentScreenStore studentScreens,
    CloudDirectoryStore directory,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    if (!TryGetTeacher(httpRequest, configuration, out var teacherIdentity))
    {
        return Results.Unauthorized();
    }

    if (!lessonRegistry.TryGet(lessonId, out var lesson))
    {
        return Results.NotFound(new { error = ApiText.Get(httpRequest, "LessonNotFound") });
    }

    if (!string.Equals(lesson!.TeacherId, teacherIdentity!.TeacherId, StringComparison.Ordinal))
    {
        return Results.Forbid();
    }

    var applications = AvailableApplications(directory);
    var validationError = ValidateCommand(httpRequest, request, applications);
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

    if (request.Kind == ClassroomCommandKind.StartStudentView)
    {
        foreach (var target in targets)
        {
            studentScreens.Authorize(lessonId, target);
        }
    }
    else if (request.Kind == ClassroomCommandKind.StopStudentView)
    {
        foreach (var target in targets)
        {
            studentScreens.Revoke(target);
        }
    }

    var payload = request.Payload.Clone();
    if (request.Kind == ClassroomCommandKind.LaunchApplication)
    {
        var applicationId = request.Payload.GetProperty("applicationId").GetString()!;
        var application = applications.First(item => string.Equals(item.Id, applicationId, StringComparison.OrdinalIgnoreCase));
        payload = JsonSerializer.SerializeToElement(new
        {
            applicationId = application.Id,
            displayName = application.DisplayName,
            fileName = application.FileName,
            arguments = application.Arguments ?? string.Empty
        });
    }
    else if (request.Kind == ClassroomCommandKind.StartScreenShare)
    {
        var viewers = ReadGuidArray(request.Payload, "viewerAgentIds")
            .Where(id => allowedTargets.Contains(id) && id != lesson.TeacherAgentId)
            .Distinct().Take(200).ToArray();
        if (viewers.Length == 0)
        {
            return Results.BadRequest(new { error = ApiText.Get(httpRequest, "NoLessonTarget") });
        }
        payload = JsonSerializer.SerializeToElement(new
        {
            monitorIndex = request.Payload.GetProperty("monitorIndex").GetInt32(),
            viewerAgentIds = viewers
        });
    }

    var command = new ClassroomCommand(Guid.NewGuid(), request.Kind, payload, DateTimeOffset.UtcNow);
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
    CloudDirectoryStore directory,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    if (!IsAdmin(httpRequest, configuration))
    {
        return Results.Unauthorized();
    }

    var applications = AvailableApplications(directory);
    var validationError = ValidateCommand(httpRequest, request, applications);
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

    var payload = request.Payload.Clone();
    if (request.Kind == ClassroomCommandKind.LaunchApplication)
    {
        var applicationId = request.Payload.GetProperty("applicationId").GetString()!;
        var application = applications.First(item => string.Equals(item.Id, applicationId, StringComparison.OrdinalIgnoreCase));
        payload = JsonSerializer.SerializeToElement(new
        {
            applicationId = application.Id,
            displayName = application.DisplayName,
            fileName = application.FileName,
            arguments = application.Arguments ?? string.Empty
        });
    }

    var command = new ClassroomCommand(Guid.NewGuid(), request.Kind, payload, DateTimeOffset.UtcNow);
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
    var sessions = request.HttpContext.RequestServices.GetRequiredService<AdminSessionService>();
    if (sessions.IsValid(request.Headers[AdminSessionService.HeaderName].ToString()))
    {
        return true;
    }

    var expectedToken = Environment.GetEnvironmentVariable("IWEBCLASSROOM_ADMIN_TOKEN")
                        ?? configuration["Security:AdminToken"];
    return SecretComparer.Equals(request.Headers[HubProtocol.AdminTokenHeader].ToString(), expectedToken);
}

static bool TryGetTeacher(HttpRequest request, IConfiguration configuration, out TeacherIdentity? teacher)
{
    var sessions = request.HttpContext.RequestServices.GetRequiredService<TeacherSessionService>();
    if (sessions.TryGet(request.Headers[TeacherSessionService.HeaderName].ToString(), out teacher))
    {
        return true;
    }

    if (IsAdmin(request, configuration))
    {
        teacher = new TeacherIdentity("local-administrator", "Administrador local");
        return true;
    }

    teacher = null;
    return false;
}

static string TeacherAccessLocale(HttpRequest request)
{
    var language = request.Headers.AcceptLanguage.ToString().ToLowerInvariant();
    return language.StartsWith("en", StringComparison.Ordinal) ? "en" :
        language.StartsWith("es", StringComparison.Ordinal) ? "es" : "pt";
}

static bool TryAuthenticateAgent(HttpRequest request, AgentRegistry registry)
{
    return TryAuthenticateAgentWithSummary(request, registry, out _);
}

static bool TryAuthenticateAgentWithSummary(HttpRequest request, AgentRegistry registry, out AgentSummary? agent)
{
    agent = null;
    return Guid.TryParse(request.Headers[HubProtocol.AgentIdHeader].ToString(), out var agentId) &&
           registry.TryAuthenticate(agentId, request.Headers[HubProtocol.AgentTokenHeader].ToString(), out agent);
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

static string? ValidateCommand(
    HttpRequest httpRequest,
    CommandRequest request,
    IReadOnlyCollection<ClassroomApplication> applications)
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
                !applications.Any(item => string.Equals(item.Id, applicationId.GetString(), StringComparison.OrdinalIgnoreCase)))
            {
                return ApiText.Get(httpRequest, "AppNotAllowed");
            }

            break;
        case ClassroomCommandKind.StartScreenShare:
            if (!request.Payload.TryGetProperty("monitorIndex", out var monitorIndex) ||
                monitorIndex.ValueKind != JsonValueKind.Number || !monitorIndex.TryGetInt32(out var display) || display is < 0 or > 15 ||
                ReadGuidArray(request.Payload, "viewerAgentIds").Count == 0)
            {
                return ApiText.Get(httpRequest, "InvalidMonitor");
            }
            break;
        case ClassroomCommandKind.StopScreenShare:
        case ClassroomCommandKind.CheckForUpdate:
        case ClassroomCommandKind.StartBlackout:
        case ClassroomCommandKind.StopBlackout:
        case ClassroomCommandKind.StartStudentView:
        case ClassroomCommandKind.StopStudentView:
        case ClassroomCommandKind.StopMedia:
            break;
        default:
            return ApiText.Get(httpRequest, "UnsupportedCommand");
    }

    return null;
}

static IReadOnlyCollection<ClassroomApplication> AvailableApplications(CloudDirectoryStore directory)
{
    var applications = directory.Current.Applications;
    return applications is not null
        ? applications
            .Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.DisplayName) && !string.IsNullOrWhiteSpace(item.FileName))
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray()
        : CloudDirectoryResponse.DefaultApplications;
}

static IReadOnlyCollection<Guid> ReadGuidArray(JsonElement payload, string propertyName)
{
    if (!payload.TryGetProperty(propertyName, out var values) || values.ValueKind != JsonValueKind.Array)
    {
        return [];
    }

    return values.EnumerateArray()
        .Select(value => value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var id) ? id : Guid.Empty)
        .Where(id => id != Guid.Empty)
        .Distinct()
        .Take(200)
        .ToArray();
}

static string? ValidateCloudSettings(CloudServerSettings settings)
{
    if (settings.Classroom.MaximumFrameBytes is < 100_000 or > 5_000_000 ||
        settings.Classroom.MinimumFrameIntervalMilliseconds is < 100 or > 5_000 ||
        settings.Classroom.DefaultLessonDurationMinutes is < 15 or > 480 ||
        settings.Classroom.AllowedApplicationIds.Count is < 1 or > 100 ||
        settings.Classroom.AllowedApplicationIds.Any(id =>
            string.IsNullOrWhiteSpace(id) || id.Length > 64 || !id.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')))
    {
        return "A configuração da sala contém valores inválidos.";
    }

    if (!Uri.TryCreate(settings.Update.ManifestUrl, UriKind.Absolute, out var manifestUri) ||
        manifestUri.Scheme != Uri.UriSchemeHttps ||
        settings.Update.CheckIntervalHours is < 1 or > 168 ||
        settings.Update.SignerThumbprint.Length > 128)
    {
        return "A configuração de atualização não é válida.";
    }

    return null;
}

public partial class Program;
