using System.Collections.Concurrent;
using Xo.Ai.Hosting;
using Xo.MatchRunner;

namespace Xo.Web.Api;

public sealed class AiRunManager
{
    private readonly ComponentRegistry registry;
    private readonly GameArchive archive;
    private readonly AiMatchRunner matchRunner;
    private readonly ConcurrentDictionary<Guid, RunState> runs = new();
    private readonly CancellationToken applicationStopping;

    public AiRunManager(
        ComponentRegistry registry,
        GameArchive archive,
        IHostApplicationLifetime applicationLifetime)
    {
        this.registry = registry;
        this.archive = archive;
        matchRunner = new AiMatchRunner(registry);
        applicationStopping = applicationLifetime.ApplicationStopping;
    }

    public AiRunView Start(CreateAiRunRequest request)
    {
        if (request.Games is < 1 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Games must be between 1 and 10000.");
        }

        _ = registry.GetRequired(request.Player1);
        _ = registry.GetRequired(request.Player2);
        _ = OpeningSuite.GetRequired(request.OpeningSet);

        var state = new RunState(request);
        if (!runs.TryAdd(state.Id, state))
        {
            throw new InvalidOperationException("Failed to create the AI run.");
        }

        _ = Task.Run(() => ExecuteAsync(state), CancellationToken.None);
        return state.Snapshot();
    }

    public AiRunView? Get(Guid id) => runs.GetValueOrDefault(id)?.Snapshot();

    private async Task ExecuteAsync(RunState state)
    {
        state.SetStatus("running");
        try
        {
            for (var index = 0; index < state.Request.Games; index++)
            {
                applicationStopping.ThrowIfCancellationRequested();
                var game = await matchRunner.PlayAsync(
                    state.Request.Player1,
                    state.Request.Player2,
                    !state.Request.AlternateSides || index % 2 == 0,
                    OpeningSuite.Select(state.Request.OpeningSet, index, state.Request.AlternateSides),
                    applicationStopping);
                archive.Add(game);
                state.Record(game);
            }

            state.SetStatus("completed");
        }
        catch (OperationCanceledException) when (applicationStopping.IsCancellationRequested)
        {
            state.SetStatus("cancelled");
        }
        catch (Exception exception)
        {
            state.Fail(exception.Message);
        }
    }

    private sealed class RunState(CreateAiRunRequest request)
    {
        private readonly object sync = new();
        private readonly MatchStatisticsAccumulator statistics = new();
        private int completedGames;
        private string status = "queued";
        private string? error;

        public Guid Id { get; } = Guid.NewGuid();

        public CreateAiRunRequest Request { get; } = request;

        public void Record(CompletedGame game)
        {
            lock (sync)
            {
                statistics.Record(game);
                completedGames++;
            }
        }

        public void SetStatus(string value)
        {
            lock (sync)
            {
                status = value;
            }
        }

        public void Fail(string message)
        {
            lock (sync)
            {
                error = message;
                status = "failed";
            }
        }

        public AiRunView Snapshot()
        {
            lock (sync)
            {
                return new AiRunView(
                    Id,
                    Request.Player1,
                    Request.Player2,
                    Request.Games,
                    completedGames,
                    status,
                    statistics.Snapshot(),
                    error,
                    Request.OpeningSet);
            }
        }
    }
}