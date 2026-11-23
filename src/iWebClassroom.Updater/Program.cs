using System.Diagnostics;
using System.Security.Cryptography;
using IWebClassroom.Agent.Services;

var arguments = ParseArguments(args);
if (!arguments.TryGetValue("package", out var packagePath) ||
    !arguments.TryGetValue("sha256", out var expectedHash) ||
    !arguments.TryGetValue("signer", out var signerThumbprint) ||
    !arguments.TryGetValue("wait-pid", out var processIdValue) ||
    !int.TryParse(processIdValue, out var processId))
{
    Console.Error.WriteLine("Required arguments: --package, --sha256, --signer and --wait-pid.");
    return 2;
}

if (!Path.IsPathFullyQualified(packagePath) || !File.Exists(packagePath))
{
    Console.Error.WriteLine("The update package does not exist.");
    return 3;
}

await using (var stream = File.OpenRead(packagePath))
{
    var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream));
    if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("The update package hash is invalid.");
        return 4;
    }
}

if (!AuthenticodeVerifier.IsTrustedAndSignedBy(packagePath, signerThumbprint))
{
    Console.Error.WriteLine("The update package signature is invalid.");
    return 5;
}

try
{
    using var process = Process.GetProcessById(processId);
    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
}
catch (ArgumentException)
{
    // The process has already stopped.
}
catch (TimeoutException)
{
    Console.Error.WriteLine("The application did not stop in time.");
    return 6;
}

using var installer = Process.Start(new ProcessStartInfo
{
    FileName = Path.Combine(Environment.SystemDirectory, "msiexec.exe"),
    UseShellExecute = false,
    ArgumentList = { "/i", packagePath, "/qn", "/norestart" }
});
if (installer is null)
{
    Console.Error.WriteLine("The Windows Installer could not be started.");
    return 7;
}

await installer.WaitForExitAsync();
if (installer.ExitCode != 0)
{
    return installer.ExitCode;
}

if (arguments.TryGetValue("scheduled-task", out var scheduledTask) && !string.IsNullOrWhiteSpace(scheduledTask))
{
    using var taskStarter = Process.Start(new ProcessStartInfo
    {
        FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
        UseShellExecute = false,
        ArgumentList = { "/Run", "/TN", scheduledTask }
    });
    if (taskStarter is not null)
    {
        await taskStarter.WaitForExitAsync();
        return taskStarter.ExitCode;
    }
}

return 0;

static Dictionary<string, string> ParseArguments(string[] values)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index + 1 < values.Length; index += 2)
    {
        if (values[index].StartsWith("--", StringComparison.Ordinal))
        {
            result[values[index][2..]] = values[index + 1];
        }
    }

    return result;
}
