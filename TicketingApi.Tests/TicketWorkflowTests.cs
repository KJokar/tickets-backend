using Database.Enums.Tickets;
using Service.Tickets;
using Xunit;

namespace TicketingApi.Tests;

public sealed class TicketWorkflowTests
{
    [Theory]
    [InlineData(TicketStatus.Open, TicketStatus.InProgress)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Answered)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Closed)]
    [InlineData(TicketStatus.Answered, TicketStatus.InProgress)]
    [InlineData(TicketStatus.Answered, TicketStatus.Closed)]
    public void CanTransition_ReturnsTrue_ForSupportedTransitions(
        TicketStatus current,
        TicketStatus next)
    {
        Assert.True(TicketWorkflow.CanTransition(current, next));
    }

    [Theory]
    [InlineData(TicketStatus.Open, TicketStatus.Answered)]
    [InlineData(TicketStatus.Open, TicketStatus.Closed)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Open)]
    [InlineData(TicketStatus.Answered, TicketStatus.Open)]
    [InlineData(TicketStatus.Closed, TicketStatus.Open)]
    [InlineData(TicketStatus.Closed, TicketStatus.InProgress)]
    [InlineData(TicketStatus.Closed, TicketStatus.Answered)]
    [InlineData(TicketStatus.Closed, TicketStatus.Closed)]
    public void CanTransition_ReturnsFalse_ForUnsupportedTransitions(
        TicketStatus current,
        TicketStatus next)
    {
        Assert.False(TicketWorkflow.CanTransition(current, next));
    }
}
