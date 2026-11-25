using System.Security.Cryptography;

namespace IWebClassroom.Server.Security;

public static class PinHasher
{
    private const int Iterations = 120_000;
    private const int HashSize = 32;

    public static (string Salt, string Hash) Create(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return (Convert.ToHexString(salt), Convert.ToHexString(hash));
    }

    public static bool Verify(string pin, string saltHex, string hashHex)
    {
        try
        {
            var salt = Convert.FromHexString(saltHex);
            var expected = Convert.FromHexString(hashHex);
            var actual = Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
