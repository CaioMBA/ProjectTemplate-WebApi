using System.Security.Cryptography;
using Domain.Extensions;

namespace UnitTests.Domain;

public sealed class CryptographyTests
{
    [Fact]
    public void Encrypt_ThenDecrypt_RoundTrips()
    {
        var key = CryptographyExtension.GenerateKey();
        const string plainText = "correct horse battery staple";

        var cipherText = plainText.Encrypt(key);

        cipherText.Decrypt(key).ShouldBe(plainText);
    }

    [Fact]
    public void Encrypt_ProducesDifferentCiphertextEachTime()
    {
        var key = CryptographyExtension.GenerateKey();
        const string plainText = "same input";

        var first = plainText.Encrypt(key);
        var second = plainText.Encrypt(key);

        first.ShouldNotBe(second);
        first.Decrypt(key).ShouldBe(second.Decrypt(key));
    }

    [Fact]
    public void Decrypt_WithTamperedCiphertext_Throws()
    {
        var key = CryptographyExtension.GenerateKey();
        var cipherText = "sensitive".Encrypt(key);

        var payload = Convert.FromBase64String(cipherText);

        payload[^1] ^= 0x01;

        var tampered = Convert.ToBase64String(payload);

        Should.Throw<CryptographicException>(() => tampered.Decrypt(key));
    }

    [Fact]
    public void Decrypt_WithWrongKey_Throws()
    {
        var cipherText = "sensitive".Encrypt(CryptographyExtension.GenerateKey());

        Should.Throw<CryptographicException>(() => cipherText.Decrypt(CryptographyExtension.GenerateKey()));
    }

    [Fact]
    public void Encrypt_WithWrongSizedKey_ThrowsArgumentException()
    {
        var shortKey = new byte[16];

        Should.Throw<ArgumentException>(() => "value".Encrypt(shortKey));
    }

    [Fact]
    public void GenerateKey_ProducesA256BitKey()
    {
        CryptographyExtension.GenerateKey().Length.ShouldBe(32);
    }

    [Fact]
    public void HashPassword_ThenVerify_Succeeds()
    {
        const string password = "s3cret-passphrase";

        var hash = password.HashPassword();

        password.VerifyPassword(hash).ShouldBeTrue();
    }

    [Fact]
    public void HashPassword_WithWrongPassword_Fails()
    {
        var hash = "correct".HashPassword();

        "incorrect".VerifyPassword(hash).ShouldBeFalse();
    }

    [Fact]
    public void HashPassword_IsSaltedSoIdenticalPasswordsDiffer()
    {
        const string password = "identical";

        var first = password.HashPassword();
        var second = password.HashPassword();

        first.ShouldNotBe(second);

        password.VerifyPassword(first).ShouldBeTrue();
        password.VerifyPassword(second).ShouldBeTrue();
    }

    [Fact]
    public void HashPassword_RecordsItsParametersForFutureRehashing()
    {
        var hash = "value".HashPassword();

        var parts = hash.Split('$');

        parts.Length.ShouldBe(4);
        parts[0].ShouldBe("pbkdf2-sha512");
        int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture).ShouldBeGreaterThanOrEqualTo(210_000);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("pbkdf2-sha512$notanumber$salt$hash")]
    [InlineData("pbkdf2-sha512$1000$!!!invalid-base64!!!$hash")]
    public void VerifyPassword_WithMalformedHash_ReturnsFalseRatherThanThrowing(string malformed)
    {
        "password".VerifyPassword(malformed).ShouldBeFalse();
    }
}
