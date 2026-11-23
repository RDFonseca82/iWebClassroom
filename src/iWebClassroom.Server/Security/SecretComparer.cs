using System.Security.Cryptography;
using System.Text;

namespace IWebClassroom.Server.Security;

public static class SecretComparer
{
    public static bool Equals(string? supplied, string? expected)
    {
        if (string.IsNullOrWhiteSpace(supplied) || string.IsNullOrWhiteSpace(expected))
        {
            return false;
        }

        var suppliedBytes = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        var expectedBytes = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
    }

    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
