using System.ComponentModel.DataAnnotations;
using System.Net;
using Database.Context;
using Database.Enums.Identity;
using Database.Enums.Tickets;
using Database.Model.Tickets;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Repository.Tickets;
using Repository.Identity;
using Service.Media;
using ViewModels.Common;
using ViewModels.Tickets;

namespace Service.Tickets
{
    public interface ITicketsService
    {
        Task<ApiResultDto<TicketDto>> CreateWithMediaAsync(int userId, UserRole role, CreateTicketRequest request, IReadOnlyList<UploadedTicketMedia> files, CancellationToken cancellationToken = default);
        Task<ApiResultDto<MessageAttachmentContentDto>> AttachmentAsync(int userId, UserRole role, int ticketId, int messageId, Guid attachmentId, CancellationToken cancellationToken = default);
        Task<ApiResultDto<TicketSummaryDto>> DashboardAsync(int userId, UserRole role, CancellationToken cancellationToken = default);
        Task<ApiResultDto<TicketReportDto>> ReportAsync(int userId, UserRole role, TicketReportRequest request, CancellationToken cancellationToken = default);
        Task<ApiResultDto<IReadOnlyList<TicketReportRowDto>>> ReportTicketsAsync(int userId, UserRole role, TicketReportTicketsRequest request, CancellationToken cancellationToken = default);
        Task<ApiResultDto<TicketDto>> CreateAsync(int userId, UserRole role, CreateTicketRequest request, CancellationToken cancellationToken = default);
        Task<ApiResultDto<TicketDto>> EditAsync(int userId, UserRole role, int ticketId, EditTicketTitleRequest request, CancellationToken cancellationToken = default);
        Task<ApiResultDto<TicketDeletedDto>> DeleteAsync(int userId, UserRole role, int ticketId, CancellationToken cancellationToken = default);
        Task<ApiResultDto<ChatMessageDto>> EditMessageAsync(int userId, UserRole role, int ticketId, int messageId, EditChatMessageRequest request, CancellationToken cancellationToken = default);
        Task<ApiResultDto<ChatMessageDto>> DeleteMessageAsync(int userId, UserRole role, int ticketId, int messageId, CancellationToken cancellationToken = default);
        Task<ApiResultDto<TicketDto>> SelectAsync(int userId, UserRole role, int ticketId, CancellationToken cancellationToken = default);
        Task<ApiResultDto<IReadOnlyList<TicketListItemDto>>> ListAsync(int userId, UserRole role, TicketListRequest request, CancellationToken cancellationToken = default);
        Task<ApiResultDto<IReadOnlyList<TicketListItemDto>>> ListUnassignedAsync(int userId, UserRole role, TicketListRequest request, CancellationToken cancellationToken = default);
        Task<ApiResultDto<TicketDto>> AcceptAsync(int userId, UserRole role, int ticketId, AcceptTicketRequest request, CancellationToken cancellationToken = default);
        Task<ApiResultDto<IReadOnlyList<TransferAgentDto>>> TransferAgentsAsync(int userId, UserRole role, int ticketId, TransferAgentsRequest request, CancellationToken cancellationToken = default);
        Task<ApiResultDto<TicketDto>> TransferAsync(int userId, UserRole role, int ticketId, TransferTicketRequest request, CancellationToken cancellationToken = default);
        Task<ApiResultDto<TicketDto>> ReleaseAsync(int userId, UserRole role, int ticketId, CancellationToken cancellationToken = default);
        Task<ApiResultDto<ConversationDto>> ConversationAsync(int userId, UserRole role, int ticketId, CancellationToken cancellationToken = default, bool includeDeleted = false);
        Task<ApiResultDto<ChatMessageDto>> SendMessageAsync(int userId, UserRole role, int ticketId, SendChatMessageRequest request, CancellationToken cancellationToken = default);
        Task<ApiResultDto<ChatReadReceiptDto>> MarkReadAsync(int userId, UserRole role, int ticketId, ReadConversationRequest request, CancellationToken cancellationToken = default);
        Task<ApiResultDto<ChatTypingDto>> SetTypingAsync(int userId, UserRole role, int ticketId, bool isTyping, CancellationToken cancellationToken = default);
        Task<ApiResultDto<TicketDto>> ChangePriorityAsync(int userId, UserRole role, int ticketId, ChangeTicketPriorityRequest request, CancellationToken cancellationToken = default);
        Task<ApiResultDto<TicketDto>> ChangeStatusAsync(int userId, UserRole role, int ticketId, ChangeTicketStatusRequest request, CancellationToken cancellationToken = default);
    }

