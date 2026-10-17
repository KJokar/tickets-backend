using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Database.Model
{
    public class Countries
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = null!;

        [Required]
        [MaxLength(2)]
        public string IsoCode { get; set; } = null!;

        [Required]
        [MaxLength(10)]
        public string PhoneCode { get; set; } = null!;

        [MaxLength(500)]
        public string? Flag { get; set; }
    }
}
