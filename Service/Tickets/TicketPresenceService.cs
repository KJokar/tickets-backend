using Database.Enums.Identity;
using Infrastructure.Realtime;
using Repository.Tickets;
using ViewModels.Common;
using ViewModels.Tickets;

namespace Service.Tickets
{
    public interface ITicketPresenceService
    {
        Task<ApiResultDto<ChatPresenceDto>> GetAsync(int userId, UserRole role, int ticketId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<int>> RecipientsAsync(int userId, CancellationToken cancellationToken = default);
    }

    public sealed class TicketPresenceService(ITicketsRepository tickets, IUserPresence presence) : ITicketPresenceService
    {
        public async Task<ApiResultDto<ChatPresenceDto>> GetAsync(int userId, UserRole role, int ticketId, CancellationToken cancellationToken = default)
        {
            var (found, peerId) = await tickets.GetPresencePeerAsync(ticketId, userId, role, cancellationToken);
            if (!found) return ApiResultDto<ChatPresenceDto>.Failure("درخواست پیدا نشد.", 404);
            return ApiResultDto<ChatPresenceDto>.Success(new(ticketId, peerId, peerId.HasValue && presence.IsOnline(peerId.Value)));
        }

        public Task<IReadOnlyList<int>> RecipientsAsync(int userId, CancellationToken cancellationToken = default) =>
            tickets.GetAssignedPeersAsync(userId, cancellationToken);
    }
}
