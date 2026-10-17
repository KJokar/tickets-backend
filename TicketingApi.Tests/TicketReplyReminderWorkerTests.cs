using System.Collections.Concurrent;
using System.Threading.Channels;
using Database.Enums.Identity;
using Database.Model.Tickets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Service.Tickets;
using TicketingApi.Workers;
using ViewModels.Common;
using ViewModels.Tickets;
using Xunit;

namespace TicketingApi.Tests;

public sealed class TicketReplyReminderWorkerTests
{
    [Fact]
    public async Task StartsImmediately_AndCancelsAndDisposesTheRunningScopeOnShutdown()
    {
        var entered = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = new WorkerState
        {
            Process = async (scopeId, token) =>
            {
                entered.SetResult(scopeId);
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return 0;
            }
        };
        using var provider = Services(state);
        using var worker = Worker(provider, new ReplyReminderOptions { PollIntervalSeconds = 60 });

        await worker.StartAsync(default);
        Guid scopeId;
        try
        {
            // The first run must not wait for the configured 60-second timer.
            scopeId = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await worker.StopAsync(timeout.Token);
        }

        Assert.True(worker.ExecuteTask!.IsCompletedSuccessfully);
        Assert.Contains(scopeId, state.Disposed);
    }

    [Fact]
    public async Task FailedRun_IsRetriedWithANewScope_AndWaitingTimerStopsCleanly()
    {
        var calls = Channel.CreateUnbounded<Guid>();
        int attempt = 0;
        var state = new WorkerState
        {
            Process = (scopeId, _) =>
            {
                calls.Writer.TryWrite(scopeId);
                return Interlocked.Increment(ref attempt) == 1
                    ? Task.FromException<int>(new InvalidOperationException("Transient failure"))
                    : Task.FromResult(1);
            }
        };
        using var provider = Services(state);
        using var worker = Worker(provider, new ReplyReminderOptions { PollIntervalSeconds = 1 });

        await worker.StartAsync(default);
        Guid first, second;
        try
        {
            first = await calls.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            second = await calls.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await worker.StopAsync(timeout.Token);
        }

        Assert.NotEqual(first, second);
        Assert.Contains(first, state.Disposed);
        Assert.Contains(second, state.Disposed);
        Assert.True(worker.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task DisabledWorker_DoesNotResolveOrRunTheReminderService()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var worker = Worker(provider, new ReplyReminderOptions { Enabled = false });
        await worker.StartAsync(default);
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(worker.ExecuteTask.IsCompletedSuccessfully);
    }

    private static TicketReplyReminderWorker Worker(ServiceProvider provider, ReplyReminderOptions options) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<TicketReplyReminderWorker>.Instance, Options.Create(options));

    private static ServiceProvider Services(WorkerState state) => new ServiceCollection()
        .AddSingleton(state)
        .AddScoped<IReplyRemindersService, ReminderService>()
        .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

    private sealed class WorkerState
    {
        public required Func<Guid, CancellationToken, Task<int>> Process { get; init; }
        public ConcurrentBag<Guid> Disposed { get; } = [];
    }

    private sealed class ReminderService(WorkerState state) : IReplyRemindersService, IAsyncDisposable
    {
        private readonly Guid scopeId = Guid.NewGuid();
        public Task<int> ProcessDueAsync(CancellationToken cancellationToken = default) => state.Process(scopeId, cancellationToken);
        public ValueTask DisposeAsync() { state.Disposed.Add(scopeId); return ValueTask.CompletedTask; }
        public Task StartAssignmentAsync(Ticket ticket, CancellationToken cancellationToken, bool existing = false) => throw new NotSupportedException();
        public Task<bool> SeenAsync(Ticket ticket, ChatMessage message, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RepliedAsync(int ticketId, int agentId, DateTime repliedAt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RemoveAsync(int ticketId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ApiResultDto<IReadOnlyList<ReplyReminderDto>>> ListAsync(int agentId, UserRole role, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ApiResultDto<DismissReplyReminderResponse>> DismissAsync(int agentId, UserRole role, int ticketId, DismissReplyReminderRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
