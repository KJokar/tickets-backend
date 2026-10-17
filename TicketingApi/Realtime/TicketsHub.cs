using Database.Enums.Identity;
using Infrastructure.Authentication;
using Infrastructure.Extension;
using Infrastructure.Realtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Service.Tickets;
using ViewModels.Common;
using ViewModels.Tickets;

namespace TicketingApi.Realtime
{
    [Authorize(Policy = AuthPolicies.TicketUser)]
    public sealed class TicketsHub(ITicketsService ticketsService, ITicketPresenceService presenceService,
        IUserPresence presence, ILogger<TicketsHub> logger) : Hub
    {
        internal const string AgentQueue = "agents:queue";
        internal static string UserGroup(int userId) => $"user:{userId}";

        public override async Task OnConnectedAsync()
        {
            int userId = Context.User!.GetUserId()!.Value;
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));
            if (Context.User!.GetUserRole() == nameof(UserRole.Agent))
                await Groups.AddToGroupAsync(Context.ConnectionId, AgentQueue);
            if (presence.Connect(userId, Context.ConnectionId)) await NotifyPresenceAsync(userId);
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            int userId = Context.User!.GetUserId()!.Value;
            if (presence.Disconnect(userId, Context.ConnectionId)) await NotifyPresenceAsync(userId);
            await base.OnDisconnectedAsync(exception);
        }

        [Authorize(Policy = TicketPermissions.Chat)]
        public Task<ApiResultDto<ChatPresenceDto>> GetPresence(int ticketId) =>
            presenceService.GetAsync(Context.User!.GetUserId()!.Value, Enum.Parse<UserRole>(Context.User!.GetUserRole()!), ticketId, Context.ConnectionAborted);

        private async Task NotifyPresenceAsync(int userId)
        {
            try
            {
                var recipients = await presenceService.RecipientsAsync(userId);
                if (recipients.Count > 0)
                    // The notification invalidates a snapshot; callers must recheck ticket access.
                    await Clients.Groups(recipients.Select(UserGroup).ToArray()).SendAsync("PresenceChanged", new PresenceChangedDto(userId));
            }
            catch (Exception exception) { logger.LogWarning(exception, "Could not deliver a presence notification."); }
        }

        [Authorize(Policy = TicketPermissions.Chat)]
        public Task<ApiResultDto<ChatMessageDto>> SendMessage(int ticketId, SendChatMessageRequest request)
        {
            return ticketsService.SendMessageAsync(Context.User!.GetUserId()!.Value,
                Enum.Parse<UserRole>(Context.User!.GetUserRole()!), ticketId, request,
                Context.ConnectionAborted);
        }
        [Authorize(Policy = TicketPermissions.Chat)]
        public Task<ApiResultDto<ChatReadReceiptDto>> MarkRead(int ticketId, ReadConversationRequest request) =>
            ticketsService.MarkReadAsync(Context.User!.GetUserId()!.Value, Enum.Parse<UserRole>(Context.User!.GetUserRole()!), ticketId, request, Context.ConnectionAborted);

        [Authorize(Policy = TicketPermissions.Chat)]
        public async Task SetTyping(int ticketId, bool isTyping)
        {
            string key = $"typing:{ticketId}";
            DateTime now = DateTime.UtcNow;
            if (Context.Items.TryGetValue(key, out object? value) && value is ValueTuple<bool, DateTime> last && last.Item1 == isTyping && now - last.Item2 < TimeSpan.FromSeconds(1)) return;
            Context.Items[key] = (isTyping, now);
            await ticketsService.SetTypingAsync(Context.User!.GetUserId()!.Value, Enum.Parse<UserRole>(Context.User!.GetUserRole()!), ticketId, isTyping, Context.ConnectionAborted);
        }
    }
}
