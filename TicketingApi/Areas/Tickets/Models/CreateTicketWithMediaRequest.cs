using System.ComponentModel.DataAnnotations;

namespace TicketingApi.Areas.Tickets.Models
{
    public sealed class CreateTicketWithMediaRequest
    {
        [Required, StringLength(200, MinimumLength = 3)]
        public string Title { get; set; } = null!;
        [Required, StringLength(4000, MinimumLength = 3)]
        public string Description { get; set; } = null!;
        public List<IFormFile> Files { get; set; } = [];
    }
}
