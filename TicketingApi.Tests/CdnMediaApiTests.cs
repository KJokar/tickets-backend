using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Database.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ticketing.Media;
using Xunit;

namespace TicketingApi.Tests;

public sealed class CdnMediaApiTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private const string Root = "/Tickets/v1/Tickets";
    private MediaTokens Tokens => factory.Services.GetRequiredService<MediaTokens>();
    private MediaGrant Receipt(int userId, string category = "MessageAttachment", int? ticketId = null) =>
        new("receipt", userId, category, DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(), Guid.NewGuid(), ticketId,
            FileName: "photo.png", ContentType: "image/png", Size: 68, Url: "/users/avatars/example.png");

    [Fact]
    public async Task Transfer_RevokesPreviouslyIssuedAttachmentAccessForThePreviousAgent()
    {
        var owner = await User("Client"); using var client = owner.Client;
        var first = await User("Agent"); using var firstAgent = first.Client;
        var second = await User("Agent"); using var secondAgent = second.Client;
        var receipt = Receipt(owner.Id);
        int id = (await Data(await client.PostAsJsonAsync(Root + "/Create", new { title = "Transfer attachment", description = "Original attachment",
            attachments = new[] { new { receipt = Tokens.Issue(receipt) } } })))["id"]!.GetValue<int>();
        await Data(await firstAgent.PostAsJsonAsync($"{Root}/{id}/Accept", new { priority = "Medium" }));
        var message = Assert.Single((await Data(await client.GetAsync($"{Root}/{id}/Conversation")))["messages"]!.AsArray())!;
        string accessPath = $"{Root}/{id}/Messages/{message["id"]}/Attachments/{receipt.Id}/Access";
        var access = await Data(await firstAgent.GetAsync(accessPath));
        string readToken = Uri.UnescapeDataString(new Uri(access["url"]!.GetValue<string>()).Query[7..]);
        await Data(await firstAgent.PostAsJsonAsync($"{Root}/{id}/Transfer", new { agentId = second.Id }));
        using var anonymous = factory.CreateBrowser();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.PostAsJsonAsync("/Tickets/v1/Media/Authorize", new { token = readToken })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await firstAgent.GetAsync(accessPath)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await secondAgent.GetAsync(accessPath)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(accessPath)).StatusCode);
    }

    [Fact]
    public async Task Create_StoresOnlyCdnMetadata_AndOldReadUrlsLoseAccessAfterAnotherAgentAccepts()
    {
        var owner = await User("Client"); using var client = owner.Client;
        var first = await User("Agent"); using var firstAgent = first.Client;
        var second = await User("Agent"); using var secondAgent = second.Client;
        var receipt = Receipt(owner.Id);
        using var created = await client.PostAsJsonAsync(Root + "/Create", new { title = "CDN request", description = "First message", attachments = new[] { new { receipt = Tokens.Issue(receipt) } } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        int id = (await Data(created))["id"]!.GetValue<int>();
        var message = Assert.Single((await Data(await client.GetAsync($"{Root}/{id}/Conversation")))["messages"]!.AsArray())!;
        string accessPath = $"{Root}/{id}/Messages/{message["id"]}/Attachments/{receipt.Id}/Access";
        var access = await Data(await firstAgent.GetAsync(accessPath));
        string readToken = Uri.UnescapeDataString(new Uri(access["url"]!.GetValue<string>()).Query[7..]);
        using var anonymous = factory.CreateBrowser();
        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.PostAsJsonAsync("/Tickets/v1/Media/Authorize", new { token = readToken })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await secondAgent.PostAsJsonAsync($"{Root}/{id}/Accept", new { priority = "Medium" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.PostAsJsonAsync("/Tickets/v1/Media/Authorize", new { token = readToken })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await firstAgent.GetAsync(accessPath)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(accessPath)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<DatabaseContext>().MessageAttachments.SingleAsync(file => file.Id == receipt.Id);
        Assert.Null(row.Content); Assert.Equal(receipt.Id.ToString("N"), row.StorageKey);
    }

    [Fact]
    public async Task MediaOnlyMessage_IsIdempotent_AndDeletionRevokesPreviouslyIssuedUrl()
    {
        var owner = await User("Client"); using var client = owner.Client;
        int id = (await Data(await client.PostAsJsonAsync(Root + "/Create", new { title = "CDN request", description = "First message" })))["id"]!.GetValue<int>();
        var receipt = Receipt(owner.Id, ticketId: id);
        var body = new { body = "", clientMessageId = Guid.NewGuid(), attachments = new[] { new { receipt = Tokens.Issue(receipt) } } };
        var saved = await Data(await client.PostAsJsonAsync($"{Root}/{id}/Messages", body));
        var retry = await Data(await client.PostAsJsonAsync($"{Root}/{id}/Messages", body));
        Assert.Equal(saved["id"]!.GetValue<int>(), retry["id"]!.GetValue<int>());
        Assert.Single(saved["attachments"]!.AsArray());
        var access = await Data(await client.GetAsync($"{Root}/{id}/Messages/{saved["id"]}/Attachments/{receipt.Id}/Access"));
        string readToken = Uri.UnescapeDataString(new Uri(access["url"]!.GetValue<string>()).Query[7..]);
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"{Root}/{id}/Messages/{saved["id"]}")).StatusCode);
        using var anonymous = factory.CreateBrowser();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.PostAsJsonAsync("/Tickets/v1/Media/Authorize", new { token = readToken })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"{Root}/{id}/Messages", body)).StatusCode);
    }

    [Theory]
    [InlineData("tampered")]
    [InlineData("owner")]
    [InlineData("expired")]
    [InlineData("category")]
    [InlineData("ticket")]
    [InlineData("size")]
    public async Task InvalidReceipt_CannotCreateTicket(string invalid)
    {
        var owner = await User("Client"); using var client = owner.Client;
        MediaGrant receipt = Receipt(owner.Id);
        receipt = invalid switch
        {
            "owner" => receipt with { UserId = owner.Id + 10000 },
            "expired" => receipt with { Expires = 1 },
            "category" => receipt with { Category = "UserAvatar" },
            "ticket" => receipt with { TicketId = 2147483647 },
            "size" => receipt with { Size = 20 * 1024 * 1024 + 1 },
            _ => receipt
        };
        string token = Tokens.Issue(receipt) + (invalid == "tampered" ? "bad" : "");
        using var response = await client.PostAsJsonAsync(Root + "/Create", new { title = "Invalid file", description = "First message", attachments = new[] { new { receipt = token } } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await Data(await client.GetAsync(Root + "/List"))).AsArray());
    }

    [Theory]
    [InlineData("Client")]
    [InlineData("Agent")]
    public async Task BothRoles_CanChangeAndRemoveOwnAvatar_ButCannotUseAnotherUsersReceipt(string role)
    {
        var owner = await User(role); using var client = owner.Client;
        var receipt = Receipt(owner.Id, "UserAvatar");
        using var granted = await client.PostAsJsonAsync("/Tickets/v1/Media/Uploads", new { category = "UserAvatar" });
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        Assert.NotNull(Tokens.Validate((await Data(granted))["token"]!.GetValue<string>(), "upload"));
        var profile = await Data(await client.PutAsJsonAsync("/Tickets/v1/Profile/Avatar", new { receipt = Tokens.Issue(receipt) }));
        Assert.Equal(receipt.Url, profile["avatarUrl"]!.GetValue<string>());
        var stranger = await User(role); using var other = stranger.Client;
        Assert.Equal(HttpStatusCode.BadRequest, (await other.PutAsJsonAsync("/Tickets/v1/Profile/Avatar", new { receipt = Tokens.Issue(receipt) })).StatusCode);
        Assert.Equal(receipt.Url, (await Data(await client.GetAsync("/Tickets/v1/Profile")))["avatarUrl"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync("/Tickets/v1/Profile/Avatar")).StatusCode);
        Assert.Null((await Data(await client.GetAsync("/Tickets/v1/Profile")))["avatarUrl"]);
    }

    [Theory]
    [InlineData("Client")]
    [InlineData("Agent")]
    public async Task ProfileEdit_ChangesOnlyOwnName_PreservesMobileRoleAndAvatar_AndPersistsOnLogin(string role)
    {
        var owner = await User(role); using var client = owner.Client;
        var stranger = await User(role); using var other = stranger.Client;
        var receipt = Receipt(owner.Id, "UserAvatar");
        var before = await Data(await client.PutAsJsonAsync("/Tickets/v1/Profile/Avatar", new { receipt = Tokens.Issue(receipt) }));
        var updated = await Data(await client.PatchAsJsonAsync("/Tickets/v1/Profile", new { fullName = "  نام جدید کاربر  " }));
        Assert.Equal("نام جدید کاربر", updated["fullName"]!.GetValue<string>());
        foreach (string property in new[] { "id", "mobile", "role", "permissions", "avatarUrl" })
            Assert.True(JsonNode.DeepEquals(before[property], updated[property]), property);
        Assert.Equal("CDN API Test", (await Data(await other.GetAsync("/Tickets/v1/Profile")))["fullName"]!.GetValue<string>());
        Assert.Equal("نام جدید کاربر", (await Data(await client.GetAsync("/Tickets/v1/Profile")))["fullName"]!.GetValue<string>());
        using var anonymous = factory.CreateBrowser();
        var login = await Data(await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { mobile = before["mobile"]!.GetValue<string>(), countryId = 105, password = "1234" }));
        Assert.Equal("نام جدید کاربر", login["user"]!["fullName"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PatchAsJsonAsync("/Tickets/v1/Profile", new { fullName = "Anonymous" })).StatusCode);
    }

    [Fact]
    public async Task ProfileEdit_RejectsInvalidNamesAndChangesToOtherFields()
    {
        var owner = await User("Client"); using var client = owner.Client;
        foreach (string name in new[] { "", " ", "  a  ", new string('a', 151) })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync("/Tickets/v1/Profile", new { fullName = name })).StatusCode);
        foreach (string property in new[] { "mobile", "role", "id", "avatarUrl" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync("/Tickets/v1/Profile", new Dictionary<string, object> { ["fullName"] = "Changed name", [property] = "forbidden" })).StatusCode);
        Assert.Equal("CDN API Test", (await Data(await client.GetAsync("/Tickets/v1/Profile")))["fullName"]!.GetValue<string>());
    }

    [Fact]
    public async Task AgentCannotUploadIntoUnacceptedOrClosedChat()
    {
        var owner = await User("Client"); using var client = owner.Client;
        var support = await User("Agent"); using var agent = support.Client;
        int id = (await Data(await client.PostAsJsonAsync(Root + "/Create", new { title = "CDN request", description = "First message" })))["id"]!.GetValue<int>();
        Assert.Equal(HttpStatusCode.Conflict, (await agent.PostAsJsonAsync("/Tickets/v1/Media/Uploads", new { category = "MessageAttachment", ticketId = id })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await agent.PostAsJsonAsync($"{Root}/{id}/Accept", new { priority = "Medium" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await agent.PostAsJsonAsync("/Tickets/v1/Media/Uploads", new { category = "MessageAttachment", ticketId = id })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await agent.PatchAsJsonAsync($"{Root}/{id}/Status", new { status = "Closed" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/Tickets/v1/Media/Uploads", new { category = "MessageAttachment", ticketId = id })).StatusCode);
    }

    private async Task<(HttpClient Client, int Id)> User(string role)
    {
        var client = factory.CreateBrowser();
        var data = await Data(await client.PostAsJsonAsync("/api/v1/auth/register", new { fullName = "CDN API Test", mobile = TestPhones.Next(), countryId = 105, password = "1234", confirmPassword = "1234", role }));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", data["token"]!.GetValue<string>());
        return (client, data["user"]!["id"]!.GetValue<int>());
    }
    private static async Task<JsonNode> Data(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonNode>())!["data"]!;
    }
}
