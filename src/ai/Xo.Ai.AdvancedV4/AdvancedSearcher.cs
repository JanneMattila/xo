using System.Diagnostics;
using Xo.Ai.Abstractions;
using Xo.Domain;

namespace Xo.Ai.AdvancedV4;

public sealed record SearchResult(Position Position, int Score, long Nodes, int Depth, long CacheHits = 0);

public sealed class AdvancedSearcher(int depth = 6, int candidateLimit = 22, int timeBudgetMilliseconds = 2000) : IGameAi
{
    private const int WinScore = 10_000_000;
    private const int ThreatExtensionDepth = 8;
    private readonly int depth = Math.Clamp(depth, 1, 8);
    private readonly int candidateLimit = Math.Clamp(candidateLimit, 4, 30);
    private readonly int timeBudgetMilliseconds = Math.Clamp(timeBudgetMilliseconds, 50, 3000);
    private readonly Stopwatch stopwatch = new();
    private readonly Dictionary<ulong, CacheEntry> cache = [];
    private readonly Dictionary<(Mark Mark, int Index), int> history = [];
    private long nodes;
    private long cacheHits;
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

        if (state.Configuration.WinLength != 5)
        {
            throw new NotSupportedException("Advanced v4 supports freestyle five-in-a-row.");
        }

