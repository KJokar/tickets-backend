namespace Infrastructure.Authentication;

public static class AuthPolicies
{
    public const string Client = "Client";
    public const string Agent = "Agent";
    public const string TicketUser = "TicketUser";
    public const string ReadTickets = "ReadTickets";
    public const string AuthenticationRateLimit = "Authentication";
}
