using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Infrastructure.Realtime;
using Xunit;

namespace TicketingApi.Tests;

public sealed class ChatPresenceTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    [Fact]
    public async Task Hub_PresenceTracksPeerAndLastTab_DisconnectReconnect_AndProtectsPrivateChat()
    {
        var client = await Register("Client"); using var clientHttp = client.Http;
        var agent = await Register("Agent"); using var agentHttp = agent.Http;
        var outsider = await Register("Agent"); using var outsiderHttp = outsider.Http;
        var created = await clientHttp.PostAsJsonAsync("/Tickets/v1/Tickets/Create", new { title = "Presence test", description = "Hello" });
        int ticketId = (await created.Content.ReadFromJsonAsync<JsonNode>())!["data"]!["id"]!.GetValue<int>();
        (await agentHttp.PostAsJsonAsync($"/Tickets/v1/Tickets/{ticketId}/Accept", new { priority = "Medium" })).EnsureSuccessStatusCode();
        using var owner = await Connect(client.Token);
        var offline = await owner.Call("GetPresence", ticketId);
        Assert.False(offline["data"]!["isOnline"]!.GetValue<bool>());
        Assert.Equal(agent.Id, offline["data"]!["userId"]!.GetValue<int>());

        using var firstTab = await Connect(agent.Token);
        Assert.Equal(agent.Id, (await owner.Event("PresenceChanged"))["userId"]!.GetValue<int>());
        Assert.True((await owner.Call("GetPresence", ticketId))["data"]!["isOnline"]!.GetValue<bool>());
        Assert.True((await firstTab.Call("GetPresence", ticketId))["data"]!["isOnline"]!.GetValue<bool>());
        using var secondTab = await Connect(agent.Token);
        await firstTab.Close();
        Assert.True((await owner.Call("GetPresence", ticketId))["data"]!["isOnline"]!.GetValue<bool>());
        await secondTab.Close();
        await owner.Event("PresenceChanged");
        Assert.False((await owner.Call("GetPresence", ticketId))["data"]!["isOnline"]!.GetValue<bool>());

        using var reconnected = await Connect(agent.Token);
        await owner.Event("PresenceChanged");
        Assert.True((await owner.Call("GetPresence", ticketId))["data"]!["isOnline"]!.GetValue<bool>());
        using var other = await Connect(outsider.Token);
        Assert.Equal(404, (await other.Call("GetPresence", ticketId))["statusCode"]!.GetValue<int>());
        Assert.Equal(404, (await owner.Call("GetPresence", int.MaxValue))["statusCode"]!.GetValue<int>());
        await owner.Close();
        Assert.Equal(client.Id, (await reconnected.Event("PresenceChanged"))["userId"]!.GetValue<int>());
        Assert.False((await reconnected.Call("GetPresence", ticketId))["data"]!["isOnline"]!.GetValue<bool>());
        await reconnected.Close(); await other.Close();
    }

    [Fact]
    public void Presence_DuplicateDisconnectCannotRemoveAnotherTabOrUser()
    {
        var presence = new UserPresence();
        Assert.True(presence.Connect(1, "tab-1"));
        Assert.False(presence.Connect(1, "tab-1"));
        Assert.False(presence.Connect(1, "tab-2"));
        Assert.True(presence.Connect(2, "other-user"));
        Assert.False(presence.Disconnect(1, "unknown"));
        Assert.False(presence.Disconnect(1, "tab-1"));
        Assert.False(presence.Disconnect(1, "tab-1"));
        Assert.True(presence.IsOnline(1));
        Assert.True(presence.Disconnect(1, "tab-2"));
        Assert.False(presence.IsOnline(1));
        Assert.True(presence.IsOnline(2));
    }

    private async Task<(HttpClient Http, string Token, int Id)> Register(string role)
    {
        var http = factory.CreateBrowser();
        var response = await http.PostAsJsonAsync("/api/v1/auth/register", new { fullName = "Presence Test", mobile = TestPhones.Next(), countryId = 105, password = "1234", confirmPassword = "1234", role });
        response.EnsureSuccessStatusCode();
        var data = (await response.Content.ReadFromJsonAsync<JsonNode>())!["data"]!;
        string token = data["token"]!.GetValue<string>();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (http, token, data["user"]!["id"]!.GetValue<int>());
    }

    private async Task<Wire> Connect(string token)
    {
        var socket = await factory.Server.CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost/hubs/tickets?access_token=" + Uri.EscapeDataString(token)), CancellationToken.None);
        var wire = new Wire(socket);
        await wire.Send(new { protocol = "json", version = 1 });
        Assert.Null((await wire.Read())["error"]);
        return wire;
    }

    private sealed class Wire(WebSocket socket) : IDisposable
    {
        private readonly Queue<JsonNode> incoming = new();
        private int sequence;
        public Task Send(object message) => socket.SendAsync(Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(message) + '\u001e').AsMemory(), WebSocketMessageType.Text, true, CancellationToken.None).AsTask();
        public async Task<JsonNode> Read()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (incoming.Count == 0)
            {
                using var content = new MemoryStream();
                var buffer = new byte[32768];
                ValueWebSocketReceiveResult frame;
                do { frame = await socket.ReceiveAsync(buffer.AsMemory(), timeout.Token); content.Write(buffer, 0, frame.Count); } while (!frame.EndOfMessage);
                foreach (string json in Encoding.UTF8.GetString(content.ToArray()).Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
                    incoming.Enqueue(JsonNode.Parse(json)!);
            }
            return incoming.Dequeue();
        }
        public async Task<JsonNode> Call(string method, int ticketId)
        {
            string id = (++sequence).ToString();
            await Send(new { type = 1, invocationId = id, target = method, arguments = new[] { ticketId } });
            while (true)
            {
                var message = await Read();
                if (message["type"]?.GetValue<int>() != 3 || message["invocationId"]?.GetValue<string>() != id) continue;
                Assert.Null(message["error"]); return message["result"]!;
            }
        }
        public async Task<JsonNode> Event(string target)
        {
            while (true)
            {
                var message = await Read();
                if (message["target"]?.GetValue<string>() == target) return message["arguments"]![0]!;
            }
        }
        public async Task Close()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test completed", timeout.Token);
        }
        public void Dispose() => socket.Dispose();
    }
}
