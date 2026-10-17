using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Database.Context;
using Database.Enums.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Repository.Identity;
using Service.Identity;
using ViewModels.Identity;
using Xunit;

namespace TicketingApi.Tests;

public sealed class RefreshTokenApiTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    [Fact]
    public async Task Refresh_RotatesTokensWithoutAccessToken_AndRejectsReuse()
    {
        using var client = factory.CreateBrowser();
        JsonNode original = await RegisterAsync(client);
        string refresh = original["refreshToken"]!.GetValue<string>();
        Assert.InRange(original["refreshTokenExpiresAt"]!.GetValue<DateTime>(),
            DateTime.UtcNow.AddDays(89.99), DateTime.UtcNow.AddDays(90));
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            string hash = Hash(refresh);
            var stored = await context.RefreshTokens.SingleAsync(token => token.TokenHash == hash);
            Assert.NotEqual(refresh, stored.TokenHash);
            Assert.Null(stored.UsedAt);
        }

        using var response = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = refresh });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonNode renewed = (await response.Content.ReadFromJsonAsync<JsonNode>())!["data"]!;
        Assert.NotEqual(original["token"]!.GetValue<string>(), renewed["token"]!.GetValue<string>());
        Assert.NotEqual(refresh, renewed["refreshToken"]!.GetValue<string>());
        Assert.Equal(original["user"]!["id"]!.GetValue<int>(), renewed["user"]!["id"]!.GetValue<int>());
        Assert.InRange(renewed["expiresAt"]!.GetValue<DateTime>(),
            DateTime.UtcNow.AddDays(29.99), DateTime.UtcNow.AddDays(30));

        using var reused = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = refresh });
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
        using var next = await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = renewed["refreshToken"]!.GetValue<string>() });
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", renewed["token"]!.GetValue<string>());
        using var tickets = await client.GetAsync("/Tickets/v1/Tickets/List");
        Assert.Equal(HttpStatusCode.OK, tickets.StatusCode);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("used")]
    [InlineData("suspended")]
    [InlineData("unknown")]
    public async Task Refresh_RejectsInvalidCredentials(string condition)
    {
        using var client = factory.CreateBrowser();
        JsonNode auth = await RegisterAsync(client);
        string refresh = auth["refreshToken"]!.GetValue<string>();
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            string hash = Hash(refresh);
            var token = await context.RefreshTokens.Include(item => item.User)
                .SingleAsync(item => item.TokenHash == hash);
            if (condition == "expired") token.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
            if (condition == "used") token.UsedAt = DateTime.UtcNow;
            if (condition == "suspended") token.User.Status = UserStatus.Suspended;
            await context.SaveChangesAsync();
        }
        using var response = await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = condition == "unknown" ? "unknown-token" : refresh });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithConcurrentLoadedToken_CommitsOnlyOneSuccessor()
    {
        using var client = factory.CreateBrowser();
        JsonNode auth = await RegisterAsync(client);
        string refresh = auth["refreshToken"]!.GetValue<string>();
        int userId = auth["user"]!["id"]!.GetValue<int>();
        using var first = factory.Services.CreateScope();
        using var second = factory.Services.CreateScope();
        // Both requests read an unused token before either one commits.
        await first.ServiceProvider.GetRequiredService<IRefreshTokensRepository>().GetByHashAsync(Hash(refresh));
        await second.ServiceProvider.GetRequiredService<IRefreshTokensRepository>().GetByHashAsync(Hash(refresh));
        var request = new RefreshTokenRequest { RefreshToken = refresh };
        var winner = await first.ServiceProvider.GetRequiredService<IAuthService>().RefreshAsync(request);
        var loser = await second.ServiceProvider.GetRequiredService<IAuthService>().RefreshAsync(request);
        Assert.Equal(200, winner.StatusCode);
        Assert.Equal(401, loser.StatusCode);
        using var verification = factory.Services.CreateScope();
        var context = verification.ServiceProvider.GetRequiredService<DatabaseContext>();
        Assert.Equal(2, await context.RefreshTokens.CountAsync(token => token.UserId == userId));
        Assert.Equal(1, await context.RefreshTokens.CountAsync(token => token.UserId == userId && token.UsedAt == null));
    }

    [Fact]
    public async Task Refresh_UsesCurrentRoleAndPermissions_DespiteExpiredAccessToken()
    {
        using var client = factory.CreateBrowser();
        JsonNode auth = await RegisterAsync(client);
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            var user = await context.Users.FindAsync(auth["user"]!["id"]!.GetValue<int>());
            user!.Role = UserRole.Agent;
            await context.SaveChangesAsync();
        }
        // Refresh authenticates with its own credential, even if Bearer authentication fails.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "expired-or-invalid");
        using var response = await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = auth["refreshToken"]!.GetValue<string>() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonNode renewed = (await response.Content.ReadFromJsonAsync<JsonNode>())!["data"]!;
        Assert.Equal("Agent", renewed["user"]!["role"]!.GetValue<string>());
        Assert.Contains("tickets.status.change", renewed["user"]!["permissions"]!.AsArray()
            .Select(item => item!.GetValue<string>()));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task Refresh_RequiresCredential(string? refreshToken)
    {
        using var client = factory.CreateBrowser();
        using var response = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task HealthEndpoint_IsRemoved()
    {
        using var client = factory.CreateBrowser();
        using var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<JsonNode> RegisterAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            fullName = "Refresh Test", mobile = TestPhones.Next(), countryId = 105,
            password = "1234", confirmPassword = "1234", role = "Client"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonNode>())!["data"]!;
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
