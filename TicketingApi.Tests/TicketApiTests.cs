using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Database.Context;
using Database.Enums.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Repository.Tickets;
using Service.Tickets;
using ViewModels.Tickets;
using Xunit;

namespace TicketingApi.Tests;

public sealed class TicketApiTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private const string TicketsUrl = "/Tickets/v1/Tickets";

    [Theory]
    [InlineData("/")]
    [InlineData("/index.html")]
    [InlineData("/app.css")]
    [InlineData("/app.js")]
    [InlineData("/lib/signalr/signalr.min.js")]
    [InlineData("/lib/report-export/report-pdf.js")]
    public async Task Api_DoesNotHostFrontendPagesOrAssets(string path)
    {
        using var client = factory.CreateBrowser();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Client_SubmitsOnlyTitleAndDescription_AndCanContinueWhileAwaitingAcceptance()
    {
        using var owner = factory.CreateBrowser();
        await RegisterAsync(owner, "Client");
        int id = await CreateTicketAsync(owner);
        JsonNode ticket = await DataAsync(await owner.GetAsync($"{TicketsUrl}/Select/{id}"));
        Assert.Equal("Open", ticket["status"]!.GetValue<string>());
        Assert.Null(ticket["priority"]);
        Assert.Null(ticket["department"]);
        Assert.Null(ticket["assignedToId"]);
        Assert.Null(ticket["acceptedAt"]);
        using var chat = await owner.GetAsync($"{TicketsUrl}/{id}/Conversation");
        Assert.Equal(HttpStatusCode.OK, chat.StatusCode);
        Assert.Null((await DataAsync(chat))["agent"]);
        using var earlyMessage = await MessageAsync(owner, id, "Hello");
        Assert.Equal(HttpStatusCode.OK, earlyMessage.StatusCode);
        using var selfAccept = await AcceptAsync(owner, id);
        Assert.Equal(HttpStatusCode.Forbidden, selfAccept.StatusCode);
        using var priority = await owner.PatchAsJsonAsync($"{TicketsUrl}/{id}/Priority", new { priority = "High" });
        Assert.Equal(HttpStatusCode.Forbidden, priority.StatusCode);
        using var removedReply = await owner.PostAsJsonAsync($"{TicketsUrl}/{id}/Replies", new { body = "Old API" });
        Assert.Equal(HttpStatusCode.NotFound, removedReply.StatusCode);
    }

    [Fact]
    public async Task Client_CanSendConsecutiveMessages_BeforeAndAfterOneAcceptance()
    {
        using var owner = factory.CreateBrowser();
        await RegisterAsync(owner, "Client");
        using var outsider = factory.CreateBrowser();
        await RegisterAsync(outsider, "Client");
        using var agent = factory.CreateBrowser();
        int agentId = await RegisterAsync(agent, "Agent");
        int id = await CreateTicketAsync(owner);
        using var otherAgent = factory.CreateBrowser();
        await RegisterAsync(otherAgent, "Agent");
        foreach (string text in new[] { "Hello", "I need help", "Here are the details" })
        {
            using var sent = await MessageAsync(owner, id, text);
            Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
            using var details = await owner.GetAsync($"{TicketsUrl}/Select/{id}");
            var ticket = await DataAsync(details);
            Assert.Equal("Open", ticket["status"]!.GetValue<string>());
            Assert.Null(ticket["assignedToId"]);
            Assert.Null(ticket["acceptedAt"]);
        }
        using var pending = await owner.GetAsync($"{TicketsUrl}/{id}/Conversation");
        var pendingConversation = await DataAsync(pending);
        Assert.Null(pendingConversation["agent"]);
        Assert.Equal(4, pendingConversation["messages"]!.AsArray().Count);
        foreach (var previewAgent in new[] { agent, otherAgent })
        {
            using var preview = await previewAgent.GetAsync($"{TicketsUrl}/{id}/Conversation");
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            var history = await DataAsync(preview);
            Assert.Null(history["agent"]);
            Assert.Equal(new[] { "Support request description", "Hello", "I need help", "Here are the details" },
                history["messages"]!.AsArray().Select(message => message!["body"]!.GetValue<string>()));
            using var earlyReply = await MessageAsync(previewAgent, id, "Not accepted yet");
            Assert.Equal(HttpStatusCode.Conflict, earlyReply.StatusCode);
        }
        using var outsiderRead = await outsider.GetAsync($"{TicketsUrl}/{id}/Conversation");
        Assert.Equal(HttpStatusCode.NotFound, outsiderRead.StatusCode);
        using var outsiderMessage = await MessageAsync(outsider, id, "Not my conversation");
        Assert.Equal(HttpStatusCode.NotFound, outsiderMessage.StatusCode);

        using var accepted = await AcceptAsync(agent, id);
        var acceptance = await DataAsync(accepted);
        DateTime acceptedAt = acceptance["acceptedAt"]!.GetValue<DateTime>();
        using var deniedPreview = await otherAgent.GetAsync($"{TicketsUrl}/{id}/Conversation");
        Assert.Equal(HttpStatusCode.NotFound, deniedPreview.StatusCode);
        using var sharedHistory = await agent.GetAsync($"{TicketsUrl}/{id}/Conversation");
        Assert.Equal(4, (await DataAsync(sharedHistory))["messages"]!.AsArray().Count);
        foreach (string text in new[] { "One more thing", "And another detail" })
        {
            using var sent = await MessageAsync(owner, id, text);
            Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
            using var details = await owner.GetAsync($"{TicketsUrl}/Select/{id}");
            var ticket = await DataAsync(details);
            Assert.Equal("InProgress", ticket["status"]!.GetValue<string>());
            Assert.Equal(agentId, ticket["assignedToId"]!.GetValue<int>());
            Assert.Equal(acceptedAt, ticket["acceptedAt"]!.GetValue<DateTime>());
        }
        using var answer = await MessageAsync(agent, id, "I can help you");
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        foreach (string text in new[] { "Thank you", "I have another question" })
        {
            using var sent = await MessageAsync(owner, id, text);
            Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        }
        using var finalHistory = await owner.GetAsync($"{TicketsUrl}/{id}/Conversation");
        Assert.Equal(9, (await DataAsync(finalHistory))["messages"]!.AsArray().Count);
    }

    [Theory]
    [InlineData("priority")]
    [InlineData("department")]
    public async Task Create_RejectsRemovedClientInputs(string property)
    {
        using var owner = factory.CreateBrowser();
        await RegisterAsync(owner, "Client");
        var request = new Dictionary<string, string>
        {
            ["title"] = "Support request", ["description"] = "Description of the issue", [property] = property == "priority" ? "High" : "Support"
        };
        using var response = await owner.PostAsJsonAsync($"{TicketsUrl}/Create", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Accept_AssignsOneAgent_CreatesPrivateConversation_AndHidesItFromOthers()
    {
        using var owner = factory.CreateBrowser();
        int ownerId = await RegisterAsync(owner, "Client");
        using var agent = factory.CreateBrowser();
        int agentId = await RegisterAsync(agent, "Agent");
        using var otherAgent = factory.CreateBrowser();
        await RegisterAsync(otherAgent, "Agent");
        using var outsider = factory.CreateBrowser();
        await RegisterAsync(outsider, "Client");
        int id = await CreateTicketAsync(owner);
        using var before = await otherAgent.GetAsync($"{TicketsUrl}/Select/{id}");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        using var earlyMessage = await MessageAsync(agent, id, "Not accepted yet");
        Assert.Equal(HttpStatusCode.Conflict, earlyMessage.StatusCode);
        using var accepted = await AcceptAsync(agent, id, "High");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        JsonNode ticket = await DataAsync(accepted);
        Assert.Equal(agentId, ticket["assignedToId"]!.GetValue<int>());
        Assert.Equal("High", ticket["priority"]!.GetValue<string>());
        Assert.Equal("InProgress", ticket["status"]!.GetValue<string>());
        Assert.NotNull(ticket["acceptedAt"]);
        JsonNode conversation = await DataAsync(await owner.GetAsync($"{TicketsUrl}/{id}/Conversation"));
        Assert.Equal(ownerId, conversation["client"]!["id"]!.GetValue<int>());
        Assert.Equal(agentId, conversation["agent"]!["id"]!.GetValue<int>());
        Assert.Equal("Support request description", Assert.Single(conversation["messages"]!.AsArray())!["body"]!.GetValue<string>());
        using var stolen = await AcceptAsync(otherAgent, id);
        Assert.Equal(HttpStatusCode.Conflict, stolen.StatusCode);
        foreach (var denied in new[] { otherAgent, outsider })
        {
            foreach (string route in new[] { $"Select/{id}", $"{id}/Conversation" })
            {
                using var response = await denied.GetAsync($"{TicketsUrl}/{route}");
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            }
            using var message = await MessageAsync(denied, id, "Forbidden message");
            Assert.Equal(HttpStatusCode.NotFound, message.StatusCode);
        }
        using var stolenPriority = await otherAgent.PatchAsJsonAsync($"{TicketsUrl}/{id}/Priority", new { priority = "Low" });
        Assert.Equal(HttpStatusCode.NotFound, stolenPriority.StatusCode);
        using var stolenStatus = await otherAgent.PatchAsJsonAsync($"{TicketsUrl}/{id}/Status", new { status = "Closed" });
        Assert.Equal(HttpStatusCode.NotFound, stolenStatus.StatusCode);
    }

    [Fact]
    public async Task Accept_WithConcurrentLoadedRequests_AllowsOnlyFirstAgent()
    {
        using var owner = factory.CreateBrowser();
        await RegisterAsync(owner, "Client");
        using var firstAgent = factory.CreateBrowser();
        int firstId = await RegisterAsync(firstAgent, "Agent");
        using var secondAgent = factory.CreateBrowser();
        int secondId = await RegisterAsync(secondAgent, "Agent");
        int id = await CreateTicketAsync(owner);
        using var firstScope = factory.Services.CreateScope();
        using var secondScope = factory.Services.CreateScope();
        await firstScope.ServiceProvider.GetRequiredService<ITicketsRepository>().GetForAcceptanceAsync(id);
        await secondScope.ServiceProvider.GetRequiredService<ITicketsRepository>().GetForAcceptanceAsync(id);
        var request = new AcceptTicketRequest { Priority = Database.Enums.Tickets.TicketPriority.High };
        var first = await firstScope.ServiceProvider.GetRequiredService<ITicketsService>().AcceptAsync(firstId, UserRole.Agent, id, request);
        var second = await secondScope.ServiceProvider.GetRequiredService<ITicketsService>().AcceptAsync(secondId, UserRole.Agent, id, request);
        Assert.Equal(200, first.StatusCode);
        Assert.Equal(409, second.StatusCode);
        using var verify = factory.Services.CreateScope();
        var stored = await verify.ServiceProvider.GetRequiredService<DatabaseContext>().Tickets.FindAsync(id);
        Assert.Equal(firstId, stored!.AssignedToId);
    }

    [Fact]
    public async Task Conversation_PersistsBothParticipantsMessages_AndDeduplicatesRetries()
    {
        using var owner = factory.CreateBrowser();
        int ownerId = await RegisterAsync(owner, "Client");
        using var agent = factory.CreateBrowser();
        await RegisterAsync(agent, "Agent");
        int id = await CreateTicketAsync(owner);
        using var acceptance = await AcceptAsync(agent, id);
        Assert.Equal(HttpStatusCode.OK, acceptance.StatusCode);
        Guid clientMessageId = Guid.NewGuid();
        using var first = await MessageAsync(owner, id, "  Hello support  ", clientMessageId);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        int messageId = (await DataAsync(first))["id"]!.GetValue<int>();
        using var retry = await MessageAsync(owner, id, "Hello support", clientMessageId);
        Assert.Equal(messageId, (await DataAsync(retry))["id"]!.GetValue<int>());
        using var reuse = await MessageAsync(owner, id, "Different text", clientMessageId);
        Assert.Equal(HttpStatusCode.Conflict, reuse.StatusCode);
        using var impersonate = await MessageAsync(agent, id, "Hello support", clientMessageId);
        Assert.Equal(HttpStatusCode.Conflict, impersonate.StatusCode);
        using var response = await MessageAsync(agent, id, "How can I help?");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var history = await owner.GetAsync($"{TicketsUrl}/{id}/Conversation");
        JsonArray messages = (await DataAsync(history))["messages"]!.AsArray();
        Assert.Equal(3, messages.Count);
        Assert.Equal("Hello support", messages[1]!["body"]!.GetValue<string>());
        Assert.Equal(ownerId, messages[1]!["author"]!["id"]!.GetValue<int>());
        Assert.Equal("Agent", messages[2]!["author"]!["role"]!.GetValue<string>());
    }

    [Fact]
    public async Task OnlyAssignedAgent_ChangesPriorityAndStatus_ClosedChatRemainsReadable()
    {
        using var owner = factory.CreateBrowser();
        await RegisterAsync(owner, "Client");
        using var agent = factory.CreateBrowser();
        await RegisterAsync(agent, "Agent");
        int id = await CreateTicketAsync(owner);
        using var earlyPriority = await agent.PatchAsJsonAsync($"{TicketsUrl}/{id}/Priority", new { priority = "High" });
        Assert.Equal(HttpStatusCode.Conflict, earlyPriority.StatusCode);
        using var earlyStatus = await agent.PatchAsJsonAsync($"{TicketsUrl}/{id}/Status", new { status = "InProgress" });
        Assert.Equal(HttpStatusCode.Conflict, earlyStatus.StatusCode);
        using var acceptance = await AcceptAsync(agent, id, "Low");
        using var changed = await agent.PatchAsJsonAsync($"{TicketsUrl}/{id}/Priority", new { priority = "High" });
        Assert.Equal("High", (await DataAsync(changed))["priority"]!.GetValue<string>());
        foreach (string status in new[] { "Open", "InProgress", "Answered" })
        {
            using var manual = await agent.PatchAsJsonAsync($"{TicketsUrl}/{id}/Status", new { status });
            Assert.Equal(HttpStatusCode.Conflict, manual.StatusCode);
        }
        using var answered = await MessageAsync(agent, id, "Here is the answer");
        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
        Assert.Equal("Answered", (await DataAsync(await owner.GetAsync($"{TicketsUrl}/Select/{id}")))["status"]!.GetValue<string>());
        using var message = await MessageAsync(owner, id, "More help please");
        Assert.Equal(HttpStatusCode.OK, message.StatusCode);
        Assert.Equal("InProgress", (await DataAsync(await owner.GetAsync($"{TicketsUrl}/Select/{id}")))["status"]!.GetValue<string>());
        using var closed = await agent.PatchAsJsonAsync($"{TicketsUrl}/{id}/Status", new { status = "Closed" });
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        using var closedMessage = await MessageAsync(owner, id, "Too late");
        Assert.Equal(HttpStatusCode.Conflict, closedMessage.StatusCode);
        using var closedAgentMessage = await MessageAsync(agent, id, "Too late");
        Assert.Equal(HttpStatusCode.Conflict, closedAgentMessage.StatusCode);
        using var closedPriority = await agent.PatchAsJsonAsync($"{TicketsUrl}/{id}/Priority", new { priority = "Low" });
        Assert.Equal(HttpStatusCode.Conflict, closedPriority.StatusCode);
        using var history = await owner.GetAsync($"{TicketsUrl}/{id}/Conversation");
        Assert.Equal(3, (await DataAsync(history))["messages"]!.AsArray().Count);
    }

    [Fact]
    public async Task AutomaticStatus_FollowsNewMessages_WithoutReplayingOldStatusesOnRetry()
    {
        using var owner = factory.CreateBrowser();
        await RegisterAsync(owner, "Client");
        using var agent = factory.CreateBrowser();
        await RegisterAsync(agent, "Agent");
        int id = await CreateTicketAsync(owner);
        async Task AssertStatusAsync(string expected, string countKey)
        {
            using var details = await owner.GetAsync($"{TicketsUrl}/Select/{id}");
            Assert.Equal(expected, (await DataAsync(details))["status"]!.GetValue<string>());
            using var summary = await owner.GetAsync($"{TicketsUrl}/Dashboard");
            Assert.Equal(1, (await DataAsync(summary))[countKey]!.GetValue<int>());
            using var list = await owner.GetAsync($"{TicketsUrl}/List?status={expected}");
            Assert.Equal(id, Assert.Single((await DataAsync(list)).AsArray())!["id"]!.GetValue<int>());
        }

        await AssertStatusAsync("Open", "open");
        using var accepted = await AcceptAsync(agent, id);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        await AssertStatusAsync("InProgress", "inProgress");
        using var initialClientMessage = await MessageAsync(owner, id, "Additional information");
        Assert.Equal(HttpStatusCode.OK, initialClientMessage.StatusCode);
        await AssertStatusAsync("InProgress", "inProgress");
        Guid answerId = Guid.NewGuid();
        using var answer = await MessageAsync(agent, id, "Agent answer", answerId);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        await AssertStatusAsync("Answered", "answered");
        using var anotherAnswer = await MessageAsync(agent, id, "Another answer");
        Assert.Equal(HttpStatusCode.OK, anotherAnswer.StatusCode);
        await AssertStatusAsync("Answered", "answered");
        using var followup = await MessageAsync(owner, id, "Client followup");
        Assert.Equal(HttpStatusCode.OK, followup.StatusCode);
        await AssertStatusAsync("InProgress", "inProgress");
        using var retriedOldAnswer = await MessageAsync(agent, id, "Agent answer", answerId);
        Assert.Equal(HttpStatusCode.OK, retriedOldAnswer.StatusCode);
        await AssertStatusAsync("InProgress", "inProgress");
        using var invalidMessage = await MessageAsync(agent, id, "   ");
        Assert.Equal(HttpStatusCode.BadRequest, invalidMessage.StatusCode);
        await AssertStatusAsync("InProgress", "inProgress");
        using var clientClose = await owner.PatchAsJsonAsync($"{TicketsUrl}/{id}/Status", new { status = "Closed" });
        Assert.Equal(HttpStatusCode.Forbidden, clientClose.StatusCode);
        using var close = await agent.PatchAsJsonAsync($"{TicketsUrl}/{id}/Status", new { status = "Closed" });
        Assert.Equal(HttpStatusCode.OK, close.StatusCode);
        await AssertStatusAsync("Closed", "closed");
        using var closedRetry = await MessageAsync(agent, id, "Agent answer", answerId);
        Assert.Equal(HttpStatusCode.OK, closedRetry.StatusCode);
        await AssertStatusAsync("Closed", "closed");
        using var reopen = await agent.PatchAsJsonAsync($"{TicketsUrl}/{id}/Status", new { status = "InProgress" });
        Assert.Equal(HttpStatusCode.Conflict, reopen.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Invalid")]
    public async Task Acceptance_RequiresValidAgentPriority(string? priority)
    {
        using var owner = factory.CreateBrowser();
        await RegisterAsync(owner, "Client");
        using var agent = factory.CreateBrowser();
        await RegisterAsync(agent, "Agent");
        int id = await CreateTicketAsync(owner);
        using var response = await AcceptAsync(agent, id, priority);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task QueueAndAgentDashboard_ExcludeOtherAgentsAcceptedRequests()
    {
        using var owner = factory.CreateBrowser();
        await RegisterAsync(owner, "Client");
        using var first = factory.CreateBrowser();
        int firstId = await RegisterAsync(first, "Agent");
        using var second = factory.CreateBrowser();
        int secondId = await RegisterAsync(second, "Agent");
        string title = $"Queue {Guid.NewGuid():N}";
        int unassigned = await CreateTicketAsync(owner, title);
        int assigned = await CreateTicketAsync(owner, title);
        using var acceptance = await AcceptAsync(first, assigned, "High");
        using var queue = await second.GetAsync($"{TicketsUrl}/Unassigned?search={Uri.EscapeDataString(title)}&unassignedOnly=false");
        Assert.Equal(unassigned, Assert.Single((await DataAsync(queue)).AsArray())!["id"]!.GetValue<int>());
        using var list = await second.GetAsync($"{TicketsUrl}/List?search={Uri.EscapeDataString(title)}");
        Assert.Equal(unassigned, Assert.Single((await DataAsync(list)).AsArray())!["id"]!.GetValue<int>());
        using var mine = await first.GetAsync($"{TicketsUrl}/List?mineOnly=true&priority=High&status=InProgress&search={Uri.EscapeDataString(title)}");
        Assert.Equal(assigned, Assert.Single((await DataAsync(mine)).AsArray())!["id"]!.GetValue<int>());
        using var ownerStats = await owner.GetAsync($"{TicketsUrl}/Dashboard");
        Assert.Equal(2, (await DataAsync(ownerStats))["total"]!.GetValue<int>());
        using var agentStats = await second.GetAsync($"{TicketsUrl}/Dashboard");
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        int visible = await context.Tickets.CountAsync(ticket => ticket.AssignedToId == secondId ||
            (ticket.AssignedToId == null && ticket.Status == Database.Enums.Tickets.TicketStatus.Open));
        Assert.Equal(visible, (await DataAsync(agentStats))["total"]!.GetValue<int>());
    }

    [Fact]
    public async Task ClientSearch_CombinesTitleAndAssignedAgent_WithinOwnTickets()
    {
        using var owner = factory.CreateBrowser();
        await RegisterAsync(owner, "Client");
        using var outsider = factory.CreateBrowser();
        await RegisterAsync(outsider, "Client");
        using var agent = factory.CreateBrowser();
        int agentId = await RegisterAsync(agent, "Agent");
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            var user = await context.Users.FindAsync(agentId);
            user!.FullName = "Aida Rahimi";
            await context.SaveChangesAsync();
        }

        int payment = await CreateTicketAsync(owner, "Payment Gateway");
        int pending = await CreateTicketAsync(owner, "Payment followup");
        int account = await CreateTicketAsync(owner, "Account access");
        int other = await CreateTicketAsync(outsider, "Payment Gateway");
        foreach (int id in new[] { payment, account, other })
        {
            using var accepted = await AcceptAsync(agent, id);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }

        foreach (string search in new[] { "  pAyMeNt    aIdA  ", "Rahimi Gateway" })
        {
            using var response = await owner.GetAsync($"{TicketsUrl}/List?search={Uri.EscapeDataString(search)}&status=InProgress&perPage=1");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
            Assert.Equal(payment, Assert.Single(body["data"]!.AsArray())!["id"]!.GetValue<int>());
            Assert.Equal(1, body["pagination"]!["totalItems"]!.GetValue<int>());
        }

        using var byName = await owner.GetAsync($"{TicketsUrl}/List?search=Aida");
        Assert.Equal(new[] { payment, account }.Order(), (await DataAsync(byName)).AsArray()
            .Select(item => item!["id"]!.GetValue<int>()).Order());
        using var bySubject = await owner.GetAsync($"{TicketsUrl}/List?search=Payment");
        Assert.Equal(new[] { payment, pending }.Order(), (await DataAsync(bySubject)).AsArray()
            .Select(item => item!["id"]!.GetValue<int>()).Order());
        using var open = await owner.GetAsync($"{TicketsUrl}/List?search=Payment&status=Open");
        Assert.Equal(pending, Assert.Single((await DataAsync(open)).AsArray())!["id"]!.GetValue<int>());
        foreach (string search in new[] { "Payment missing", "Support request description" })
        {
            using var none = await owner.GetAsync($"{TicketsUrl}/List?search={Uri.EscapeDataString(search)}");
            Assert.Empty((await DataAsync(none)).AsArray());
        }
    }

    [Theory]
    [InlineData("High")]
    [InlineData("Low")]
    public async Task Client_CannotReadOrInferAgentPriority(string priority)
    {
        using var owner = factory.CreateBrowser();
        await RegisterAsync(owner, "Client");
        using var agent = factory.CreateBrowser();
        await RegisterAsync(agent, "Agent");
        int id = await CreateTicketAsync(owner);
        using var accepted = await AcceptAsync(agent, id, priority);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        using var details = await owner.GetAsync($"{TicketsUrl}/Select/{id}");
        Assert.False((await DataAsync(details)).AsObject().ContainsKey("priority"));
        using var list = await owner.GetAsync($"{TicketsUrl}/List");
        JsonNode item = Assert.Single((await DataAsync(list)).AsArray())!;
        Assert.False(item.AsObject().ContainsKey("priority"));
        using var filtered = await owner.GetAsync($"{TicketsUrl}/List?priority={priority}");
        Assert.Equal(HttpStatusCode.Forbidden, filtered.StatusCode);

        using var agentDetails = await agent.GetAsync($"{TicketsUrl}/Select/{id}");
        Assert.Equal(priority, (await DataAsync(agentDetails))["priority"]!.GetValue<string>());
        using var agentList = await agent.GetAsync($"{TicketsUrl}/List?mineOnly=true&priority={priority}");
        Assert.Equal(priority, Assert.Single((await DataAsync(agentList)).AsArray())!["priority"]!.GetValue<string>());
    }

    [Fact]
    public async Task Hub_RequiresAuthentication_QueryTokensAreRestrictedToHub()
    {
        using var client = factory.CreateBrowser();
        using var anonymous = await client.PostAsync("/hubs/tickets/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        await RegisterAsync(client, "Client");
        string token = client.DefaultRequestHeaders.Authorization!.Parameter!;
        client.DefaultRequestHeaders.Authorization = null;
        using var invalidUse = await client.GetAsync($"{TicketsUrl}/List?access_token={token}");
        Assert.Equal(HttpStatusCode.Unauthorized, invalidUse.StatusCode);
        using var negotiation = await client.PostAsync($"/hubs/tickets/negotiate?negotiateVersion=1&access_token={token}", null);
        Assert.Equal(HttpStatusCode.OK, negotiation.StatusCode);
    }

    private static async Task<int> RegisterAsync(HttpClient client, string role)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            fullName = "Ticket Test User", mobile = TestPhones.Next(), countryId = 105, password = "1234", confirmPassword = "1234", role
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonNode data = await DataAsync(response);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", data["token"]!.GetValue<string>());
        return data["user"]!["id"]!.GetValue<int>();
    }
    private static async Task<int> CreateTicketAsync(HttpClient client, string title = "Support request")
    {
        using var response = await client.PostAsJsonAsync($"{TicketsUrl}/Create", new { title, description = "Support request description" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await DataAsync(response))["id"]!.GetValue<int>();
    }
    private static Task<HttpResponseMessage> AcceptAsync(HttpClient client, int id, string? priority = "Medium") =>
        client.PostAsJsonAsync($"{TicketsUrl}/{id}/Accept", new { priority });
    private static Task<HttpResponseMessage> MessageAsync(HttpClient client, int id, string body, Guid? clientMessageId = null) =>
        client.PostAsJsonAsync($"{TicketsUrl}/{id}/Messages", new { body, clientMessageId = clientMessageId ?? Guid.NewGuid() });
    private static async Task<JsonNode> DataAsync(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<JsonNode>())!["data"]!;
}
