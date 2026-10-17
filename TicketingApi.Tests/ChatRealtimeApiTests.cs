using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Database.Enums.Identity;
using Microsoft.Extensions.DependencyInjection;
using Service.Tickets;
using Xunit;

namespace TicketingApi.Tests;

public sealed class ChatRealtimeApiTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private const string Url = "/Tickets/v1/Tickets";

    [Fact]
    public async Task ReadCursor_IsDurableAndMonotonic_WithoutChangingTicketStatus()
    {
        using var client = factory.CreateBrowser(); int clientId = await Register(client, "Client");
        using var agent = factory.CreateBrowser(); int agentId = await Register(agent, "Agent");
        int id = await Create(client);
        using var accepted = await agent.PostAsJsonAsync($"{Url}/{id}/Accept", new { priority = "Medium" });
        int first = await Message(agent, id, "First answer");
        int second = await Message(agent, id, "Second answer");
        var before = await Data(await client.GetAsync($"{Url}/Select/{id}"));
        foreach (int cursor in new[] { first, second, first, second })
        {
            using var response = await client.PostAsJsonAsync($"{Url}/{id}/Read", new { lastReadMessageId = cursor });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var receipt = await Data(response);
            Assert.Equal(clientId, receipt["userId"]!.GetValue<int>());
            Assert.True(receipt["lastReadMessageId"]!.GetValue<int>() >= cursor);
        }
        var own = await Data(await client.GetAsync($"{Url}/{id}/Conversation"));
        var peer = await Data(await agent.GetAsync($"{Url}/{id}/Conversation"));
        Assert.Equal(second, own["lastReadMessageId"]!.GetValue<int>());
        Assert.Equal(second, peer["peerLastReadMessageId"]!.GetValue<int>());
        var after = await Data(await client.GetAsync($"{Url}/Select/{id}"));
        Assert.Equal("Answered", after["status"]!.GetValue<string>());
        Assert.Equal(before["updatedAt"]!.GetValue<string>(), after["updatedAt"]!.GetValue<string>());
        int followup = await Message(client, id, "Followup");
        using var agentRead = await agent.PostAsJsonAsync($"{Url}/{id}/Read", new { lastReadMessageId = followup });
        Assert.Equal(agentId, (await Data(agentRead))["userId"]!.GetValue<int>());
        using var closed = await agent.PatchAsJsonAsync($"{Url}/{id}/Status", new { status = "Closed" });
        using var readClosedHistory = await client.PostAsJsonAsync($"{Url}/{id}/Read", new { lastReadMessageId = followup });
        Assert.Equal(HttpStatusCode.OK, readClosedHistory.StatusCode);
        var restored = await Data(await client.GetAsync($"{Url}/{id}/Conversation"));
        Assert.Equal(followup, restored["peerLastReadMessageId"]!.GetValue<int>());
    }

    [Fact]
    public async Task ReadCursor_RejectsOtherParticipantsAndMessagesFromAnotherTicket()
    {
        using var client = factory.CreateBrowser(); await Register(client, "Client");
        using var agent = factory.CreateBrowser(); await Register(agent, "Agent");
        using var outsider = factory.CreateBrowser(); await Register(outsider, "Agent");
        int id = await Create(client), other = await Create(client);
        using var accepted = await agent.PostAsJsonAsync($"{Url}/{id}/Accept", new { priority = "Low" });
        using var acceptedOther = await agent.PostAsJsonAsync($"{Url}/{other}/Accept", new { priority = "Low" });
        int message = await Message(agent, id, "Private");
        int otherMessage = await Message(agent, other, "Another ticket");
        using var denied = await outsider.PostAsJsonAsync($"{Url}/{id}/Read", new { lastReadMessageId = message });
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var wrongTicket = await client.PostAsJsonAsync($"{Url}/{id}/Read", new { lastReadMessageId = otherMessage });
        Assert.Equal(HttpStatusCode.NotFound, wrongTicket.StatusCode);
        using var invalid = await client.PostAsJsonAsync($"{Url}/{id}/Read", new { lastReadMessageId = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var conversation = await Data(await agent.GetAsync($"{Url}/{id}/Conversation"));
        Assert.Null(conversation["peerLastReadMessageId"]);
    }

    [Fact]
    public async Task Typing_IsPrivateTransientAndOnlyAllowedInActiveAcceptedChat()
    {
        using var client = factory.CreateBrowser(); int clientId = await Register(client, "Client");
        using var agent = factory.CreateBrowser(); int agentId = await Register(agent, "Agent");
        using var outsider = factory.CreateBrowser(); int outsiderId = await Register(outsider, "Agent");
        int id = await Create(client);
        using var scope = factory.Services.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<ITicketsService>();
        Assert.Equal(409, (await service.SetTypingAsync(clientId, UserRole.Client, id, true)).StatusCode);
        using var accepted = await agent.PostAsJsonAsync($"{Url}/{id}/Accept", new { priority = "Medium" });
        Assert.Equal(404, (await service.SetTypingAsync(outsiderId, UserRole.Agent, id, true)).StatusCode);
        var typing = await service.SetTypingAsync(clientId, UserRole.Client, id, true);
        Assert.Equal(200, typing.StatusCode);
        Assert.Equal(clientId, typing.Data!.UserId);
        Assert.True(typing.Data.IsTyping);
        Assert.InRange(typing.Data.ExpiresAt, DateTime.UtcNow, DateTime.UtcNow.AddSeconds(6));
        Assert.False((await service.SetTypingAsync(agentId, UserRole.Agent, id, false)).Data!.IsTyping);
        using var closed = await agent.PatchAsJsonAsync($"{Url}/{id}/Status", new { status = "Closed" });
        Assert.Equal(409, (await service.SetTypingAsync(clientId, UserRole.Client, id, true)).StatusCode);
    }

    private static async Task<int> Register(HttpClient client, string role)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/auth/register", new { fullName = "Realtime Test", mobile = TestPhones.Next(), countryId = 105, password = "1234", confirmPassword = "1234", role });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); var data = await Data(response);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", data["token"]!.GetValue<string>());
        return data["user"]!["id"]!.GetValue<int>();
    }
    private static async Task<int> Create(HttpClient client) { using var response = await client.PostAsJsonAsync($"{Url}/Create", new { title = "Realtime test", description = "Testing a private conversation" }); return (await Data(response))["id"]!.GetValue<int>(); }
    private static async Task<int> Message(HttpClient client, int id, string body) { using var response = await client.PostAsJsonAsync($"{Url}/{id}/Messages", new { body, clientMessageId = Guid.NewGuid() }); Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await Data(response))["id"]!.GetValue<int>(); }
    private static async Task<JsonNode> Data(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<JsonNode>())!["data"]!;
}
