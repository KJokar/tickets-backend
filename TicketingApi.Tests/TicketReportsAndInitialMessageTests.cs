using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Database.Context;
using Database.Model.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Database.Enums.Identity;
using Repository.Tickets;
using Service.Tickets;
using ViewModels.Tickets;
using Xunit;

namespace TicketingApi.Tests;

public sealed class TicketReportsAndInitialMessageTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private const string Url = "/Tickets/v1/Tickets";

    [Fact]
    public async Task FirstMessage_IsARegularMessage_AndTicketEditChangesOnlyTitle()
    {
        using var owner = factory.CreateBrowser(); await Register(owner, "Client");
        int id = await Create(owner);
        var ticket = await Data(await owner.GetAsync($"{Url}/Select/{id}"));
        Assert.Null(ticket["description"]);
        var conversation = await Data(await owner.GetAsync($"{Url}/{id}/Conversation"));
        var first = Assert.Single(conversation["messages"]!.AsArray())!;
        Assert.Equal("Initial message", first["body"]!.GetValue<string>());
        int messageId = first["id"]!.GetValue<int>();
        using var title = await owner.PatchAsJsonAsync($"{Url}/{id}", new { title = "Changed title" });
        Assert.Equal(HttpStatusCode.OK, title.StatusCode);
        using var oldEdit = await owner.PatchAsJsonAsync($"{Url}/{id}", new { title = "Cannot bypass message rules", description = "Different body" });
        Assert.Equal(HttpStatusCode.BadRequest, oldEdit.StatusCode);
        using var editMessage = await owner.PatchAsJsonAsync($"{Url}/{id}/Messages/{messageId}", new { body = "Edited initial message" });
        Assert.Equal(HttpStatusCode.OK, editMessage.StatusCode);
        conversation = await Data(await owner.GetAsync($"{Url}/{id}/Conversation"));
        Assert.Equal("Edited initial message", conversation["messages"]![0]!["body"]!.GetValue<string>());
        using var deleteMessage = await owner.DeleteAsync($"{Url}/{id}/Messages/{messageId}");
        Assert.Equal(HttpStatusCode.OK, deleteMessage.StatusCode);
        Assert.Empty((await Data(await owner.GetAsync($"{Url}/{id}/Conversation")))["messages"]!.AsArray());
        using var deleteTicket = await owner.DeleteAsync($"{Url}/{id}");
        Assert.Equal(HttpStatusCode.OK, deleteTicket.StatusCode);
    }

    [Fact]
    public async Task ReadingMigratedFirstMessage_DoesNotMarkUnseenLaterMessagesRead()
    {
        using var owner = factory.CreateBrowser(); int ownerId = await Register(owner, "Client");
        using var agent = factory.CreateBrowser(); await Register(agent, "Agent");
        int id = await Create(owner);
        int restoredId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            // Backfilled messages have later IDs but keep their original, earlier timestamp.
            var restored = new ChatMessage { TicketId = id, AuthorUserId = ownerId, Body = "Backfilled first message",
                ClientMessageId = Guid.NewGuid(), CreatedAt = DateTime.UtcNow.AddHours(-1) };
            db.ChatMessages.Add(restored); await db.SaveChangesAsync(); restoredId = restored.Id;
        }
        using var read = await agent.PostAsJsonAsync($"{Url}/{id}/Read", new { lastReadMessageId = restoredId });
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var messages = (await Data(await owner.GetAsync($"{Url}/{id}/Conversation")))["messages"]!.AsArray();
        Assert.True(messages[0]!["isRead"]!.GetValue<bool>());
        Assert.False(messages[1]!["isRead"]!.GetValue<bool>());
        Assert.True((await Data(await agent.GetAsync($"{Url}/Select/{id}")))["hasUnreadMessages"]!.GetValue<bool>());
        int unread = messages[1]!["id"]!.GetValue<int>();
        using var edit = await owner.PatchAsJsonAsync($"{Url}/{id}/Messages/{unread}", new { body = "Still editable" });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        using var readLatest = await agent.PostAsJsonAsync($"{Url}/{id}/Read", new { lastReadMessageId = unread });
        Assert.Equal(HttpStatusCode.OK, readLatest.StatusCode);
        Assert.False((await Data(await agent.GetAsync($"{Url}/Select/{id}")))["hasUnreadMessages"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Report_UsesRoleScope_AndTehranDayBoundaries()
    {
        using var owner = factory.CreateBrowser(); await Register(owner, "Client");
        using var other = factory.CreateBrowser(); await Register(other, "Client");
        using var agent = factory.CreateBrowser(); await Register(agent, "Agent");
        using var otherAgent = factory.CreateBrowser(); await Register(otherAgent, "Agent");
        int open = await Create(owner), mine = await Create(owner), privateTicket = await Create(owner);
        int queue = await Create(other), hidden = await Create(other), old = await Create(owner);
        using var accepted = await agent.PostAsJsonAsync($"{Url}/{mine}/Accept", new { priority = "High" });
        foreach (int id in new[] { privateTicket, hidden })
        { using var ignored = await otherAgent.PostAsJsonAsync($"{Url}/{id}/Accept", new { priority = "Low" }); }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            foreach (int id in new[] { open, mine, privateTicket, queue, hidden, old })
            {
                var ticket = await db.Tickets.SingleAsync(ticket => ticket.Id == id);
                ticket.CreatedAt = id == old ? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    : id == open ? new DateTime(2026, 2, 10, 20, 29, 59, DateTimeKind.Utc)
                    : new DateTime(2026, 2, 10, 20, 30, 0, DateTimeKind.Utc);
            }
            await db.SaveChangesAsync();
        }
        string range = "from=2026-02-10&to=2026-02-11";
        using var clientReport = await owner.GetAsync($"{Url}/Reports?{range}");
        Assert.Equal(HttpStatusCode.Forbidden, clientReport.StatusCode);
        var agentReport = await Data(await agent.GetAsync($"{Url}/Reports?{range}"));
        Assert.Equal(1, agentReport["summary"]!["total"]!.GetValue<int>());
        Assert.Equal(0, agentReport["dailyCreated"]![0]!["count"]!.GetValue<int>());
        Assert.Equal(1, agentReport["dailyCreated"]![1]!["count"]!.GetValue<int>());
        Assert.Equal("Asia/Tehran", agentReport["timeZone"]!.GetValue<string>());
        var rows = await Data(await agent.GetAsync($"{Url}/Reports/Tickets?{range}"));
        var row = Assert.Single(rows.AsArray())!;
        Assert.Equal(mine, row["id"]!.GetValue<int>());
        Assert.Equal("Test request", row["title"]!.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(row["customerName"]!.GetValue<string>()));
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync($"{Url}/Reports/Tickets?{range}")).StatusCode);
        Assert.Empty((await Data(await agent.GetAsync($"{Url}/Reports/Tickets?from=2026-02-10&to=2026-02-10"))).AsArray());
        // Removed scope filters cannot expose the queue or another agent's work.
        foreach (string query in new[] { "mineOnly=false", "unassignedOnly=true", "assignedToId=999" })
        {
            var scoped = await Data(await agent.GetAsync($"{Url}/Reports?{range}&{query}"));
            Assert.Equal(1, scoped["summary"]!["total"]!.GetValue<int>());
            var scopedRows = await Data(await agent.GetAsync($"{Url}/Reports/Tickets?{range}&{query}"));
            Assert.Equal(mine, Assert.Single(scopedRows.AsArray())!["id"]!.GetValue<int>());
        }
        var empty = await Data(await agent.GetAsync($"{Url}/Reports?from=2026-03-01&to=2026-03-03"));
        Assert.Equal(0, empty["summary"]!["total"]!.GetValue<int>());
        Assert.Equal(3, empty["dailyCreated"]!.AsArray().Count);
        Assert.Empty((await Data(await agent.GetAsync($"{Url}/Reports/Tickets?from=2026-03-01&to=2026-03-03"))).AsArray());
    }

    [Fact]
    public async Task ReportTickets_PagesOnlyOwnAcceptedRequests_WithDeterministicOrdering()
    {
        using var owner = factory.CreateBrowser(); await Register(owner, "Client");
        using var agent = factory.CreateBrowser(); int agentId = await Register(agent, "Agent");
        var expected = new List<int>();
        for (int index = 0; index < 5; index++)
        {
            int id = await Create(owner); expected.Add(id);
            (await agent.PostAsJsonAsync($"{Url}/{id}/Accept", new { priority = "Medium" })).EnsureSuccessStatusCode();
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            foreach (var ticket in await db.Tickets.Where(ticket => ticket.AssignedToId == agentId).ToListAsync())
                ticket.CreatedAt = new DateTime(2026, 2, 10, 20, 30, 0, DateTimeKind.Utc);
            await db.SaveChangesAsync();
        }
        expected.Reverse();
        var actual = new List<int>();
        for (int page = 1; page <= 3; page++)
        {
            using var response = await agent.GetAsync($"{Url}/Reports/Tickets?from=2026-02-11&to=2026-02-11&page={page}&perPage=2");
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadFromJsonAsync<JsonNode>();
            Assert.Equal(5, payload!["pagination"]!["totalItems"]!.GetValue<int>());
            Assert.Equal(3, payload["pagination"]!["totalPages"]!.GetValue<int>());
            Assert.Equal(page, payload["pagination"]!["page"]!.GetValue<int>());
            actual.AddRange(payload["data"]!.AsArray().Select(row => row!["id"]!.GetValue<int>()));
        }
        Assert.Equal(expected, actual);
        Assert.Empty((await Data(await agent.GetAsync($"{Url}/Reports/Tickets?from=2026-02-11&to=2026-02-11&page=4&perPage=2"))).AsArray());
        foreach (string invalid in new[] { "page=0", "perPage=101", "page=2147483647&perPage=100", "from=2026-03-10&to=2026-03-01", "from=0001-01-01", "from=invalid" })
            Assert.Equal(HttpStatusCode.BadRequest, (await agent.GetAsync($"{Url}/Reports/Tickets?{invalid}")).StatusCode);
    }

    [Theory]
    [InlineData("from=2026-03-10&to=2026-03-01")]
    [InlineData("from=2024-01-01&to=2026-01-01")]
    [InlineData("from=0001-01-01&to=0001-01-01")]
    [InlineData("from=invalid")]
    public async Task Report_RejectsInvalidFilters(string query)
    {
        using var agent = factory.CreateBrowser(); await Register(agent, "Agent");
        using var response = await agent.GetAsync($"{Url}/Reports?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Acceptance_RetriesAnUnrelatedReadRevision_ButStillAssignsOnlyOnce()
    {
        using var owner = factory.CreateBrowser(); await Register(owner, "Client");
        using var agent = factory.CreateBrowser(); int agentId = await Register(agent, "Agent");
        int id = await Create(owner);
        int messageId = (await Data(await owner.GetAsync($"{Url}/{id}/Conversation")))["messages"]![0]!["id"]!.GetValue<int>();
        using var scope = factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITicketsRepository>();
        await repo.GetForAcceptanceAsync(id);
        using var read = await agent.PostAsJsonAsync($"{Url}/{id}/Read", new { lastReadMessageId = messageId });
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var result = await scope.ServiceProvider.GetRequiredService<ITicketsService>().AcceptAsync(agentId, UserRole.Agent, id,
            new AcceptTicketRequest { Priority = Database.Enums.Tickets.TicketPriority.Medium });
        Assert.Equal(200, result.StatusCode);
        Assert.Equal(agentId, result.Data!.AssignedToId);
    }

    private static async Task<int> Create(HttpClient client) { using var response = await client.PostAsJsonAsync($"{Url}/Create", new { title = "Test request", description = "Initial message" }); Assert.Equal(HttpStatusCode.Created, response.StatusCode); return (await Data(response))["id"]!.GetValue<int>(); }
    private static async Task<JsonNode> Data(HttpResponseMessage response) { Assert.Equal(HttpStatusCode.OK, response.StatusCode == HttpStatusCode.Created ? HttpStatusCode.OK : response.StatusCode); return (await response.Content.ReadFromJsonAsync<JsonNode>())!["data"]!; }
    private static async Task<int> Register(HttpClient client, string role)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/auth/register", new { fullName = "Reports Test", mobile = TestPhones.Next(), countryId = 105, password = "1234", confirmPassword = "1234", role });
        var data = await Data(response); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", data["token"]!.GetValue<string>());
        return data["user"]!["id"]!.GetValue<int>();
    }
}
