using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Database.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Repository.Tickets;
using Service.Tickets;
using Xunit;

namespace TicketingApi.Tests;

public sealed class ReplyReminderApiTests
{
    private const string Root = "/Tickets/v1/Tickets";
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset current = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(int seconds) => current = current.AddSeconds(seconds);
    }

    [Fact]
    public async Task SeenAndAccepted_StartsFiveMinuteClock_RepeatedReadsDoNotPostpone_AndDismissSnoozesForFiveMinutes()
    {
        var clock = new Clock(); using var factory = new AuthApiFactory { Clock = clock };
        using var client = factory.CreateBrowser(); await Register(client, "Client");
        using var agent = factory.CreateBrowser(); await Register(agent, "Agent");
        using var other = factory.CreateBrowser(); await Register(other, "Agent");
        var (id, message) = await Create(client); await Accept(agent, id);
        clock.Advance(600); Assert.Equal(0, await Process(factory));
        await Read(agent, id, message);
        clock.Advance(299); Assert.Equal(0, await Process(factory));
        await Read(agent, id, message);
        clock.Advance(1); Assert.Equal(1, await Process(factory));
        var first = Assert.Single(await Alerts(agent));
        Assert.Equal(id, first!["ticketId"]!.GetValue<int>());
        Assert.Equal(0, await Process(factory));
        Assert.Empty(await Alerts(other));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Root + "/ReplyReminders")).StatusCode);
        clock.Advance(240);
        using var dismissed = await Dismiss(agent, first);
        Assert.Equal(HttpStatusCode.OK, dismissed.StatusCode); Assert.Empty(await Alerts(agent));
        clock.Advance(299); Assert.Equal(0, await Process(factory));
        clock.Advance(1); Assert.Equal(1, await Process(factory));
        var next = Assert.Single(await Alerts(agent));
        Assert.NotEqual(first["notificationId"]!.GetValue<string>(), next!["notificationId"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Conflict, (await Dismiss(agent, first)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Dismiss(other, next)).StatusCode);
        clock.Advance(300); Assert.Equal(1, await Process(factory));
        Assert.Single(await Alerts(agent));
    }

    [Fact]
    public async Task ReplyStopsReminders_AndOnlyReadingANewerCustomerMessageStartsAnotherClock()
    {
        var clock = new Clock(); using var factory = new AuthApiFactory { Clock = clock };
        using var client = factory.CreateBrowser(); await Register(client, "Client");
        using var agent = factory.CreateBrowser(); await Register(agent, "Agent");
        var (id, first) = await Create(client); await Accept(agent, id); await Read(agent, id, first);
        clock.Advance(300); Assert.Equal(1, await Process(factory));
        await Message(agent, id, "Here is the answer");
        Assert.Empty(await Alerts(agent)); clock.Advance(600); Assert.Equal(0, await Process(factory));
        int latest = await Message(client, id, "One more question");
        await Read(agent, id, first); clock.Advance(600); Assert.Equal(0, await Process(factory));
        await Read(agent, id, latest); clock.Advance(299); Assert.Equal(0, await Process(factory));
        clock.Advance(1); Assert.Equal(1, await Process(factory));
    }

    [Theory]
    [InlineData("close")]
    [InlineData("release")]
    [InlineData("transfer")]
    public async Task ClosingReleasingOrTransferring_ClearsThePreviousAgentsReminder(string action)
    {
        var clock = new Clock(); using var factory = new AuthApiFactory { Clock = clock };
        using var client = factory.CreateBrowser(); await Register(client, "Client");
        using var first = factory.CreateBrowser(); await Register(first, "Agent");
        using var second = factory.CreateBrowser(); int secondId = await Register(second, "Agent");
        var (id, message) = await Create(client); await Accept(first, id); await Read(first, id, message);
        clock.Advance(300); Assert.Equal(1, await Process(factory)); var alert = Assert.Single(await Alerts(first));
        await Data(action == "transfer" ? await first.PostAsJsonAsync($"{Root}/{id}/Transfer", new { agentId = secondId })
            : action == "release" ? await first.PostAsync($"{Root}/{id}/Release", null)
            : await first.PatchAsJsonAsync($"{Root}/{id}/Status", new { status = "Closed" }));
        Assert.Empty(await Alerts(first)); clock.Advance(600); Assert.Equal(0, await Process(factory));
        Assert.False((await Dismiss(first, alert!)).IsSuccessStatusCode);
        if (action == "transfer")
        {
            await Read(second, id, message); clock.Advance(300);
            Assert.Equal(1, await Process(factory)); Assert.Single(await Alerts(second)); Assert.Empty(await Alerts(first));
        }
    }

    [Fact]
    public async Task QueuePreviewBeforeAcceptance_CountsFromAcceptance_AndSurvivesANewServiceScope()
    {
        var clock = new Clock(); using var factory = new AuthApiFactory { Clock = clock };
        using var client = factory.CreateBrowser(); await Register(client, "Client");
        using var agent = factory.CreateBrowser(); await Register(agent, "Agent");
        var (id, message) = await Create(client); await Read(agent, id, message);
        clock.Advance(600); Assert.Equal(0, await Process(factory)); await Accept(agent, id);
        clock.Advance(299); Assert.Equal(0, await Process(factory));
        clock.Advance(1); Assert.Equal(1, await Process(factory)); Assert.Single(await Alerts(agent));
    }

    [Fact]
    public async Task AtomicClaim_PreventsDuplicateWorkers_AndRejectsCandidatesAfterClosure()
    {
        var clock = new Clock(); using var factory = new AuthApiFactory { Clock = clock };
        using var client = factory.CreateBrowser(); await Register(client, "Client");
        using var agent = factory.CreateBrowser(); await Register(agent, "Agent");
        var (id, message) = await Create(client); await Accept(agent, id); await Read(agent, id, message); clock.Advance(300);
        using var firstScope = factory.Services.CreateScope(); using var secondScope = factory.Services.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<IReplyRemindersRepository>();
        var second = secondScope.ServiceProvider.GetRequiredService<IReplyRemindersRepository>();
        DateTime now = clock.GetUtcNow().UtcDateTime;
        var one = Assert.Single(await first.GetDueAsync(now, default)); var two = Assert.Single(await second.GetDueAsync(now, default));
        Assert.True(await first.ClaimAsync(one, now, now.AddMinutes(5), default));
        Assert.False(await second.ClaimAsync(two, now, now.AddMinutes(5), default));
        await Data(await agent.PatchAsJsonAsync($"{Root}/{id}/Status", new { status = "Closed" }));
        Assert.False(await second.ClaimAsync(two, now.AddMinutes(10), now.AddMinutes(15), default));
    }

    [Fact]
    public async Task ExistingAcceptedAndReadTickets_AreInitializedAfterDeployment()
    {
        var clock = new Clock(); using var factory = new AuthApiFactory { Clock = clock };
        using var client = factory.CreateBrowser(); await Register(client, "Client");
        using var agent = factory.CreateBrowser(); await Register(agent, "Agent");
        var (id, message) = await Create(client); await Accept(agent, id); await Read(agent, id, message);
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<DatabaseContext>().TicketReplyReminders.Where(row => row.TicketId == id).ExecuteDeleteAsync();
        clock.Advance(600); Assert.Equal(1, await Process(factory)); Assert.Single(await Alerts(agent));
    }

    private static async Task<int> Process(AuthApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IReplyRemindersService>().ProcessDueAsync();
    }
    private static async Task<JsonArray> Alerts(HttpClient client) => (await Data(await client.GetAsync(Root + "/ReplyReminders"))).AsArray();
    private static Task<HttpResponseMessage> Dismiss(HttpClient client, JsonNode reminder) =>
        client.PostAsJsonAsync($"{Root}/{reminder["ticketId"]}/ReplyReminder/Dismiss", new { notificationId = reminder["notificationId"]!.GetValue<string>() });
    private static async Task Read(HttpClient client, int id, int message) =>
        await Data(await client.PostAsJsonAsync($"{Root}/{id}/Read", new { lastReadMessageId = message }));
    private static async Task<int> Message(HttpClient client, int id, string body) =>
        (await Data(await client.PostAsJsonAsync($"{Root}/{id}/Messages", new { body, clientMessageId = Guid.NewGuid() })))["id"]!.GetValue<int>();
    private static async Task Accept(HttpClient client, int id) => await Data(await client.PostAsJsonAsync($"{Root}/{id}/Accept", new { priority = "Medium" }));
    private static async Task<(int TicketId, int MessageId)> Create(HttpClient client)
    {
        int id = (await Data(await client.PostAsJsonAsync(Root + "/Create", new { title = "Reply reminder", description = "Customer question" })))["id"]!.GetValue<int>();
        var chat = await Data(await client.GetAsync($"{Root}/{id}/Conversation"));
        return (id, chat["messages"]![0]!["id"]!.GetValue<int>());
    }
    private static async Task<int> Register(HttpClient client, string role)
    {
        var data = await Data(await client.PostAsJsonAsync("/api/v1/auth/register", new
        { fullName = "Reminder tests", countryId = 105, mobile = TestPhones.Next(), password = "x", confirmPassword = "x", role }));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", data["token"]!.GetValue<string>());
        return data["user"]!["id"]!.GetValue<int>();
    }
    private static async Task<JsonNode> Data(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<JsonNode>())!["data"]!;
    }
}
