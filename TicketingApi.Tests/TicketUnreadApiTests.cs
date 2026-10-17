using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace TicketingApi.Tests;

public sealed class TicketUnreadApiTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private const string Url = "/Tickets/v1/Tickets";

    [Fact]
    public async Task Unread_IsIncomingOnly_AndClearsWhenLatestIncomingMessageIsSeen()
    {
        using var owner = factory.CreateBrowser(); await Register(owner, "Client");
        using var agent = factory.CreateBrowser(); await Register(agent, "Agent");
        using var other = factory.CreateBrowser(); await Register(other, "Client");
        int id = (await Data(await owner.PostAsJsonAsync($"{Url}/Create", new { title = "Unread test", description = "First client message" })))["id"]!.GetValue<int>();
        int first = (await Data(await agent.GetAsync($"{Url}/{id}/Conversation")))["messages"]![0]!["id"]!.GetValue<int>();
        Assert.False(await Unread(owner, id));
        Assert.True(await Unread(agent, id, "Unassigned"));
        Assert.Empty((await Data(await other.GetAsync($"{Url}/List"))).AsArray());
        Assert.Equal(HttpStatusCode.OK, (await agent.PostAsJsonAsync($"{Url}/{id}/Accept", new { priority = "Medium" })).StatusCode);
        await Read(agent, id, first);
        Assert.False(await Unread(agent, id));
        int answer = await Message(agent, id, "Agent answer");
        Assert.False(await Unread(agent, id));
        Assert.True(await Unread(owner, id));
        int second = await Message(agent, id, "Second answer");
        await Message(owner, id, "Continue the active conversation");
        Assert.True(await Unread(owner, id));
        // The inbox marker tracks new conversation activity; individual read checks stay granular.
        await Read(owner, id, second);
        Assert.False(await Unread(owner, id));
        Assert.False((await Data(await owner.GetAsync($"{Url}/Select/{id}")))["hasUnreadMessages"]!.GetValue<bool>());
        var history = (await Data(await owner.GetAsync($"{Url}/{id}/Conversation")))["messages"]!.AsArray();
        Assert.False(history.Single(message => message!["id"]!.GetValue<int>() == answer)!["isRead"]!.GetValue<bool>());
        await Read(owner, id, answer);
        Assert.False(await Unread(owner, id));
        await Message(owner, id, "Client followup");
        Assert.False(await Unread(owner, id));
        Assert.True(await Unread(agent, id));
    }

    [Fact]
    public async Task ReadingQueueByAnotherAgent_DoesNotClearAssignedAgentsInbox()
    {
        using var owner = factory.CreateBrowser(); await Register(owner, "Client");
        using var firstAgent = factory.CreateBrowser(); await Register(firstAgent, "Agent");
        using var assignedAgent = factory.CreateBrowser(); await Register(assignedAgent, "Agent");
        int id = (await Data(await owner.PostAsJsonAsync($"{Url}/Create", new { title = "Personal inbox", description = "Message to support" })))["id"]!.GetValue<int>();
        int first = (await Data(await firstAgent.GetAsync($"{Url}/{id}/Conversation")))["messages"]![0]!["id"]!.GetValue<int>();
        await Read(firstAgent, id, first);
        Assert.False(await Unread(firstAgent, id));
        Assert.True(await Unread(assignedAgent, id));
        await assignedAgent.PostAsJsonAsync($"{Url}/{id}/Accept", new { priority = "Medium" });
        Assert.True(await Unread(assignedAgent, id));
        await Read(assignedAgent, id, first);
        Assert.False(await Unread(assignedAgent, id));
        int ownAnswer = await Message(assignedAgent, id, "My answer");
        await Message(owner, id, "New incoming message");
        await Read(assignedAgent, id, ownAnswer);
        Assert.True(await Unread(assignedAgent, id));
    }

    [Fact]
    public async Task Marker_HidesClosedTicketsAndDeletedMessages_WithoutChangingIndividualReadReceipts()
    {
        using var owner = factory.CreateBrowser(); await Register(owner, "Client");
        using var agent = factory.CreateBrowser(); await Register(agent, "Agent");
        int id = (await Data(await owner.PostAsJsonAsync($"{Url}/Create", new { title = "Unread cleanup", description = "Initial message" })))["id"]!.GetValue<int>();
        await agent.PostAsJsonAsync($"{Url}/{id}/Accept", new { priority = "Low" });
        int answer = await Message(agent, id, "Answer to delete");
        Assert.True(await Unread(owner, id));
        await Message(owner, id, "New followup");
        Assert.True(await Unread(owner, id));
        Assert.Equal(HttpStatusCode.OK, (await agent.DeleteAsync($"{Url}/{id}/Messages/{answer}")).StatusCode);
        Assert.False(await Unread(owner, id));
        int last = await Message(agent, id, "Final answer");
        Assert.True(await Unread(owner, id));
        await Message(owner, id, "Last client followup");
        Assert.True(await Unread(owner, id));
        Assert.True(await Unread(agent, id));
        await agent.PatchAsJsonAsync($"{Url}/{id}/Status", new { status = "Closed" });
        Assert.False(await Unread(owner, id));
        Assert.False(await Unread(agent, id));
        var history = (await Data(await owner.GetAsync($"{Url}/{id}/Conversation")))["messages"]!.AsArray();
        Assert.False(history.Single(message => message!["id"]!.GetValue<int>() == last)!["isRead"]!.GetValue<bool>());
        await Read(owner, id, last);
        Assert.False(await Unread(owner, id));
    }

    [Fact]
    public async Task ClientAnsweredMarker_ClearsAfterReading_AndReturnsOnlyForNewIncomingAnswer()
    {
        using var owner = factory.CreateBrowser(); await Register(owner, "Client");
        using var agent = factory.CreateBrowser(); await Register(agent, "Agent");
        int id = (await Data(await owner.PostAsJsonAsync($"{Url}/Create", new { title = "Client inbox", description = "Please help" })))["id"]!.GetValue<int>();
        await agent.PostAsJsonAsync($"{Url}/{id}/Accept", new { priority = "Medium" });
        int answer = await Message(agent, id, "First response");
        Assert.True(await Unread(owner, id));
        var answeredList = (await Data(await owner.GetAsync($"{Url}/List?status=Answered"))).AsArray();
        Assert.True(Assert.Single(answeredList)!["hasUnreadMessages"]!.GetValue<bool>());
        // Loading history alone must not mark messages as read.
        await owner.GetAsync($"{Url}/{id}/Conversation");
        Assert.True(await Unread(owner, id));
        await Read(owner, id, answer);
        var seen = await Data(await owner.GetAsync($"{Url}/Select/{id}"));
        Assert.Equal("Answered", seen["status"]!.GetValue<string>());
        Assert.False(seen["hasUnreadMessages"]!.GetValue<bool>());
        Assert.False(await Unread(owner, id));
        int latest = await Message(agent, id, "Additional response");
        await Read(owner, id, answer);
        Assert.True(await Unread(owner, id));
        await Read(owner, id, latest);
        Assert.False(await Unread(owner, id));
    }

    private static async Task<bool> Unread(HttpClient client, int id, string list = "List") =>
        (await Data(await client.GetAsync($"{Url}/{list}?perPage=100"))).AsArray().Single(item => item!["id"]!.GetValue<int>() == id)!["hasUnreadMessages"]!.GetValue<bool>();
    private static async Task Read(HttpClient client, int id, int messageId) =>
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"{Url}/{id}/Read", new { lastReadMessageId = messageId })).StatusCode);
    private static async Task<int> Message(HttpClient client, int id, string body) =>
        (await Data(await client.PostAsJsonAsync($"{Url}/{id}/Messages", new { body, clientMessageId = Guid.NewGuid() })))["id"]!.GetValue<int>();
    private static async Task Register(HttpClient client, string role)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/auth/register", new { fullName = "Unread Test", mobile = TestPhones.Next(), countryId = 105, password = "1234", confirmPassword = "1234", role });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await Data(response))["token"]!.GetValue<string>());
    }
    private static async Task<JsonNode> Data(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"HTTP {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<JsonNode>())!["data"]!;
    }
}
