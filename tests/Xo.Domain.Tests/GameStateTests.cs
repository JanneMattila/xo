using Xo.Domain;

namespace Xo.Domain.Tests;

public sealed class GameStateTests
{
    [Fact]
    public void Create_UsesExpectedDefaults()
    {
        var game = GameState.Create();

        Assert.Equal(20, game.Configuration.Width);
        Assert.Equal(20, game.Configuration.Height);
        Assert.Equal(5, game.Configuration.WinLength);
        Assert.Equal(Mark.X, game.NextMark);
        Assert.Equal(GameStatus.InProgress, game.Status);
        Assert.Equal(400, game.Cells.Count);
    }

    [Fact]
    public void ApplyMove_UsesOriginAwareCoordinatesAndAdvancesRevision()
    {
        var game = GameState.Create(new GameConfiguration(3, 3, -1, -2, 3));

        var updated = game.ApplyMove(new Position(-1, -2), expectedRevision: 0);

        Assert.Equal(0, game.Revision);
        Assert.Equal(Mark.Empty, game.GetMark(new Position(-1, -2)));
        Assert.Equal(1, updated.Revision);
        Assert.Equal(Mark.X, updated.GetMark(new Position(-1, -2)));
        Assert.Equal(Mark.O, updated.NextMark);
    }

    [Fact]
    public void ApplyMove_RejectsStaleRevision()
    {
        var game = GameState.Create();

        var exception = Assert.Throws<GameRuleException>(
            () => game.ApplyMove(new Position(0, 0), expectedRevision: 1));

        Assert.Equal(MoveError.RevisionConflict, exception.Error);
    }

    [Fact]
    public void ApplyMove_RejectsOccupiedPosition()
    {
        var game = GameState.Create().ApplyMove(new Position(0, 0), expectedRevision: 0);

        var exception = Assert.Throws<GameRuleException>(
            () => game.ApplyMove(new Position(0, 0), expectedRevision: 1));

        Assert.Equal(MoveError.PositionOccupied, exception.Error);
    }

    [Fact]
    public void ApplyMove_RecognizesFreestyleOverlineCreatedByFillingGap()
    {
        var game = GameState.Create();
        Position[] moves =
        [
            new(0, 0), new(0, 2),
            new(1, 0), new(2, 2),
            new(2, 0), new(4, 2),
            new(4, 0), new(6, 2),
            new(5, 0), new(8, 2),
            new(3, 0)
        ];

        foreach (var position in moves)
        {
            game = game.ApplyMove(position, game.Revision);
        }

        Assert.Equal(GameStatus.XWon, game.Status);
        Assert.Equal(Mark.Empty, game.NextMark);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(20, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 20)]
    public void ApplyMove_RejectsPositionOutsideBoard(int x, int y)
    {
        var game = GameState.Create();

        var exception = Assert.Throws<GameRuleException>(
            () => game.ApplyMove(new Position(x, y), expectedRevision: 0));

        Assert.Equal(MoveError.PositionOutsideBoard, exception.Error);
    }
}