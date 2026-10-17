using Database.Model.Identity;

namespace Database.Model.Tickets;

public sealed class TicketReplyReminder
{
    public int TicketId { get; set; }
    public int AgentId { get; set; }
    public DateTime? LastReplyAt { get; set; }
    public DateTime? SeenAt { get; set; }
    public DateTime? NextReminderAt { get; set; }
    public Guid? NotificationId { get; set; }
    public DateTime? RaisedAt { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
    public Ticket Ticket { get; set; } = null!;
    public User Agent { get; set; } = null!;
}
