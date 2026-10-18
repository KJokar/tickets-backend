using System.Text.Json;
using System.Text.Json.Serialization;
using Asp.Versioning;
using Database.Context;
using Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Repository;
using Service;
using Service.Tickets;
using TicketingApi.Realtime;
using TicketingApi.ExceptionHandling;
using TicketingApi.Security;
using TicketingApi.Workers;
using ViewModels.Common;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<ReplyReminderOptions>().Bind(builder.Configuration.GetSection("ReplyReminders"))
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddHostedService<TicketReplyReminderWorker>();
builder.Services.AddSignalR(options => options.MaximumReceiveMessageSize = 32768)
    .AddJsonProtocol(options => ConfigureJson(options.PayloadSerializerOptions));
builder.Services.AddScoped<ITicketEvents, TicketEvents>();
string connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "";
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("Configure ConnectionStrings:DefaultConnection through user secrets or environment variables.");
builder.Services.AddDbContext<DatabaseContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<IDatabaseUnitOfWork>(provider => provider.GetRequiredService<DatabaseContext>());
builder.Services.AddTicketingSecurity(builder.Configuration, builder.Environment);
builder.Services.AddInfrastructure();
builder.Services.AddRepositories();
builder.Services.AddServices();
builder.Services.AddSingleton(provider => new Ticketing.Media.MediaTokens(provider.GetRequiredService<IConfiguration>()["Media:SigningKey"] ?? ""));

builder.Services.AddControllers(options =>
    {
        options.SuppressAsyncSuffixInActionNames = false;
    })
    .AddJsonOptions(options => ConfigureJson(options.JsonSerializerOptions))
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            string message = context.ModelState.Values.SelectMany(value => value.Errors)
                .Select(error => error.ErrorMessage)
                .FirstOrDefault(error => !string.IsNullOrWhiteSpace(error)) ?? "The request is invalid.";
            return new BadRequestObjectResult(ApiResultDto<object>.Failure(message, 400));
        };
    });
builder.Services.ConfigureHttpJsonOptions(options => ConfigureJson(options.SerializerOptions));
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.ReportApiVersions = true;
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ApiVersionReader = new HeaderApiVersionReader("api-version");
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Ticketing API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
        Description = "Register or log in, then paste data.token here to call protected endpoints."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() }
        });
});

var app = builder.Build();
if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<DatabaseContext>().Database.MigrateAsync();
}

app.UseApiResultExceptionHandler();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.CacheControl = "no-store";
    await next();
});
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Ticketing API v1");
        options.RoutePrefix = "swagger";
    });
}
app.UseRouting();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<TicketsHub>("/hubs/tickets", options => options.CloseOnAuthenticationExpiration = true);
app.Run();

static void ConfigureJson(JsonSerializerOptions options)
{
    options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
}

public partial class Program;
