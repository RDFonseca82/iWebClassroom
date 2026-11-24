using System.Security;
using System.Globalization;
using Microsoft.Win32;

namespace IWebClassroom.Agent.Configuration;

public static class InstallerPreferences
{
    private const string RegistryPath = @"SOFTWARE\iWebClassroom\Agent";

    public static string? ServerUrl() => Read("ServerUrl");

    public static string Language()
    {
        var configured = Read("InstallLanguage")?.ToLowerInvariant();
        return configured switch
        {
            "pt-pt" => "pt",
            "es-es" => "es",
            "en-us" => "en",
            _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
            {
                "pt" => "pt",
                "es" => "es",
                _ => "en"
            }
        };
    }

    private static string? Read(string name)
    {
        try
        {
            using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = localMachine.OpenSubKey(RegistryPath, writable: false);
            return key?.GetValue(name) as string;
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