        nodes = 0;
        cacheHits = 0;
        cache.Clear();
        history.Clear();
        stopwatch.Restart();
        var board = new SearchBoard(state);
        var ratings = RateCandidates(board, aiMark);
        var candidates = SelectCandidates(ratings, candidateLimit);
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("The board has no legal move.");
        }

        var bestMove = candidates[0].Index;
        var bestScore = Evaluate(board, ratings, aiMark);
        var completedDepth = 0;
        for (var iteration = 1; iteration <= depth; iteration++)
        {
            var iterationMove = bestMove;
            var iterationScore = int.MinValue + 1;
            var alpha = int.MinValue + 1;
            try
            {
                foreach (var candidate in candidates.OrderByDescending(candidate => candidate.Index == bestMove))
                {
                    CheckBudget();
                    var score = iterationScore == int.MinValue + 1
                        ? ScoreMove(board, candidate, aiMark, iteration - 1, ThreatExtensionDepth, alpha, int.MaxValue, 0)
                        : ScoreMove(board, candidate, aiMark, iteration - 1, ThreatExtensionDepth, alpha, alpha + 1, 0);
                    if (iterationScore != int.MinValue + 1 && score > alpha)
                    {
                        score = ScoreMove(board, candidate, aiMark, iteration - 1,
                            ThreatExtensionDepth, alpha, int.MaxValue, 0);
                    }
                    if (score > iterationScore)
                    {
                        iterationScore = score;
                        iterationMove = candidate.Index;
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

        return new SearchResult(board.ToPosition(bestMove), bestScore, nodes, completedDepth, cacheHits);
    }

    private int Search(SearchBoard board, Mark mark, int remainingDepth, int extensionDepth, int alpha, int beta, int ply)
    {
        nodes++;
        CheckBudget();
        if (board.IsFull)
        {
            return 0;
        }

        var key = board.Hash ^ (mark == Mark.X ? 0xA0761D6478BD642FUL : 0xE7037ED1A0B428DBUL);
        var cachedMove = -1;
        if (remainingDepth > 0 && cache.TryGetValue(key, out var entry))
        {
            cachedMove = entry.BestMove;
            if (entry.Depth >= remainingDepth && entry.ExtensionDepth >= extensionDepth)
            {
                cacheHits++;
                if (entry.Bound == Bound.Exact)
                {
                    return entry.Score;
                }

                if (entry.Bound == Bound.Lower)
                {
                    alpha = Math.Max(alpha, entry.Score);
                }
                else
                {
                    beta = Math.Min(beta, entry.Score);
                }

                if (alpha >= beta)
                {
                    return entry.Score;
                }
            }
        }

        var originalAlpha = alpha;
        var originalBeta = beta;
        var ratings = RateCandidates(board, mark);
        if (ratings.Any(candidate => candidate.Attack.IsWin))
        {
            return WinScore - ply - 1;
        }

        var threats = ratings.Count(candidate => candidate.Defense.IsWin);
        if (threats > 1)
        {
            return -WinScore + ply + 2;
        }

        if (threats == 0 && ratings.Any(candidate => candidate.Attack.WinningReplies >= 2))
        {
            return WinScore - ply - 3;
        }

        var value = int.MinValue + 1;
        var limit = Math.Max(10, candidateLimit - ply);
        IReadOnlyList<Candidate> candidates;
        if (remainingDepth <= 0)
        {
            var evaluation = Evaluate(board, ratings, mark);
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
                    .Take(Math.Min(limit, 10)).ToArray();
                if (candidates.Count == 0)
                {
                    return evaluation;
                }

                if (!defendFork)
                {
                    value = evaluation;
                    alpha = Math.Max(alpha, value);
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

        var bestMove = candidates[0].Index;
        var first = true;
        var extendForced = threats == 1 && remainingDepth > 0 && extensionDepth > 0;
        var nextDepth = remainingDepth - (extendForced ? 0 : 1);
        var nextExtension = extensionDepth - (remainingDepth <= 0 || extendForced ? 1 : 0);
        foreach (var candidate in OrderCandidates(candidates, cachedMove, mark))
        {
            var score = !first && remainingDepth > 1
                ? ScoreMove(board, candidate, mark, nextDepth, nextExtension, alpha, alpha + 1, ply)
                : ScoreMove(board, candidate, mark, nextDepth, nextExtension, alpha, beta, ply);
            if (!first && remainingDepth > 1 && score > alpha && score < beta)
            {
                score = ScoreMove(board, candidate, mark, nextDepth, nextExtension, alpha, beta, ply);
            }
            first = false;
            if (score > value)
            {
                value = score;
                bestMove = candidate.Index;
            }

            alpha = Math.Max(alpha, value);
            if (alpha >= beta)
            {
                if (remainingDepth > 0 && candidate.Attack.WinningReplies == 0 &&
                    candidate.Defense.WinningReplies == 0)
                {
                    var historyKey = (mark, candidate.Index);
                    history[historyKey] = Math.Min(100_000, HistoryScore(mark, candidate.Index) +
                        remainingDepth * remainingDepth);
                }
                break;
            }
        }

        if (remainingDepth > 0 && cache.Count < 100_000)
        {
            var bound = value <= originalAlpha ? Bound.Upper : value >= originalBeta ? Bound.Lower : Bound.Exact;
            cache[key] = new CacheEntry(remainingDepth, extensionDepth, value, bound, bestMove);
        }

        return value;
    }

    private int ScoreMove(
        SearchBoard board, Candidate candidate, Mark mark, int remainingDepth,
        int extensionDepth, int alpha, int beta, int ply)
    {
        if (candidate.Attack.IsWin)
        {
            return WinScore - ply - 1;
        }

        board.Place(candidate.Index, mark);
        try
        {
            return -Search(board, OpponentOf(mark), remainingDepth, extensionDepth, -beta, -alpha, ply + 1);
        }
        finally
        {
            board.Undo(candidate.Index);
        }
    }

    private IEnumerable<Candidate> OrderCandidates(IReadOnlyList<Candidate> candidates, int cachedMove, Mark mark) =>
        candidates.OrderByDescending(candidate => candidate.Index == cachedMove)
            .ThenByDescending(candidate => candidate.Attack.WinningReplies > 0 || candidate.Defense.WinningReplies > 0)
            .ThenByDescending(candidate => HistoryScore(mark, candidate.Index))
            .ThenByDescending(candidate => candidate.Ordering)
            .ThenBy(candidate => candidate.Index);

    private int HistoryScore(Mark mark, int index) => history.GetValueOrDefault((mark, index));

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

    private static List<Candidate> RateCandidates(SearchBoard board, Mark mark)
    {
        var ratings = new List<Candidate>();
        foreach (var index in board.Candidates)
        {
            var attack = board.GetPotential(index, mark);
            var defense = board.GetPotential(index, OpponentOf(mark));
            ratings.Add(new Candidate(index, attack, defense,
                attack.Score + defense.Score * 11 / 10 + board.CenterBonus(index)));
        }

        ratings.Sort(static (left, right) =>
        {
            var order = right.Ordering.CompareTo(left.Ordering);
            return order == 0 ? left.Index.CompareTo(right.Index) : order;
        });
        return ratings;
    }

    private static int Evaluate(SearchBoard board, IReadOnlyList<Candidate> ratings, Mark mark)
    {
        long bestAttack = 0, secondAttack = 0, bestDefense = 0, secondDefense = 0;
        foreach (var candidate in ratings)
        {
            UpdateBest(candidate.Attack.Score, ref bestAttack, ref secondAttack);
            UpdateBest(candidate.Defense.Score, ref bestDefense, ref secondDefense);
        }

        var score = bestAttack + secondAttack / 4 -
            (bestDefense + secondDefense / 4) * 9 / 10 + board.PositionalScore(mark);
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

    private static Mark OpponentOf(Mark mark) => mark == Mark.X ? Mark.O : Mark.X;

    private void CheckBudget()
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (stopwatch.ElapsedMilliseconds >= timeBudgetMilliseconds)
        {
            throw new SearchBudgetExceededException();
        }
    }

    private enum Bound { Exact, Lower, Upper }
    private readonly record struct CacheEntry(int Depth, int ExtensionDepth, int Score, Bound Bound, int BestMove);
    private readonly record struct Candidate(int Index, Potential Attack, Potential Defense, long Ordering);
    private sealed class SearchBudgetExceededException : Exception;
}
