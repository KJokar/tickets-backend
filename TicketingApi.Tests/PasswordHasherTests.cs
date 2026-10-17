using Infrastructure.Authentication;
using Xunit;

namespace TicketingApi.Tests;

public sealed class PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Hash_AndVerify_RoundTripSuccessfully()
    {
        string hash = _hasher.Hash("A-strong-password-123!");

        Assert.NotEqual("A-strong-password-123!", hash);
        Assert.True(_hasher.Verify(hash, "A-strong-password-123!"));
        Assert.False(_hasher.Verify(hash, "wrong-password"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-supported-hash")]
    [InlineData("pbkdf2-sha256$bad$bad$bad")]
    public void Verify_ReturnsFalse_ForMalformedHash(string hash)
    {
        Assert.False(_hasher.Verify(hash, "password"));
    }
}
