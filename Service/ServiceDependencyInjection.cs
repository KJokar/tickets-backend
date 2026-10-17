using Microsoft.Extensions.DependencyInjection;
using Service.Identity;
using Service.Tickets;

namespace Service
{
    public static class ServiceDependencyInjection
    {
        public static IServiceCollection AddServices(
            this IServiceCollection services)
        {
            #region Identity

            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<Service.Common.ICountriesService, Service.Common.CountriesService>();
            services.AddScoped<IPasswordResetService, PasswordResetService>();
            services.AddScoped<Service.Media.IMediaService, Service.Media.MediaService>();

            #endregion Identity

            #region Tickets

            services.AddScoped<ITicketsService, TicketsService>();
            services.AddScoped<ITicketPresenceService, TicketPresenceService>();
            services.AddScoped<IReplyRemindersService, ReplyRemindersService>();

            #endregion Tickets

            return services;
        }
    }
}
