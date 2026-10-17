namespace Database.Model.Tickets;

// A separate cursor avoids read acknowledgements conflicting with ticket/message writes.
public sealed class ChatReadReceipt
{
    public int TicketId { get; set; }
    public int UserId { get; set; }
    public int LastReadMessageId { get; set; }
    public DateTime ReadAt { get; set; }
}
