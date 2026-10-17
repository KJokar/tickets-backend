namespace Infrastructure.Realtime
{
    public interface IUserPresence
    {
        bool Connect(int userId, string connectionId);
        bool Disconnect(int userId, string connectionId);
        bool IsOnline(int userId);
    }

    public sealed class UserPresence : IUserPresence
    {
        private readonly object gate = new();
        private readonly Dictionary<int, HashSet<string>> connections = [];

        public bool Connect(int userId, string connectionId)
        {
            lock (gate)
            {
                if (!connections.TryGetValue(userId, out var active))
                    connections[userId] = active = [];
                return active.Add(connectionId) && active.Count == 1;
            }
        }

        public bool Disconnect(int userId, string connectionId)
        {
            lock (gate)
            {
                if (!connections.TryGetValue(userId, out var active) || !active.Remove(connectionId) || active.Count > 0)
                    return false;
                connections.Remove(userId);
                return true;
            }
        }

        public bool IsOnline(int userId)
        {
            lock (gate) return connections.ContainsKey(userId);
        }
    }
}
