namespace Infrastructure.Messaging
{
    public interface ISmsSender
    {
        Task SendAsync(
            string mobile,
            string message,
            CancellationToken cancellationToken = default);
    }
}
