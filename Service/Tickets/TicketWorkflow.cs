using Database.Enums.Tickets;

namespace Service.Tickets
{
    public static class TicketWorkflow
    {
        public static bool CanTransition(
            TicketStatus current,
            TicketStatus next)
        {
            return (current, next) switch
            {
                (TicketStatus.Open, TicketStatus.InProgress) => true,
                (TicketStatus.InProgress, TicketStatus.Answered) => true,
                (TicketStatus.InProgress, TicketStatus.Closed) => true,
                (TicketStatus.Answered, TicketStatus.InProgress) => true,
                (TicketStatus.Answered, TicketStatus.Closed) => true,
                _ => false
            };
        }
    }
}
