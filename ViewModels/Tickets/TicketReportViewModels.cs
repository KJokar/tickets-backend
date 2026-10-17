using System.ComponentModel.DataAnnotations;

namespace ViewModels.Tickets
{
    public class TicketReportRequest
    {
        public DateOnly? From { get; set; }
        public DateOnly? To { get; set; }
    }

    public sealed class TicketReportTicketsRequest : TicketReportRequest
    {
        [Range(1, int.MaxValue)]
        public int Page { get; set; } = 1;
        [Range(1, 100)]
        public int PerPage { get; set; } = 20;
    }

    public sealed record TicketReportRowDto(int Id, string CustomerName, string Title);

    public sealed record TicketReportDayDto(DateOnly Date, int Count);
    public sealed record TicketReportDto(DateOnly From, DateOnly To, DateTime GeneratedAt,
        string TimeZone, TicketSummaryDto Summary, IReadOnlyList<TicketReportDayDto> DailyCreated);
}
