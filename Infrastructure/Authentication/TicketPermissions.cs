namespace Infrastructure.Authentication
{
    public static class TicketPermissions
    {
        public const string ClaimType = "permission";
        public const string Create = "tickets.create";
        public const string ReadOwn = "tickets.read.own";
        public const string ReadQueue = "tickets.queue.read";
        public const string Accept = "tickets.accept";
        public const string Chat = "tickets.chat";
        public const string ChangePriority = "tickets.priority.change";
        public const string ChangeStatus = "tickets.status.change";
        public const string Filter = "tickets.filter";

        private static readonly IReadOnlyList<string> ClientPermissions =
            Array.AsReadOnly<string>([Create, ReadOwn, Chat, Filter]);

        private static readonly IReadOnlyList<string> AgentPermissions =
            Array.AsReadOnly<string>([ReadOwn, ReadQueue, Accept, Chat, ChangePriority, ChangeStatus, Filter]);

        public static IReadOnlyList<string> ForRole(string role)
        {
            return role switch
            {
                "Client" => ClientPermissions,
                "Agent" => AgentPermissions,
                _ => Array.Empty<string>()
            };
        }
    }
}