    public sealed class TicketsService(ITicketsRepository ticketsRepository, IDatabaseUnitOfWork database,
        ITicketEvents events, IUsersRepository usersRepository, IReplyRemindersService reminders, IMediaService? media = null) : ITicketsService
    {
        public async Task<ApiResultDto<TicketSummaryDto>> DashboardAsync(int userId, UserRole role, CancellationToken cancellationToken = default)
        {
            return ApiResultDto<TicketSummaryDto>.Success(await ticketsRepository.GetSummaryAsync(userId, role, cancellationToken));
        }

        public Task<ApiResultDto<TicketDto>> CreateAsync(int userId, UserRole role, CreateTicketRequest request, CancellationToken cancellationToken = default) =>
            CreateWithMediaAsync(userId, role, request, [], cancellationToken);

        public async Task<ApiResultDto<MessageAttachmentContentDto>> AttachmentAsync(int userId, UserRole role,
            int ticketId, int messageId, Guid attachmentId, CancellationToken cancellationToken = default)
        {
            var attachment = await ticketsRepository.GetAttachmentAsync(ticketId, messageId, attachmentId, userId, role, cancellationToken);
            return attachment == null ? NotFound<MessageAttachmentContentDto>() : ApiResultDto<MessageAttachmentContentDto>.Success(attachment);
        }

        public async Task<ApiResultDto<TicketDto>> CreateWithMediaAsync(int userId, UserRole role, CreateTicketRequest request,
            IReadOnlyList<UploadedTicketMedia> files, CancellationToken cancellationToken = default)
        {
            if (role != UserRole.Client) return Failure<TicketDto>("Only clients can create tickets.", 403);
            string? error = Validate(request);
            if (error != null) return Failure<TicketDto>(error, 400);
            error = TicketMedia.Validate(files);
            if (error != null) return Failure<TicketDto>(error, 400);
            if (request.Attachments == null) return Failure<TicketDto>("پیوست معتبر نیست.", 400);
            List<MessageAttachment> attachments = files.Select(TicketMedia.ToEntity).ToList();
            if (request.Attachments.Count > 0)
            {
                if (files.Count > 0 || media == null) return Failure<TicketDto>("پیوست معتبر نیست.", 400);
                var resolved = await media.ResolveAsync(userId, null, request.Attachments, cancellationToken);
                if (resolved.Error != null) return Failure<TicketDto>(resolved.Error, 400);
                attachments = resolved.Files;
            }
            Ticket ticket = new() { CreatedById = userId, Title = request.Title.Trim(), Status = TicketStatus.Open };
            ticket.Messages.Add(new ChatMessage { AuthorUserId = userId, Body = request.Description.Trim(),
                ClientMessageId = Guid.NewGuid(), CreatedAt = ticket.CreatedAt, Attachments = attachments });
            ticketsRepository.Add(ticket);
            try { await database.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "PK_MessageAttachments" })
            { return Failure<TicketDto>("این فایل قبلاً به پیام دیگری متصل شده است.", 409); }
            TicketDto created = (await ticketsRepository.GetAsync(ticket.Id, userId, role, cancellationToken))!;
            await events.CreatedAsync(created, cancellationToken);
            return ApiResultDto<TicketDto>.Success(created, "Ticket created successfully.", 201);
        }

        public async Task<ApiResultDto<TicketReportDto>> ReportAsync(int userId, UserRole role, TicketReportRequest request, CancellationToken cancellationToken = default)
        {
            if (role != UserRole.Agent) return Failure<TicketReportDto>("گزارش‌ها فقط برای پشتیبان فعال است.", 403);
            string? error = ReportRange(request, out DateOnly from, out DateOnly to);
            if (error != null) return Failure<TicketReportDto>(error, 400);
            var (summary, days) = await ticketsRepository.GetReportAsync(userId, role, from, to, cancellationToken);
            var counts = days.ToDictionary(day => day.Date, day => day.Count);
            var timeline = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
                .Select(offset => { DateOnly date = from.AddDays(offset); return new TicketReportDayDto(date, counts.GetValueOrDefault(date)); }).ToList();
            return ApiResultDto<TicketReportDto>.Success(new TicketReportDto(from, to, DateTime.UtcNow,
                "Asia/Tehran", summary, timeline));
        }

        public async Task<ApiResultDto<IReadOnlyList<TicketReportRowDto>>> ReportTicketsAsync(int userId, UserRole role, TicketReportTicketsRequest request, CancellationToken cancellationToken = default)
        {
            if (role != UserRole.Agent) return Failure<IReadOnlyList<TicketReportRowDto>>("گزارش‌ها فقط برای پشتیبان فعال است.", 403);
            string? error = ReportRange(request, out DateOnly from, out DateOnly to);
            if (error != null) return Failure<IReadOnlyList<TicketReportRowDto>>(error, 400);
            if (request.Page < 1 || request.PerPage < 1 || request.PerPage > 100 || (long)(request.Page - 1) * request.PerPage > int.MaxValue)
                return Failure<IReadOnlyList<TicketReportRowDto>>("صفحه‌بندی معتبر نیست.", 400);
            var (items, total) = await ticketsRepository.GetReportTicketsAsync(userId, role, from, to, request.Page, request.PerPage, cancellationToken);
            return ApiResultDto<IReadOnlyList<TicketReportRowDto>>.Success(items, pagination:
                new(request.Page, request.PerPage, total, (int)Math.Ceiling(total / (double)request.PerPage)));
        }

