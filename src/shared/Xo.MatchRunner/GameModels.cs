using Xo.Domain;

namespace Xo.MatchRunner;

public enum GameOutcome
{
    XWon,
    OWon,
    Draw,
    XForfeit,
    OForfeit,
    Cancelled
}

public sealed record RecordedMove(
    long Number,
    int X,
    int Y,
    Mark Mark,
    long ElapsedMilliseconds);

public sealed record CompletedGame(
    Guid Id,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string Player1Id,
    string Player2Id,
    int XPlayerNumber,
    GameOutcome Outcome,
    IReadOnlyList<RecordedMove> Moves,
    string? Detail,
    string OpeningId = "standard",
    int OpeningMoveCount = 0)
{
    public int? WinnerPlayerNumber => Outcome switch
    {
        GameOutcome.XWon => XPlayerNumber,
        GameOutcome.OWon => XPlayerNumber == 1 ? 2 : 1,
        GameOutcome.XForfeit => XPlayerNumber == 1 ? 2 : 1,
        GameOutcome.OForfeit => XPlayerNumber,
        _ => null
    };
}

public sealed class GameArchive
{
    private readonly object sync = new();
    private readonly LinkedList<CompletedGame> games = [];
    private readonly int capacity;

    public GameArchive(int capacity = 100)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        this.capacity = capacity;
    }

    public void Add(CompletedGame game)
    {
        lock (sync)
        {
            games.AddFirst(game);
            while (games.Count > capacity)
            {
                games.RemoveLast();
            }
        }
    }

    public IReadOnlyList<CompletedGame> GetRecent(int limit = 100)
    {
        lock (sync)
        {
            return games.Take(Math.Clamp(limit, 1, capacity)).ToArray();
        }
    }

    public CompletedGame? Get(Guid id)
    {
        lock (sync)
        {
            return games.FirstOrDefault(game => game.Id == id);
        }
    }
}

public sealed record MatchStatistics(
    long Games,
    long Player1Wins,
    long Player2Wins,
    long Draws,
    long TotalMoves,
    TimeSpan Elapsed)
{
    public double GamesPerSecond => Elapsed.TotalSeconds <= 0 ? 0 : Games / Elapsed.TotalSeconds;
}

public sealed class MatchStatisticsAccumulator
{
    private readonly object sync = new();
    private readonly DateTimeOffset startedAt = DateTimeOffset.UtcNow;
    private long games;
    private long player1Wins;
    private long player2Wins;
    private long draws;
    private long totalMoves;

    public void Record(CompletedGame game)
    {
        lock (sync)
        {
            games++;
            totalMoves += game.Moves.Count;
            switch (game.WinnerPlayerNumber)
            {
                case 1:
                    player1Wins++;
                    break;
                case 2:
                    player2Wins++;
                    break;
                default:
                    draws++;
                    break;
            }
        }
    }

    public MatchStatistics Snapshot()
    {
        lock (sync)
        {
            return new MatchStatistics(
                games,
                player1Wins,
                player2Wins,
                draws,
                totalMoves,
                DateTimeOffset.UtcNow - startedAt);
        }
    }
}