using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Infrastructure.Authentication;
using Infrastructure.Extension;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ViewModels.Common;

namespace TicketingApi.Security;

public static class AuthenticationSetup
{
    public static IServiceCollection AddTicketingSecurity(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection("JWT"))
            .ValidateDataAnnotations().Validate(settings => settings.HasStrongKey(), "JWT:Secret must have at least 32 UTF-8 bytes.")
            .ValidateOnStart();
        var jwt = configuration.GetSection("JWT").Get<JwtOptions>() ?? new JwtOptions();
        if (!jwt.HasStrongKey())
            throw new InvalidOperationException("Configure JWT:Secret (at least 32 random bytes) through user secrets or environment variables.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.RequireHttpsMetadata = !environment.IsDevelopment();
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwt.ValidIssuer,
                ValidAudience = jwt.ValidAudience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                RoleClaimType = ClaimTypes.Role,
                NameClaimType = ClaimTypes.Name,
                ClockSkew = TimeSpan.Zero
            };
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    if (context.Request.Path.StartsWithSegments("/hubs/tickets") &&
                        !string.IsNullOrEmpty(context.Request.Query["access_token"]))
                        context.Token = context.Request.Query["access_token"];
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    int? userId = context.Principal?.GetUserId();
                    string? role = context.Principal?.GetUserRole();
                    if (userId is null or <= 0 || role is not ("Client" or "Agent"))
                        context.Fail("The authentication claims are invalid.");
                    return Task.CompletedTask;
                },
                OnChallenge = async context =>
                {
                    context.HandleResponse();
                    context.Response.StatusCode = 401;
                    context.Response.Headers.WWWAuthenticate = "Bearer";
                    await context.Response.WriteAsJsonAsync(ApiResultDto<object>.Failure("Authentication is required.", 401));
                },
                OnForbidden = context =>
                {
                    context.Response.StatusCode = 403;
                    return context.Response.WriteAsJsonAsync(ApiResultDto<object>.Failure("Access is forbidden.", 403));
                }
            };
        });
        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthPolicies.Client, policy => policy.RequireAuthenticatedUser().RequireRole("Client"));
            options.AddPolicy(AuthPolicies.Agent, policy => policy.RequireAuthenticatedUser().RequireRole("Agent"));
            options.AddPolicy(AuthPolicies.TicketUser, policy => policy.RequireAuthenticatedUser().RequireRole("Client", "Agent"));
            options.AddPolicy(TicketPermissions.Create, policy => policy.RequireAuthenticatedUser()
                .RequireClaim(TicketPermissions.ClaimType, TicketPermissions.Create));
            options.AddPolicy(AuthPolicies.ReadTickets, policy => policy.RequireAuthenticatedUser()
                .RequireClaim(TicketPermissions.ClaimType, TicketPermissions.ReadOwn, TicketPermissions.ReadQueue));
            foreach (string permission in new[] { TicketPermissions.Accept, TicketPermissions.Chat, TicketPermissions.ChangePriority })
                options.AddPolicy(permission, policy => policy.RequireAuthenticatedUser()
                    .RequireClaim(TicketPermissions.ClaimType, permission));
            options.AddPolicy(TicketPermissions.ChangeStatus, policy => policy.RequireAuthenticatedUser()
                .RequireClaim(TicketPermissions.ClaimType, TicketPermissions.ChangeStatus));
        });

        int permits = configuration.GetValue("Authentication:RateLimit:PermitLimit", 10);
        int seconds = configuration.GetValue("Authentication:RateLimit:WindowSeconds", 60);
        if (permits is < 1 or > 100 || seconds is < 1 or > 3600)
            throw new InvalidOperationException("Invalid authentication rate-limit configuration.");
        services.AddRateLimiter(options =>
        {
            options.AddPolicy(AuthPolicies.AuthenticationRateLimit, context => RateLimitPartition.GetSlidingWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = permits,
                    Window = TimeSpan.FromSeconds(seconds),
                    SegmentsPerWindow = 6,
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = 429;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                await context.HttpContext.Response.WriteAsJsonAsync(ApiResultDto<object>.Failure("Too many authentication attempts. Try again later.", 429), cancellationToken);
            };
        });
        string[] origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Any(origin => !Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
            uri.AbsolutePath != "/" || origin.Contains('*') ||
            (!environment.IsDevelopment() && uri.Scheme != "https") ||
            (uri.Scheme != "http" && uri.Scheme != "https")))
            throw new InvalidOperationException("CORS requires explicit origins (HTTPS in production).");
        services.AddCors(options => options.AddDefaultPolicy(policy =>
        {
            if (origins.Length > 0)
                policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
        }));
        return services;
    }
}
