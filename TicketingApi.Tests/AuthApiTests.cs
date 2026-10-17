using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using Database.Context;
using Database.Enums.Identity;
using Infrastructure.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace TicketingApi.Tests;

public sealed class AuthApiTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private const string Password = "1234";
    private const string RegisterUrl = "/api/v1/auth/register";
    private const string LoginUrl = "/api/v1/auth/login";
    private const string TicketsUrl = "/Tickets/v1/Tickets/List";

    [Theory]
    [InlineData("Client")]
    [InlineData("Agent")]
    public async Task Register_AndLogin_ReturnBearerTokensWithSelectedRole(string role)
    {
        using var client = factory.CreateBrowser();
        var request = Registration(role);
        DateTime requestedAt = DateTime.UtcNow;
        using var registration = await client.PostAsJsonAsync(RegisterUrl, request);
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        Assert.False(registration.Headers.Contains("Set-Cookie"));
        JsonNode data = (await ReadAsync(registration))["data"]!;
        string token = data["token"]!.GetValue<string>();
        Assert.Equal(role, data["user"]!["role"]!.GetValue<string>());
        string[] permissions = data["user"]!["permissions"]!.AsArray()
            .Select(item => item!.GetValue<string>()).ToArray();
        Assert.Contains(role == "Client" ? TicketPermissions.Create : TicketPermissions.ChangeStatus, permissions);
        Assert.DoesNotContain(role == "Client" ? TicketPermissions.ReadQueue : TicketPermissions.Create, permissions);
        DateTime expiresAt = data["expiresAt"]!.GetValue<DateTime>();
        Assert.InRange(expiresAt, requestedAt.AddDays(30), DateTime.UtcNow.AddDays(30));
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.InRange((expiresAt - jwt.ValidTo).TotalSeconds, 0, 1);
        Assert.InRange((jwt.ValidTo - jwt.ValidFrom).TotalDays, 29.99, 30.001);
        Assert.Equal(role, jwt.Claims.Single(claim => claim.Type == ClaimTypes.Role).Value);
        Assert.Equal(permissions.Order(), jwt.Claims.Where(claim => claim.Type == TicketPermissions.ClaimType)
            .Select(claim => claim.Value).Order());
        Assert.Equal(data["user"]!["id"]!.GetValue<int>().ToString(),
            jwt.Claims.Single(claim => claim.Type == "UserId").Value);

        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            string mobile = request["mobile"]!.GetValue<string>();
            var user = await database.Users.SingleAsync(user => user.Mobile == mobile);
            Assert.Equal(role, user.Role.ToString());
            Assert.NotEqual(Password, user.PasswordHash);
            Assert.True(scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Verify(user.PasswordHash, Password));
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var authorized = await client.GetAsync(TicketsUrl);
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);

        using var login = await client.PostAsJsonAsync(LoginUrl, new
        {
            mobile = request["mobile"]!.GetValue<string>(), countryId = 105, password = Password
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        JsonNode loginData = (await ReadAsync(login))["data"]!;
        Assert.Equal(role, loginData["user"]!["role"]!.GetValue<string>());
        Assert.Equal(permissions, loginData["user"]!["permissions"]!.AsArray()
            .Select(item => item!.GetValue<string>()).ToArray());
        Assert.NotEqual(token, loginData["token"]!.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(loginData["refreshToken"]!.GetValue<string>()));
        Assert.NotEqual(data["refreshToken"]!.GetValue<string>(), loginData["refreshToken"]!.GetValue<string>());
        Assert.InRange(loginData["refreshTokenExpiresAt"]!.GetValue<DateTime>(),
            DateTime.UtcNow.AddDays(89.99), DateTime.UtcNow.AddDays(90));

        // Signing in again does not revoke an earlier stateless JWT.
        using var earlierToken = await client.GetAsync(TicketsUrl);
        Assert.Equal(HttpStatusCode.OK, earlierToken.StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        using var signedOut = await client.GetAsync(TicketsUrl);
        Assert.Equal(HttpStatusCode.Unauthorized, signedOut.StatusCode);
    }

    [Theory]
    [InlineData("role", null)]
    [InlineData("role", "Admin")]
    [InlineData("role", "1")]
    [InlineData("fullName", " ")]
    [InlineData("mobile", "not-an-mobile")]
    [InlineData("password", "")]
    [InlineData("confirmPassword", "Mismatch-Password-123!")]
    public async Task Register_RejectsInvalidFields(string field, string? value)
    {
        using var client = factory.CreateBrowser();
        var request = Registration();
        if (value == null)
            request.Remove(field);
        else
            request[field] = value;
        using var response = await client.PostAsJsonAsync(RegisterUrl, request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Register_RejectsNumericAndNullRoles()
    {
        using var client = factory.CreateBrowser();
        foreach (JsonNode? role in new JsonNode?[] { JsonValue.Create(1), null })
        {
            var request = Registration();
            request["role"] = role;
            using var response = await client.PostAsJsonAsync(RegisterUrl, request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task Register_RejectsDuplicateMobileAcrossFormats()
    {
        using var client = factory.CreateBrowser();
        var request = Registration();
        using var first = await client.PostAsJsonAsync(RegisterUrl, request);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        string mobile = request["mobile"]!.GetValue<string>();
        request["mobile"] = "00" + mobile[1..];
        request["role"] = "Agent";
        using var duplicate = await client.PostAsJsonAsync(RegisterUrl, request);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        Assert.Equal(UserRole.Client, (await database.Users.SingleAsync(user => user.Mobile == mobile)).Role);
    }

    [Fact]
    public async Task Login_RejectsUnknownUserWrongPasswordAndSuspendedUser()
    {
        using var client = factory.CreateBrowser();
        var request = Registration();
        string mobile = request["mobile"]!.GetValue<string>();
        using var registration = await client.PostAsJsonAsync(RegisterUrl, request);
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        using var wrong = await client.PostAsJsonAsync(LoginUrl, new { mobile, countryId = 105, password = "Wrong-Password-123!" });
        using var unknown = await client.PostAsJsonAsync(LoginUrl, new { mobile = "+989000000001", countryId = 105, password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(await wrong.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            var user = await database.Users.SingleAsync(user => user.Mobile == mobile);
            user.Status = UserStatus.Suspended;
            await database.SaveChangesAsync();
        }
        using var suspended = await client.PostAsJsonAsync(LoginUrl, new { mobile, countryId = 105, password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, suspended.StatusCode);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("wrong-signature")]
    [InlineData("wrong-issuer")]
    [InlineData("invalid-role")]
    [InlineData("invalid-user")]
    [InlineData("malformed")]
    [InlineData("missing-permission")]
    public async Task ProtectedEndpoints_RejectTokensWithoutValidClaimsOrPermissions(string problem)
    {
        using var client = factory.CreateBrowser();
        JwtOptions options = factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
        var jwt = new JwtSecurityToken(
            issuer: problem == "wrong-issuer" ? "OtherIssuer" : options.ValidIssuer,
            audience: options.ValidAudience,
            claims:
            [
                new Claim("UserId", problem == "invalid-user" ? "0" : "1"),
                new Claim(ClaimTypes.Role, problem == "invalid-role" ? "Admin" : "Client")
            ],
            notBefore: DateTime.UtcNow.AddHours(-1),
            expires: problem == "expired" ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                problem == "wrong-signature" ? new string('x', 64) : options.Secret)), SecurityAlgorithms.HmacSha256));
        string token = problem == "malformed" ? "invalid-token" : new JwtSecurityTokenHandler().WriteToken(jwt);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.GetAsync(TicketsUrl);
        Assert.Equal(problem == "missing-permission" ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoints_RequireAuthorizationHeader_AndIgnoreCookies()
    {
        using var client = factory.CreateBrowser();
        using var registration = await client.PostAsJsonAsync(RegisterUrl, Registration());
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        string token = (await ReadAsync(registration))["data"]!["token"]!.GetValue<string>();
        client.DefaultRequestHeaders.Add("Cookie", $"ticketing.session={token}");
        using var cookieOnly = await client.GetAsync(TicketsUrl);
        Assert.Equal(HttpStatusCode.Unauthorized, cookieOnly.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var bearer = await client.GetAsync(TicketsUrl);
        Assert.Equal(HttpStatusCode.OK, bearer.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/v1/auth/csrf")]
    [InlineData("GET", "/api/v1/auth/me")]
    [InlineData("POST", "/api/v1/auth/logout")]
    public async Task RemovedEndpoints_AreNotAvailable(string method, string url)
    {
        using var client = factory.CreateBrowser();
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("Client", HttpStatusCode.Forbidden, HttpStatusCode.Created)]
    [InlineData("Agent", HttpStatusCode.OK, HttpStatusCode.Forbidden)]
    public async Task SelectedRole_ControlsTicketPermissions(string role, HttpStatusCode agentAccess, HttpStatusCode clientAccess)
    {
        using var client = factory.CreateBrowser();
        using var registration = await client.PostAsJsonAsync(RegisterUrl, Registration(role));
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        string token = (await ReadAsync(registration))["data"]!["token"]!.GetValue<string>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var unassigned = await client.GetAsync("/Tickets/v1/Tickets/Unassigned");
        Assert.Equal(agentAccess, unassigned.StatusCode);
        using var create = await client.PostAsJsonAsync("/Tickets/v1/Tickets/Create", new
        {
            title = "Test ticket", description = "Test ticket description"
        });
        Assert.Equal(clientAccess, create.StatusCode);
    }

    [Fact]
    public async Task Login_IsRateLimited()
    {
        using var limitedFactory = new AuthApiFactory { PermitLimit = 2 };
        using var client = limitedFactory.CreateBrowser();
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using var response = await client.PostAsJsonAsync(LoginUrl, new { mobile = "+989000000001", countryId = 105, password = Password });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using var limited = await client.PostAsJsonAsync(LoginUrl, new { mobile = "+989000000001", countryId = 105, password = Password });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    private static JsonObject Registration(string role = "Client") => new()
    {
        ["fullName"] = "Test User",
        ["mobile"] = TestPhones.Next(), ["countryId"] = 105,
        ["password"] = Password,
        ["confirmPassword"] = Password,
        ["role"] = role
    };

    private static async Task<JsonNode> ReadAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
}
