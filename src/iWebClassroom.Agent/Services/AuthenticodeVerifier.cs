using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace IWebClassroom.Agent.Services;

public static class AuthenticodeVerifier
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    public static bool IsTrustedAndSignedBy(string filePath, string expectedThumbprint)
    {
        if (!File.Exists(filePath) || string.IsNullOrWhiteSpace(expectedThumbprint) || !VerifyTrust(filePath))
        {
            return false;
        }

        try
        {
#pragma warning disable SYSLIB0057 // Required to extract the signer from an Authenticode-signed PE/MSI file.
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));
#pragma warning restore SYSLIB0057
            return string.Equals(
                Normalize(certificate.Thumbprint),
                Normalize(expectedThumbprint),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static bool VerifyTrust(string filePath)
    {
        var fileInfo = new WinTrustFileInfo(filePath);
        var fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);
            var trustData = new WinTrustData(fileInfoPointer);
            return WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref trustData) == 0;
        }
        finally
        {
            Marshal.FreeHGlobal(fileInfoPointer);
        }
    }

    private static string Normalize(string value) => value.Replace(" ", string.Empty, StringComparison.Ordinal);

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr window, [MarshalAs(UnmanagedType.LPStruct)] Guid actionId, ref WinTrustData trustData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private readonly struct WinTrustFileInfo(string filePath)
    {
        public readonly uint StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>();
        public readonly string FilePath = filePath;
        public readonly IntPtr FileHandle = IntPtr.Zero;
        public readonly IntPtr KnownSubject = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private readonly struct WinTrustData(IntPtr fileInfo)
    {
        public readonly uint StructSize = (uint)Marshal.SizeOf<WinTrustData>();
        public readonly IntPtr PolicyCallbackData = IntPtr.Zero;
        public readonly IntPtr SipClientData = IntPtr.Zero;
        public readonly uint UiChoice = 2;
        public readonly uint RevocationChecks = 0;
        public readonly uint UnionChoice = 1;
        public readonly IntPtr FileInfo = fileInfo;
        public readonly uint StateAction = 0;
        public readonly IntPtr StateData = IntPtr.Zero;
        public readonly string? UrlReference = null;
        public readonly uint ProviderFlags = 0x00001000;
        public readonly uint UiContext = 0;
    }
}
