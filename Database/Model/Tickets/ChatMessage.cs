using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Database.Model.Identity;

namespace Database.Model.Tickets;

public class ChatMessage
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int TicketId { get; set; }

    public int AuthorUserId { get; set; }

    public Guid ClientMessageId { get; set; }
    public long Version { get; set; } = 1;

    [Required]
    [MaxLength(4000)]
    public string Body { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EditedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime? ReadAt { get; set; }

    public ICollection<MessageAttachment> Attachments { get; set; } = new List<MessageAttachment>();

    public virtual Ticket Ticket { get; set; } = null!;

    public virtual User Author { get; set; } = null!;
}
