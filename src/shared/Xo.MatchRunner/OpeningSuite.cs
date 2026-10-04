using Xo.Domain;

namespace Xo.MatchRunner;

public sealed record AiOpening(string Id, IReadOnlyList<Position> Moves);

public static class OpeningSuite
{
    private static readonly IReadOnlyList<AiOpening> Standard =
        Array.AsReadOnly(new[] { Opening("standard") });
    private static readonly IReadOnlyList<AiOpening> Varied = Array.AsReadOnly(new[]
    {
        Opening("adjacent-diagonal", new(9, 9), new(10, 10)),
        Opening("adjacent-orthogonal", new(9, 9), new(9, 10)),
        Opening("spaced-diagonal", new(9, 9), new(11, 11)),
        Opening("spaced-orthogonal", new(9, 9), new(9, 11)),
        Opening("cross-four", new(9, 9), new(10, 10), new(10, 8), new(8, 10)),
        Opening("wide-four", new(9, 9), new(11, 10), new(8, 10), new(10, 8))
    });

    public static IReadOnlyList<AiOpening> GetRequired(string name) => name switch
    {
        "standard" => Standard,
        "varied" => Varied,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Opening set must be 'standard' or 'varied'.")
    };

    public static AiOpening Select(string name, int gameIndex, bool alternateSides)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(gameIndex);
        var openings = GetRequired(name);
        return openings[(alternateSides ? gameIndex / 2 : gameIndex) % openings.Count];
    }

    private static AiOpening Opening(string id, params Position[] moves) =>
        new(id, Array.AsReadOnly(moves));
}
