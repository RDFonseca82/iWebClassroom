using Microsoft.Win32;

namespace IWebClassroom.Agent.Configuration;

public static class MachineIdentity
{
    public static string Get()
    {
        var machineGuid = Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography",
            "MachineGuid",
            null) as string;
        return string.IsNullOrWhiteSpace(machineGuid)
            ? $"{Environment.UserDomainName}:{Environment.MachineName}"
            : machineGuid;
    }
}
