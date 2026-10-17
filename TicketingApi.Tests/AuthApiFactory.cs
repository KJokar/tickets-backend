using Database.Context;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Infrastructure.Messaging;

namespace TicketingApi.Tests;

public sealed class AuthApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public int PermitLimit { get; init; } = 100;
    public ISmsSender? SmsSender { get; init; }
    public TimeProvider? Clock { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=unused;Database=unused");
        builder.UseSetting("JWT:Secret", "test-only-signing-key-at-least-32-bytes-long");
        builder.UseSetting("JWT:ValidIssuer", "TicketingTests");
        builder.UseSetting("JWT:ValidAudience", "TicketingTests");
        builder.UseSetting("Media:SigningKey", "test-only-media-key-at-least-32-bytes-long");
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
        builder.UseSetting("ReplyReminders:Enabled", "false");
        builder.UseSetting("Authentication:RateLimit:PermitLimit", PermitLimit.ToString());
        builder.ConfigureServices(services =>
        {
            if (Clock != null) { services.RemoveAll<TimeProvider>(); services.AddSingleton(Clock); }
            if (SmsSender != null) { services.RemoveAll<ISmsSender>(); services.AddSingleton(SmsSender); }
            services.RemoveAll<DbContextOptions<DatabaseContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<DatabaseContext>>();
            _connection.Open();
            services.AddDbContext<DatabaseContext>(options => options.UseSqlite(_connection));
        });
    }

    public HttpClient CreateBrowser()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<DatabaseContext>().Database.EnsureCreated();
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _connection.Dispose();
    }
}
