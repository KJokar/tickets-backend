using Database.Context;
using Database.Enums.Identity;
using Database.Enums.Tickets;
using Database.Model.Tickets;
using Microsoft.EntityFrameworkCore;
using ViewModels.Tickets;

namespace Repository.Tickets;

public interface IReplyRemindersRepository
{
    ValueTask<TicketReplyReminder?> FindAsync(int ticketId, CancellationToken cancellationToken = default);
    void Add(TicketReplyReminder reminder);
    void Remove(TicketReplyReminder reminder);
    Task<IReadOnlyList<Ticket>> GetMissingAssignmentsAsync(CancellationToken cancellationToken);
    Task<DateTime?> LastAgentReplyAsync(int ticketId, int agentId, CancellationToken cancellationToken);
    Task<DateTime?> SeenCustomerAsync(int ticketId, int agentId, DateTime? lastReplyAt, CancellationToken cancellationToken);
    Task<IReadOnlyList<TicketReplyReminder>> GetDueAsync(DateTime now, CancellationToken cancellationToken);
    Task<bool> ClaimAsync(TicketReplyReminder candidate, DateTime now, DateTime next, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReplyReminderDto>> GetActiveAsync(int agentId, CancellationToken cancellationToken);
}

public sealed class ReplyRemindersRepository(DatabaseContext context) : IReplyRemindersRepository
{
    public ValueTask<TicketReplyReminder?> FindAsync(int ticketId, CancellationToken cancellationToken = default) =>
        context.TicketReplyReminders.FindAsync([ticketId], cancellationToken);
    public void Add(TicketReplyReminder reminder) => context.TicketReplyReminders.Add(reminder);
    public void Remove(TicketReplyReminder reminder) => context.TicketReplyReminders.Remove(reminder);

    private IQueryable<TicketReplyReminder> Eligible() => context.TicketReplyReminders.Where(reminder =>
        reminder.Ticket.AssignedToId == reminder.AgentId && reminder.Ticket.Status == TicketStatus.InProgress &&
        reminder.Agent.Status == UserStatus.Active && reminder.Agent.Role == UserRole.Agent);

    public async Task<IReadOnlyList<Ticket>> GetMissingAssignmentsAsync(CancellationToken cancellationToken) =>
        await context.Tickets.AsNoTracking().Where(ticket => ticket.AssignedToId != null && ticket.Status == TicketStatus.InProgress &&
            !context.TicketReplyReminders.Any(reminder => reminder.TicketId == ticket.Id))
            .OrderBy(ticket => ticket.Id).Take(100).ToListAsync(cancellationToken);

    public Task<DateTime?> LastAgentReplyAsync(int ticketId, int agentId, CancellationToken cancellationToken) =>
        context.ChatMessages.Where(message => message.TicketId == ticketId && message.AuthorUserId == agentId && message.DeletedAt == null)
            .Select(message => (DateTime?)message.CreatedAt).MaxAsync(cancellationToken);

    public async Task<DateTime?> SeenCustomerAsync(int ticketId, int agentId, DateTime? lastReplyAt, CancellationToken cancellationToken)
    {
        var cursor = await context.ChatReadReceipts.AsNoTracking().SingleOrDefaultAsync(receipt => receipt.TicketId == ticketId && receipt.UserId == agentId, cancellationToken);
        if (cursor == null) return null;
        var lastSeen = await context.ChatMessages.AsNoTracking().SingleOrDefaultAsync(message => message.Id == cursor.LastReadMessageId && message.TicketId == ticketId, cancellationToken);
        if (lastSeen == null) return null;
        bool seen = await context.ChatMessages.AnyAsync(message => message.TicketId == ticketId &&
            message.AuthorUserId == message.Ticket.CreatedById && message.DeletedAt == null &&
            (!lastReplyAt.HasValue || message.CreatedAt > lastReplyAt.Value) &&
            (message.CreatedAt < lastSeen.CreatedAt || message.CreatedAt == lastSeen.CreatedAt && message.Id <= lastSeen.Id), cancellationToken);
        return seen ? cursor.ReadAt : null;
    }

    public async Task<IReadOnlyList<TicketReplyReminder>> GetDueAsync(DateTime now, CancellationToken cancellationToken) =>
        await Eligible().AsNoTracking().Where(reminder => reminder.SeenAt != null && reminder.NextReminderAt <= now)
            .OrderBy(reminder => reminder.NextReminderAt).ThenBy(reminder => reminder.TicketId).Take(100).ToListAsync(cancellationToken);

    public async Task<bool> ClaimAsync(TicketReplyReminder candidate, DateTime now, DateTime next, CancellationToken cancellationToken)
    {
        Guid notificationId = Guid.NewGuid(), revision = Guid.NewGuid();
        return await context.TicketReplyReminders.Where(reminder => reminder.TicketId == candidate.TicketId && reminder.Revision == candidate.Revision &&
            reminder.NextReminderAt <= now && reminder.SeenAt != null &&
            context.Tickets.Any(ticket => ticket.Id == reminder.TicketId && ticket.AssignedToId == reminder.AgentId && ticket.Status == TicketStatus.InProgress) &&
            context.Users.Any(user => user.Id == reminder.AgentId && user.Role == UserRole.Agent && user.Status == UserStatus.Active))
            .ExecuteUpdateAsync(update => update.SetProperty(reminder => reminder.NextReminderAt, next)
                .SetProperty(reminder => reminder.RaisedAt, now).SetProperty(reminder => reminder.NotificationId, notificationId)
                .SetProperty(reminder => reminder.Revision, revision), cancellationToken) == 1;
    }

    public async Task<IReadOnlyList<ReplyReminderDto>> GetActiveAsync(int agentId, CancellationToken cancellationToken) =>
        await Eligible().AsNoTracking().Where(reminder => reminder.AgentId == agentId && reminder.NotificationId != null && reminder.SeenAt != null && reminder.RaisedAt != null)
            .OrderBy(reminder => reminder.RaisedAt).ThenBy(reminder => reminder.TicketId).Take(100)
            .Select(reminder => new ReplyReminderDto(reminder.TicketId, reminder.Ticket.Title, reminder.Ticket.Owner.FullName,
                reminder.NotificationId!.Value, reminder.RaisedAt!.Value)).ToListAsync(cancellationToken);
}
