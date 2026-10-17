using Infrastructure.Authentication;
using Infrastructure.Realtime;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure;

public static class InfrastructureDependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services)
    {
        services.AddScoped<IJwtTokenProvider, JwtTokenProvider>();
        services.AddScoped<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IUserPresence, UserPresence>();
        services.AddHttpClient<Infrastructure.Messaging.ISmsSender, Infrastructure.Messaging.SmsIrSender>()
            .RemoveAllLoggers();

        return services;
    }
}
