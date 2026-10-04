using System.Diagnostics;
using Xo.Ai.Hosting;
using Xo.Domain;

namespace Xo.MatchRunner;

public sealed class AiMatchRunner(ComponentRegistry registry)
{
    public Task<CompletedGame> PlayAsync(
        string player1Id,
        string player2Id,
        bool player1Starts,
        CancellationToken cancellationToken = default) =>
        PlayAsync(player1Id, player2Id, player1Starts, null, cancellationToken);

    public async Task<CompletedGame> PlayAsync(
        string player1Id,
        string player2Id,
        bool player1Starts,
        AiOpening? opening,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startedAt = DateTimeOffset.UtcNow;
        var player1 = registry.GetRequired(player1Id).CreateAi();
        var player2 = registry.GetRequired(player2Id).CreateAi();
        var xPlayerNumber = player1Starts ? 1 : 2;
        var game = GameState.Create();
        var recordedMoves = new List<RecordedMove>();
        if (opening is not null && opening.Moves.Count > 0)
        {
            if (opening.Moves[0] != new Position(9, 9))
            {
                throw new ArgumentException("A seeded opening must begin at the standard board center (9,9).", nameof(opening));
            }

            foreach (var position in opening.Moves)
            {
                game = game.ApplyMove(position, game.Revision);
                recordedMoves.Add(new RecordedMove(game.Revision, position.X, position.Y, game.Moves[^1].Mark, 0));
            }

            if (game.Status != GameStatus.InProgress)
            {
                throw new ArgumentException("A seeded opening must leave an unfinished game.", nameof(opening));
            }
        }

        while (game.Status == GameStatus.InProgress)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var actorPlayerNumber = game.NextMark == Mark.X ? xPlayerNumber : 3 - xPlayerNumber;
            var ai = actorPlayerNumber == 1 ? player1 : player2;
            var stopwatch = Stopwatch.StartNew();
            Position position;
            try
            {
                position = await AiMoveExecutor.ChooseMoveAsync(ai, game, cancellationToken);
            }
            catch (AiMoveException exception)
            {
                return CreateCompleted(actorPlayerNumber == xPlayerNumber ? GameOutcome.XForfeit : GameOutcome.OForfeit,
                    exception.Message);
            }

            var elapsedMilliseconds = game.Revision == 0 ? 0 : stopwatch.ElapsedMilliseconds;
            game = game.ApplyMove(position, game.Revision);
            recordedMoves.Add(new RecordedMove(game.Revision, position.X, position.Y, game.Moves[^1].Mark, elapsedMilliseconds));
        }

        return CreateCompleted(ToOutcome(game.Status), detail: null);

        CompletedGame CreateCompleted(GameOutcome outcome, string? detail) => new(
            Guid.NewGuid(),
            startedAt,
            DateTimeOffset.UtcNow,
            player1Id,
            player2Id,
            xPlayerNumber,
            outcome,
            recordedMoves.ToArray(),
            detail,
            opening?.Id ?? "standard",
            opening?.Moves.Count ?? 0);
    }

    private static GameOutcome ToOutcome(GameStatus status) => status switch
    {
        GameStatus.XWon => GameOutcome.XWon,
        GameStatus.OWon => GameOutcome.OWon,
        GameStatus.Draw => GameOutcome.Draw,
        _ => throw new InvalidOperationException("The game has not finished.")
    };
}
