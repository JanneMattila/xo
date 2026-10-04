using System.Collections.Concurrent;
using Xo.Ai.Hosting;
using Xo.Domain;
using Xo.MatchRunner;

namespace Xo.Web.Api;

public sealed class HumanGameStore(ComponentRegistry registry, GameArchive archive) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, HumanGameSession> sessions = new();

    public async Task<HumanGameSession> CreateAsync(
        string aiId,
        Mark humanMark,
        CancellationToken cancellationToken)
    {
        var definition = registry.GetRequired(aiId);
        var session = await HumanGameSession.CreateAsync(definition, humanMark, cancellationToken);
        if (!sessions.TryAdd(session.Id, session))
        {
            await session.DisposeAsync();
            throw new InvalidOperationException("Failed to register the human game.");
        }

        return session;
    }

    public HumanGameSession? Get(Guid id) => sessions.GetValueOrDefault(id);

    public async Task<HumanGameSession> PlayAsync(
        Guid id,
        Position position,
        CancellationToken cancellationToken)
    {
        var session = Get(id) ?? throw new KeyNotFoundException("Human game was not found.");
        await session.PlayHumanMoveAsync(position, cancellationToken);
        if (session.State.Status != GameStatus.InProgress && sessions.TryRemove(id, out _))
        {
            archive.Add(session.ToCompletedGame());
            await session.DisposeAsync();
        }

        return session;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in sessions.Values)
        {
            await session.DisposeAsync();
        }

        sessions.Clear();
    }
}