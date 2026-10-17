using Microsoft.Extensions.DependencyInjection;
using Repository.Identity;
using Repository.Tickets;

namespace Repository
{
    public static class RepositoryDependencyInjection
    {
        public static IServiceCollection AddRepositories(
            this IServiceCollection services)
        {
            #region Identity

            services.AddScoped<IUsersRepository, UsersRepository>();
            services.AddScoped<Repository.Common.ICountriesRepository, Repository.Common.CountriesRepository>();
            services.AddScoped<IRefreshTokensRepository, RefreshTokensRepository>();

            #endregion Identity

            #region Tickets

            services.AddScoped<ITicketsRepository, TicketsRepository>();
            services.AddScoped<IReplyRemindersRepository, ReplyRemindersRepository>();

            #endregion Tickets

            return services;
        }
    }
}
