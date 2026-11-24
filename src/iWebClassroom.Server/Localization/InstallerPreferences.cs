using System.Security;
using Microsoft.Win32;

namespace IWebClassroom.Server.Localization;

public static class InstallerPreferences
{
    public static string Language()
    {
        try
        {
            using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = localMachine.OpenSubKey(@"SOFTWARE\iWebClassroom\Server", writable: false);
            return Normalize(key?.GetValue("InstallLanguage") as string);
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            return "pt-PT";
        }
    }

    private static string Normalize(string? value) => value?.ToLowerInvariant() switch
    {
        "en-us" => "en",
        "es-es" => "es",
        _ => "pt-PT"
    };
}
