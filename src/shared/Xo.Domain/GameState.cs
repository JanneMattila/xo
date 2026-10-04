using System.Collections.ObjectModel;

namespace Xo.Domain;

public enum Mark
{
    Empty,
    X,
    O
}

public enum GameStatus
{
    InProgress,
    XWon,
    OWon,
    Draw
}

public enum MoveError
{
    RevisionConflict,
    GameAlreadyFinished,
    PositionOutsideBoard,
    PositionOccupied
}

public readonly record struct Position(int X, int Y);

public sealed record Move(long Revision, Position Position, Mark Mark);

public sealed class GameRuleException : InvalidOperationException
{
    public GameRuleException(MoveError error, string message)
        : base(message)
    {
        Error = error;
    }

    public MoveError Error { get; }
}

public sealed class GameState
{
    private static readonly (int X, int Y)[] WinDirections =
    [
        (1, 0),
        (0, 1),
        (1, 1),
        (1, -1)
    ];

    private readonly Mark[] cells;
    private readonly Move[] moves;
    private readonly ReadOnlyCollection<Mark> cellsView;
    private readonly ReadOnlyCollection<Move> movesView;

    private GameState(
        GameConfiguration configuration,
        Mark[] cells,
        Move[] moves,
        long revision,
        Mark nextMark,
        GameStatus status)
    {
        Configuration = configuration;
        this.cells = cells;
        this.moves = moves;
        cellsView = Array.AsReadOnly(cells);
        movesView = Array.AsReadOnly(moves);
        Revision = revision;
        NextMark = nextMark;
        Status = status;
    }

    public GameConfiguration Configuration { get; }

    public IReadOnlyList<Mark> Cells => cellsView;

    public IReadOnlyList<Move> Moves => movesView;

    public long Revision { get; }

    public Mark NextMark { get; }

    public GameStatus Status { get; }

    public static GameState Create(GameConfiguration? configuration = null)
    {
        var effectiveConfiguration = configuration ?? new GameConfiguration();
        return new GameState(
            effectiveConfiguration,
            new Mark[effectiveConfiguration.Width * effectiveConfiguration.Height],
            [],
            revision: 0,
            nextMark: Mark.X,
            status: GameStatus.InProgress);
    }

    public bool Contains(Position position) =>
        position.X >= Configuration.OriginX &&
        position.X < Configuration.OriginX + Configuration.Width &&
        position.Y >= Configuration.OriginY &&
        position.Y < Configuration.OriginY + Configuration.Height;

    public Mark GetMark(Position position)
    {
        EnsureInsideBoard(position);
        return cells[GetIndex(position)];
    }

    public GameState ApplyMove(Position position, long expectedRevision)
    {
        if (expectedRevision != Revision)
        {
            throw new GameRuleException(
                MoveError.RevisionConflict,
                $"Expected revision {expectedRevision}, but the current revision is {Revision}.");
        }

        if (Status != GameStatus.InProgress)
        {
            throw new GameRuleException(MoveError.GameAlreadyFinished, "The game has already finished.");
        }

        EnsureInsideBoard(position);
        var index = GetIndex(position);
        if (cells[index] != Mark.Empty)
        {
            throw new GameRuleException(MoveError.PositionOccupied, "The position is already occupied.");
        }

        var updatedCells = (Mark[])cells.Clone();
        updatedCells[index] = NextMark;

        var newRevision = Revision + 1;
        var updatedMoves = new Move[moves.Length + 1];
        moves.CopyTo(updatedMoves, 0);
        updatedMoves[^1] = new Move(newRevision, position, NextMark);

        var status = HasWinningLine(updatedCells, position, NextMark)
            ? NextMark == Mark.X ? GameStatus.XWon : GameStatus.OWon
            : newRevision == updatedCells.Length ? GameStatus.Draw : GameStatus.InProgress;

        return new GameState(
            Configuration,
            updatedCells,
            updatedMoves,
            newRevision,
            status == GameStatus.InProgress ? OpponentOf(NextMark) : Mark.Empty,
            status);
    }

    private bool HasWinningLine(Mark[] board, Position position, Mark mark)
    {
        foreach (var (deltaX, deltaY) in WinDirections)
        {
            var length = 1 +
                Count(board, position, deltaX, deltaY, mark) +
                Count(board, position, -deltaX, -deltaY, mark);

            if (length >= Configuration.WinLength)
            {
                return true;
            }
        }

        return false;
    }

    private int Count(Mark[] board, Position start, int deltaX, int deltaY, Mark mark)
    {
        var count = 0;
        var position = new Position(start.X + deltaX, start.Y + deltaY);

        while (Contains(position) && board[GetIndex(position)] == mark)
        {
            count++;
            position = new Position(position.X + deltaX, position.Y + deltaY);
        }

        return count;
    }

    private void EnsureInsideBoard(Position position)
    {
        if (!Contains(position))
        {
            throw new GameRuleException(
                MoveError.PositionOutsideBoard,
                $"Position ({position.X}, {position.Y}) is outside the board.");
        }
    }

    private int GetIndex(Position position) =>
        ((position.Y - Configuration.OriginY) * Configuration.Width) +
        position.X - Configuration.OriginX;

    private static Mark OpponentOf(Mark mark) => mark == Mark.X ? Mark.O : Mark.X;
}