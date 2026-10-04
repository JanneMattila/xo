namespace Xo.Domain;

public enum RuleSet
{
    Freestyle
}

public enum ExpansionPolicy
{
    Fixed
}

public sealed record GameConfiguration
{
    public const int DefaultBoardSize = 20;
    public const int DefaultWinLength = 5;

    public GameConfiguration(
        int width = DefaultBoardSize,
        int height = DefaultBoardSize,
        int originX = 0,
        int originY = 0,
        int winLength = DefaultWinLength,
        RuleSet ruleSet = RuleSet.Freestyle,
        ExpansionPolicy expansionPolicy = ExpansionPolicy.Fixed)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Board width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Board height must be positive.");
        }

        if (winLength <= 1 || winLength > Math.Max(width, height))
        {
            throw new ArgumentOutOfRangeException(
                nameof(winLength),
                "Win length must be greater than one and fit a board dimension.");
        }

        Width = width;
        Height = height;
        OriginX = originX;
        OriginY = originY;
        WinLength = winLength;
        RuleSet = ruleSet;
        ExpansionPolicy = expansionPolicy;
    }

    public int Width { get; }

    public int Height { get; }

    public int OriginX { get; }

    public int OriginY { get; }

    public int WinLength { get; }

    public RuleSet RuleSet { get; }

    public ExpansionPolicy ExpansionPolicy { get; }
}