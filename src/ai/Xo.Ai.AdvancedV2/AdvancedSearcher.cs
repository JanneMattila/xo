using System.Diagnostics;
using System.Numerics;
using Xo.Ai.Abstractions;
using Xo.Domain;

namespace Xo.Ai.AdvancedV2;

public sealed record SearchResult(Position Position, int Score, long Nodes, int Depth);

public sealed class AdvancedSearcher(int depth = 4, int candidateLimit = 18, int timeBudgetMilliseconds = 1500) : IGameAi
{
    private const int WinScore = 10_000_000;
    private const int ThreatExtensionDepth = 6;
    private static readonly (int X, int Y)[] Directions = [(1, 0), (0, 1), (1, 1), (1, -1)];
    private static readonly string[] OpenThreePatterns = ["..XXX.", ".XXX..", ".XX.X.", ".X.XX."];
    private readonly int depth = Math.Clamp(depth, 1, 5);
    private readonly int candidateLimit = Math.Clamp(candidateLimit, 4, 30);
    private readonly int timeBudgetMilliseconds = Math.Clamp(timeBudgetMilliseconds, 50, 3000);
    private readonly Stopwatch stopwatch = new();
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
        stopwatch.Restart();
        var ratings = RateCandidates(state);
        var candidates = SelectCandidates(ratings, candidateLimit);
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("The board has no legal move.");
        }

        var bestMove = candidates[0].Position;
        var bestScore = Evaluate(ratings, aiMark, state.NextMark);
        var completedDepth = 0;
        for (var iteration = 1; iteration <= depth; iteration++)
        {
            var iterationMove = bestMove;
            var iterationScore = int.MinValue + 1;
            var alpha = int.MinValue + 1;
            try
            {
                foreach (var candidate in candidates.OrderByDescending(candidate => candidate.Position == bestMove))
                {
                    CheckBudget();
                    var next = state.ApplyMove(candidate.Position, state.Revision);
                    var score = AlphaBeta(next, iteration - 1, ThreatExtensionDepth, alpha, int.MaxValue, aiMark, 1);
                    if (score > iterationScore)
                    {
                        iterationScore = score;
                        iterationMove = candidate.Position;
                    }

                    alpha = Math.Max(alpha, iterationScore);
                }
            }
            catch (SearchBudgetExceededException)
            {
                break;
            }

            bestMove = iterationMove;
            bestScore = iterationScore;
            completedDepth = iteration;
            if (Math.Abs(bestScore) >= WinScore - 100)
            {
                break;
            }
        }

        return new SearchResult(bestMove, bestScore, nodes, completedDepth);
    }

    private int AlphaBeta(
        GameState state,
        int remainingDepth,
        int extensionDepth,
        int alpha,
        int beta,
        Mark aiMark,
        int ply)
    {
        nodes++;
        CheckBudget();
        if (state.Status != GameStatus.InProgress)
        {
            return state.Status == GameStatus.Draw ? 0 : ScoreWin(
                state.Status == GameStatus.XWon ? Mark.X : Mark.O, aiMark, ply);
        }

        var ratings = RateCandidates(state);
        if (ratings.Count == 0)
        {
            return 0;
        }

        if (ratings.Any(candidate => candidate.Attack.IsWin))
        {
            return ScoreWin(state.NextMark, aiMark, ply + 1);
        }

        var threats = ratings.Count(candidate => candidate.Defense.IsWin);
        if (threats > 1)
        {
            return ScoreWin(OpponentOf(state.NextMark), aiMark, ply + 2);
        }

        if (threats == 0 && ratings.Any(candidate => candidate.Attack.WinningReplies >= 2))
        {
            return ScoreWin(state.NextMark, aiMark, ply + 3);
        }

        var maximizing = state.NextMark == aiMark;
        var value = maximizing ? int.MinValue + 1 : int.MaxValue;
        var limit = Math.Max(6, candidateLimit - (ply * 2));
        IReadOnlyList<Candidate> candidates;
        if (remainingDepth <= 0)
        {
            var evaluation = Evaluate(ratings, aiMark, state.NextMark);
            if (extensionDepth == 0)
            {
                return evaluation;
            }

            if (threats == 1)
            {
                candidates = ratings.Where(candidate => candidate.Defense.IsWin).ToArray();
            }
            else
            {
                var defendFork = ratings.Any(candidate => candidate.Defense.WinningReplies >= 2);
                candidates = ratings.Where(candidate =>
                    candidate.Attack.WinningReplies > 0 || candidate.Defense.WinningReplies >= 2)
                    .Take(Math.Min(limit, 8)).ToArray();
                if (candidates.Count == 0)
                {
                    return evaluation;
                }

                if (!defendFork)
                {
                    value = evaluation;
                    if (maximizing)
                    {
                        alpha = Math.Max(alpha, value);
                    }
                    else
                    {
                        beta = Math.Min(beta, value);
                    }

                    if (alpha >= beta)
                    {
                        return value;
                    }
                }
            }
        }
        else
        {
            candidates = SelectCandidates(ratings, limit);
        }

        foreach (var candidate in candidates)
        {
            var next = state.ApplyMove(candidate.Position, state.Revision);
            var score = AlphaBeta(next, remainingDepth - 1,
                remainingDepth <= 0 ? extensionDepth - 1 : extensionDepth,
                alpha, beta, aiMark, ply + 1);
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

    private static IReadOnlyList<Candidate> SelectCandidates(IReadOnlyList<Candidate> ratings, int limit)
    {
        var winning = ratings.Where(candidate => candidate.Attack.IsWin).ToArray();
        if (winning.Length > 0)
        {
            return winning;
        }

        var blocks = ratings.Where(candidate => candidate.Defense.IsWin).ToArray();
        if (blocks.Length > 0)
        {
            return blocks;
        }

        var forks = ratings.Where(candidate => candidate.Attack.WinningReplies >= 2).ToArray();
        return forks.Length > 0 ? forks : ratings.Take(limit).ToArray();
    }

    private static IReadOnlyList<Candidate> RateCandidates(GameState state)
    {
        var mark = state.NextMark;
        var opponent = OpponentOf(mark);
        return CollectNearbyEmptyCells(state)
            .Select(position => new Candidate(position,
                PatternPotential(state, position, mark),
                PatternPotential(state, position, opponent)))
            .OrderByDescending(candidate => candidate.Attack.Score +
                (candidate.Defense.Score * 11L / 10) + CenterBonus(state, candidate.Position))
            .ThenBy(candidate => candidate.Position.Y)
            .ThenBy(candidate => candidate.Position.X)
            .ToArray();
    }

    private static int Evaluate(IReadOnlyList<Candidate> ratings, Mark aiMark, Mark nextMark)
    {
        long bestAttack = 0, secondAttack = 0, bestDefense = 0, secondDefense = 0;
        foreach (var candidate in ratings)
        {
            var attack = nextMark == aiMark ? candidate.Attack.Score : candidate.Defense.Score;
            var defense = nextMark == aiMark ? candidate.Defense.Score : candidate.Attack.Score;
            UpdateBest(attack, ref bestAttack, ref secondAttack);
            UpdateBest(defense, ref bestDefense, ref secondDefense);
        }

        var attackValue = bestAttack + secondAttack / 4;
        var defenseValue = bestDefense + secondDefense / 4;
        var score = nextMark == aiMark
            ? attackValue - defenseValue * 9 / 10
            : attackValue * 9 / 10 - defenseValue;
        return (int)Math.Clamp(score, -WinScore / 2, WinScore / 2);
    }

    private static void UpdateBest(long score, ref long best, ref long second)
    {
        if (score > best)
        {
            second = best;
            best = score;
        }
        else if (score > second)
        {
            second = score;
        }
    }

    private static Potential PatternPotential(GameState state, Position position, Mark mark)
    {
        long score = 0;
        var winningReplies = 0;
        var openThrees = 0;
        var isWin = false;
        Span<char> line = stackalloc char[9];
        foreach (var (deltaX, deltaY) in Directions)
        {
            for (var index = 0; index < line.Length; index++)
            {
                var offset = index - 4;
                var current = new Position(position.X + offset * deltaX, position.Y + offset * deltaY);
                line[index] = index == 4 ? 'X' :
                    !state.Contains(current) ? '#' :
                    state.GetMark(current) == mark ? 'X' :
                    state.GetMark(current) == Mark.Empty ? '.' : '#';
            }

            uint winningCells = 0;
            for (var start = 0; start <= 4; start++)
            {
                var own = 0;
                var empty = -1;
                var blocked = false;
                for (var index = start; index < start + 5; index++)
                {
                    if (line[index] == '#')
                    {
                        blocked = true;
                        break;
                    }

                    if (line[index] == 'X')
                    {
                        own++;
                    }
                    else
                    {
                        empty = index;
                    }
                }

                if (blocked)
                {
                    continue;
                }

                if (own == 5)
                {
                    isWin = true;
                }
                else if (own == 4)
                {
                    winningCells |= 1U << empty;
                }

                score += own switch { 5 => WinScore, 4 => 8000, 3 => 350, 2 => 40, _ => 3 };
            }

            winningReplies += BitOperations.PopCount(winningCells);
            var openThree = false;
            foreach (var pattern in OpenThreePatterns)
            {
                for (var start = 0; start <= line.Length - pattern.Length; start++)
                {
                    if (start <= 4 && start + pattern.Length > 4 &&
                        line.Slice(start, pattern.Length).SequenceEqual(pattern.AsSpan()))
                    {
                        openThree = true;
                    }
                }
            }

            if (openThree)
            {
                openThrees++;
            }
        }

        score += winningReplies >= 2 ? 2_000_000 : winningReplies == 1 ? 180_000 : 0;
        score += openThrees >= 2 ? 160_000 : openThrees == 1 ? 12_000 : 0;
        return new Potential(score, isWin, winningReplies);
    }

    private static List<Position> CollectNearbyEmptyCells(GameState state)
    {
        var configuration = state.Configuration;
        if (state.Moves.Count == 0)
        {
            return [new Position(configuration.OriginX + (configuration.Width - 1) / 2,
                configuration.OriginY + (configuration.Height - 1) / 2)];
        }

        var cells = new HashSet<Position>();
        foreach (var move in state.Moves)
        {
            for (var deltaY = -2; deltaY <= 2; deltaY++)
            {
                for (var deltaX = -2; deltaX <= 2; deltaX++)
                {
                    var position = new Position(move.Position.X + deltaX, move.Position.Y + deltaY);
                    if (state.Contains(position) && state.GetMark(position) == Mark.Empty)
                    {
                        cells.Add(position);
                    }
                }
            }
        }

        if (cells.Count == 0)
        {
            for (var y = configuration.OriginY; y < configuration.OriginY + configuration.Height; y++)
            {
                for (var x = configuration.OriginX; x < configuration.OriginX + configuration.Width; x++)
                {
                    var position = new Position(x, y);
                    if (state.GetMark(position) == Mark.Empty)
                    {
                        cells.Add(position);
                    }
                }
            }
        }

        return cells.ToList();
    }

    private static long CenterBonus(GameState state, Position position)
    {
        var configuration = state.Configuration;
        var centerXTwice = 2L * configuration.OriginX + configuration.Width - 1;
        var centerYTwice = 2L * configuration.OriginY + configuration.Height - 1;
        var distance = Math.Max(Math.Abs(2L * position.X - centerXTwice),
            Math.Abs(2L * position.Y - centerYTwice));
        return Math.Max(0, 100 - distance);
    }

    private static int ScoreWin(Mark winner, Mark aiMark, int ply) =>
        winner == aiMark ? WinScore - ply : -WinScore + ply;

    private static Mark OpponentOf(Mark mark) => mark == Mark.X ? Mark.O : Mark.X;

    private void CheckBudget()
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (stopwatch.ElapsedMilliseconds >= timeBudgetMilliseconds)
        {
            throw new SearchBudgetExceededException();
        }
    }

    private sealed class SearchBudgetExceededException : Exception;
    private sealed record Potential(long Score, bool IsWin, int WinningReplies);
    private sealed record Candidate(Position Position, Potential Attack, Potential Defense);
}
