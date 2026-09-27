using System.Security.Cryptography;
using System.Text;

namespace Domain.Extensions;

public static class CryptographyExtension
{
    public const int KeySizeBytes = 32;

    public const int NonceSizeBytes = 12;

    public const int TagSizeBytes = 16;

    private const int Pbkdf2Iterations = 210_000;
    private const int SaltSizeBytes = 16;
    private const int DerivedKeySizeBytes = 32;

    public static string Encrypt(this string plainText, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(plainText);
        ValidateKey(key);

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var output = new byte[NonceSizeBytes + TagSizeBytes + plainBytes.Length];

        var nonce = output.AsSpan(0, NonceSizeBytes);
        var tag = output.AsSpan(NonceSizeBytes, TagSizeBytes);
        var cipherText = output.AsSpan(NonceSizeBytes + TagSizeBytes);

        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key, TagSizeBytes);
        aes.Encrypt(nonce, plainBytes, cipherText, tag);

        return Convert.ToBase64String(output);
    }

    public static string Decrypt(this string cipherTextBase64, byte[] key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cipherTextBase64);
        ValidateKey(key);

        var payload = Convert.FromBase64String(cipherTextBase64);

        if (payload.Length < NonceSizeBytes + TagSizeBytes)
        {
            throw new CryptographicException("The payload is too short to be a valid AES-GCM message.");
        }

        var nonce = payload.AsSpan(0, NonceSizeBytes);
        var tag = payload.AsSpan(NonceSizeBytes, TagSizeBytes);
        var cipherText = payload.AsSpan(NonceSizeBytes + TagSizeBytes);
        var plainBytes = new byte[cipherText.Length];

        using var aes = new AesGcm(key, TagSizeBytes);
        aes.Decrypt(nonce, cipherText, tag, plainBytes);

        return Encoding.UTF8.GetString(plainBytes);
    }

    public static byte[] GenerateKey() => RandomNumberGenerator.GetBytes(KeySizeBytes);

    public static byte[] KeyFromBase64(string base64Key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(base64Key);

        var key = Convert.FromBase64String(base64Key);
        ValidateKey(key);

        return key;
    }

    public static string HashPassword(this string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);

        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA512,
            DerivedKeySizeBytes);

        return string.Join(
            '$',
            "pbkdf2-sha512",
            Pbkdf2Iterations.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public static bool VerifyPassword(this string password, string encodedHash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(encodedHash))
        {
            return false;
        }

        var parts = encodedHash.Split('$');

        if (parts.Length != 4
            || !string.Equals(parts[0], "pbkdf2-sha512", StringComparison.Ordinal)
            || !int.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out var iterations))
        {
            return false;
        }

        byte[] salt;
        byte[] expected;

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA512,
            expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public static string ToHmacSha256(this string value, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(key);

        var mac = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value));

        return Convert.ToHexStringLower(mac);
    }

    private static void ValidateKey(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (key.Length != KeySizeBytes)
        {
            throw new ArgumentException(
                $"The key must be exactly {KeySizeBytes} bytes (AES-256), but was {key.Length}.",
                nameof(key));
        }
    }
}
