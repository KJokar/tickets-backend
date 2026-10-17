using Microsoft.AspNetCore.SignalR;
using Database.Enums.Identity;
using Repository.Tickets;
using Service.Tickets;
using ViewModels.Tickets;

namespace TicketingApi.Realtime
{
    public sealed class TicketEvents(IHubContext<TicketsHub> hub, ILogger<TicketEvents> logger, ITicketsRepository ticketsRepository) : ITicketEvents
    {
        public Task ReplyRemindersChangedAsync(int agentId, int ticketId, CancellationToken cancellationToken = default) =>
            NotifyAsync(() => hub.Clients.Group(TicketsHub.UserGroup(agentId))
                .SendAsync("ReplyRemindersChanged", new TicketAccessChangedDto(ticketId), cancellationToken));

        public Task CreatedAsync(TicketDto ticket, CancellationToken cancellationToken = default)
        {
            return NotifyAsync(async () =>
            {
                await hub.Clients.Group(TicketsHub.AgentQueue).SendAsync("QueueChanged", new { ticketId = ticket.Id }, cancellationToken);
                await NotifyTicketChangedAsync(ticket, cancellationToken);
            });
        }

        public Task ChangedAsync(TicketDto ticket, CancellationToken cancellationToken = default)
        {
            return NotifyAsync(async () =>
            {
                // The shared queue never receives private ticket or conversation data.
                await hub.Clients.Group(TicketsHub.AgentQueue).SendAsync("QueueChanged", new { ticketId = ticket.Id }, cancellationToken);
                await NotifyTicketChangedAsync(ticket, cancellationToken);
            });
        }

        public Task AssignmentChangedAsync(TicketDto ticket, int previousAgentId, CancellationToken cancellationToken = default) =>
            NotifyAsync(async () =>
            {
                await hub.Clients.Group(TicketsHub.UserGroup(previousAgentId))
                    .SendAsync("TicketAccessChanged", new TicketAccessChangedDto(ticket.Id), cancellationToken);
                await hub.Clients.Group(TicketsHub.AgentQueue).SendAsync("QueueChanged", new { ticketId = ticket.Id }, cancellationToken);
                await NotifyTicketChangedAsync(ticket, cancellationToken);
            });

        public Task MessageSentAsync(TicketDto ticket, ChatMessageDto message, CancellationToken cancellationToken = default)
        {
            return NotifyAsync(async () =>
            {
                if (!ticket.AssignedToId.HasValue)
                    await hub.Clients.Group(TicketsHub.AgentQueue).SendAsync("QueueChanged", new { ticketId = ticket.Id }, cancellationToken);
                await Participants(ticket).SendAsync("MessageReceived", message, cancellationToken);
                await NotifyTicketChangedAsync(ticket, cancellationToken);
            });
        }

        public Task DeletedAsync(int ticketId, int ownerId, CancellationToken cancellationToken = default) =>
            NotifyAsync(async () =>
            {
                await hub.Clients.Group(TicketsHub.AgentQueue).SendAsync("QueueChanged", new { ticketId }, cancellationToken);
                await hub.Clients.Group(TicketsHub.UserGroup(ownerId)).SendAsync("TicketDeleted", new TicketDeletedDto(ticketId), cancellationToken);
            });

        public Task ReadAsync(TicketDto ticket, ChatReadReceiptDto receipt, CancellationToken cancellationToken = default) =>
            NotifyAsync(async () =>
            {
                await Participants(ticket).SendAsync("MessagesRead", receipt, cancellationToken);
                if (!ticket.AssignedToId.HasValue)
                    await hub.Clients.Group(TicketsHub.AgentQueue).SendAsync("QueueChanged", new { ticketId = ticket.Id }, cancellationToken);
            });

        public Task TypingAsync(TicketDto ticket, ChatTypingDto typing, CancellationToken cancellationToken = default) =>
            NotifyAsync(() => hub.Clients.Group(TicketsHub.UserGroup(typing.UserId == ticket.CreatedById ? ticket.AssignedToId!.Value : ticket.CreatedById))
                .SendAsync("TypingChanged", typing, cancellationToken));

        private async Task NotifyTicketChangedAsync(TicketDto ticket, CancellationToken cancellationToken)
        {
            // Unread state belongs to the recipient, not the user who triggered the event.
            TicketDto? clientView = await ticketsRepository.GetAsync(ticket.Id, ticket.CreatedById, UserRole.Client, cancellationToken);
            if (clientView != null)
                await hub.Clients.Group(TicketsHub.UserGroup(ticket.CreatedById))
                    .SendAsync("TicketChanged", clientView, cancellationToken);
            if (ticket.AssignedToId.HasValue)
            {
                TicketDto? agentView = await ticketsRepository.GetAsync(ticket.Id, ticket.AssignedToId.Value, UserRole.Agent, cancellationToken);
                if (agentView != null)
                    await hub.Clients.Group(TicketsHub.UserGroup(ticket.AssignedToId.Value))
                        .SendAsync("TicketChanged", agentView, cancellationToken);
            }
        }

        private IClientProxy Participants(TicketDto ticket)
        {
            string[] groups = ticket.AssignedToId.HasValue
                ? [TicketsHub.UserGroup(ticket.CreatedById), TicketsHub.UserGroup(ticket.AssignedToId.Value)]
                : [TicketsHub.UserGroup(ticket.CreatedById)];
            return hub.Clients.Groups(groups);
        }

        private async Task NotifyAsync(Func<Task> notify)
        {
            try { await notify(); }
            catch (Exception exception)
            {
                // The database commit succeeded; history remains available on reconnect.
                logger.LogWarning(exception, "Could not deliver a ticket notification.");
            }
        }
    }
}
