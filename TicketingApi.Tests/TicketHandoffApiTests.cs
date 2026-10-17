using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Database.Context;
using Database.Enums.Identity;
using Database.Model.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Repository.Identity;
using Repository.Tickets;
using Service.Tickets;
using ViewModels.Tickets;
using Xunit;

namespace TicketingApi.Tests;

public sealed class TicketHandoffApiTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private const string Root = "/Tickets/v1/Tickets";

    [Fact]
    public async Task DirectTransfer_PreservesHistoryAndPriority_AndRevokesThePreviousAgent()
    {
        using var owner = factory.CreateBrowser(); await Register(owner, "Client");
        using var first = factory.CreateBrowser(); int firstId = await Register(first, "Agent");
        using var second = factory.CreateBrowser(); int secondId = await Register(second, "Agent");
        int id = await Create(owner);
        var accepted = await Accept(first, id);
        int oldReply = await Message(first, id, "Original support answer");
        var transferred = await Data(await first.PostAsJsonAsync($"{Root}/{id}/Transfer", new { agentId = secondId }));
        Assert.Equal(secondId, transferred["assignedToId"]!.GetValue<int>());
        Assert.Equal("InProgress", transferred["status"]!.GetValue<string>());
        Assert.Equal("High", transferred["priority"]!.GetValue<string>());
        Assert.Equal(accepted["acceptedAt"]!.GetValue<string>(), transferred["acceptedAt"]!.GetValue<string>());
        var history = await Data(await second.GetAsync($"{Root}/{id}/Conversation"));
        Assert.Equal(secondId, history["agent"]!["id"]!.GetValue<int>());
        Assert.Equal(2, history["messages"]!.AsArray().Count);
        Assert.Equal(firstId, history["messages"]![1]!["author"]!["id"]!.GetValue<int>());
        foreach (string path in new[] { $"/Select/{id}", $"/{id}/Conversation", $"/{id}/TransferAgents" })
            Assert.Equal(HttpStatusCode.NotFound, (await first.GetAsync(Root + path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await first.PostAsJsonAsync($"{Root}/{id}/Messages", new { body = "Forbidden", clientMessageId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await first.PatchAsJsonAsync($"{Root}/{id}/Messages/{oldReply}", new { body = "Forbidden edit" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await first.PostAsync($"{Root}/{id}/Release", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await first.PatchAsJsonAsync($"{Root}/{id}/Priority", new { priority = "Low" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await first.PatchAsJsonAsync($"{Root}/{id}/Status", new { status = "Closed" })).StatusCode);
        await Message(second, id, "New support answer");
        var clientView = await Data(await owner.GetAsync($"{Root}/Select/{id}"));
        Assert.Null(clientView["priority"]);
        Assert.Equal(secondId, clientView["assignedTo"]!["id"]!.GetValue<int>());
        Assert.Equal("Answered", clientView["status"]!.GetValue<string>());
        Assert.Equal(3, (await Data(await owner.GetAsync($"{Root}/{id}/Conversation")))["messages"]!.AsArray().Count);
    }

    [Fact]
    public async Task Release_ReopensQueue_PreservesConversation_AndDoesNotRestoreCustomerDeletionRights()
    {
        using var owner = factory.CreateBrowser(); await Register(owner, "Client");
        using var first = factory.CreateBrowser(); await Register(first, "Agent");
        using var second = factory.CreateBrowser(); int secondId = await Register(second, "Agent");
        int id = await Create(owner); var accepted = await Accept(first, id);
        int reply = await Message(first, id, "Previous response");
        var released = await Data(await first.PostAsync($"{Root}/{id}/Release", null));
        Assert.Equal("Open", released["status"]!.GetValue<string>());
        Assert.Null(released["assignedToId"]); Assert.Null(released["assignedTo"]);
        Assert.Equal(accepted["acceptedAt"]!.GetValue<string>(), released["acceptedAt"]!.GetValue<string>());
        var queue = (await Data(await second.GetAsync($"{Root}/Unassigned"))).AsArray();
        Assert.Contains(queue, item => item!["id"]!.GetValue<int>() == id);
        Assert.Equal(HttpStatusCode.Conflict, (await first.PostAsJsonAsync($"{Root}/{id}/Messages", new { body = "Not assigned", clientMessageId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await first.DeleteAsync($"{Root}/{id}/Messages/{reply}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PatchAsJsonAsync($"{Root}/{id}", new { title = "Cannot erase an established conversation" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.DeleteAsync($"{Root}/{id}")).StatusCode);
        await Message(owner, id, "Customer can continue while awaiting another agent");
        var next = await Accept(second, id);
        Assert.Equal(secondId, next["assignedToId"]!.GetValue<int>());
        Assert.Equal(accepted["acceptedAt"]!.GetValue<string>(), next["acceptedAt"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Conflict, (await first.PostAsJsonAsync($"{Root}/{id}/Accept", new { priority = "Low" })).StatusCode);
        Assert.Equal(3, (await Data(await second.GetAsync($"{Root}/{id}/Conversation")))["messages"]!.AsArray().Count);
    }

    [Fact]
    public async Task Handoff_RequiresAnActiveAssignedAgent_AndAnEligibleDifferentTarget()
    {
        using var owner = factory.CreateBrowser(); int ownerId = await Register(owner, "Client");
        using var first = factory.CreateBrowser(); int firstId = await Register(first, "Agent");
        using var second = factory.CreateBrowser(); int secondId = await Register(second, "Agent");
        using var inactive = factory.CreateBrowser(); int inactiveId = await Register(inactive, "Agent");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            (await db.Users.FindAsync(inactiveId))!.Status = UserStatus.Suspended;
            await db.SaveChangesAsync();
        }
        int id = await Create(owner);
        Assert.Equal(HttpStatusCode.Conflict, (await first.PostAsync($"{Root}/{id}/Release", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await first.PostAsJsonAsync($"{Root}/{id}/Transfer", new { agentId = secondId })).StatusCode);
        await Accept(first, id);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync($"{Root}/{id}/Transfer", new { agentId = secondId })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsync($"{Root}/{id}/Release", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync($"{Root}/{id}/TransferAgents")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await second.PostAsync($"{Root}/{id}/Release", null)).StatusCode);
        foreach (int target in new[] { 0, firstId, ownerId, inactiveId, int.MaxValue })
            Assert.Equal(HttpStatusCode.BadRequest, (await first.PostAsJsonAsync($"{Root}/{id}/Transfer", new { agentId = target })).StatusCode);
        await Data(await first.PatchAsJsonAsync($"{Root}/{id}/Status", new { status = "Closed" }));
        Assert.Equal(HttpStatusCode.Conflict, (await first.PostAsync($"{Root}/{id}/Release", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await first.PostAsJsonAsync($"{Root}/{id}/Transfer", new { agentId = secondId })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await first.GetAsync($"{Root}/{id}/TransferAgents")).StatusCode);
    }

    [Fact]
    public async Task DestinationList_IsSearchablePaginated_AndContainsOnlyOtherActiveAgents()
    {
        using var owner = factory.CreateBrowser(); await Register(owner, "Client");
        using var first = factory.CreateBrowser(); int firstId = await Register(first, "Agent", "Candidate current");
        int id = await Create(owner); await Accept(first, id);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            for (int index = 0; index < 14; index++)
                db.Users.Add(new User { FullName = $"Candidate {index:D2}", Mobile = TestPhones.Next(), CountryId = 105,
                    PasswordHash = "not-used-for-authentication", Role = index == 13 ? UserRole.Client : UserRole.Agent,
                    Status = index == 12 ? UserStatus.Suspended : UserStatus.Active });
            await db.SaveChangesAsync();
        }
        using var response = await first.GetAsync($"{Root}/{id}/TransferAgents?search=Candidate&page=1&perPage=10");
        var payload = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(12, payload["pagination"]!["totalItems"]!.GetValue<int>());
        Assert.Equal(10, payload["data"]!.AsArray().Count);
        foreach (var agent in payload["data"]!.AsArray())
        {
            Assert.NotEqual(firstId, agent!["id"]!.GetValue<int>());
            Assert.Equal("Agent", agent["role"]!.GetValue<string>()); Assert.StartsWith("+", agent["mobile"]!.GetValue<string>());
        }
        Assert.Equal(2, (await Data(await first.GetAsync($"{Root}/{id}/TransferAgents?search=Candidate&page=2&perPage=10"))).AsArray().Count);
        Assert.Single((await Data(await first.GetAsync($"{Root}/{id}/TransferAgents?search=Candidate%2007"))).AsArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await first.GetAsync($"{Root}/{id}/TransferAgents?page=0")).StatusCode);
    }

    [Fact]
    public async Task DestinationList_DisambiguatesIdenticalNamesWithUniqueMobileNumbers()
    {
        const string name = "Same name";
        using var owner = factory.CreateBrowser(); await Register(owner, "Client");
        using var first = factory.CreateBrowser(); await Register(first, "Agent");
        using var second = factory.CreateBrowser(); await Register(second, "Agent", name);
        using var third = factory.CreateBrowser(); await Register(third, "Agent", name);
        int id = await Create(owner); await Accept(first, id);
        var matches = (await Data(await first.GetAsync($"{Root}/{id}/TransferAgents?search=Same%20name"))).AsArray();
        Assert.Equal(2, matches.Count);
        string[] numbers = matches.Select(agent => agent!["mobile"]!.GetValue<string>()).ToArray();
        Assert.Equal(2, numbers.Distinct().Count());
        var byNumber = (await Data(await first.GetAsync($"{Root}/{id}/TransferAgents?search={Uri.EscapeDataString(numbers[0])}"))).AsArray();
        Assert.Single(byNumber); Assert.Equal(numbers[0], byNumber[0]!["mobile"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentTransferAndRelease_CommitOnlyOneAssignment(bool transferWins)
    {
        using var owner = factory.CreateBrowser(); await Register(owner, "Client");
        using var first = factory.CreateBrowser(); int firstId = await Register(first, "Agent");
        using var second = factory.CreateBrowser(); int secondId = await Register(second, "Agent");
        int id = await Create(owner); await Accept(first, id);
        using var winnerScope = factory.Services.CreateScope();
        using var loserScope = factory.Services.CreateScope();
        var winner = winnerScope.ServiceProvider.GetRequiredService<ITicketsService>();
        var loserDb = loserScope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var racingSave = new RacingSave(loserDb, async () =>
        {
            var result = transferWins ? await winner.TransferAsync(firstId, UserRole.Agent, id, new() { AgentId = secondId })
                : await winner.ReleaseAsync(firstId, UserRole.Agent, id);
            Assert.Equal(200, result.StatusCode);
        });
        var loser = new TicketsService(loserScope.ServiceProvider.GetRequiredService<ITicketsRepository>(), racingSave,
            loserScope.ServiceProvider.GetRequiredService<ITicketEvents>(), loserScope.ServiceProvider.GetRequiredService<IUsersRepository>(),
            loserScope.ServiceProvider.GetRequiredService<IReplyRemindersService>());
        var failed = transferWins ? await loser.ReleaseAsync(firstId, UserRole.Agent, id)
            : await loser.TransferAsync(firstId, UserRole.Agent, id, new() { AgentId = secondId });
        Assert.Equal(409, failed.StatusCode);
        var stored = await Data(await owner.GetAsync($"{Root}/Select/{id}"));
        Assert.Equal(transferWins ? "InProgress" : "Open", stored["status"]!.GetValue<string>());
        Assert.Equal(transferWins ? secondId : (int?)null, stored["assignedToId"]?.GetValue<int>());
    }

    [Fact]
    public async Task ReadingPreviousAgentHistory_DoesNotMarkTheirReplyAsSeenByTheCustomer()
    {
        using var owner = factory.CreateBrowser(); await Register(owner, "Client");
        using var first = factory.CreateBrowser(); await Register(first, "Agent");
        using var second = factory.CreateBrowser(); int secondId = await Register(second, "Agent");
        int id = await Create(owner); await Accept(first, id);
        int reply = await Message(first, id, "Historical support response");
        await Data(await first.PostAsJsonAsync($"{Root}/{id}/Transfer", new { agentId = secondId }));
        var receipt = await Data(await second.PostAsJsonAsync($"{Root}/{id}/Read", new { lastReadMessageId = reply }));
        Assert.Null(receipt["messageId"]);
        var history = await Data(await owner.GetAsync($"{Root}/{id}/Conversation"));
        Assert.False(history["messages"]!.AsArray().Single(message => message!["id"]!.GetValue<int>() == reply)!["isRead"]!.GetValue<bool>());
        await Data(await owner.PostAsJsonAsync($"{Root}/{id}/Read", new { lastReadMessageId = reply }));
        history = await Data(await second.GetAsync($"{Root}/{id}/Conversation"));
        Assert.True(history["messages"]!.AsArray().Single(message => message!["id"]!.GetValue<int>() == reply)!["isRead"]!.GetValue<bool>());
    }

    private sealed class RacingSave(DatabaseContext db, Func<Task> race) : IDatabaseUnitOfWork
    {
        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            await race();
            return await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task<int> Register(HttpClient client, string role, string fullName = "Handoff tests")
    {
        var data = await Data(await client.PostAsJsonAsync("/api/v1/auth/register", new
        { fullName, countryId = 105, mobile = TestPhones.Next(), password = "x", confirmPassword = "x", role }));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", data["token"]!.GetValue<string>());
        return data["user"]!["id"]!.GetValue<int>();
    }
    private static async Task<int> Create(HttpClient client) =>
        (await Data(await client.PostAsJsonAsync(Root + "/Create", new { title = "Handoff request", description = "Original customer request" })))["id"]!.GetValue<int>();
    private static Task<JsonNode> Accept(HttpClient client, int id) => Data(client.PostAsJsonAsync($"{Root}/{id}/Accept", new { priority = "High" }));
    private static async Task<int> Message(HttpClient client, int id, string body) =>
        (await Data(await client.PostAsJsonAsync($"{Root}/{id}/Messages", new { body, clientMessageId = Guid.NewGuid() })))["id"]!.GetValue<int>();
    private static async Task<JsonNode> Data(Task<HttpResponseMessage> response) => await Data(await response);
    private static async Task<JsonNode> Data(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<JsonNode>())!["data"]!;
    }
}
