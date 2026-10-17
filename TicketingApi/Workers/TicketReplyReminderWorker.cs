using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Service.Tickets;

namespace TicketingApi.Workers
{
    public sealed class TicketReplyReminderWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<TicketReplyReminderWorker> _logger;
        private readonly ReplyReminderOptions _options;

        public TicketReplyReminderWorker(
            IServiceScopeFactory scopeFactory,
            ILogger<TicketReplyReminderWorker> logger,
            IOptions<ReplyReminderOptions> options)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _options = options.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.Enabled)
            {
                _logger.LogInformation("Ticket reply reminder worker is disabled.");
                return;
            }

            TimeSpan interval = TimeSpan.FromSeconds(_options.PollIntervalSeconds);
            _logger.LogInformation("Ticket reply reminder worker started. Interval: {Interval}.", interval);

            using PeriodicTimer timer = new(interval);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogDebug("Ticket reply reminder worker tick starting at {Now:o} (UTC).", DateTime.UtcNow);

                    try
                    {
                        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
                        IReplyRemindersService service =
                            scope.ServiceProvider.GetRequiredService<IReplyRemindersService>();

                        int count = await service.ProcessDueAsync(stoppingToken);
                        if (count > 0)
                            _logger.LogInformation("Ticket reply reminder worker raised {Count} reminders.", count);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception exception)
                    {
                        _logger.LogError(exception, "Ticket reply reminder worker tick failed.");
                    }

                    try
                    {
                        if (!await timer.WaitForNextTickAsync(stoppingToken))
                            break;
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }
            finally
            {
                _logger.LogInformation("Ticket reply reminder worker stopping.");
            }
        }
    }
}
