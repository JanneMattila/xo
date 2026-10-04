using System.Numerics;
using Xo.Domain;

namespace Xo.Ai.AdvancedV3;

internal readonly record struct Potential(long Score, bool IsWin, int WinningReplies, int OpenThrees);

internal sealed class SearchBoard
{
    private const byte Border = 3;
    private static readonly AxisPattern[] Patterns = BuildPatterns();
    private static readonly long[] WindowWeights = [0, 2, 20, 300, 8000, 1_000_000];
    private readonly GameConfiguration configuration;
    private readonly int stride;
    private readonly byte[] cells;
    private readonly byte[] neighbors;
    private readonly byte[] valid;
    private readonly Potential[] xRatings;
    private readonly Potential[] oRatings;
    private readonly HashSet<int> frontier = [];
    private readonly int[] directions;
    private int stoneCount;
    private long xScore;
    private long oScore;

    public SearchBoard(GameState state)
    {
        configuration = state.Configuration;
        stride = configuration.Width + 8;
        cells = new byte[stride * (configuration.Height + 8)];
        Array.Fill(cells, Border);
        neighbors = new byte[cells.Length];
        valid = new byte[cells.Length];
        xRatings = new Potential[cells.Length];
        oRatings = new Potential[cells.Length];
        directions = [1, stride, stride + 1, stride - 1];
        for (var y = 0; y < configuration.Height; y++)
        {
            Array.Clear(cells, (y + 4) * stride + 4, configuration.Width);
        }

        foreach (var move in state.Moves)
        {
            Place(ToIndex(move.Position), move.Mark);
        }
    }

    public ulong Hash { get; private set; }

    public bool IsFull => stoneCount == configuration.Width * configuration.Height;

    public IEnumerable<int> Candidates
    {
        get
        {
            if (stoneCount == 0)
            {
                yield return (4 + (configuration.Height - 1) / 2) * stride +
                    4 + (configuration.Width - 1) / 2;
                yield break;
            }

            if (frontier.Count > 0)
            {
                foreach (var index in frontier)
                {
                    yield return index;
                }

                yield break;
            }

            for (var y = 0; y < configuration.Height; y++)
            {
                for (var x = 0; x < configuration.Width; x++)
                {
                    var index = (y + 4) * stride + x + 4;
                    if (cells[index] == 0)
                    {
                        yield return index;
                    }
                }
            }
        }
    }

    public Position ToPosition(int index) => new(
        configuration.OriginX + index % stride - 4,
        configuration.OriginY + index / stride - 4);

    public long CenterBonus(int index)
    {
        var x = index % stride - 4;
        var y = index / stride - 4;
        return Math.Max(0, 100 - Math.Max(
            Math.Abs(2L * x - configuration.Width + 1),
            Math.Abs(2L * y - configuration.Height + 1)));
    }

    public long PositionalScore(Mark mark) => mark == Mark.X ? xScore - oScore : oScore - xScore;

    public Potential GetPotential(int index, Mark mark)
    {
        if (valid[index] == 0)
        {
            xRatings[index] = CalculatePotential(index, 1);
            oRatings[index] = CalculatePotential(index, 2);
            valid[index] = 1;
        }

        return mark == Mark.X ? xRatings[index] : oRatings[index];
    }

    public void Place(int index, Mark mark)
    {
        if (cells[index] != 0 || mark == Mark.Empty)
        {
            throw new InvalidOperationException("Search tried to place an invalid move.");
        }

        var value = mark == Mark.X ? (byte)1 : (byte)2;
        UpdateWindowScores(index, value);
        cells[index] = value;
        Hash ^= StoneHash(index, value);
        stoneCount++;
        frontier.Remove(index);
        for (var dy = -2; dy <= 2; dy++)
        {
            for (var dx = -2; dx <= 2; dx++)
            {
                var neighbor = index + dy * stride + dx;
                if (cells[neighbor] == Border)
                {
                    continue;
                }

                neighbors[neighbor]++;
                if (cells[neighbor] == 0)
                {
                    frontier.Add(neighbor);
                }
            }
        }

        Invalidate(index);
    }

    public void Undo(int index)
    {
        var value = cells[index];
        if (value is 0 or Border)
        {
            throw new InvalidOperationException("Search tried to undo an empty cell.");
        }

        UpdateWindowScores(index, 0);
        cells[index] = 0;
        Hash ^= StoneHash(index, value);
        stoneCount--;
        for (var dy = -2; dy <= 2; dy++)
        {
            for (var dx = -2; dx <= 2; dx++)
            {
                var neighbor = index + dy * stride + dx;
                if (cells[neighbor] == Border)
                {
                    continue;
                }

                neighbors[neighbor]--;
                if (neighbors[neighbor] == 0)
                {
                    frontier.Remove(neighbor);
                }
                else if (cells[neighbor] == 0)
                {
                    frontier.Add(neighbor);
                }
            }
        }

        Invalidate(index);
    }

