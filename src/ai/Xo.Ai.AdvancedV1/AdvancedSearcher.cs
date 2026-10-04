using Xo.Ai.Abstractions;
using Xo.Domain;

namespace Xo.Ai.AdvancedV1;

public sealed record SearchResult(Position Position, int Score, long Nodes, int Depth);

public sealed class AdvancedSearcher(int depth = 3, int candidateLimit = 14) : IGameAi
{
    private const int WinScore = 10_000_000;
    private static readonly (int X, int Y)[] Directions =
    [
        (1, 0),
        (0, 1),
        (1, 1),
        (1, -1)
    ];

    private readonly int depth = Math.Clamp(depth, 1, 5);
    private readonly int candidateLimit = Math.Clamp(candidateLimit, 4, 30);
    private long nodes;
    private CancellationToken cancellationToken;

    Position IGameAi.ChooseMove(GameState state, CancellationToken cancellationToken) =>
        ChooseMove(state, state.NextMark, cancellationToken).Position;

    public SearchResult ChooseMove(GameState state, Mark aiMark, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.cancellationToken = cancellationToken;
        if (state.Status != GameStatus.InProgress || state.NextMark != aiMark)
        {
            throw new InvalidOperationException("The game is not waiting for this AI player.");
        }

        nodes = 0;
        var candidates = GenerateCandidates(state, aiMark, candidateLimit);
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("The board has no legal move.");
        }

        var bestMove = candidates[0];
        var bestScore = int.MinValue;
        var alpha = int.MinValue + 1;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = state.ApplyMove(candidate, state.Revision);
            var score = AlphaBeta(next, depth - 1, alpha, int.MaxValue, aiMark);
            if (score > bestScore)
            {
                bestScore = score;
                bestMove = candidate;
            }

