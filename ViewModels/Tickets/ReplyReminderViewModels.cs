using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ViewModels.Tickets;

public sealed record ReplyReminderDto(int TicketId, string Title, string CustomerName, Guid NotificationId, DateTime RaisedAt);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class DismissReplyReminderRequest
{
    [Required]
    public Guid? NotificationId { get; set; }
}

public sealed record DismissReplyReminderResponse(int TicketId, DateTime NextReminderAt);