        private static string? ReportRange(TicketReportRequest request, out DateOnly from, out DateOnly to)
        {
            to = request.To ?? DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(210));
            from = request.From ?? to;
            if (to.Year < 2000 || to.Year > 2100 || from.Year < 2000 || from.Year > 2100)
                return "Choose dates between 2000 and 2100.";
            from = request.From ?? to.AddDays(-29);
            return from > to || to.DayNumber - from.DayNumber > 365 || from.Year < 2000
                ? "Choose a date range of up to 366 days between 2000 and 2100." : null;
        }

        public async Task<ApiResultDto<TicketDto>> SelectAsync(int userId, UserRole role, int ticketId, CancellationToken cancellationToken = default)
        {
            TicketDto? ticket = await ticketsRepository.GetAsync(ticketId, userId, role, cancellationToken);
            return ticket == null ? NotFound<TicketDto>() : ApiResultDto<TicketDto>.Success(ticket);
        }

        public async Task<ApiResultDto<TicketDto>> EditAsync(int userId, UserRole role, int ticketId, EditTicketTitleRequest request, CancellationToken cancellationToken = default)
        {
            if (role != UserRole.Client) return Failure<TicketDto>("Only the owner can edit a request.", 403);
            string? error = Validate(request);
            if (error != null) return Failure<TicketDto>(error, 400);
            Ticket? ticket = await ticketsRepository.GetEntityAsync(ticketId, userId, role, cancellationToken);
            if (ticket == null) return NotFound<TicketDto>();
            if (ticket.AssignedToId != null || ticket.AcceptedAt != null || ticket.Status != TicketStatus.Open)
                return Failure<TicketDto>("Accepted requests cannot be edited.", 409);
            ticket.Title = request.Title.Trim();
            return await SaveTicketAsync(ticket, userId, role, cancellationToken);
        }

        public async Task<ApiResultDto<TicketDeletedDto>> DeleteAsync(int userId, UserRole role, int ticketId, CancellationToken cancellationToken = default)
        {
            if (role != UserRole.Client) return Failure<TicketDeletedDto>("Only the owner can delete a request.", 403);
            Ticket? ticket = await ticketsRepository.GetEntityAsync(ticketId, userId, role, cancellationToken);
            if (ticket == null) return NotFound<TicketDeletedDto>();
            if (ticket.AssignedToId != null || ticket.AcceptedAt != null || ticket.Status != TicketStatus.Open)
                return Failure<TicketDeletedDto>("Accepted requests cannot be deleted.", 409);
            ticketsRepository.Remove(ticket);
            try { await database.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Failure<TicketDeletedDto>("The request changed. Reload it and retry.", 409); }
            await events.DeletedAsync(ticket.Id, ticket.CreatedById, cancellationToken);
            return ApiResultDto<TicketDeletedDto>.Success(new TicketDeletedDto(ticket.Id));
        }

        public Task<ApiResultDto<ChatMessageDto>> EditMessageAsync(int userId, UserRole role, int ticketId, int messageId, EditChatMessageRequest request, CancellationToken cancellationToken = default)
        {
            string? error = Validate(request);
            if (error != null || string.IsNullOrWhiteSpace(request.Body))
                return Task.FromResult(Failure<ChatMessageDto>(error ?? "A message cannot be empty.", 400));
            return ChangeMessageAsync(userId, role, ticketId, messageId, request.Body.Trim(), cancellationToken);
        }

        public Task<ApiResultDto<ChatMessageDto>> DeleteMessageAsync(int userId, UserRole role, int ticketId, int messageId, CancellationToken cancellationToken = default) =>
            ChangeMessageAsync(userId, role, ticketId, messageId, null, cancellationToken);

        private async Task<ApiResultDto<ChatMessageDto>> ChangeMessageAsync(int userId, UserRole role, int ticketId, int messageId, string? body, CancellationToken cancellationToken)
        {
            Ticket? ticket = await ticketsRepository.GetEntityAsync(ticketId, userId, role, cancellationToken);
            if (ticket == null) return NotFound<ChatMessageDto>();
            if (role == UserRole.Agent && ticket.AssignedToId != userId)
                return Failure<ChatMessageDto>("فقط پشتیبان مسئول می‌تواند پیام خود را تغییر دهد.", 403);
            ChatMessage? message = await ticketsRepository.GetMessageEntityAsync(ticketId, messageId, cancellationToken);
            if (message == null) return NotFound<ChatMessageDto>();
            if (message.AuthorUserId != userId) return Failure<ChatMessageDto>("Only the sender can change a message.", 403);
            if (message.DeletedAt != null)
                return body == null ? ApiResultDto<ChatMessageDto>.Success((await ticketsRepository.GetMessageAsync(ticketId, message.ClientMessageId, cancellationToken))!)
                    : NotFound<ChatMessageDto>();
            if (message.ReadAt != null)
                return Failure<ChatMessageDto>("این پیام سین شده و دیگر قابل ویرایش یا حذف نیست.", 409);
            if (body == null) { message.DeletedAt = DateTime.UtcNow; message.Body = string.Empty; }
            else { message.Body = body; message.EditedAt = DateTime.UtcNow; }
            // A message competes only with reads/edits of that same message, not other ticket activity.
            try { await database.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException)
            {
                ChatMessageDto? current = await ticketsRepository.GetMessageAsync(ticketId, message.ClientMessageId, cancellationToken);
                if (body == null && current?.DeletedAt != null) return ApiResultDto<ChatMessageDto>.Success(current);
                return Failure<ChatMessageDto>(current?.IsRead == true ? "این پیام سین شده و دیگر قابل ویرایش یا حذف نیست."
                    : "این پیام هم‌زمان تغییر کرده است. دوباره تلاش کنید.", 409);
            }
            ChatMessageDto saved = (await ticketsRepository.GetMessageAsync(ticketId, message.ClientMessageId, cancellationToken))!;
            TicketDto? updated = await ticketsRepository.GetAsync(ticketId, userId, role, cancellationToken);
            if (updated?.AssignedToId is int assignedId && role == UserRole.Client)
                updated = await ticketsRepository.GetAsync(ticketId, assignedId, UserRole.Agent, cancellationToken);
            if (updated != null) await events.MessageSentAsync(updated, saved, cancellationToken);
            return ApiResultDto<ChatMessageDto>.Success(saved);
        }

        public async Task<ApiResultDto<IReadOnlyList<TicketListItemDto>>> ListAsync(int userId, UserRole role, TicketListRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Priority.HasValue && role != UserRole.Agent)
                return Failure<IReadOnlyList<TicketListItemDto>>("Only agents can filter by priority.", 403);
            if (request.SortBy == TicketSortBy.Priority && role != UserRole.Agent)
                return Failure<IReadOnlyList<TicketListItemDto>>("Only agents can sort by priority.", 403);
            if (request.UnassignedOnly && role != UserRole.Agent)
                return Failure<IReadOnlyList<TicketListItemDto>>("Access is forbidden.", 403);
            if (request.UnassignedOnly && request.MineOnly)
                return Failure<IReadOnlyList<TicketListItemDto>>("Choose either the queue or your accepted tickets.", 400);
            (IReadOnlyList<TicketListItemDto> items, int count) = await ticketsRepository.GetListAsync(userId, role, request, cancellationToken);
            return ApiResultDto<IReadOnlyList<TicketListItemDto>>.Success(items,
                pagination: new PaginationMeta(request.Page, request.PerPage, count, (int)Math.Ceiling(count / (double)request.PerPage)));
        }

        public Task<ApiResultDto<IReadOnlyList<TicketListItemDto>>> ListUnassignedAsync(int userId, UserRole role, TicketListRequest request, CancellationToken cancellationToken = default)
        {
            request.UnassignedOnly = true;
            return ListAsync(userId, role, request, cancellationToken);
        }

        public async Task<ApiResultDto<TicketDto>> AcceptAsync(int userId, UserRole role, int ticketId, AcceptTicketRequest request, CancellationToken cancellationToken = default)
        {
            if (role != UserRole.Agent) return Failure<TicketDto>("Only agents can accept tickets.", 403);
            string? error = Validate(request);
            if (error != null) return Failure<TicketDto>(error, 400);
            Ticket? ticket = await ticketsRepository.GetForAcceptanceAsync(ticketId, cancellationToken);
            if (ticket == null) return NotFound<TicketDto>();
            for (int attempt = 0; ; attempt++)
            {
                if (ticket.AssignedToId != null || ticket.Status != TicketStatus.Open)
                    return Failure<TicketDto>("This request has already been accepted or closed.", 409);
                ticket.AssignedToId = userId;
                ticket.AcceptedAt ??= DateTime.UtcNow;
                ticket.UpdatedAt = DateTime.UtcNow;
                ticket.Priority = request.Priority;
                ticket.Status = TicketStatus.InProgress;
                await reminders.StartAssignmentAsync(ticket, cancellationToken);
                ticketsRepository.Modify(ticket);
                try { await database.SaveChangesAsync(cancellationToken); break; }
                catch (DbUpdateConcurrencyException)
                {
                    if (!await ticketsRepository.ReloadAsync(ticket, cancellationToken)) return NotFound<TicketDto>();
                    if (ticket.AssignedToId != null || ticket.Status != TicketStatus.Open)
                        return Failure<TicketDto>("This request has already been accepted or closed.", 409);
                    if (attempt >= 2) return Failure<TicketDto>("The request changed. Reload it and retry.", 409);
                }
            }
            TicketDto accepted = (await ticketsRepository.GetAsync(ticketId, userId, role, cancellationToken))!;
            await events.ChangedAsync(accepted, cancellationToken);
            return ApiResultDto<TicketDto>.Success(accepted, "Ticket accepted. Your private conversation is ready.");
        }

        public async Task<ApiResultDto<IReadOnlyList<TransferAgentDto>>> TransferAgentsAsync(int userId, UserRole role, int ticketId,
            TransferAgentsRequest request, CancellationToken cancellationToken = default)
        {
            if (role != UserRole.Agent) return Failure<IReadOnlyList<TransferAgentDto>>("فقط پشتیبان مسئول می‌تواند درخواست را ارجاع دهد.", 403);
            string? error = Validate(request);
            if (error != null || (long)(request.Page - 1) * request.PerPage > int.MaxValue)
                return Failure<IReadOnlyList<TransferAgentDto>>(error ?? "صفحه‌بندی معتبر نیست.", 400);
            Ticket? ticket = await ticketsRepository.GetEntityAsync(ticketId, userId, role, cancellationToken);
            if (ticket == null) return NotFound<IReadOnlyList<TransferAgentDto>>();
            if (ticket.AssignedToId != userId || ticket.Status == TicketStatus.Closed)
                return Failure<IReadOnlyList<TransferAgentDto>>("درخواست باید فعال و در مسئولیت شما باشد.", 409);
            var (items, total) = await usersRepository.GetTransferAgentsAsync(userId, request, cancellationToken);
            return ApiResultDto<IReadOnlyList<TransferAgentDto>>.Success(items, pagination:
                new(request.Page, request.PerPage, total, (int)Math.Ceiling(total / (double)request.PerPage)));
        }

        public Task<ApiResultDto<TicketDto>> TransferAsync(int userId, UserRole role, int ticketId,
            TransferTicketRequest request, CancellationToken cancellationToken = default)
        {
            string? error = Validate(request);
            return error != null ? Task.FromResult(Failure<TicketDto>(error, 400))
                : ChangeAssignmentAsync(userId, role, ticketId, request.AgentId, cancellationToken);
        }

        public Task<ApiResultDto<TicketDto>> ReleaseAsync(int userId, UserRole role, int ticketId, CancellationToken cancellationToken = default) =>
            ChangeAssignmentAsync(userId, role, ticketId, null, cancellationToken);

        private async Task<ApiResultDto<TicketDto>> ChangeAssignmentAsync(int userId, UserRole role, int ticketId,
            int? agentId, CancellationToken cancellationToken)
        {
            if (role != UserRole.Agent) return Failure<TicketDto>("فقط پشتیبان مسئول می‌تواند درخواست را واگذار کند.", 403);
            Ticket? ticket = await ticketsRepository.GetEntityAsync(ticketId, userId, role, cancellationToken);
            if (ticket == null) return NotFound<TicketDto>();
            if (ticket.AssignedToId != userId || ticket.AcceptedAt == null || ticket.Status == TicketStatus.Closed)
                return Failure<TicketDto>("فقط درخواست فعال در مسئولیت شما قابل واگذاری است.", 409);
            if (agentId.HasValue)
            {
                var target = await usersRepository.GetByIdAsync(agentId.Value, cancellationToken);
                if (target == null || target.Id == userId || target.Role != UserRole.Agent || target.Status != UserStatus.Active)
                    return Failure<TicketDto>("یک پشتیبان فعال دیگر را انتخاب کنید.", 400);
            }
            ticket.AssignedToId = agentId;
            ticket.Status = agentId.HasValue ? TicketStatus.InProgress : TicketStatus.Open;
            if (agentId.HasValue) await reminders.StartAssignmentAsync(ticket, cancellationToken);
            else await reminders.RemoveAsync(ticketId, cancellationToken);
            // AcceptedAt records the first acceptance: returning to the queue must not
            // make an established conversation editable/deletable by the customer.
            ticket.UpdatedAt = DateTime.UtcNow;
            ticketsRepository.Modify(ticket);
            try { await database.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Failure<TicketDto>("درخواست هم‌زمان تغییر کرده است؛ آن را به‌روزرسانی و دوباره تلاش کنید.", 409); }
            TicketDto updated = (await ticketsRepository.GetAsync(ticketId, agentId ?? userId, UserRole.Agent, cancellationToken))!;
            await events.AssignmentChangedAsync(updated, userId, cancellationToken);
            return ApiResultDto<TicketDto>.Success(updated,
                agentId.HasValue ? "درخواست به پشتیبان جدید ارجاع شد." : "درخواست به صف عمومی بازگردانده شد.");
        }

        public async Task<ApiResultDto<ConversationDto>> ConversationAsync(int userId, UserRole role, int ticketId, CancellationToken cancellationToken = default, bool includeDeleted = false)
        {
            TicketDto? ticket = await ticketsRepository.GetAsync(ticketId, userId, role, cancellationToken);
            if (ticket == null) return NotFound<ConversationDto>();
            var receipts = await ticketsRepository.GetReadReceiptsAsync(ticketId, cancellationToken);
            return ApiResultDto<ConversationDto>.Success(new ConversationDto(ticket.Id, ticket.CreatedAt,
                ticket.Owner, ticket.AssignedTo, await ticketsRepository.GetMessagesAsync(ticketId, cancellationToken, includeDeleted),
                receipts.FirstOrDefault(receipt => receipt.UserId == userId)?.LastReadMessageId,
                receipts.Where(receipt => receipt.UserId == (role == UserRole.Client ? ticket.AssignedToId : ticket.CreatedById))
                    .Select(receipt => (int?)receipt.LastReadMessageId).Max()));
        }

        public async Task<ApiResultDto<ChatReadReceiptDto>> MarkReadAsync(int userId, UserRole role, int ticketId, ReadConversationRequest request, CancellationToken cancellationToken = default)
        {
            string? error = Validate(request);
            if (error != null) return Failure<ChatReadReceiptDto>(error, 400);
            Ticket? ticket = await ticketsRepository.GetEntityAsync(ticketId, userId, role, cancellationToken);
            if (ticket == null) return NotFound<ChatReadReceiptDto>();
            TicketDto? notificationTicket = await ticketsRepository.GetAsync(ticketId, userId, role, cancellationToken);
            if (notificationTicket == null) return NotFound<ChatReadReceiptDto>();
            ChatMessage? message = await ticketsRepository.GetMessageEntityAsync(ticketId, request.LastReadMessageId, cancellationToken);
            if (message == null || message.DeletedAt != null) return NotFound<ChatReadReceiptDto>();
            // A sender cannot advance their incoming-message inbox by acknowledging their own message.
            if (message.AuthorUserId == userId)
                return ApiResultDto<ChatReadReceiptDto>.Success(new(ticketId, userId, message.Id, DateTime.UtcNow, message.Id));
            bool isMessageRecipient = role == UserRole.Client || message.AuthorUserId == ticket.CreatedById;
            if (isMessageRecipient && message.ReadAt == null && request.Version.HasValue && request.Version.Value != message.Version)
                return Failure<ChatReadReceiptDto>("متن پیام تغییر کرده است؛ نسخهٔ جدید را بخوانید.", 409);
            bool messageChanged = isMessageRecipient && message.ReadAt == null;
            if (messageChanged) message.ReadAt = DateTime.UtcNow;
            var (receipt, changed) = await ticketsRepository.MarkReadAsync(ticketId, userId, request.LastReadMessageId, cancellationToken);
            receipt = receipt with { MessageId = isMessageRecipient ? message.Id : null };
            bool reminderChanged = role == UserRole.Agent && ticket.AssignedToId == userId && ticket.Status == TicketStatus.InProgress &&
                message.AuthorUserId == ticket.CreatedById && await reminders.SeenAsync(ticket, message, cancellationToken);
            if (changed || messageChanged || reminderChanged)
            {
                // Reading and editing compete on the same revision, so an edit cannot pass a committed read.
                ticketsRepository.Modify(ticket);
                try { await database.SaveChangesAsync(cancellationToken); }
                catch (DbUpdateConcurrencyException) { return Failure<ChatReadReceiptDto>("The conversation changed. Retry marking it read.", 409); }
                catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                { return Failure<ChatReadReceiptDto>("The conversation changed. Retry marking it read.", 409); }
                await events.ReadAsync(notificationTicket, receipt, cancellationToken);
            }
            return ApiResultDto<ChatReadReceiptDto>.Success(receipt);
        }

        public async Task<ApiResultDto<ChatTypingDto>> SetTypingAsync(int userId, UserRole role, int ticketId, bool isTyping, CancellationToken cancellationToken = default)
        {
            TicketDto? ticket = await ticketsRepository.GetAsync(ticketId, userId, role, cancellationToken);
            if (ticket == null) return NotFound<ChatTypingDto>();
            if (ticket.AssignedToId == null || ticket.AcceptedAt == null || ticket.Status == TicketStatus.Closed)
                return Failure<ChatTypingDto>("This conversation is not active.", 409);
            var typing = new ChatTypingDto(ticketId, userId, isTyping, DateTime.UtcNow.AddSeconds(5));
            await events.TypingAsync(ticket, typing, cancellationToken);
            return ApiResultDto<ChatTypingDto>.Success(typing);
        }

        public async Task<ApiResultDto<ChatMessageDto>> SendMessageAsync(int userId, UserRole role, int ticketId, SendChatMessageRequest request, CancellationToken cancellationToken = default)
        {
            if (role is not (UserRole.Client or UserRole.Agent)) return Failure<ChatMessageDto>("Access is forbidden.", 403);
            string? error = Validate(request);
            if (error != null || request.ClientMessageId == Guid.Empty || request.Attachments == null || string.IsNullOrWhiteSpace(request.Body) && request.Attachments.Count == 0)
                return Failure<ChatMessageDto>(error ?? "A valid client message id is required.", 400);
            request.Body = request.Body?.Trim() ?? "";
            Ticket? ticket = await ticketsRepository.GetEntityAsync(ticketId, userId, role, cancellationToken);
            if (ticket == null) return NotFound<ChatMessageDto>();
            if (role == UserRole.Agent && (ticket.AssignedToId == null || ticket.AcceptedAt == null))
                return Failure<ChatMessageDto>("Chat starts after an agent accepts the request.", 409);
            ChatMessageDto? existing = await ticketsRepository.GetMessageAsync(ticketId, request.ClientMessageId!.Value, cancellationToken);
            if (existing != null) return ExistingMessageWithMedia(existing, userId, request);
            if (ticket.Status == TicketStatus.Closed)
                return Failure<ChatMessageDto>("This conversation is closed.", 409);
            List<MessageAttachment> attachments = [];
            if (request.Attachments.Count > 0)
            {
                if (media == null) return Failure<ChatMessageDto>("ارسال مدیا در دسترس نیست.", 503);
                var resolved = await media.ResolveAsync(userId, ticketId, request.Attachments, cancellationToken);
                if (resolved.Error != null) return Failure<ChatMessageDto>(resolved.Error, 400);
                attachments = resolved.Files;
            }
            ChatMessage message = new() { TicketId = ticketId, AuthorUserId = userId, Body = request.Body.Trim(), ClientMessageId = request.ClientMessageId.Value, Attachments = attachments };
            ticketsRepository.AddMessage(message);
            if (role == UserRole.Agent) await reminders.RepliedAsync(ticketId, userId, message.CreatedAt, cancellationToken);
            ticket.Status = ticket.AssignedToId == null ? TicketStatus.Open
                : role == UserRole.Agent ? TicketStatus.Answered : TicketStatus.InProgress;
            ticket.UpdatedAt = DateTime.UtcNow;
            ticketsRepository.Modify(ticket);
            try { await database.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException)
            {
                return Failure<ChatMessageDto>("The ticket changed concurrently. Reload it and retry.", 409);
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_ChatMessages_TicketId_ClientMessageId" })
            {
                existing = await ticketsRepository.GetMessageAsync(ticketId, request.ClientMessageId.Value, cancellationToken);
                return existing == null ? Failure<ChatMessageDto>("Retry sending this message.", 409) : ExistingMessageWithMedia(existing, userId, request);
            }
            ChatMessageDto saved = (await ticketsRepository.GetMessageAsync(ticketId, message.ClientMessageId, cancellationToken))!;
            TicketDto updated = (await ticketsRepository.GetAsync(ticketId,
                ticket.AssignedToId ?? userId, ticket.AssignedToId.HasValue ? UserRole.Agent : UserRole.Client, cancellationToken))!;
            await events.MessageSentAsync(updated, saved, cancellationToken);
            return ApiResultDto<ChatMessageDto>.Success(saved, "Message sent successfully.");
        }

        public async Task<ApiResultDto<TicketDto>> ChangePriorityAsync(int userId, UserRole role, int ticketId, ChangeTicketPriorityRequest request, CancellationToken cancellationToken = default)
        {
            if (role != UserRole.Agent) return Failure<TicketDto>("Only the assigned agent can change priority.", 403);
            string? error = Validate(request);
            if (error != null) return Failure<TicketDto>(error, 400);
            Ticket? ticket = await ticketsRepository.GetEntityAsync(ticketId, userId, role, cancellationToken);
            if (ticket == null) return NotFound<TicketDto>();
            if (ticket.AssignedToId != userId) return Failure<TicketDto>("Accept the request before changing priority.", 409);
            if (ticket.Status == TicketStatus.Closed) return Failure<TicketDto>("This conversation is closed.", 409);
            ticket.Priority = request.Priority;
            return await SaveTicketAsync(ticket, userId, role, cancellationToken);
        }

        public async Task<ApiResultDto<TicketDto>> ChangeStatusAsync(int userId, UserRole role, int ticketId, ChangeTicketStatusRequest request, CancellationToken cancellationToken = default)
        {
            if (role != UserRole.Agent) return Failure<TicketDto>("Only the assigned agent can change ticket status.", 403);
            string? error = Validate(request);
            if (error != null) return Failure<TicketDto>(error, 400);
            Ticket? ticket = await ticketsRepository.GetEntityAsync(ticketId, userId, role, cancellationToken);
            if (ticket == null) return NotFound<TicketDto>();
            if (ticket.AssignedToId != userId) return Failure<TicketDto>("Accept the request before changing status.", 409);
            if (request.Status != TicketStatus.Closed)
                return Failure<TicketDto>("Ticket status is automatic. The assigned agent can only close the conversation.", 409);
            if (!TicketWorkflow.CanTransition(ticket.Status, request.Status!.Value))
                return Failure<TicketDto>($"Transition from {ticket.Status} to {request.Status} is not allowed.", 409);
            ticket.Status = request.Status.Value;
            ticket.ClosedAt = request.Status == TicketStatus.Closed ? DateTime.UtcNow : null;
            await reminders.RemoveAsync(ticketId, cancellationToken);
            return await SaveTicketAsync(ticket, userId, role, cancellationToken);
        }

        private async Task<ApiResultDto<TicketDto>> SaveTicketAsync(Ticket ticket, int userId, UserRole role, CancellationToken cancellationToken)
        {
            ticket.UpdatedAt = DateTime.UtcNow;
            ticketsRepository.Modify(ticket);
            try { await database.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Failure<TicketDto>("The ticket changed concurrently. Reload it and retry.", 409); }
            TicketDto updated = (await ticketsRepository.GetAsync(ticket.Id, userId, role, cancellationToken))!;
            await events.ChangedAsync(updated, cancellationToken);
            return ApiResultDto<TicketDto>.Success(updated);
        }

        private static ApiResultDto<ChatMessageDto> ExistingMessage(ChatMessageDto message, int userId, string body)
        {
            if (message.DeletedAt != null) return Failure<ChatMessageDto>("This message was deleted. Use a new message id.", 409);
            return message.Author.Id == userId && message.Body == body ? ApiResultDto<ChatMessageDto>.Success(message)
                : Failure<ChatMessageDto>("This message id has already been used.", 409);
        }
        private ApiResultDto<ChatMessageDto> ExistingMessageWithMedia(ChatMessageDto message, int userId, SendChatMessageRequest request)
        {
            if (request.Attachments.Count == 0 && message.Attachments.Count == 0) return ExistingMessage(message, userId, request.Body);
            var ids = request.Attachments.Select(file => ReadReceiptId(file?.Receipt)).ToArray();
            return message.DeletedAt == null && message.Author.Id == userId && message.Body == request.Body && ids.All(id => id.HasValue) &&
                ids.OrderBy(id => id).SequenceEqual(message.Attachments.Select(file => (Guid?)file.Id).OrderBy(id => id))
                ? ApiResultDto<ChatMessageDto>.Success(message) : Failure<ChatMessageDto>("This message id has already been used.", 409);
        }
        private static Guid? ReadReceiptId(string? receipt)
        {
            // Used solely for comparing an already persisted UUID retry. New attachments
            // always go through signature/ownership validation in MediaService.
            try
            {
                if (receipt == null || receipt.Length > 8192) return null;
                string text = receipt.Split('.')[0].Replace('-', '+').Replace('_', '/');
                using var document = System.Text.Json.JsonDocument.Parse(Convert.FromBase64String(text.PadRight((text.Length + 3) / 4 * 4, '=')));
                return document.RootElement.GetProperty("Id").GetGuid();
            }
            catch { return null; }
        }
        private static ApiResultDto<T> Failure<T>(string message, int status) where T : class => ApiResultDto<T>.Failure(message, status);
        private static ApiResultDto<T> NotFound<T>() where T : class => Failure<T>("Ticket was not found.", (int)HttpStatusCode.NotFound);
        private static string? Validate(object? request)
        {
            if (request == null) return "A request body is required.";
            List<ValidationResult> errors = [];
            if (!Validator.TryValidateObject(request, new ValidationContext(request), errors, true)) return errors[0].ErrorMessage;
            if (request is CreateTicketRequest create && (create.Title.Trim().Length < 3 || create.Description.Trim().Length < 3)) return "Title and description must contain at least three non-padding characters.";
            if (request is EditTicketTitleRequest edit && edit.Title.Trim().Length < 3) return "Title must contain at least three non-padding characters.";
            if (request is SendChatMessageRequest message && string.IsNullOrWhiteSpace(message.Body) && (message.Attachments?.Count ?? 0) == 0) return "A message cannot be empty.";
            return null;
        }
    }
}
