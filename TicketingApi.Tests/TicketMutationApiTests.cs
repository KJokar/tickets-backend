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

public sealed class TicketMutationApiTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private const string Url = "/Tickets/v1/Tickets";

    [Fact]
    public async Task PendingRequest_OwnerCanEditAndDelete_WithMessagesAndReadReceipts()
    {
        using var owner = factory.CreateBrowser(); await Register(owner, "Client");
        using var outsider = factory.CreateBrowser(); await Register(outsider, "Client");
        using var agent = factory.CreateBrowser(); await Register(agent, "Agent");
        int id = await Create(owner);
        int message = (await Message(owner, id))["id"]!.GetValue<int>();
        using var read = await agent.PostAsJsonAsync($"{Url}/{id}/Read", new { lastReadMessageId = message });
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var deniedEdit = await Edit(outsider, id);
        using var deniedDelete = await outsider.DeleteAsync($"{Url}/{id}");
        Assert.Equal(HttpStatusCode.NotFound, deniedEdit.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deniedDelete.StatusCode);
        using var agentEdit = await Edit(agent, id);
        using var agentDelete = await agent.DeleteAsync($"{Url}/{id}");
        Assert.Equal(HttpStatusCode.Forbidden, agentEdit.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, agentDelete.StatusCode);
        using var edited = await Edit(owner, id);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal("Updated subject", (await Data(edited))["title"]!.GetValue<string>());
        var history = await Data(await owner.GetAsync($"{Url}/{id}/Conversation"));
        Assert.Equal(2, history["messages"]!.AsArray().Count);
        using var deleted = await owner.DeleteAsync($"{Url}/{id}");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        foreach (var client in new[] { owner, agent })
        {
            using var missing = await client.GetAsync($"{Url}/Select/{id}");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        Assert.False(await db.ChatMessages.AnyAsync(item => item.TicketId == id));
        Assert.False(await db.ChatReadReceipts.AnyAsync(item => item.TicketId == id));
    }

    [Fact]
    public async Task AcceptedRequest_RejectsEditingAndDeletion()
    {
        using var owner = factory.CreateBrowser(); await Register(owner, "Client");
        using var agent = factory.CreateBrowser(); await Register(agent, "Agent");
        int id = await Create(owner);
        using var accepted = await Accept(agent, id);
        using var edit = await Edit(owner, id);
        using var delete = await owner.DeleteAsync($"{Url}/{id}");
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
    }

    [Fact]
    public async Task UnreadMessages_OnlySenderCanChange_AndDeletedUuidCannotResurrect()
    {
        using var owner = factory.CreateBrowser(); await Register(owner, "Client");
        using var agent = factory.CreateBrowser(); await Register(agent, "Agent");
        using var outsider = factory.CreateBrowser(); await Register(outsider, "Client");
        int id = await Create(owner);
        var message = await Message(owner, id);
        int messageId = message["id"]!.GetValue<int>();
        string path = $"{Url}/{id}/Messages/{messageId}";
        using var selfRead = await owner.PostAsJsonAsync($"{Url}/{id}/Read", new { lastReadMessageId = messageId });
        using var changed = await owner.PatchAsJsonAsync(path, new { body = "Corrected text" });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var data = await Data(changed);
        Assert.Equal("Corrected text", data["body"]!.GetValue<string>());
        Assert.NotNull(data["editedAt"]);
        Assert.False(data["isRead"]!.GetValue<bool>());
        using var forbidden = await agent.PatchAsJsonAsync(path, new { body = "Someone else's text" });
        using var forbiddenDelete = await agent.DeleteAsync(path);
        using var privateDelete = await outsider.DeleteAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenDelete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, privateDelete.StatusCode);
        using var deleted = await owner.DeleteAsync(path);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Equal("", (await Data(deleted))["body"]!.GetValue<string>());
        using var retry = await owner.PostAsJsonAsync($"{Url}/{id}/Messages", new { body = "Hello", clientMessageId = message["clientMessageId"]!.GetValue<string>() });
        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        var history = await Data(await owner.GetAsync($"{Url}/{id}/Conversation"));
        Assert.Equal("Original description", Assert.Single(history["messages"]!.AsArray())!["body"]!.GetValue<string>());
        using var repeated = await owner.DeleteAsync(path);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
    }

    [Fact]
    public async Task ReadingPreviewLocksClientMessage_EvenBeforeAcceptance_AndSurvivesAnotherAgentAccepting()
    {
        using var owner = factory.CreateBrowser(); await Register(owner, "Client");
        using var previewAgent = factory.CreateBrowser(); await Register(previewAgent, "Agent");
        using var acceptingAgent = factory.CreateBrowser(); await Register(acceptingAgent, "Agent");
        int id = await Create(owner);
        int first = (await Message(owner, id))["id"]!.GetValue<int>();
        int second = (await Message(owner, id))["id"]!.GetValue<int>();
        using var read = await previewAgent.PostAsJsonAsync($"{Url}/{id}/Read", new { lastReadMessageId = first });
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var editRead = await owner.PatchAsJsonAsync($"{Url}/{id}/Messages/{first}", new { body = "Too late" });
        using var deleteRead = await owner.DeleteAsync($"{Url}/{id}/Messages/{first}");
        Assert.Equal(HttpStatusCode.Conflict, editRead.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, deleteRead.StatusCode);
        using var editUnread = await owner.PatchAsJsonAsync($"{Url}/{id}/Messages/{second}", new { body = "Still unread" });
        Assert.Equal(HttpStatusCode.OK, editUnread.StatusCode);
        using var accepted = await Accept(acceptingAgent, id);
        var history = await Data(await owner.GetAsync($"{Url}/{id}/Conversation"));
        Assert.True(history["messages"]![1]!["isRead"]!.GetValue<bool>());
        Assert.False(history["messages"]![2]!["isRead"]!.GetValue<bool>());
        using var afterAccept = await owner.DeleteAsync($"{Url}/{id}/Messages/{first}");
        Assert.Equal(HttpStatusCode.Conflict, afterAccept.StatusCode);
    }

    [Fact]
    public async Task AgentMessages_AreMutableUntilClientReads_IncludingUnreadMessagesInClosedChat()
    {
        using var owner = factory.CreateBrowser(); await Register(owner, "Client");
        using var agent = factory.CreateBrowser(); await Register(agent, "Agent");
        int id = await Create(owner);
        using var accepted = await Accept(agent, id);
        int first = (await Message(agent, id))["id"]!.GetValue<int>();
        string path = $"{Url}/{id}/Messages/{first}";
        using var edit = await agent.PatchAsJsonAsync(path, new { body = "Revised answer" });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        using var read = await owner.PostAsJsonAsync($"{Url}/{id}/Read", new { lastReadMessageId = first });
        using var tooLate = await agent.PatchAsJsonAsync(path, new { body = "Seen" });
        using var tooLateDelete = await agent.DeleteAsync(path);
        Assert.Equal(HttpStatusCode.Conflict, tooLate.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, tooLateDelete.StatusCode);
        int second = (await Message(agent, id))["id"]!.GetValue<int>();
        using var removed = await agent.DeleteAsync($"{Url}/{id}/Messages/{second}");
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        int third = (await Message(agent, id))["id"]!.GetValue<int>();
        using var closed = await agent.PatchAsJsonAsync($"{Url}/{id}/Status", new { status = "Closed" });
        using var closedEdit = await agent.PatchAsJsonAsync($"{Url}/{id}/Messages/{third}", new { body = "Unread correction after closure" });
        Assert.Equal(HttpStatusCode.OK, closedEdit.StatusCode);
        using var closedDelete = await agent.DeleteAsync($"{Url}/{id}/Messages/{third}");
        Assert.Equal(HttpStatusCode.OK, closedDelete.StatusCode);
        Assert.Equal("Closed", (await Data(await owner.GetAsync($"{Url}/Select/{id}")))["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task StaleEditAfterRead_IsRejectedAtomically()
    {
        using var owner = factory.CreateBrowser(); int ownerId = await Register(owner, "Client");
        using var agent = factory.CreateBrowser(); await Register(agent, "Agent");
        int id = await Create(owner);
        int message = (await Message(owner, id))["id"]!.GetValue<int>();
        using var staleScope = factory.Services.CreateScope();
        var repo = staleScope.ServiceProvider.GetRequiredService<ITicketsRepository>();
        await repo.GetEntityAsync(id, ownerId, UserRole.Client);
        using var read = await agent.PostAsJsonAsync($"{Url}/{id}/Read", new { lastReadMessageId = message });
        var service = staleScope.ServiceProvider.GetRequiredService<ITicketsService>();
        var result = await service.EditMessageAsync(ownerId, UserRole.Client, id, message, new EditChatMessageRequest { Body = "Must not persist" });
        Assert.Equal(409, result.StatusCode);
        var history = await Data(await owner.GetAsync($"{Url}/{id}/Conversation"));
        Assert.Equal("Hello", history["messages"]![1]!["body"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MessageEdit_RejectsEmptyText(string body)
    {
        using var owner = factory.CreateBrowser(); await Register(owner, "Client");
        int id = await Create(owner), message = (await Message(owner, id))["id"]!.GetValue<int>();
        using var response = await owner.PatchAsJsonAsync($"{Url}/{id}/Messages/{message}", new { body });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ConcurrentRead_RollsBackAnAlreadyPreparedMessageEdit()
    {
        using var owner = factory.CreateBrowser(); int ownerId = await Register(owner, "Client");
        using var agent = factory.CreateBrowser(); await Register(agent, "Agent");
        int id = await Create(owner), messageId = (await Message(owner, id))["id"]!.GetValue<int>();
        using var scope = factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITicketsRepository>();
        var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var ticket = (await repo.GetEntityAsync(id, ownerId, UserRole.Client))!;
        var message = (await repo.GetMessageEntityAsync(id, messageId))!;
        message.Body = "Concurrent edit must roll back";
        // Do not modify the ticket: only this message's own version must protect it.
        using var read = await agent.PostAsJsonAsync($"{Url}/{id}/Read", new { lastReadMessageId = messageId });
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
        var history = await Data(await owner.GetAsync($"{Url}/{id}/Conversation"));
        Assert.Equal("Hello", history["messages"]![1]!["body"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnreadMessageChange_IsNotBlockedByOtherReadsOrTicketAcceptance(bool deleting)
    {
        using var owner = factory.CreateBrowser(); int ownerId = await Register(owner, "Client");
        using var agent = factory.CreateBrowser(); await Register(agent, "Agent");
        int id = await Create(owner), target = (await Message(owner, id))["id"]!.GetValue<int>();
        int otherMessage = (await Message(owner, id))["id"]!.GetValue<int>();
        using var scope = factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITicketsRepository>();
        await repo.GetEntityAsync(id, ownerId, UserRole.Client);
        await repo.GetMessageEntityAsync(id, target);
        using var otherRead = await agent.PostAsJsonAsync($"{Url}/{id}/Read", new { lastReadMessageId = otherMessage });
        using var accept = await Accept(agent, id);
        Assert.Equal(HttpStatusCode.OK, otherRead.StatusCode);
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        var service = scope.ServiceProvider.GetRequiredService<ITicketsService>();
        var result = deleting ? await service.DeleteMessageAsync(ownerId, UserRole.Client, id, target)
            : await service.EditMessageAsync(ownerId, UserRole.Client, id, target, new EditChatMessageRequest { Body = "Unread edit succeeds" });
        Assert.Equal(200, result.StatusCode);
        Assert.False(result.Data!.IsRead);
    }

    [Fact]
    public async Task ReadingAnOldTextVersion_DoesNotLockNewUnseenText()
    {
        using var owner = factory.CreateBrowser(); await Register(owner, "Client");
        using var agent = factory.CreateBrowser(); await Register(agent, "Agent");
        int id = await Create(owner);
        var original = await Message(owner, id); int messageId = original["id"]!.GetValue<int>();
        using var edit = await owner.PatchAsJsonAsync($"{Url}/{id}/Messages/{messageId}", new { body = "New unseen text" });
        var edited = await Data(edit);
        Assert.True(edited["version"]!.GetValue<long>() > original["version"]!.GetValue<long>());
        using var staleRead = await agent.PostAsJsonAsync($"{Url}/{id}/Read", new { lastReadMessageId = messageId, version = original["version"]!.GetValue<long>() });
        Assert.Equal(HttpStatusCode.Conflict, staleRead.StatusCode);
        var history = await Data(await owner.GetAsync($"{Url}/{id}/Conversation"));
        Assert.False(history["messages"]![1]!["isRead"]!.GetValue<bool>());
        using var read = await agent.PostAsJsonAsync($"{Url}/{id}/Read", new { lastReadMessageId = messageId, version = edited["version"]!.GetValue<long>() });
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var denied = await owner.DeleteAsync($"{Url}/{id}/Messages/{messageId}");
        Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
    }

    [Fact]
    public async Task DeletedMessageSync_RetainsOnlyClearedVersionedMetadata()
    {
        using var owner = factory.CreateBrowser(); await Register(owner, "Client");
        int id = await Create(owner); var sent = await Message(owner, id); int messageId = sent["id"]!.GetValue<int>();
        using var deleted = await owner.DeleteAsync($"{Url}/{id}/Messages/{messageId}");
        var history = await Data(await owner.GetAsync($"{Url}/{id}/Conversation?includeDeleted=true"));
        var tombstone = history["messages"]!.AsArray().Single(message => message!["id"]!.GetValue<int>() == messageId)!;
        Assert.Equal("", tombstone["body"]!.GetValue<string>());
        Assert.NotNull(tombstone["deletedAt"]);
        Assert.True(tombstone["version"]!.GetValue<long>() > sent["version"]!.GetValue<long>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentAcceptance_RejectsPreparedRequestEditOrDeletion(bool deleting)
    {
        using var owner = factory.CreateBrowser(); int ownerId = await Register(owner, "Client");
        using var agent = factory.CreateBrowser(); await Register(agent, "Agent");
        int id = await Create(owner);
        using var scope = factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITicketsRepository>();
        var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var ticket = (await repo.GetEntityAsync(id, ownerId, UserRole.Client))!;
        if (deleting) repo.Remove(ticket);
        else { ticket.Title = "Must not overwrite accepted ticket"; repo.Modify(ticket); }
        using var accepted = await Accept(agent, id);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
        var persisted = await Data(await owner.GetAsync($"{Url}/Select/{id}"));
        Assert.Equal("InProgress", persisted["status"]!.GetValue<string>());
        Assert.Equal("Mutable request", persisted["title"]!.GetValue<string>());
    }

    private static Task<HttpResponseMessage> Edit(HttpClient owner, int id) => owner.PatchAsJsonAsync($"{Url}/{id}", new { title = "Updated subject" });
    private static Task<HttpResponseMessage> Accept(HttpClient agent, int id) => agent.PostAsJsonAsync($"{Url}/{id}/Accept", new { priority = "Medium" });
    private static async Task<int> Create(HttpClient owner) { using var result = await owner.PostAsJsonAsync($"{Url}/Create", new { title = "Mutable request", description = "Original description" }); Assert.Equal(HttpStatusCode.Created, result.StatusCode); return (await Data(result))["id"]!.GetValue<int>(); }
    private static async Task<JsonNode> Message(HttpClient user, int id) { using var result = await user.PostAsJsonAsync($"{Url}/{id}/Messages", new { body = "Hello", clientMessageId = Guid.NewGuid() }); Assert.Equal(HttpStatusCode.OK, result.StatusCode); return await Data(result); }
    private static async Task<JsonNode> Data(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<JsonNode>())!["data"]!;
    private static async Task<int> Register(HttpClient client, string role)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/auth/register", new { fullName = "Mutation Test", mobile = TestPhones.Next(), countryId = 105, password = "1234", confirmPassword = "1234", role });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = await Data(response);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", data["token"]!.GetValue<string>());
        return data["user"]!["id"]!.GetValue<int>();
    }
}
