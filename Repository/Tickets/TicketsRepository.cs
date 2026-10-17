using Database.Context;
using Database.Enums.Identity;
using Database.Enums.Tickets;
using Database.Model.Tickets;
using Microsoft.EntityFrameworkCore;
using ViewModels.Tickets;

namespace Repository.Tickets
{
    public interface ITicketsRepository
    {
        Task<IReadOnlyList<int>> GetAssignedPeersAsync(int userId, CancellationToken cancellationToken = default);
        Task<(bool Found, int? PeerId)> GetPresencePeerAsync(int ticketId, int userId, UserRole role, CancellationToken cancellationToken = default);
        Task<bool> FileIsAttachedAsync(Guid id, CancellationToken cancellationToken = default);
        Task<bool> CanReadFileAsync(Guid id, int userId, UserRole role, CancellationToken cancellationToken = default);
        Task<MessageAttachmentContentDto?> GetAttachmentAsync(int ticketId, int messageId, Guid attachmentId, int userId, UserRole role, CancellationToken cancellationToken = default);
        void Add(Ticket ticket);
        void Modify(Ticket ticket);
        void AddMessage(ChatMessage message);
        void Remove(Ticket ticket);
        Task<bool> ReloadAsync(Ticket ticket, CancellationToken cancellationToken = default);
        Task<ChatMessage?> GetMessageEntityAsync(int ticketId, int messageId, CancellationToken cancellationToken = default);
        Task<TicketSummaryDto> GetSummaryAsync(int userId, UserRole role, CancellationToken cancellationToken = default);
        Task<(TicketSummaryDto Summary, IReadOnlyList<TicketReportDayDto> Days)> GetReportAsync(
            int userId, UserRole role, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
        Task<(IReadOnlyList<TicketReportRowDto> Items, int TotalItems)> GetReportTicketsAsync(
            int userId, UserRole role, DateOnly from, DateOnly to, int page, int perPage, CancellationToken cancellationToken = default);
        Task<TicketDto?> GetAsync(int ticketId, int userId, UserRole role, CancellationToken cancellationToken = default);
        Task<Ticket?> GetEntityAsync(int ticketId, int userId, UserRole role, CancellationToken cancellationToken = default);
        Task<Ticket?> GetForAcceptanceAsync(int ticketId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ChatMessageDto>> GetMessagesAsync(int ticketId, CancellationToken cancellationToken = default, bool includeDeleted = false);
        Task<ChatMessageDto?> GetMessageAsync(int ticketId, Guid clientMessageId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ChatReadReceiptDto>> GetReadReceiptsAsync(int ticketId, CancellationToken cancellationToken = default);
        Task<bool> MessageExistsAsync(int ticketId, int messageId, CancellationToken cancellationToken = default);
        Task<(ChatReadReceiptDto Receipt, bool Changed)> MarkReadAsync(int ticketId, int userId, int messageId, CancellationToken cancellationToken = default);
        Task<(IReadOnlyList<TicketListItemDto> Items, int TotalItems)> GetListAsync(
            int userId, UserRole role, TicketListRequest request, CancellationToken cancellationToken = default);
    }

    public sealed class TicketsRepository(DatabaseContext context) : ITicketsRepository
    {
        public async Task<(bool Found, int? PeerId)> GetPresencePeerAsync(int ticketId, int userId, UserRole role, CancellationToken cancellationToken = default)
        {
            var peer = await VisibleTo(userId, role).AsNoTracking().Where(ticket => ticket.Id == ticketId)
                .Select(ticket => new { PeerId = role == UserRole.Client ? ticket.AssignedToId : (int?)ticket.CreatedById })
                .SingleOrDefaultAsync(cancellationToken);
            return (peer != null, peer?.PeerId);
        }
        public async Task<IReadOnlyList<int>> GetAssignedPeersAsync(int userId, CancellationToken cancellationToken = default) =>
            await context.Tickets.AsNoTracking()
                .Where(ticket => ticket.AssignedToId != null && (ticket.CreatedById == userId || ticket.AssignedToId == userId))
                .Select(ticket => ticket.CreatedById == userId ? ticket.AssignedToId!.Value : ticket.CreatedById)
                .Distinct().ToListAsync(cancellationToken);
        public Task<bool> FileIsAttachedAsync(Guid id, CancellationToken cancellationToken = default) =>
            context.MessageAttachments.AnyAsync(file => file.Id == id, cancellationToken);
        public Task<bool> CanReadFileAsync(Guid id, int userId, UserRole role, CancellationToken cancellationToken = default) =>
            context.MessageAttachments.AnyAsync(file => file.Id == id && file.StorageKey != null && file.Message.DeletedAt == null &&
                VisibleTo(userId, role).Any(ticket => ticket.Id == file.Message.TicketId), cancellationToken);
        public Task<MessageAttachmentContentDto?> GetAttachmentAsync(int ticketId, int messageId, Guid attachmentId,
            int userId, UserRole role, CancellationToken cancellationToken = default) =>
            context.MessageAttachments.AsNoTracking().Where(attachment => attachment.Id == attachmentId &&
                attachment.MessageId == messageId && attachment.Message.TicketId == ticketId && attachment.Message.DeletedAt == null &&
                VisibleTo(userId, role).Any(ticket => ticket.Id == ticketId))
                .Select(attachment => new MessageAttachmentContentDto(attachment.FileName, attachment.ContentType, attachment.Content, attachment.StorageKey))
                .SingleOrDefaultAsync(cancellationToken);

        public void Add(Ticket ticket) => context.Tickets.Add(ticket);
        public void Modify(Ticket ticket) => context.Tickets.Update(ticket);
        public void AddMessage(ChatMessage message) => context.ChatMessages.Add(message);
        public void Remove(Ticket ticket) => context.Tickets.Remove(ticket);
        public async Task<bool> ReloadAsync(Ticket ticket, CancellationToken cancellationToken = default)
        {
            await context.Entry(ticket).ReloadAsync(cancellationToken);
            return context.Entry(ticket).State != EntityState.Detached;
        }
        public Task<ChatMessage?> GetMessageEntityAsync(int ticketId, int messageId, CancellationToken cancellationToken = default) =>
            context.ChatMessages.SingleOrDefaultAsync(message => message.TicketId == ticketId && message.Id == messageId, cancellationToken);

        private IQueryable<Ticket> VisibleTo(int userId, UserRole role)
        {
            return context.Tickets.Where(ticket =>
                role == UserRole.Client ? ticket.CreatedById == userId :
                role == UserRole.Agent && (ticket.AssignedToId == userId ||
                    (ticket.AssignedToId == null && ticket.Status == TicketStatus.Open)));
        }

        public async Task<TicketSummaryDto> GetSummaryAsync(int userId, UserRole role, CancellationToken cancellationToken = default)
        {
            return await VisibleTo(userId, role).AsNoTracking().GroupBy(ticket => 1)
                .Select(group => new TicketSummaryDto
                {
                    Total = group.Count(),
                    Open = group.Count(ticket => ticket.Status == TicketStatus.Open),
                    InProgress = group.Count(ticket => ticket.Status == TicketStatus.InProgress),
                    Answered = group.Count(ticket => ticket.Status == TicketStatus.Answered),
                    Closed = group.Count(ticket => ticket.Status == TicketStatus.Closed)
                }).SingleOrDefaultAsync(cancellationToken) ?? new TicketSummaryDto();
        }

        public Task<TicketDto?> GetAsync(int ticketId, int userId, UserRole role, CancellationToken cancellationToken = default)
        {
            return Project(VisibleTo(userId, role).AsNoTracking().Where(ticket => ticket.Id == ticketId), userId, role)
                .SingleOrDefaultAsync(cancellationToken);
        }

        public async Task<(TicketSummaryDto Summary, IReadOnlyList<TicketReportDayDto> Days)> GetReportAsync(
            int userId, UserRole role, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        {
            IQueryable<Ticket> query = ReportQuery(userId, role, from, to);
            TicketSummaryDto summary = await query.GroupBy(ticket => 1).Select(group => new TicketSummaryDto
            {
                Total = group.Count(), Open = group.Count(ticket => ticket.Status == TicketStatus.Open),
                InProgress = group.Count(ticket => ticket.Status == TicketStatus.InProgress),
                Answered = group.Count(ticket => ticket.Status == TicketStatus.Answered),
                Closed = group.Count(ticket => ticket.Status == TicketStatus.Closed)
            }).SingleOrDefaultAsync(cancellationToken) ?? new TicketSummaryDto();
            var days = await query.GroupBy(ticket => ticket.CreatedAt.AddMinutes(210).Date)
                .Select(group => new { Date = group.Key, Count = group.Count() }).ToListAsync(cancellationToken);
            return (summary, days.Select(day => new TicketReportDayDto(DateOnly.FromDateTime(day.Date), day.Count)).ToList());
        }

        public async Task<(IReadOnlyList<TicketReportRowDto> Items, int TotalItems)> GetReportTicketsAsync(
            int userId, UserRole role, DateOnly from, DateOnly to, int page, int perPage, CancellationToken cancellationToken = default)
        {
            var query = ReportQuery(userId, role, from, to);
            int total = await query.CountAsync(cancellationToken);
            var items = await query.OrderByDescending(ticket => ticket.CreatedAt).ThenByDescending(ticket => ticket.Id)
                .Skip((page - 1) * perPage).Take(perPage)
                .Select(ticket => new TicketReportRowDto(ticket.Id, ticket.Owner.FullName, ticket.Title)).ToListAsync(cancellationToken);
            return (items, total);
        }

        private IQueryable<Ticket> ReportQuery(int userId, UserRole role, DateOnly from, DateOnly to)
        {
            DateTime start = DateTime.SpecifyKind(from.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc).AddMinutes(-210);
            DateTime end = DateTime.SpecifyKind(to.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc).AddMinutes(-210);
            return VisibleTo(userId, role).AsNoTracking()
                .Where(ticket => role == UserRole.Agent && ticket.AssignedToId == userId && ticket.AcceptedAt != null &&
                    ticket.CreatedAt >= start && ticket.CreatedAt < end);
        }

        public Task<Ticket?> GetEntityAsync(int ticketId, int userId, UserRole role, CancellationToken cancellationToken = default)
        {
            return VisibleTo(userId, role).SingleOrDefaultAsync(ticket => ticket.Id == ticketId, cancellationToken);
        }

        public Task<Ticket?> GetForAcceptanceAsync(int ticketId, CancellationToken cancellationToken = default)
        {
            return context.Tickets.SingleOrDefaultAsync(ticket => ticket.Id == ticketId, cancellationToken);
        }

        public async Task<(IReadOnlyList<TicketListItemDto> Items, int TotalItems)> GetListAsync(
            int userId, UserRole role, TicketListRequest request, CancellationToken cancellationToken = default)
        {
            IQueryable<Ticket> query = VisibleTo(userId, role).AsNoTracking();
            if (request.UnassignedOnly) query = query.Where(ticket => ticket.AssignedToId == null && ticket.Status == TicketStatus.Open);
            if (request.MineOnly && role == UserRole.Agent) query = query.Where(ticket => ticket.AssignedToId == userId);
            if (request.Status.HasValue) query = query.Where(ticket => ticket.Status == request.Status);
            if (request.Priority.HasValue) query = query.Where(ticket => ticket.Priority == request.Priority);
            if (!string.IsNullOrWhiteSpace(request.Search))
            {
                string search = request.Search.Trim().ToLowerInvariant();
                if (role == UserRole.Client)
                {
                    // Each word may match either the subject or the assigned agent's name.
                    foreach (string term in search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    {
                        query = query.Where(ticket => ticket.Title.ToLower().Contains(term) ||
                            (ticket.AssignedTo != null && ticket.AssignedTo.FullName.ToLower().Contains(term)));
                    }
                }
                else
                {
                    query = query.Where(ticket => ticket.Title.ToLower().Contains(search) || ticket.Messages.Any(message =>
                        message.DeletedAt == null && message.Body.ToLower().Contains(search)));
                }
            }
            int total = await query.CountAsync(cancellationToken);
            IOrderedQueryable<Ticket> ordered;
            if (request.SortBy == TicketSortBy.Priority)
            {
                // Priorities are stored as strings; rank explicitly and keep unset values last.
                var withPriority = query.OrderByDescending(ticket => ticket.Priority.HasValue);
                ordered = (request.SortOrder == TicketSortOrder.Asc
                    ? withPriority.ThenBy(ticket => ticket.Priority == TicketPriority.High ? 3 : ticket.Priority == TicketPriority.Medium ? 2 : ticket.Priority == TicketPriority.Low ? 1 : 0)
                    : withPriority.ThenByDescending(ticket => ticket.Priority == TicketPriority.High ? 3 : ticket.Priority == TicketPriority.Medium ? 2 : ticket.Priority == TicketPriority.Low ? 1 : 0))
                    .ThenByDescending(ticket => ticket.UpdatedAt ?? ticket.CreatedAt).ThenByDescending(ticket => ticket.Id);
            }
            else ordered = request.SortOrder == TicketSortOrder.Asc
                ? query.OrderBy(ticket => ticket.UpdatedAt ?? ticket.CreatedAt).ThenBy(ticket => ticket.Id)
                : query.OrderByDescending(ticket => ticket.UpdatedAt ?? ticket.CreatedAt).ThenByDescending(ticket => ticket.Id);
            var tickets = await Project(ordered.Skip((request.Page - 1) * request.PerPage).Take(request.PerPage), userId, role)
                .ToListAsync(cancellationToken);
            return (tickets.Cast<TicketListItemDto>().ToList(), total);
        }

        public async Task<IReadOnlyList<ChatMessageDto>> GetMessagesAsync(int ticketId, CancellationToken cancellationToken = default, bool includeDeleted = false)
        {
            return await ProjectMessages(context.ChatMessages.AsNoTracking().Where(message => message.TicketId == ticketId && (includeDeleted || message.DeletedAt == null))
                .OrderBy(message => message.CreatedAt).ThenBy(message => message.Id)).ToListAsync(cancellationToken);
        }

        public Task<ChatMessageDto?> GetMessageAsync(int ticketId, Guid clientMessageId, CancellationToken cancellationToken = default)
        {
            return ProjectMessages(context.ChatMessages.AsNoTracking().Where(message =>
                message.TicketId == ticketId && message.ClientMessageId == clientMessageId)).SingleOrDefaultAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<ChatReadReceiptDto>> GetReadReceiptsAsync(int ticketId, CancellationToken cancellationToken = default) =>
            await context.ChatReadReceipts.AsNoTracking().Where(receipt => receipt.TicketId == ticketId)
                .Select(receipt => new ChatReadReceiptDto(receipt.TicketId, receipt.UserId, receipt.LastReadMessageId, receipt.ReadAt)).ToListAsync(cancellationToken);

        public Task<bool> MessageExistsAsync(int ticketId, int messageId, CancellationToken cancellationToken = default) =>
            context.ChatMessages.AnyAsync(message => message.TicketId == ticketId && message.Id == messageId, cancellationToken);

        public async Task<(ChatReadReceiptDto Receipt, bool Changed)> MarkReadAsync(int ticketId, int userId, int messageId, CancellationToken cancellationToken = default)
        {
            ChatReadReceipt? current = await context.ChatReadReceipts.SingleOrDefaultAsync(
                receipt => receipt.TicketId == ticketId && receipt.UserId == userId, cancellationToken);
            ChatMessage target = (await context.ChatMessages.FindAsync([messageId], cancellationToken))!;
            ChatMessage? previous = current == null ? null : await context.ChatMessages.FindAsync([current.LastReadMessageId], cancellationToken);
            // Migrated first messages can have higher IDs but earlier timestamps.
            bool changed = previous == null || target.CreatedAt > previous.CreatedAt ||
                target.CreatedAt == previous.CreatedAt && target.Id > previous.Id;
            if (current == null)
            {
                current = new ChatReadReceipt { TicketId = ticketId, UserId = userId };
                context.ChatReadReceipts.Add(current);
            }
            if (changed) { current.LastReadMessageId = messageId; current.ReadAt = DateTime.UtcNow; }
            // The service commits the cursor and ticket revision together.
            return (new ChatReadReceiptDto(current.TicketId, current.UserId, current.LastReadMessageId, current.ReadAt), changed);
        }

        private IQueryable<ChatMessageDto> ProjectMessages(IQueryable<ChatMessage> query)
        {
            return query.Select(message => new ChatMessageDto
            {
                Id = message.Id, TicketId = message.TicketId, ClientMessageId = message.ClientMessageId,
                Version = message.Version,
                Body = message.Body, CreatedAt = message.CreatedAt, EditedAt = message.EditedAt, DeletedAt = message.DeletedAt,
                IsRead = message.ReadAt != null,
                Attachments = message.Attachments.Where(attachment => attachment.Message.DeletedAt == null).OrderBy(attachment => attachment.Id)
                    .Select(attachment => new MessageAttachmentDto(attachment.Id, attachment.FileName, attachment.ContentType, attachment.Size, attachment.StorageKey)).ToList(),
                Author = new TicketUserDto { Id = message.Author.Id, DisplayName = message.Author.FullName, Role = message.Author.Role, AvatarUrl = message.Author.AvatarUrl }
            });
        }

        private IQueryable<TicketDto> Project(IQueryable<Ticket> query, int userId, UserRole role)
        {
            return query.Select(ticket => new TicketDto
            {
                Id = ticket.Id, CreatedById = ticket.CreatedById, AssignedToId = ticket.AssignedToId,
                Title = ticket.Title, Priority = role == UserRole.Agent ? ticket.Priority : null,
                HasUnreadMessages = (ticket.Status == TicketStatus.Open || ticket.Status == TicketStatus.InProgress ||
                    role == UserRole.Client && ticket.Status == TicketStatus.Answered) &&
                    ticket.Messages.Any(message => message.AuthorUserId != userId && message.DeletedAt == null &&
                    !context.ChatReadReceipts.Any(receipt => receipt.TicketId == ticket.Id && receipt.UserId == userId &&
                        context.ChatMessages.Any(seen => seen.Id == receipt.LastReadMessageId && seen.TicketId == ticket.Id &&
                            (seen.CreatedAt > message.CreatedAt || seen.CreatedAt == message.CreatedAt && seen.Id >= message.Id)))),
                Status = ticket.Status, CreatedAt = ticket.CreatedAt, UpdatedAt = ticket.UpdatedAt,
                AcceptedAt = ticket.AcceptedAt, ClosedAt = ticket.ClosedAt,
                Owner = new TicketUserDto { Id = ticket.Owner.Id, DisplayName = ticket.Owner.FullName, Role = ticket.Owner.Role, AvatarUrl = ticket.Owner.AvatarUrl },
                AssignedTo = ticket.AssignedTo == null ? null : new TicketUserDto
                { Id = ticket.AssignedTo.Id, DisplayName = ticket.AssignedTo.FullName, Role = ticket.AssignedTo.Role, AvatarUrl = ticket.AssignedTo.AvatarUrl }
            });
        }
    }
}
