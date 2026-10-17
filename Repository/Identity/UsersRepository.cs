using Database.Context;
using Database.Model.Identity;
using Microsoft.EntityFrameworkCore;
using Database.Enums.Identity;
using ViewModels.Tickets;

namespace Repository.Identity
{
    public interface IUsersRepository
    {
        Task<User?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
        Task<(IReadOnlyList<TransferAgentDto> Items, int TotalItems)> GetTransferAgentsAsync(
            int currentAgentId, TransferAgentsRequest request, CancellationToken cancellationToken = default);
        Task<bool> MobileExistsAsync(
            string mobile,
            CancellationToken cancellationToken = default);

        Task<User?> GetByMobileAsync(
            string mobile,
            CancellationToken cancellationToken = default);

        Task<User?> GetForPasswordResetAsync(string mobile, CancellationToken cancellationToken = default);
        void Add(User user);

        void Modify(User user);

    }

    public sealed class UsersRepository(DatabaseContext context) : IUsersRepository
    {
        public async Task<(IReadOnlyList<TransferAgentDto> Items, int TotalItems)> GetTransferAgentsAsync(
            int currentAgentId, TransferAgentsRequest request, CancellationToken cancellationToken = default)
        {
            var query = context.Users.AsNoTracking().Where(user => user.Id != currentAgentId &&
                user.Role == UserRole.Agent && user.Status == UserStatus.Active);
            if (!string.IsNullOrWhiteSpace(request.Search))
            {
                string term = request.Search.Trim().ToLower();
                query = query.Where(user => user.FullName.ToLower().Contains(term) || user.Mobile.Contains(term));
            }
            int total = await query.CountAsync(cancellationToken);
            var items = await query.OrderBy(user => user.FullName).ThenBy(user => user.Id)
                .Skip((request.Page - 1) * request.PerPage).Take(request.PerPage)
                .Select(user => new TransferAgentDto(user.Id, user.FullName, user.Role, user.Mobile, user.AvatarUrl))
                .ToListAsync(cancellationToken);
            return (items, total);
        }

        public Task<User?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => context.Users.SingleOrDefaultAsync(user => user.Id == id, cancellationToken);
        public Task<bool> MobileExistsAsync(
            string mobile,
            CancellationToken cancellationToken = default)
        {
            return context.Users.AnyAsync(
                user => user.Mobile == mobile,
                cancellationToken);
        }

        public Task<User?> GetByMobileAsync(
            string mobile,
            CancellationToken cancellationToken = default)
        {
            return context.Users.SingleOrDefaultAsync(
                user => user.Mobile == mobile,
                cancellationToken);
        }

        public Task<User?> GetForPasswordResetAsync(string mobile, CancellationToken cancellationToken = default) =>
            context.Users.SingleOrDefaultAsync(user => user.Mobile == mobile, cancellationToken);

        public void Add(User user)
        {
            context.Users.Add(user);
        }

        public void Modify(User user)
        {
            context.Users.Update(user);
        }

    }
}
