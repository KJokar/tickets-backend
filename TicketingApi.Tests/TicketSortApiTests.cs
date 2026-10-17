using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Database.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TicketingApi.Tests;

public sealed class TicketSortApiTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private const string Url = "/Tickets/v1/Tickets";
    [Fact]
    public async Task Sorting_UsesPriorityRankAndUpdateTime_BeforePagination_WithoutLeakingPriority()
    {
        using var owner = factory.CreateBrowser(); await Register(owner, "Client");
        using var agent = factory.CreateBrowser(); await Register(agent, "Agent");
        string search = $"Sort-{Guid.NewGuid():N}";
        List<int> ids = [];
        foreach (string? priority in new[] { "High", "Medium", "Low", "High", null })
        {
            var created = await owner.PostAsJsonAsync($"{Url}/Create", new { title = search, description = "Sorting request" });
            int id = (await Data(created))["id"]!.GetValue<int>(); ids.Add(id);
            if (priority != null) Assert.Equal(HttpStatusCode.OK, (await agent.PostAsJsonAsync($"{Url}/{id}/Accept", new { priority })).StatusCode);
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            for (int index = 0; index < ids.Count; index++)
            {
                var ticket = await db.Tickets.SingleAsync(ticket => ticket.Id == ids[index]);
                ticket.CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(index);
                ticket.UpdatedAt = index == 4 ? null : ticket.CreatedAt.AddDays(new[] { 1, 2, 0, -2 }[index]);
            }
            await db.SaveChangesAsync();
        }
        string filter = $"search={search}";
        Assert.Equal(new[] { ids[4], ids[1], ids[2], ids[3], ids[0] }, await Items(agent, filter));
        Assert.Equal(new[] { ids[0], ids[3], ids[2], ids[1], ids[4] }, await Items(agent, $"{filter}&sortBy=UpdatedAt&sortOrder=Asc"));
        Assert.Equal(new[] { ids[3], ids[0], ids[1], ids[2], ids[4] }, await Items(agent, $"{filter}&sortBy=Priority&sortOrder=Desc"));
        Assert.Equal(new[] { ids[2], ids[1], ids[3], ids[0], ids[4] }, await Items(agent, $"{filter}&sortBy=Priority&sortOrder=Asc"));
        Assert.Equal(new[] { ids[1], ids[2] }, await Items(agent, $"{filter}&sortBy=Priority&perPage=2&page=2"));
        Assert.Equal(new[] { ids[3], ids[0], ids[1], ids[2] }, await Items(agent, $"{filter}&sortBy=Priority&mineOnly=true&status=InProgress"));
        Assert.Equal(new[] { ids[4] }, await Items(agent, $"{filter}&sortBy=Priority", "Unassigned"));
        Assert.Equal(new[] { ids[0], ids[3], ids[2], ids[1], ids[4] }, await Items(owner, $"{filter}&sortBy=UpdatedAt&sortOrder=Asc"));
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync($"{Url}/List?sortBy=Priority")).StatusCode);
        foreach (string invalid in new[] { "sortBy=Unknown", "sortOrder=Unknown", "sortBy=99", "sortOrder=99" })
            Assert.Equal(HttpStatusCode.BadRequest, (await agent.GetAsync($"{Url}/List?{invalid}")).StatusCode);
    }
    private static async Task<int[]> Items(HttpClient client, string query, string endpoint = "List") =>
        (await Data(await client.GetAsync($"{Url}/{endpoint}?{query}"))).AsArray().Select(item => item!["id"]!.GetValue<int>()).ToArray();
    private static async Task Register(HttpClient client, string role)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new { fullName = "Sort Test", mobile = TestPhones.Next(), countryId = 105, password = "1234", confirmPassword = "1234", role });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await Data(response))["token"]!.GetValue<string>());
    }
    private static async Task<JsonNode> Data(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonNode>())!["data"]!;
    }
}
