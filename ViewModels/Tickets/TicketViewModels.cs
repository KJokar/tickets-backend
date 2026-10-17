using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Database.Enums.Identity;
using Database.Enums.Tickets;

namespace ViewModels.Tickets
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class CreateTicketRequest
    {
        [Required, StringLength(200, MinimumLength = 3)]
        public string Title { get; set; } = null!;
        [Required, StringLength(4000, MinimumLength = 3)]
        public string Description { get; set; } = null!;
        public List<MediaFileReference> Attachments { get; set; } = [];
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class EditTicketTitleRequest
    {
        [Required, StringLength(200, MinimumLength = 3)]
        public string Title { get; set; } = null!;
    }

    public enum TicketSortBy { UpdatedAt, Priority }
    public enum TicketSortOrder { Desc, Asc }

    public sealed class TicketListRequest
    {
        public bool UnassignedOnly { get; set; }
        public bool MineOnly { get; set; }
        [EnumDataType(typeof(TicketStatus))]
        public TicketStatus? Status { get; set; }
        [EnumDataType(typeof(TicketPriority))]
        public TicketPriority? Priority { get; set; }
        [StringLength(200)]
        public string? Search { get; set; }
        [EnumDataType(typeof(TicketSortBy))]
        public TicketSortBy SortBy { get; set; } = TicketSortBy.UpdatedAt;
        [EnumDataType(typeof(TicketSortOrder))]
        public TicketSortOrder SortOrder { get; set; } = TicketSortOrder.Desc;
        [Range(1, int.MaxValue)]
        public int Page { get; set; } = 1;
        [Range(1, 100)]
        public int PerPage { get; set; } = 20;
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class AcceptTicketRequest
    {
        [Required, EnumDataType(typeof(TicketPriority))]
        public TicketPriority? Priority { get; set; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class TransferTicketRequest
    {
        [Range(1, int.MaxValue)]
        public int AgentId { get; set; }
    }

    public sealed class TransferAgentsRequest
    {
        [StringLength(150)]
        public string? Search { get; set; }
        [Range(1, int.MaxValue)]
        public int Page { get; set; } = 1;
        [Range(1, 100)]
        public int PerPage { get; set; } = 10;
    }

    public sealed record TicketAccessChangedDto(int TicketId);

    public sealed record TransferAgentDto(int Id, string DisplayName, UserRole Role, string Mobile, string? AvatarUrl);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class ChangeTicketPriorityRequest
    {
        [Required, EnumDataType(typeof(TicketPriority))]
        public TicketPriority? Priority { get; set; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class SendChatMessageRequest
    {
        [StringLength(4000)]
        public string Body { get; set; } = "";
        public List<MediaFileReference> Attachments { get; set; } = [];
        [Required]
        public Guid? ClientMessageId { get; set; }
    }

    public sealed class ChangeTicketStatusRequest
    {
        [Required, EnumDataType(typeof(TicketStatus))]
        public TicketStatus? Status { get; set; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class EditChatMessageRequest
    {
        [Required, StringLength(4000, MinimumLength = 1)]
        public string Body { get; set; } = null!;
    }

    public sealed record TicketDeletedDto(int TicketId);

    public record TicketListItemDto
    {
        public int Id { get; set; }
        public int CreatedById { get; set; }
        public int? AssignedToId { get; set; }
        public string Title { get; set; } = null!;
        public TicketPriority? Priority { get; set; }
        public bool HasUnreadMessages { get; set; }
        public TicketStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? AcceptedAt { get; set; }
        public TicketUserDto Owner { get; set; } = null!;
        public TicketUserDto? AssignedTo { get; set; }
    }

    public sealed record TicketDto : TicketListItemDto
    {
        public DateTime? ClosedAt { get; set; }
    }

    public sealed class TicketSummaryDto
    {
        public int Total { get; set; }
        public int Open { get; set; }
        public int InProgress { get; set; }
        public int Answered { get; set; }
        public int Closed { get; set; }
    }

    public sealed record ConversationDto(
        int TicketId, DateTime StartedAt, TicketUserDto Client,
        TicketUserDto? Agent, IReadOnlyList<ChatMessageDto> Messages,
        int? LastReadMessageId = null, int? PeerLastReadMessageId = null);

    public sealed class ReadConversationRequest
    {
        [Range(1, int.MaxValue)]
        public int LastReadMessageId { get; set; }
        [Range(1, long.MaxValue)]
        public long? Version { get; set; }
    }

    public sealed record ChatReadReceiptDto(int TicketId, int UserId, int LastReadMessageId, DateTime ReadAt, int? MessageId = null);
    public sealed record ChatTypingDto(int TicketId, int UserId, bool IsTyping, DateTime ExpiresAt);
    public sealed record ChatPresenceDto(int TicketId, int? UserId, bool IsOnline);
    public sealed record PresenceChangedDto(int UserId);

    public sealed class ChatMessageDto
    {
        public int Id { get; set; }
        public int TicketId { get; set; }
        public Guid ClientMessageId { get; set; }
        public long Version { get; set; }
        public string Body { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
        public TicketUserDto Author { get; set; } = null!;
        public DateTime? EditedAt { get; set; }
        public DateTime? DeletedAt { get; set; }
        public bool IsRead { get; set; }
        public IReadOnlyList<MessageAttachmentDto> Attachments { get; set; } = [];
    }

    public sealed class TicketUserDto
    {
        public int Id { get; set; }
        public string DisplayName { get; set; } = null!;
        public string? AvatarUrl { get; set; }
        public UserRole Role { get; set; }
    }
}
