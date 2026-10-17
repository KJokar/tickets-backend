using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace TicketingApi.Tests;

public sealed class MobileIdentityApiTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    [Fact]
    public async Task CountryCatalogueAndMobileFormats_IdentifyOneAccountAcrossRoles()
    {
        using var client = factory.CreateBrowser();
        var countries = (await Data(await client.GetAsync("/Common/v1/Countries/List"))).AsArray();
        Assert.Equal(248, countries.Count);
        var iran = countries.Single(country => country!["isoCode"]!.GetValue<string>() == "IR")!;
        Assert.Equal(105, iran["id"]!.GetValue<int>()); Assert.Equal("+98", iran["phoneCode"]!.GetValue<string>());
        string canonical = TestPhones.Next(), national = canonical[3..];
        await Data(await client.PostAsJsonAsync("/api/v1/auth/register", Registration(105, "0" + national)));
        string persian = string.Concat(national.Select(digit => "۰۱۲۳۴۵۶۷۸۹"[digit - '0']));
        foreach (string mobile in new[] { canonical, national, persian, "00" + canonical[1..], canonical[1..] })
        {
            var duplicate = Registration(105, mobile); duplicate["role"] = "Agent";
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/auth/register", duplicate)).StatusCode);
            var login = await Data(await client.PostAsJsonAsync("/api/v1/auth/login", new { countryId = 105, mobile, password = "1234" }));
            Assert.Equal(canonical, login["user"]!["mobile"]!.GetValue<string>());
            Assert.Equal("Client", login["user"]!["role"]!.GetValue<string>()); Assert.Null(login["user"]!["email"]);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Common/v1/Countries/Select/99999")).StatusCode);
    }

    [Fact]
    public async Task SameInternationalNumber_IsUniqueEvenWhenCountriesShareACallingCode_AndEmailIsRejected()
    {
        using var client = factory.CreateBrowser();
        var countries = (await Data(await client.GetAsync("/Common/v1/Countries/List"))).AsArray();
        int canada = countries.Single(country => country!["isoCode"]!.GetValue<string>() == "CA")!["id"]!.GetValue<int>();
        await Data(await client.PostAsJsonAsync("/api/v1/auth/register", Registration(236, "4155550123")));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/auth/register", Registration(canada, "+14155550123"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/register", Registration(236, TestPhones.Next()))).StatusCode);
        var oldPayload = Registration(105, TestPhones.Next()); oldPayload["email"] = "removed@example.test";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/register", oldPayload)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "removed@example.test", password = "1234" })).StatusCode);
    }

    private static JsonObject Registration(int countryId, string mobile) => new()
    { ["countryId"] = countryId, ["mobile"] = mobile, ["fullName"] = "Mobile test", ["password"] = "1234", ["confirmPassword"] = "1234", ["role"] = "Client" };
    private static async Task<JsonNode> Data(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<JsonNode>())!["data"]!;
    }
}
