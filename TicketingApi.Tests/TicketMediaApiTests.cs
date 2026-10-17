using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Database.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TicketingApi.Tests;

public sealed class TicketMediaApiTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private const string Url = "/Tickets/v1/Tickets";
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aZ0cAAAAASUVORK5CYII=");

    [Fact]
    public async Task Create_PersistsMultipleFilesOnFirstMessage_WithoutExposingContentInConversation()
    {
        using var owner = await User("Client");
        byte[] pdf = "%PDF-1.7\nExample"u8.ToArray();
        using var form = Form(("تصویر.png", Png), ("guide.pdf", pdf));
        using var created = await owner.PostAsync(Url + "/CreateWithMedia", form);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        int id = (await Data(created))["id"]!.GetValue<int>();
        var history = await Data(await owner.GetAsync($"{Url}/{id}/Conversation"));
        var message = Assert.Single(history["messages"]!.AsArray())!;
        Assert.Equal("First message with attachments", message["body"]!.GetValue<string>());
        Assert.Equal(2, message["attachments"]!.AsArray().Count);
        foreach (var attachment in message["attachments"]!.AsArray())
        {
            Assert.Null(attachment!["content"]);
            using var download = await owner.GetAsync(Path(id, message, attachment));
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            byte[] expected = attachment["contentType"]!.GetValue<string>() == "image/png" ? Png : pdf;
            Assert.Equal(expected, await download.Content.ReadAsByteArrayAsync());
            Assert.Equal(expected.Length, attachment["size"]!.GetValue<int>());
            Assert.NotNull(download.Content.Headers.ContentDisposition);
            Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
        }
        Assert.False(message["isRead"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Files_FollowExclusiveTicketPermissions_AndRequireAuthentication()
    {
        using var owner = await User("Client");
        using var stranger = await User("Client");
        using var agent = await User("Agent");
        using var otherAgent = await User("Agent");
        var (id, message, attachment) = await Create(owner);
        string path = Path(id, message, attachment);
        using var anonymous = factory.CreateBrowser();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await agent.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await otherAgent.GetAsync(path)).StatusCode);
        using var accepted = await agent.PostAsJsonAsync($"{Url}/{id}/Accept", new { priority = "Medium" });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherAgent.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await agent.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(Path(id + 1, message, attachment))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"{Url}/{id}/Messages/2147483647/Attachments/{attachment["id"]}")).StatusCode);
    }

    [Fact]
    public async Task UnreadMessageEdit_PreservesAttachments_AndDeleteRevokesDownloads()
    {
        using var owner = await User("Client");
        var (id, message, attachment) = await Create(owner);
        int messageId = message["id"]!.GetValue<int>();
        using var edited = await owner.PatchAsJsonAsync($"{Url}/{id}/Messages/{messageId}", new { body = "Edited first message" });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Single((await Data(edited))["attachments"]!.AsArray());
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(Path(id, message, attachment))).StatusCode);
        using var deleted = await owner.DeleteAsync($"{Url}/{id}/Messages/{messageId}");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Empty((await Data(deleted))["attachments"]!.AsArray());
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(Path(id, message, attachment))).StatusCode);
        var tombstone = Assert.Single((await Data(await owner.GetAsync($"{Url}/{id}/Conversation?includeDeleted=true")))["messages"]!.AsArray())!;
        Assert.Empty(tombstone["attachments"]!.AsArray());
    }

    [Fact]
    public async Task TicketDeletion_CascadesAttachmentData()
    {
        using var owner = await User("Client");
        var (id, message, attachment) = await Create(owner);
        Guid attachmentId = attachment["id"]!.GetValue<Guid>();
        Assert.Equal(HttpStatusCode.OK, (await owner.DeleteAsync($"{Url}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(Path(id, message, attachment))).StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<DatabaseContext>().MessageAttachments.AnyAsync(file => file.Id == attachmentId));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("html")]
    [InlineData("count")]
    [InlineData("size")]
    [InlineData("title")]
    public async Task InvalidUpload_LeavesNoPartialTicketOrAttachment(string invalid)
    {
        using var owner = await User("Client");
        (string, byte[])[] files = invalid switch
        {
            "empty" => [("empty.png", [])],
            "html" => [("spoofed.png", Encoding.UTF8.GetBytes("<script>alert(1)</script>"))],
            "count" => Enumerable.Range(0, 6).Select(index => ($"image{index}.png", Png)).ToArray(),
            "size" => [("large.png", new byte[20 * 1024 * 1024 + 1])],
            _ => [("image.png", Png)]
        };
        using var form = invalid == "title" ? new MultipartFormDataContent
        {
            { new StringContent("x"), "title" }, { new StringContent("First message"), "description" },
            { new ByteArrayContent(Png), "files", "image.png" }
        } : Form(files);
        using var response = await owner.PostAsync(Url + "/CreateWithMedia", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var tickets = await Data(await owner.GetAsync(Url + "/List"));
        Assert.Empty(tickets.AsArray());
    }

    [Fact]
    public async Task AgentCannotCreateWithFiles()
    {
        using var agent = await User("Agent"); using var form = Form(("image.png", Png));
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.PostAsync(Url + "/CreateWithMedia", form)).StatusCode);
    }

    [Theory]
    [InlineData("video.mp4", "000000146674797069736F6D", "video/mp4")]
    [InlineData("video.webm", "1A45DFA30000", "video/webm")]
    [InlineData("recording.wav", "524946460000000057415645", "audio/wav")]
    [InlineData("recording.mp3", "49443304000000000000", "audio/mpeg")]
    [InlineData("recording.m4a", "00000014667479704D344120", "audio/mp4")]
    [InlineData("recording.ogg", "4F6767530002", "audio/ogg")]
    [InlineData("photo.webp", "524946460000000057454250", "image/webp")]
    [InlineData("photo.heif", "00000014667479706D696631", "image/heif")]
    [InlineData("photo.avif", "000000146674797061766966", "image/avif")]
    public async Task ContentType_UsesSignatureInsteadOfBrowserClaim(string name, string signature, string expectedType)
    {
        using var owner = await User("Client");
        using var form = Form((name, Convert.FromHexString(signature)));
        using var response = await owner.PostAsync(Url + "/CreateWithMedia", form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        int id = (await Data(response))["id"]!.GetValue<int>();
        var history = await Data(await owner.GetAsync($"{Url}/{id}/Conversation"));
        var message = Assert.Single(history["messages"]!.AsArray())!;
        var attachment = Assert.Single(message["attachments"]!.AsArray())!;
        Assert.Equal(expectedType, attachment["contentType"]!.GetValue<string>());
        using var download = await owner.GetAsync(Path(id, message, attachment));
        Assert.Equal(expectedType, download.Content.Headers.ContentType!.MediaType);
    }

    private static string Path(int id, JsonNode message, JsonNode attachment) => $"{Url}/{id}/Messages/{message["id"]}/Attachments/{attachment["id"]}";
    private async Task<(int Id, JsonNode Message, JsonNode Attachment)> Create(HttpClient owner)
    {
        using var form = Form(("image.png", Png));
        using var response = await owner.PostAsync(Url + "/CreateWithMedia", form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        int id = (await Data(response))["id"]!.GetValue<int>();
        var history = await Data(await owner.GetAsync($"{Url}/{id}/Conversation"));
        var message = Assert.Single(history["messages"]!.AsArray())!;
        return (id, message, Assert.Single(message["attachments"]!.AsArray())!);
    }
    private static MultipartFormDataContent Form(params (string Name, byte[] Content)[] files)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent("Request with media"), "title");
        form.Add(new StringContent("First message with attachments"), "description");
        foreach (var file in files)
        {
            var content = new ByteArrayContent(file.Content);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/png"); // Intentionally untrusted.
            form.Add(content, "files", file.Name);
        }
        return form;
    }
    private async Task<HttpClient> User(string role)
    {
        var client = factory.CreateBrowser();
        using var response = await client.PostAsJsonAsync("/api/v1/auth/register", new { fullName = "Media Test", mobile = TestPhones.Next(), countryId = 105, password = "1234", confirmPassword = "1234", role });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await Data(response))["token"]!.GetValue<string>());
        return client;
    }
    private static async Task<JsonNode> Data(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<JsonNode>())!["data"]!;
}
