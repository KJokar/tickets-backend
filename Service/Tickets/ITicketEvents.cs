using ViewModels.Tickets;

namespace Service.Tickets
{
    public interface ITicketEvents
    {
        Task CreatedAsync(TicketDto ticket, CancellationToken cancellationToken = default);
        Task ChangedAsync(TicketDto ticket, CancellationToken cancellationToken = default);
        Task AssignmentChangedAsync(TicketDto ticket, int previousAgentId, CancellationToken cancellationToken = default);
        Task ReplyRemindersChangedAsync(int agentId, int ticketId, CancellationToken cancellationToken = default);
        Task DeletedAsync(int ticketId, int ownerId, CancellationToken cancellationToken = default);
        Task MessageSentAsync(TicketDto ticket, ChatMessageDto message, CancellationToken cancellationToken = default);
        Task ReadAsync(TicketDto ticket, ChatReadReceiptDto receipt, CancellationToken cancellationToken = default);
        Task TypingAsync(TicketDto ticket, ChatTypingDto typing, CancellationToken cancellationToken = default);
    }
}
