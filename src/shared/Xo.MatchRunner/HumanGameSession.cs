using System.Diagnostics;
using Xo.Ai.Abstractions;
using Xo.Ai.Hosting;
using Xo.Domain;

namespace Xo.MatchRunner;

public sealed class HumanGameSession : IAsyncDisposable
{
    private readonly IGameAi ai;
    private readonly SemaphoreSlim sync = new(1, 1);
    private readonly DateTimeOffset startedAt = DateTimeOffset.UtcNow;
    private readonly List<RecordedMove> recordedMoves = [];
    private long turnStartedTimestamp;
    private bool disposed;

    private HumanGameSession(string aiId, Mark humanMark, IGameAi ai)
    {
        AiId = aiId;
        HumanMark = humanMark;
        this.ai = ai;
        State = GameState.Create();
    }

    public Guid Id { get; } = Guid.NewGuid();

    public string AiId { get; }

    public Mark HumanMark { get; }

    public GameState State { get; private set; }

    public IReadOnlyList<RecordedMove> RecordedMoves => recordedMoves.ToArray();

    public static async Task<HumanGameSession> CreateAsync(
        AiComponentDefinition definition,
        Mark humanMark,
        CancellationToken cancellationToken = default)
    {
        if (humanMark is not (Mark.X or Mark.O))
        {
            throw new ArgumentOutOfRangeException(nameof(humanMark));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var session = new HumanGameSession(definition.Id, humanMark, definition.CreateAi());
        if (humanMark == Mark.O)
        {
            var position = await AiMoveExecutor.ChooseMoveAsync(session.ai, session.State, cancellationToken);
            session.State = session.State.ApplyMove(position, session.State.Revision);
            session.recordedMoves.Add(ToRecordedMove(session.State.Moves[^1], elapsedMilliseconds: 0));
        }

        session.turnStartedTimestamp = Stopwatch.GetTimestamp();
        return session;
    }

    public async Task<GameState> PlayHumanMoveAsync(
        Position position,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await sync.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (State.Status != GameStatus.InProgress || State.NextMark != HumanMark)
            {
                throw new GameRuleException(MoveError.GameAlreadyFinished, "It is not the human player's turn.");
            }

            var elapsedMilliseconds = State.Moves.Count == 0
                ? 0
                : (long)Stopwatch.GetElapsedTime(turnStartedTimestamp).TotalMilliseconds;
            var nextState = State.ApplyMove(position, State.Revision);
            var humanMove = ToRecordedMove(nextState.Moves[^1], elapsedMilliseconds);
            RecordedMove? aiMove = null;
            if (nextState.Status == GameStatus.InProgress)
            {
                var stopwatch = Stopwatch.StartNew();
                var aiPosition = await AiMoveExecutor.ChooseMoveAsync(ai, nextState, cancellationToken);
                nextState = nextState.ApplyMove(aiPosition, nextState.Revision);
                aiMove = ToRecordedMove(nextState.Moves[^1], stopwatch.ElapsedMilliseconds);
            }

            cancellationToken.ThrowIfCancellationRequested();
            State = nextState;
            recordedMoves.Add(humanMove);
            if (aiMove is not null)
            {
                recordedMoves.Add(aiMove);
            }
            turnStartedTimestamp = Stopwatch.GetTimestamp();
            return State;
        }
        finally
        {
            sync.Release();
        }
    }

    public CompletedGame ToCompletedGame()
    {
        if (State.Status == GameStatus.InProgress)
        {
            throw new InvalidOperationException("The game is still in progress.");
        }

        return new CompletedGame(
            Id,
            startedAt,
            DateTimeOffset.UtcNow,
            "human",
            AiId,
            HumanMark == Mark.X ? 1 : 2,
            State.Status switch
            {
                GameStatus.XWon => GameOutcome.XWon,
                GameStatus.OWon => GameOutcome.OWon,
                GameStatus.Draw => GameOutcome.Draw,
                _ => throw new InvalidOperationException()
            },
            recordedMoves.ToArray(),
            Detail: null);
    }

    public async ValueTask DisposeAsync()
    {
        await sync.WaitAsync();
        try
        {
            disposed = true;
        }
        finally
        {
            sync.Release();
        }
    }

    private static RecordedMove ToRecordedMove(Move move, long elapsedMilliseconds) => new(
        move.Revision,
        move.Position.X,
        move.Position.Y,
        move.Mark,
        elapsedMilliseconds);
}