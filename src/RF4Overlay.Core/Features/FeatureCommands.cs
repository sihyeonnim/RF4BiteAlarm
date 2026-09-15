namespace RF4Overlay.Core.Features;

public enum FeatureId { BiteAlarm, Metronome, AutoPilking }
public enum FeatureAction { Start, Stop, Toggle }
public enum FeatureState { Unavailable, Stopped, Running, Stopping, Faulted }
public sealed record FeatureStatus(FeatureId Id, string Name, FeatureState State, string Description, string? Error = null);
public sealed record FeatureCommand(FeatureId Feature, FeatureAction Action);
public sealed record FeatureCommandResult(bool Succeeded, string Message);

public interface IFeature
{
    FeatureStatus InitialStatus { get; }
    // Runs until cancellation. Implementations release resources in finally.
    Task RunAsync(CancellationToken cancellationToken);
}

/// <summary>All entry points share this runtime. Commands serialize per feature; failures stay isolated.</summary>
public sealed class FeatureCommandDispatcher : IAsyncDisposable
{
    private readonly Dictionary<FeatureId, Entry> _entries;
    private readonly object _shutdownLock = new();
    private Task? _shutdown;
    private int _closing;
    public event EventHandler<FeatureStatus>? StatusChanged;

    public FeatureCommandDispatcher(IEnumerable<IFeature> features) =>
        _entries = features.ToDictionary(f => f.InitialStatus.Id, f => new Entry(f));

    public IReadOnlyList<FeatureStatus> GetStatuses() =>
        _entries.Values.Select(e => Volatile.Read(ref e.Status)).ToArray();

    public async Task<FeatureCommandResult> ExecuteAsync(FeatureCommand command, CancellationToken cancellationToken = default)
    {
        if (!_entries.TryGetValue(command.Feature, out var entry))
            return new(false, "등록되지 않은 기능입니다.");
        if (!Enum.IsDefined(command.Action))
            return new(false, "잘못된 명령입니다.");
        try
        {
            await entry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return new(false, "명령이 취소되었습니다."); }
        try
        {
            if (Volatile.Read(ref _closing) != 0) return new(false, "프로그램 종료 중입니다.");
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Status.State == FeatureState.Unavailable) return new(false, entry.Status.Description);
            var stop = command.Action == FeatureAction.Stop ||
                command.Action == FeatureAction.Toggle && entry.Run is { IsCompleted: false };
            if (stop)
            {
                await StopAsync(entry).ConfigureAwait(false);
                return new(entry.Status.State != FeatureState.Faulted, entry.Status.Error ?? "정지했습니다.");
            }
            if (entry.Run is { IsCompleted: false }) return new(true, "이미 실행 중입니다.");
            entry.Cancellation?.Dispose();
            entry.Cancellation = new();
            var token = entry.Cancellation.Token;
            SetStatus(entry, FeatureState.Running);
            entry.Run = Task.Run(async () =>
            {
                try
                {
                    await entry.Feature.RunAsync(token).ConfigureAwait(false);
                    SetStatus(entry, FeatureState.Stopped);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    SetStatus(entry, FeatureState.Stopped);
                }
                catch (Exception error) { SetStatus(entry, FeatureState.Faulted, error.Message); }
            }, CancellationToken.None);
            return new(true, "시작했습니다.");
        }
        catch (OperationCanceledException) { return new(false, "명령이 취소되었습니다."); }
        finally { entry.Gate.Release(); }
    }

    private async Task StopAsync(Entry entry)
    {
        if (entry.Run is { IsCompleted: false })
        {
            SetStatus(entry, FeatureState.Stopping);
            await entry.Cancellation!.CancelAsync().ConfigureAwait(false);
            await entry.Run.ConfigureAwait(false);
        }
    }

    private void SetStatus(Entry entry, FeatureState state, string? error = null)
    {
        var status = entry.Feature.InitialStatus with { State = state, Error = error };
        Volatile.Write(ref entry.Status, status);
        // Observer bugs cannot terminate a feature or prevent cleanup.
        foreach (EventHandler<FeatureStatus> handler in StatusChanged?.GetInvocationList() ?? [])
        {
            try { handler(this, status); }
            catch (Exception observerError) { System.Diagnostics.Trace.TraceError(observerError.ToString()); }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_shutdownLock)
        {
            Interlocked.Exchange(ref _closing, 1);
            return new(_shutdown ??= ShutdownAsync());
        }
    }

    private async Task ShutdownAsync()
    {
        await Task.WhenAll(_entries.Values.Select(async entry =>
        {
            await entry.Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                await StopAsync(entry).ConfigureAwait(false);
                entry.Cancellation?.Dispose();
                entry.Cancellation = null;
            }
            finally { entry.Gate.Release(); }
        })).ConfigureAwait(false);
    }

    private sealed class Entry(IFeature feature)
    {
        public IFeature Feature { get; } = feature;
        public FeatureStatus Status = feature.InitialStatus;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public CancellationTokenSource? Cancellation;
        public Task? Run;
    }
}