            alpha = Math.Max(alpha, bestScore);
        }

        return new SearchResult(bestMove, bestScore, nodes, depth);
    }

    private int AlphaBeta(GameState state, int remainingDepth, int alpha, int beta, Mark aiMark)
    {
        cancellationToken.ThrowIfCancellationRequested();
        nodes++;
        if (state.Status != GameStatus.InProgress)
        {
            return TerminalScore(state.Status, aiMark, remainingDepth);
        }

        if (remainingDepth == 0)
        {
            return Evaluate(state, aiMark);
        }

        var maximizing = state.NextMark == aiMark;
        var value = maximizing ? int.MinValue + 1 : int.MaxValue;
        var limit = Math.Max(6, candidateLimit - ((depth - remainingDepth) * 2));
        var candidates = GenerateCandidates(state, state.NextMark, limit);

        foreach (var candidate in candidates)
        {
            var next = state.ApplyMove(candidate, state.Revision);
            var score = AlphaBeta(next, remainingDepth - 1, alpha, beta, aiMark);
            if (maximizing)
            {
                value = Math.Max(value, score);
                alpha = Math.Max(alpha, value);
            }
            else
            {
                value = Math.Min(value, score);
                beta = Math.Min(beta, value);
            }

            if (alpha >= beta)
            {
                break;
            }
        }

        return value;
    }

    private IReadOnlyList<Position> GenerateCandidates(GameState state, Mark mark, int limit)
    {
        var candidates = CollectNearbyEmptyCells(state);
        if (candidates.Count == 0)
        {
            return [];
        }

        var winning = candidates.Where(position => WouldWin(state, position, mark)).ToArray();
        if (winning.Length > 0)
        {
            return OrderByHeuristic(state, winning, mark).ToArray();
        }

        var opponent = OpponentOf(mark);
        var forcedBlocks = candidates.Where(position => WouldWin(state, position, opponent)).ToArray();
        if (forcedBlocks.Length > 0)
        {
            return OrderByHeuristic(state, forcedBlocks, mark).Take(limit).ToArray();
        }

        return OrderByHeuristic(state, candidates, mark).Take(limit).ToArray();
    }

    private IOrderedEnumerable<Position> OrderByHeuristic(
        GameState state,
        IEnumerable<Position> candidates,
        Mark mark)
    {
        var opponent = OpponentOf(mark);
        return candidates
            .OrderByDescending(position =>
                PatternPotential(state, position, mark) +
                (PatternPotential(state, position, opponent) * 11L / 10L) +
                CenterBonus(state, position))
            .ThenBy(position => position.Y)
            .ThenBy(position => position.X);
    }

    private int Evaluate(GameState state, Mark aiMark)
    {
        var candidates = CollectNearbyEmptyCells(state);
        if (candidates.Count == 0)
        {
            return 0;
        }

        var opponent = OpponentOf(aiMark);
        var bestAttack = candidates.Max(position => PatternPotential(state, position, aiMark));
        var bestDefense = candidates.Max(position => PatternPotential(state, position, opponent));
        var secondAttack = candidates
            .Select(position => PatternPotential(state, position, aiMark))
            .OrderByDescending(score => score)
            .Skip(1)
            .FirstOrDefault();
        var secondDefense = candidates
            .Select(position => PatternPotential(state, position, opponent))
            .OrderByDescending(score => score)
            .Skip(1)
            .FirstOrDefault();

        return ClampScore(
            bestAttack + (secondAttack / 3L) -
            ((bestDefense * 11L) / 10L) - (secondDefense / 3L));
    }

    private static List<Position> CollectNearbyEmptyCells(GameState state)
    {
        var configuration = state.Configuration;
        if (state.Moves.Count == 0)
        {
            return [CenterPosition(configuration)];
        }

        var cells = new HashSet<Position>();
        foreach (var move in state.Moves)
        {
            for (var deltaY = -2; deltaY <= 2; deltaY++)
            {
                for (var deltaX = -2; deltaX <= 2; deltaX++)
                {
                    var position = new Position(
                        move.Position.X + deltaX,
                        move.Position.Y + deltaY);
                    if (state.Contains(position) && state.GetMark(position) == Mark.Empty)
                    {
                        cells.Add(position);
                    }
                }
            }
        }

        return cells.Count > 0 ? cells.ToList() : CollectAllEmptyCells(state);
    }

    private static List<Position> CollectAllEmptyCells(GameState state)
    {
        var result = new List<Position>();
        var configuration = state.Configuration;
        for (var y = configuration.OriginY; y < configuration.OriginY + configuration.Height; y++)
        {
            for (var x = configuration.OriginX; x < configuration.OriginX + configuration.Width; x++)
            {
                var position = new Position(x, y);
                if (state.GetMark(position) == Mark.Empty)
                {
                    result.Add(position);
                }
            }
        }

        return result;
    }

    private static long PatternPotential(GameState state, Position position, Mark mark)
    {
        if (state.GetMark(position) != Mark.Empty)
        {
            return long.MinValue / 4;
        }

        long score = 0;
        var opponent = OpponentOf(mark);
        foreach (var (deltaX, deltaY) in Directions)
        {
            for (var startOffset = -4; startOffset <= 0; startOffset++)
            {
                var own = 0;
                var blocked = false;
                var containsCandidate = false;
                for (var index = 0; index < 5; index++)
                {
                    var offset = startOffset + index;
                    var current = new Position(
                        position.X + (offset * deltaX),
                        position.Y + (offset * deltaY));
                    if (!state.Contains(current))
                    {
                        blocked = true;
                        break;
                    }

                    if (current == position)
                    {
                        own++;
                        containsCandidate = true;
                    }
                    else
                    {
                        var cell = state.GetMark(current);
                        if (cell == opponent)
                        {
                            blocked = true;
                            break;
                        }

                        if (cell == mark)
                        {
                            own++;
                        }
                    }
                }

                if (!blocked && containsCandidate)
                {
                    score += own switch
                    {
                        5 => 1_000_000,
                        4 => 35_000,
                        3 => 3_000,
                        2 => 250,
                        _ => 8
                    };
                }
            }
        }

        return score;
    }

    private static bool WouldWin(GameState state, Position position, Mark mark)
    {
        foreach (var (deltaX, deltaY) in Directions)
        {
            var length = 1 +
                Count(state, position, deltaX, deltaY, mark) +
                Count(state, position, -deltaX, -deltaY, mark);
            if (length >= state.Configuration.WinLength)
            {
                return true;
            }
        }

        return false;
    }

    private static int Count(GameState state, Position start, int deltaX, int deltaY, Mark mark)
    {
        var count = 0;
        var current = new Position(start.X + deltaX, start.Y + deltaY);
        while (state.Contains(current) && state.GetMark(current) == mark)
        {
            count++;
            current = new Position(current.X + deltaX, current.Y + deltaY);
        }

        return count;
    }

    private static long CenterBonus(GameState state, Position position)
    {
        var configuration = state.Configuration;
        var centerXTwice = (2L * configuration.OriginX) + configuration.Width - 1L;
        var centerYTwice = (2L * configuration.OriginY) + configuration.Height - 1L;
        var distance = Math.Max(
            Math.Abs((2L * position.X) - centerXTwice),
            Math.Abs((2L * position.Y) - centerYTwice));
        return Math.Max(0, 100 - distance);
    }

    private static Position CenterPosition(GameConfiguration configuration) => new(
        configuration.OriginX + ((configuration.Width - 1) / 2),
        configuration.OriginY + ((configuration.Height - 1) / 2));

    private static int TerminalScore(GameStatus status, Mark aiMark, int remainingDepth)
    {
        var aiWon = (status == GameStatus.XWon && aiMark == Mark.X) ||
            (status == GameStatus.OWon && aiMark == Mark.O);
        return status == GameStatus.Draw
            ? 0
            : aiWon ? WinScore + remainingDepth : -WinScore - remainingDepth;
    }

    private static Mark OpponentOf(Mark mark) => mark == Mark.X ? Mark.O : Mark.X;

    private static int ClampScore(long value) => (int)Math.Clamp(value, int.MinValue + 1L, int.MaxValue);
}