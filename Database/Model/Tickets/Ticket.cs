using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Database.Enums.Tickets;
using Database.Model.Identity;

namespace Database.Model.Tickets;

public class Ticket
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();

    public int CreatedById { get; set; }

    public int? AssignedToId { get; set; }

    public virtual User? AssignedTo { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = null!;

    public TicketPriority? Priority { get; set; }

    public DateTime? AcceptedAt { get; set; }

    public TicketStatus Status { get; set; } = TicketStatus.Open;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public DateTime? ClosedAt { get; set; }

    public virtual User Owner { get; set; } = null!;

    public virtual ICollection<ChatMessage> Messages { get; set; } = [];
}
