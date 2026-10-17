using Asp.Versioning;
using Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Service.Tickets;
using TicketingApi.Areas.Tickets.Models;
using ViewModels.Common;
using ViewModels.Tickets;

namespace TicketingApi.Areas.Tickets.Controllers
{
    [Authorize(Policy = AuthPolicies.TicketUser)]
    [ApiVersion("1.0")]
    [Route("Tickets/v1/[controller]")]
    public sealed class TicketsController(ITicketsService ticketsService, Service.Media.IMediaService media, IReplyRemindersService reminders)
        : TicketingApiBaseController
    {
        [HttpGet("ReplyReminders")]
        [Authorize(Policy = TicketPermissions.ChangeStatus)]
        [ProducesResponseType(typeof(ApiResultDto<IReadOnlyList<ReplyReminderDto>>), StatusCodes.Status200OK, "application/json")]
        public async Task<ActionResult<ApiResultDto<IReadOnlyList<ReplyReminderDto>>>> ReplyRemindersAsync(CancellationToken cancellationToken)
        {
            var result = await reminders.ListAsync(CurrentUserId, CurrentUserRole, cancellationToken);
            return StatusCode(result.StatusCode, result);
        }

        [HttpPost("{id:int}/ReplyReminder/Dismiss")]
        [Authorize(Policy = TicketPermissions.ChangeStatus)]
        [ProducesResponseType(typeof(ApiResultDto<DismissReplyReminderResponse>), StatusCodes.Status200OK, "application/json")]
        public async Task<ActionResult<ApiResultDto<DismissReplyReminderResponse>>> DismissReplyReminderAsync(int id,
            [FromBody] DismissReplyReminderRequest request, CancellationToken cancellationToken)
        {
            var result = await reminders.DismissAsync(CurrentUserId, CurrentUserRole, id, request, cancellationToken);
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("Dashboard")]
        [Authorize(Policy = AuthPolicies.ReadTickets)]
        [ProducesResponseType(typeof(ApiResultDto<TicketSummaryDto>), StatusCodes.Status200OK, "application/json")]
        public async Task<ActionResult<ApiResultDto<TicketSummaryDto>>> DashboardAsync(
            CancellationToken cancellationToken)
        {
            ApiResultDto<TicketSummaryDto> result = await ticketsService.DashboardAsync(
                CurrentUserId,
                CurrentUserRole,
                cancellationToken);

            return StatusCode(result.StatusCode, result);
        }

        [HttpPost("Create")]
        [Authorize(Policy = TicketPermissions.Create)]
        [ProducesResponseType(
            typeof(ApiResultDto<TicketDto>),
            StatusCodes.Status201Created,
            "application/json")]
        public async Task<ActionResult<ApiResultDto<TicketDto>>> CreateAsync(
            [FromBody] CreateTicketRequest request,
            CancellationToken cancellationToken)
        {
            ApiResultDto<TicketDto> result = await ticketsService.CreateAsync(
                CurrentUserId,
                CurrentUserRole,
                request,
                cancellationToken);

            return StatusCode(result.StatusCode, result);
        }

        [HttpPost("CreateWithMedia")]
        [Authorize(Policy = TicketPermissions.Create)]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(TicketMediaLimits.MaxRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = TicketMediaLimits.MaxRequestBytes)]
        [ProducesResponseType(typeof(ApiResultDto<TicketDto>), StatusCodes.Status201Created, "application/json")]
        public async Task<ActionResult<ApiResultDto<TicketDto>>> CreateWithMediaAsync(
            [FromForm] CreateTicketWithMediaRequest request, CancellationToken cancellationToken)
        {
            if (request.Files.Count > TicketMediaLimits.MaxFiles || request.Files.Any(file => file.Length == 0) ||
                request.Files.Sum(file => file.Length) > TicketMediaLimits.MaxTotalBytes)
                return BadRequest(ApiResultDto<TicketDto>.Failure("حداکثر ۵ فایل غیرخالی با مجموع حجم ۲۰ مگابایت انتخاب کنید.", 400));
            List<UploadedTicketMedia> files = [];
            foreach (IFormFile file in request.Files)
            {
                using MemoryStream content = new((int)file.Length);
                await file.CopyToAsync(content, cancellationToken);
                files.Add(new UploadedTicketMedia(file.FileName, content.ToArray()));
            }
            var result = await ticketsService.CreateWithMediaAsync(CurrentUserId, CurrentUserRole,
                new CreateTicketRequest { Title = request.Title, Description = request.Description }, files, cancellationToken);
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("{id:int}/Messages/{messageId:int}/Attachments/{attachmentId:guid}")]
        [Authorize(Policy = TicketPermissions.Chat)]
        [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
        public async Task<IActionResult> AttachmentAsync(int id, int messageId, Guid attachmentId, CancellationToken cancellationToken)
        {
            var result = await ticketsService.AttachmentAsync(CurrentUserId, CurrentUserRole, id, messageId, attachmentId, cancellationToken);
            if (result.Data == null) return StatusCode(result.StatusCode, result);
            if (result.Data.StorageKey != null)
            {
                var access = await media.AccessAsync(CurrentUserId, CurrentUserRole, id, messageId, attachmentId, cancellationToken);
                return access.Data == null ? StatusCode(access.StatusCode, access) : Redirect(access.Data.Url);
            }
            return File(result.Data.Content!, result.Data.ContentType, result.Data.FileName, enableRangeProcessing: true);
        }

        [HttpGet("{id:int}/Messages/{messageId:int}/Attachments/{attachmentId:guid}/Access")]
        [Authorize(Policy = TicketPermissions.Chat)]
        public async Task<IActionResult> AttachmentAccess(int id, int messageId, Guid attachmentId, CancellationToken cancellationToken)
        {
            var result = await media.AccessAsync(CurrentUserId, CurrentUserRole, id, messageId, attachmentId, cancellationToken);
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("Reports")]
        [Authorize(Policy = AuthPolicies.Agent)]
        [ProducesResponseType(typeof(ApiResultDto<TicketReportDto>), StatusCodes.Status200OK, "application/json")]
        public async Task<ActionResult<ApiResultDto<TicketReportDto>>> ReportAsync([FromQuery] TicketReportRequest request, CancellationToken cancellationToken)
        {
            var result = await ticketsService.ReportAsync(CurrentUserId, CurrentUserRole, request, cancellationToken);
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("Reports/Tickets")]
        [Authorize(Policy = AuthPolicies.Agent)]
        [ProducesResponseType(typeof(ApiResultDto<IReadOnlyList<TicketReportRowDto>>), StatusCodes.Status200OK, "application/json")]
        public async Task<ActionResult<ApiResultDto<IReadOnlyList<TicketReportRowDto>>>> ReportTicketsAsync([FromQuery] TicketReportTicketsRequest request, CancellationToken cancellationToken)
        {
            var result = await ticketsService.ReportTicketsAsync(CurrentUserId, CurrentUserRole, request, cancellationToken);
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("Select/{id:int}")]
        [Authorize(Policy = AuthPolicies.ReadTickets)]
        [ProducesResponseType(
            typeof(ApiResultDto<TicketDto>),
            StatusCodes.Status200OK,
            "application/json")]
        public async Task<ActionResult<ApiResultDto<TicketDto>>> SelectAsync(
            [FromRoute] int id,
            CancellationToken cancellationToken)
        {
            ApiResultDto<TicketDto> result = await ticketsService.SelectAsync(
                CurrentUserId,
                CurrentUserRole,
                id,
                cancellationToken);

            return StatusCode(result.StatusCode, result);
        }

        [HttpPatch("{id:int}")]
        [Authorize(Policy = TicketPermissions.Create)]
        public async Task<ActionResult<ApiResultDto<TicketDto>>> EditAsync(int id, [FromBody] EditTicketTitleRequest request, CancellationToken cancellationToken)
        {
            var result = await ticketsService.EditAsync(CurrentUserId, CurrentUserRole, id, request, cancellationToken);
            return StatusCode(result.StatusCode, result);
        }

        [HttpDelete("{id:int}")]
        [Authorize(Policy = TicketPermissions.Create)]
        public async Task<ActionResult<ApiResultDto<TicketDeletedDto>>> DeleteAsync(int id, CancellationToken cancellationToken)
        {
            var result = await ticketsService.DeleteAsync(CurrentUserId, CurrentUserRole, id, cancellationToken);
            return StatusCode(result.StatusCode, result);
        }

        [HttpPatch("{id:int}/Messages/{messageId:int}")]
        [Authorize(Policy = TicketPermissions.Chat)]
        public async Task<ActionResult<ApiResultDto<ChatMessageDto>>> EditMessageAsync(int id, int messageId, [FromBody] EditChatMessageRequest request, CancellationToken cancellationToken)
        {
            var result = await ticketsService.EditMessageAsync(CurrentUserId, CurrentUserRole, id, messageId, request, cancellationToken);
            return StatusCode(result.StatusCode, result);
        }

        [HttpDelete("{id:int}/Messages/{messageId:int}")]
        [Authorize(Policy = TicketPermissions.Chat)]
        public async Task<ActionResult<ApiResultDto<ChatMessageDto>>> DeleteMessageAsync(int id, int messageId, CancellationToken cancellationToken)
        {
            var result = await ticketsService.DeleteMessageAsync(CurrentUserId, CurrentUserRole, id, messageId, cancellationToken);
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("List")]
        [Authorize(Policy = AuthPolicies.ReadTickets)]
        [ProducesResponseType(
            typeof(ApiResultDto<IReadOnlyList<TicketListItemDto>>),
            StatusCodes.Status200OK,
            "application/json")]
        public async Task<ActionResult<ApiResultDto<IReadOnlyList<TicketListItemDto>>>>
            ListAsync(
                [FromQuery] TicketListRequest request,
                CancellationToken cancellationToken)
        {
            ApiResultDto<IReadOnlyList<TicketListItemDto>> result =
                await ticketsService.ListAsync(
                    CurrentUserId,
                    CurrentUserRole,
                    request,
                    cancellationToken);

            return StatusCode(result.StatusCode, result);
        }

        [HttpPost("{id:int}/Messages")]
        [Authorize(Policy = TicketPermissions.Chat)]
        [ProducesResponseType(
            typeof(ApiResultDto<ChatMessageDto>),
            StatusCodes.Status200OK,
            "application/json")]
        public async Task<ActionResult<ApiResultDto<ChatMessageDto>>> SendMessageAsync(
            [FromRoute] int id,
            [FromBody] SendChatMessageRequest request,
            CancellationToken cancellationToken)
        {
            ApiResultDto<ChatMessageDto> result = await ticketsService.SendMessageAsync(
                CurrentUserId,
                CurrentUserRole,
                id,
                request,
                cancellationToken);

            return StatusCode(result.StatusCode, result);
        }

        [HttpPost("{id:int}/Accept")]
        [Authorize(Policy = TicketPermissions.Accept)]
        [ProducesResponseType(typeof(ApiResultDto<TicketDto>), StatusCodes.Status200OK, "application/json")]
        [ProducesResponseType(typeof(ApiResultDto<TicketDto>), StatusCodes.Status409Conflict, "application/json")]
        public async Task<ActionResult<ApiResultDto<TicketDto>>> AcceptAsync(
            int id, [FromBody] AcceptTicketRequest request, CancellationToken cancellationToken)
        {
            ApiResultDto<TicketDto> result = await ticketsService.AcceptAsync(
                CurrentUserId, CurrentUserRole, id, request, cancellationToken);
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("{id:int}/Conversation")]
        [Authorize(Policy = TicketPermissions.Chat)]
        [ProducesResponseType(typeof(ApiResultDto<ConversationDto>), StatusCodes.Status200OK, "application/json")]
        public async Task<ActionResult<ApiResultDto<ConversationDto>>> ConversationAsync(int id, CancellationToken cancellationToken, [FromQuery] bool includeDeleted = false)
        {
            ApiResultDto<ConversationDto> result = await ticketsService.ConversationAsync(
                CurrentUserId, CurrentUserRole, id, cancellationToken, includeDeleted);
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("{id:int}/TransferAgents")]
        [Authorize(Policy = TicketPermissions.ChangeStatus)]
        [ProducesResponseType(typeof(ApiResultDto<IReadOnlyList<TransferAgentDto>>), StatusCodes.Status200OK, "application/json")]
        public async Task<ActionResult<ApiResultDto<IReadOnlyList<TransferAgentDto>>>> TransferAgentsAsync(
            int id, [FromQuery] TransferAgentsRequest request, CancellationToken cancellationToken)
        {
            var result = await ticketsService.TransferAgentsAsync(CurrentUserId, CurrentUserRole, id, request, cancellationToken);
            return StatusCode(result.StatusCode, result);
        }

        [HttpPost("{id:int}/Transfer")]
        [Authorize(Policy = TicketPermissions.ChangeStatus)]
        [ProducesResponseType(typeof(ApiResultDto<TicketDto>), StatusCodes.Status200OK, "application/json")]
        public async Task<ActionResult<ApiResultDto<TicketDto>>> TransferAsync(
            int id, [FromBody] TransferTicketRequest request, CancellationToken cancellationToken)
        {
            var result = await ticketsService.TransferAsync(CurrentUserId, CurrentUserRole, id, request, cancellationToken);
            return StatusCode(result.StatusCode, result);
        }

        [HttpPost("{id:int}/Release")]
        [Authorize(Policy = TicketPermissions.ChangeStatus)]
        [ProducesResponseType(typeof(ApiResultDto<TicketDto>), StatusCodes.Status200OK, "application/json")]
        public async Task<ActionResult<ApiResultDto<TicketDto>>> ReleaseAsync(int id, CancellationToken cancellationToken)
        {
            var result = await ticketsService.ReleaseAsync(CurrentUserId, CurrentUserRole, id, cancellationToken);
            return StatusCode(result.StatusCode, result);
        }

        [HttpPost("{id:int}/Read")]
        [Authorize(Policy = TicketPermissions.Chat)]
        public async Task<ActionResult<ApiResultDto<ChatReadReceiptDto>>> MarkReadAsync(int id, [FromBody] ReadConversationRequest request, CancellationToken cancellationToken)
        {
            var result = await ticketsService.MarkReadAsync(CurrentUserId, CurrentUserRole, id, request, cancellationToken);
            return StatusCode(result.StatusCode, result);
        }

        [HttpPatch("{id:int}/Priority")]
        [Authorize(Policy = TicketPermissions.ChangePriority)]
        [ProducesResponseType(typeof(ApiResultDto<TicketDto>), StatusCodes.Status200OK, "application/json")]
        public async Task<ActionResult<ApiResultDto<TicketDto>>> ChangePriorityAsync(
            int id, [FromBody] ChangeTicketPriorityRequest request, CancellationToken cancellationToken)
        {
            ApiResultDto<TicketDto> result = await ticketsService.ChangePriorityAsync(
                CurrentUserId, CurrentUserRole, id, request, cancellationToken);
            return StatusCode(result.StatusCode, result);
        }

        [HttpPatch("{id:int}/Status")]
        [Authorize(Policy = TicketPermissions.ChangeStatus)]
        [ProducesResponseType(
            typeof(ApiResultDto<TicketDto>),
            StatusCodes.Status200OK,
            "application/json")]
        public async Task<ActionResult<ApiResultDto<TicketDto>>> ChangeStatusAsync(
            [FromRoute] int id,
            [FromBody] ChangeTicketStatusRequest request,
            CancellationToken cancellationToken)
        {
            ApiResultDto<TicketDto> result = await ticketsService.ChangeStatusAsync(
                CurrentUserId,
                CurrentUserRole,
                id,
                request,
                cancellationToken);

            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("Unassigned")]
        [Authorize(Policy = AuthPolicies.Agent)]
        [ProducesResponseType(
            typeof(ApiResultDto<IReadOnlyList<TicketListItemDto>>),
            StatusCodes.Status200OK,
            "application/json")]
        public async Task<ActionResult<ApiResultDto<IReadOnlyList<TicketListItemDto>>>>
            UnassignedAsync(
                [FromQuery] TicketListRequest request,
                CancellationToken cancellationToken)
        {
            ApiResultDto<IReadOnlyList<TicketListItemDto>> result =
                await ticketsService.ListUnassignedAsync(
                    CurrentUserId,
                    CurrentUserRole,
                    request,
                    cancellationToken);

            return StatusCode(result.StatusCode, result);
        }
    }
}
