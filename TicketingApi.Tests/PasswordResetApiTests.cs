using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Database.Context;
using Database.Enums.Identity;
using Infrastructure.Authentication;
using Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Repository.Identity;
using Service.Identity;
using ViewModels.Identity;
using Xunit;

namespace TicketingApi.Tests;

public sealed class PasswordResetApiTests
{
    private const string Root = "/api/v1/auth/forgot-password";
    [Theory]
    [InlineData("Client")]
    [InlineData("Agent")]
    public async Task SmsVerifyReset_IsOneUse_PreservesMobileAndRole_AndRevokesRefreshTokens(string role)
    {
        var sms = new CapturingSms(); using var factory = new AuthApiFactory { SmsSender = sms }; using var client = factory.CreateBrowser();
        string mobile = TestPhones.Next(); var auth = await Register(client, mobile, role);
        var sent = await Data(await client.PostAsJsonAsync(Root + "/request", new { countryId = 105, mobile }));
        Assert.Equal(300, sent["expiresInSeconds"]!.GetValue<int>());
        string code = sms.Code; Assert.Equal(mobile, sms.Mobile);
        using (var scope = factory.Services.CreateScope())
        {
            var user = await scope.ServiceProvider.GetRequiredService<DatabaseContext>().Users.SingleAsync(user => user.Mobile == mobile);
            Assert.NotEqual(code, user.PasswordResetCodeHash);
            Assert.True(scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Verify(user.PasswordResetCodeHash!, code));
        }
        Assert.DoesNotContain(code, sent.ToJsonString());
        var verified = await Data(await client.PostAsJsonAsync(Root + "/verify", new { countryId = 105, mobile, code }));
        Assert.Equal(300, verified["expiresInSeconds"]!.GetValue<int>());
        string token = verified["resetToken"]!.GetValue<string>(); Assert.Matches("^[A-F0-9]{64}$", token);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Root + "/verify", new { countryId = 105, mobile, code })).StatusCode);
        var request = new { countryId = 105, mobile, resetToken = token, password = "x", confirmPassword = "x" };
        Assert.True((await Data(await client.PostAsJsonAsync(Root + "/reset", request)))["passwordChanged"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Root + "/reset", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/login", new { countryId = 105, mobile, password = "1234" })).StatusCode);
        var login = await Data(await client.PostAsJsonAsync("/api/v1/auth/login", new { countryId = 105, mobile, password = "x" }));
        Assert.Equal(role, login["user"]!["role"]!.GetValue<string>()); Assert.Equal(mobile, login["user"]!["mobile"]!.GetValue<string>());
        Assert.Null(login["user"]!["email"]);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = auth["refreshToken"]!.GetValue<string>() })).StatusCode);
    }

    [Fact]
    public async Task FiveWrongCodes_ExpireTheChallenge_AndSendingAgainInvalidatesAnEarlierResetToken()
    {
        var sms = new CapturingSms(); using var factory = new AuthApiFactory { SmsSender = sms }; using var client = factory.CreateBrowser();
        string mobile = TestPhones.Next(); await Register(client, mobile);
        await Data(await client.PostAsJsonAsync(Root + "/request", new { countryId = 105, mobile }));
        string code = sms.Code;
        for (int attempt = 0; attempt < 5; attempt++)
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Root + "/verify", new { countryId = 105, mobile, code = "000000" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Root + "/verify", new { countryId = 105, mobile, code })).StatusCode);
        await Data(await client.PostAsJsonAsync(Root + "/request", new { countryId = 105, mobile }));
        var verified = await Data(await client.PostAsJsonAsync(Root + "/verify", new { countryId = 105, mobile, code = sms.Code }));
        await Data(await client.PostAsJsonAsync(Root + "/request", new { countryId = 105, mobile }));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Root + "/reset", new { countryId = 105, mobile, resetToken = verified["resetToken"]!.GetValue<string>(), password = "new", confirmPassword = "new" })).StatusCode);
    }

    [Theory]
    [InlineData("code")]
    [InlineData("token")]
    public async Task ExpiredCredentialsCannotChangeThePassword(string credential)
    {
        var sms = new CapturingSms(); using var factory = new AuthApiFactory { SmsSender = sms }; using var client = factory.CreateBrowser();
        string mobile = TestPhones.Next(); await Register(client, mobile);
        await Data(await client.PostAsJsonAsync(Root + "/request", new { countryId = 105, mobile }));
        string? token = null;
        if (credential == "token") token = (await Data(await client.PostAsJsonAsync(Root + "/verify", new { countryId = 105, mobile, code = sms.Code })))["resetToken"]!.GetValue<string>();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>(); var user = await db.Users.SingleAsync(user => user.Mobile == mobile);
            if (credential == "code") user.PasswordResetCodeExpiresAt = DateTime.UtcNow.AddSeconds(-1); else user.PasswordResetTokenExpiresAt = DateTime.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }
        var result = credential == "code" ? await client.PostAsJsonAsync(Root + "/verify", new { countryId = 105, mobile, code = sms.Code })
            : await client.PostAsJsonAsync(Root + "/reset", new { countryId = 105, mobile, resetToken = token, password = "new", confirmPassword = "new" });
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task ConcurrentLoadedCodeAndToken_CanOnlyBeConsumedOnce()
    {
        var sms = new CapturingSms(); using var factory = new AuthApiFactory { SmsSender = sms }; using var client = factory.CreateBrowser();
        string mobile = TestPhones.Next(); await Register(client, mobile);
        await Data(await client.PostAsJsonAsync(Root + "/request", new { countryId = 105, mobile }));
        using var first = factory.Services.CreateScope(); using var second = factory.Services.CreateScope();
        await first.ServiceProvider.GetRequiredService<IUsersRepository>().GetForPasswordResetAsync(mobile);
        await second.ServiceProvider.GetRequiredService<IUsersRepository>().GetForPasswordResetAsync(mobile);
        var verify = new VerifyPasswordResetRequest { CountryId = 105, Mobile = mobile, Code = sms.Code };
        var winner = await first.ServiceProvider.GetRequiredService<IPasswordResetService>().VerifyPasswordResetAsync(verify);
        Assert.Equal(200, winner.StatusCode);
        Assert.Equal(400, (await second.ServiceProvider.GetRequiredService<IPasswordResetService>().VerifyPasswordResetAsync(verify)).StatusCode);
        using var resetFirst = factory.Services.CreateScope(); using var resetSecond = factory.Services.CreateScope();
        await resetFirst.ServiceProvider.GetRequiredService<IUsersRepository>().GetForPasswordResetAsync(mobile);
        await resetSecond.ServiceProvider.GetRequiredService<IUsersRepository>().GetForPasswordResetAsync(mobile);
        var reset = new ResetPasswordRequest { CountryId = 105, Mobile = mobile, ResetToken = winner.Data!.ResetToken, Password = "new", ConfirmPassword = "new" };
        Assert.Equal(200, (await resetFirst.ServiceProvider.GetRequiredService<IPasswordResetService>().ResetPasswordAsync(reset)).StatusCode);
        Assert.Equal(400, (await resetSecond.ServiceProvider.GetRequiredService<IPasswordResetService>().ResetPasswordAsync(reset)).StatusCode);
    }

    [Fact]
    public async Task SmsFailureClearsChallenge_UnknownNumberAndMismatchedCountryAreRejected()
    {
        var sms = new CapturingSms { Fail = true }; using var factory = new AuthApiFactory { SmsSender = sms }; using var client = factory.CreateBrowser();
        string mobile = TestPhones.Next(); await Register(client, mobile);
        Assert.Equal(HttpStatusCode.BadGateway, (await client.PostAsJsonAsync(Root + "/request", new { countryId = 105, mobile })).StatusCode);
        using (var scope = factory.Services.CreateScope())
            Assert.Null((await scope.ServiceProvider.GetRequiredService<DatabaseContext>().Users.SingleAsync(user => user.Mobile == mobile)).PasswordResetCodeHash);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(Root + "/request", new { countryId = 105, mobile = TestPhones.Next() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Root + "/request", new { countryId = 235, mobile })).StatusCode);
    }

    private static async Task<JsonNode> Register(HttpClient client, string mobile, string role = "Client") =>
        await Data(await client.PostAsJsonAsync("/api/v1/auth/register", new { countryId = 105, mobile, fullName = "Recovery test", password = "1234", confirmPassword = "1234", role }));
    private static async Task<JsonNode> Data(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<JsonNode>())!["data"]!;
    }
    private sealed class CapturingSms : ISmsSender
    {
        public bool Fail { get; set; }
        public string Code { get; private set; } = "";
        public string Mobile { get; private set; } = "";
        public Task SendAsync(string mobile, string message, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new HttpRequestException("Simulated provider outage");
            Mobile = mobile; Code = Regex.Match(message, "[0-9]{6}").Value; return Task.CompletedTask;
        }
    }
}
