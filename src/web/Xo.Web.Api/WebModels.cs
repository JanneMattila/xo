using Xo.Domain;
using Xo.MatchRunner;

namespace Xo.Web.Api;

public sealed record CreateHumanGameRequest(string AiId, Mark HumanMark);

public sealed record HumanMoveRequest(int X, int Y);

public sealed record CreateAiRunRequest(
    string Player1,
    string Player2,
    int Games = 10,
    bool AlternateSides = true,
    string OpeningSet = "standard");

public sealed record GameView(
    Guid Id,
    string Kind,
    int Width,
    int Height,
    int OriginX,
    int OriginY,
    GameStatus Status,
    Mark NextMark,
    Mark? HumanMark,
    string Player1Id,
    string Player2Id,
    IReadOnlyList<RecordedMove> Moves,
    string OpeningId = "standard",
    int OpeningMoveCount = 0)
{
    public static GameView FromHuman(HumanGameSession session) => new(
        session.Id,
        "human-vs-ai",
        session.State.Configuration.Width,
        session.State.Configuration.Height,
        session.State.Configuration.OriginX,
        session.State.Configuration.OriginY,
        session.State.Status,
        session.State.NextMark,
        session.HumanMark,
        "human",
        session.AiId,
        session.RecordedMoves);

    public static GameView FromCompleted(CompletedGame game) => new(
        game.Id,
        "completed",
        GameConfiguration.DefaultBoardSize,
        GameConfiguration.DefaultBoardSize,
        0,
        0,
        game.Outcome switch
        {
            GameOutcome.XWon => GameStatus.XWon,
            GameOutcome.OWon => GameStatus.OWon,
            GameOutcome.XForfeit => GameStatus.OWon,
            GameOutcome.OForfeit => GameStatus.XWon,
            _ => GameStatus.Draw
        },
        Mark.Empty,
        null,
        game.Player1Id,
        game.Player2Id,
        game.Moves,
        game.OpeningId,
        game.OpeningMoveCount);
}

public sealed record AiRunView(
    Guid Id,
    string Player1,
    string Player2,
    int RequestedGames,
    int CompletedGames,
    string Status,
    MatchStatistics Statistics,
    string? Error,
    string OpeningSet = "standard");