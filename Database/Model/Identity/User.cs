using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Database.Enums.Identity;
using Database.Model.Tickets;

namespace Database.Model.Identity;

public class User
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(16)]
    public string Mobile { get; set; } = null!;
    public int CountryId { get; set; }
    public virtual Database.Model.Countries Country { get; set; } = null!;
    [MaxLength(500)]
    public string? PasswordResetCodeHash { get; set; }
    public DateTime? PasswordResetCodeExpiresAt { get; set; }
    public int PasswordResetCodeAttempts { get; set; }
    [MaxLength(500)]
    public string? PasswordResetTokenHash { get; set; }
    public DateTime? PasswordResetTokenExpiresAt { get; set; }

    [Required]
    [MaxLength(150)]
    public string FullName { get; set; } = null!;

    [Required]
    [MaxLength(500)]
    public string PasswordHash { get; set; } = null!;

    public UserRole Role { get; set; } = UserRole.Client;

    public UserStatus Status { get; set; } = UserStatus.Active;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
    [MaxLength(300)]
    public string? AvatarUrl { get; set; }

    public virtual ICollection<Ticket> Tickets { get; set; } = [];

    public virtual ICollection<ChatMessage> Messages { get; set; } = [];
}
