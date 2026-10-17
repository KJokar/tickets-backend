using System.ComponentModel.DataAnnotations;
using Database.Context;
using Database.Enums.Identity;
using Database.Model.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Repository.Tickets;
using ViewModels.Common;
using ViewModels.Tickets;

namespace Service.Tickets;

public sealed class ReplyReminderOptions
{
    public bool Enabled { get; set; } = true;
    [Range(1, 3600)] public int DelaySeconds { get; set; } = 300;
    [Range(1, 60)] public int PollIntervalSeconds { get; set; } = 15;
}

public interface IReplyRemindersService
{
    Task StartAssignmentAsync(Ticket ticket, CancellationToken cancellationToken, bool existing = false);
    Task<bool> SeenAsync(Ticket ticket, ChatMessage message, CancellationToken cancellationToken);
    Task RepliedAsync(int ticketId, int agentId, DateTime repliedAt, CancellationToken cancellationToken);
    Task RemoveAsync(int ticketId, CancellationToken cancellationToken);
    Task<int> ProcessDueAsync(CancellationToken cancellationToken = default);
    Task<ApiResultDto<IReadOnlyList<ReplyReminderDto>>> ListAsync(int agentId, UserRole role, CancellationToken cancellationToken);
    Task<ApiResultDto<DismissReplyReminderResponse>> DismissAsync(int agentId, UserRole role, int ticketId, DismissReplyReminderRequest request, CancellationToken cancellationToken);
}

public sealed class ReplyRemindersService(IReplyRemindersRepository repository, ITicketsRepository tickets,
    IDatabaseUnitOfWork database, ITicketEvents events, TimeProvider clock, IOptions<ReplyReminderOptions> options) : IReplyRemindersService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private DateTime Next(DateTime from) => from.AddSeconds(options.Value.DelaySeconds);

    private static void Clear(TicketReplyReminder reminder)
    {
        reminder.SeenAt = null; reminder.NextReminderAt = null;
        reminder.NotificationId = null; reminder.RaisedAt = null;
    }

    public async Task StartAssignmentAsync(Ticket ticket, CancellationToken cancellationToken, bool existing = false)
    {
        if (ticket.AssignedToId is not int agentId) return;
        var reminder = await repository.FindAsync(ticket.Id, cancellationToken);
        if (reminder == null) { reminder = new() { TicketId = ticket.Id }; repository.Add(reminder); }
        reminder.AgentId = agentId;
        reminder.LastReplyAt = existing ? await repository.LastAgentReplyAsync(ticket.Id, agentId, cancellationToken) : null;
        Clear(reminder);
        DateTime? readAt = await repository.SeenCustomerAsync(ticket.Id, agentId, reminder.LastReplyAt, cancellationToken);
        if (readAt.HasValue)
        {
            DateTime start = existing ? readAt.Value : Now;
            if (ticket.AcceptedAt > start) start = ticket.AcceptedAt.Value;
            reminder.SeenAt = start; reminder.NextReminderAt = Next(start);
        }
    }

    public async Task<bool> SeenAsync(Ticket ticket, ChatMessage message, CancellationToken cancellationToken)
    {
        var reminder = await repository.FindAsync(ticket.Id, cancellationToken);
        bool initialized = reminder == null || reminder.AgentId != ticket.AssignedToId;
        if (initialized)
        {
            await StartAssignmentAsync(ticket, cancellationToken, existing: true);
            reminder = await repository.FindAsync(ticket.Id, cancellationToken);
        }
        if (reminder == null || reminder.SeenAt != null || reminder.LastReplyAt >= message.CreatedAt) return initialized;
        reminder.SeenAt = Now; reminder.NextReminderAt = Next(reminder.SeenAt.Value);
        return true;
    }

    public async Task RepliedAsync(int ticketId, int agentId, DateTime repliedAt, CancellationToken cancellationToken)
    {
        var reminder = await repository.FindAsync(ticketId, cancellationToken);
        if (reminder == null) { reminder = new() { TicketId = ticketId, AgentId = agentId }; repository.Add(reminder); }
        reminder.AgentId = agentId;
        reminder.LastReplyAt = repliedAt;
        Clear(reminder);
    }

    public async Task RemoveAsync(int ticketId, CancellationToken cancellationToken)
    {
        var reminder = await repository.FindAsync(ticketId, cancellationToken);
        if (reminder != null) repository.Remove(reminder);
    }

    public async Task<int> ProcessDueAsync(CancellationToken cancellationToken = default)
    {
        // Existing accepted conversations are picked up after deployment as well.
        foreach (Ticket ticket in await repository.GetMissingAssignmentsAsync(cancellationToken))
        {
            await StartAssignmentAsync(ticket, cancellationToken, existing: true);
            // Bootstrap competes with acceptance/handoff on the same ticket revision.
            // A stale assignment must never overwrite the new agent's reminder state.
            tickets.Modify(ticket);
            await database.SaveChangesAsync(cancellationToken);
        }
        DateTime now = Now;
        int sent = 0;
        foreach (var reminder in await repository.GetDueAsync(now, cancellationToken))
        {
            if (!await repository.ClaimAsync(reminder, now, Next(now), cancellationToken)) continue;
            await events.ReplyRemindersChangedAsync(reminder.AgentId, reminder.TicketId, cancellationToken);
            sent++;
        }
        return sent;
    }

    public async Task<ApiResultDto<IReadOnlyList<ReplyReminderDto>>> ListAsync(int agentId, UserRole role, CancellationToken cancellationToken) =>
        role != UserRole.Agent ? ApiResultDto<IReadOnlyList<ReplyReminderDto>>.Failure("یادآوری فقط برای پشتیبان فعال است.", 403)
            : ApiResultDto<IReadOnlyList<ReplyReminderDto>>.Success(await repository.GetActiveAsync(agentId, cancellationToken));

    public async Task<ApiResultDto<DismissReplyReminderResponse>> DismissAsync(int agentId, UserRole role, int ticketId,
        DismissReplyReminderRequest request, CancellationToken cancellationToken)
    {
        if (role != UserRole.Agent) return ApiResultDto<DismissReplyReminderResponse>.Failure("دسترسی مجاز نیست.", 403);
        if (request.NotificationId == null || request.NotificationId == Guid.Empty)
            return ApiResultDto<DismissReplyReminderResponse>.Failure("شناسه یادآوری معتبر نیست.", 400);
        var ticket = await tickets.GetAsync(ticketId, agentId, role, cancellationToken);
        if (ticket?.AssignedToId != agentId) return ApiResultDto<DismissReplyReminderResponse>.Failure("درخواست در مسئولیت شما نیست.", 404);
        var reminder = await repository.FindAsync(ticketId, cancellationToken);
        if (ticket.Status != Database.Enums.Tickets.TicketStatus.InProgress || reminder?.AgentId != agentId ||
            reminder.NotificationId != request.NotificationId || reminder.SeenAt == null)
            return ApiResultDto<DismissReplyReminderResponse>.Failure("این یادآوری دیگر فعال نیست.", 409);
        reminder.NotificationId = null; reminder.RaisedAt = null; reminder.NextReminderAt = Next(Now);
        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return ApiResultDto<DismissReplyReminderResponse>.Failure("یادآوری هم‌زمان تغییر کرده است.", 409); }
        await events.ReplyRemindersChangedAsync(agentId, ticketId, cancellationToken);
        return ApiResultDto<DismissReplyReminderResponse>.Success(new(ticketId, reminder.NextReminderAt.Value));
    }
}