    private int ToIndex(Position position) =>
        (position.Y - configuration.OriginY + 4) * stride + position.X - configuration.OriginX + 4;

    private void Invalidate(int index)
    {
        valid[index] = 0;
        foreach (var direction in directions)
        {
            for (var offset = 1; offset <= 4; offset++)
            {
                valid[index + offset * direction] = 0;
                valid[index - offset * direction] = 0;
            }
        }
    }

    private Potential CalculatePotential(int index, byte mark)
    {
        long score = 0;
        var isWin = false;
        var winningReplies = 0;
        var openThrees = 0;
        foreach (var direction in directions)
        {
            var code = 0;
            var multiplier = 1;
            for (var offset = -4; offset <= 4; offset++)
            {
                if (offset == 0)
                {
                    continue;
                }

                var cell = cells[index + offset * direction];
                code += (cell == 0 ? 0 : cell == mark ? 1 : 2) * multiplier;
                multiplier *= 3;
            }

            var pattern = Patterns[code];
            score += pattern.Score;
            isWin |= pattern.IsWin;
            winningReplies += pattern.WinningReplies;
            openThrees += pattern.OpenThree ? 1 : 0;
        }

        score += winningReplies >= 2 ? 2_000_000 : winningReplies == 1 ? 180_000 : 0;
        score += openThrees >= 2 ? 160_000 : openThrees == 1 ? 12_000 : 0;
        return new Potential(score, isWin, winningReplies, openThrees);
    }

    private void UpdateWindowScores(int index, byte replacement)
    {
        var previous = cells[index];
        foreach (var direction in directions)
        {
            for (var start = -4; start <= 0; start++)
            {
                var xs = 0;
                var os = 0;
                var blocked = false;
                for (var offset = start; offset < start + 5; offset++)
                {
                    if (offset == 0)
                    {
                        continue;
                    }

                    var cell = cells[index + offset * direction];
                    if (cell == Border)
                    {
                        blocked = true;
                        break;
                    }

                    xs += cell == 1 ? 1 : 0;
                    os += cell == 2 ? 1 : 0;
                }

                if (blocked)
                {
                    continue;
                }

                var oldXs = xs + (previous == 1 ? 1 : 0);
                var oldOs = os + (previous == 2 ? 1 : 0);
                var newXs = xs + (replacement == 1 ? 1 : 0);
                var newOs = os + (replacement == 2 ? 1 : 0);
                xScore += (newOs == 0 ? WindowWeights[newXs] : 0) -
                    (oldOs == 0 ? WindowWeights[oldXs] : 0);
                oScore += (newXs == 0 ? WindowWeights[newOs] : 0) -
                    (oldXs == 0 ? WindowWeights[oldOs] : 0);
            }
        }
    }

    private static ulong StoneHash(int index, byte mark)
    {
        var value = ((ulong)index * 2 + mark) + 0x9E3779B97F4A7C15UL;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }

    private static AxisPattern[] BuildPatterns()
    {
        string[] openThreePatterns = ["..XXX.", ".XXX..", ".XX.X.", ".X.XX."];
        var patterns = new AxisPattern[6561];
        Span<char> line = stackalloc char[9];
        for (var code = 0; code < patterns.Length; code++)
        {
            var remaining = code;
            for (var index = 0; index < line.Length; index++)
            {
                if (index == 4)
                {
                    line[index] = 'X';
                    continue;
                }

                line[index] = (remaining % 3) switch { 0 => '.', 1 => 'X', _ => '#' };
                remaining /= 3;
            }

            long score = 0;
            var isWin = false;
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

                isWin |= own == 5;
                if (own == 4)
                {
                    winningCells |= 1U << empty;
                }

                score += own switch { 5 => 10_000_000, 4 => 8000, 3 => 350, 2 => 40, _ => 3 };
            }

            var openThree = false;
            foreach (var pattern in openThreePatterns)
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

            patterns[code] = new AxisPattern(score, isWin, BitOperations.PopCount(winningCells), openThree);
        }

        return patterns;
    }

    private readonly record struct AxisPattern(long Score, bool IsWin, int WinningReplies, bool OpenThree);
}
