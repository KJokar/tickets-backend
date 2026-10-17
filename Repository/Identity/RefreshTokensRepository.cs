using Database.Context;
using Database.Model.Identity;
using Microsoft.EntityFrameworkCore;

namespace Repository.Identity
{
    public interface IRefreshTokensRepository
    {
        Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default);
        void Add(RefreshToken token);
        void Modify(RefreshToken token);
        Task RevokeForUserAsync(int userId, CancellationToken cancellationToken = default);
    }

    public sealed class RefreshTokensRepository(DatabaseContext context) : IRefreshTokensRepository
    {
        public async Task RevokeForUserAsync(int userId, CancellationToken cancellationToken = default)
        {
            var tokens = await context.RefreshTokens.Where(token => token.UserId == userId && token.UsedAt == null).ToListAsync(cancellationToken);
            foreach (var token in tokens) token.UsedAt = DateTime.UtcNow;
        }
        public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        {
            return context.RefreshTokens.Include(token => token.User)
                .SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);
        }

        public void Add(RefreshToken token)
        {
            context.RefreshTokens.Add(token);
        }

        public void Modify(RefreshToken token)
        {
            context.Entry(token).Property(item => item.UsedAt).IsModified = true;
        }
    }
}
