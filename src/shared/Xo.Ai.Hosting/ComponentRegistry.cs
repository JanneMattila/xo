using Xo.Ai.Abstractions;

namespace Xo.Ai.Hosting;

public sealed record AiSearchSettings(int Depth, int CandidateLimit, int? TimeBudgetMilliseconds = null);

public sealed record AiComponentDefinition(
    string Id,
    string DisplayName,
    Func<IGameAi> CreateAi,
    AiSearchSettings? Settings = null);

public sealed class ComponentRegistry
{
    public ComponentRegistry(IEnumerable<AiComponentDefinition> components)
    {
        var definitions = components.ToArray();
        var duplicate = definitions.GroupBy(component => component.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"AI component ID '{duplicate.Key}' is duplicated.", nameof(components));
        }

        Components = Array.AsReadOnly(definitions);
    }

    public IReadOnlyList<AiComponentDefinition> Components { get; }

    public AiComponentDefinition GetRequired(string id) =>
        Components.FirstOrDefault(component => string.Equals(component.Id, id, StringComparison.Ordinal)) ??
        throw new KeyNotFoundException($"AI component '{id}' is not registered.");

    public static ComponentRegistry CreateDefault() => new(
    [
        new("xo-ai-advanced-v1", "Advanced Search v1 (C#)", () => new AdvancedV1.AdvancedSearcher(), new(3, 14)),
        new("xo-ai-advanced-v2", "Advanced Search v2 (C#)", () => new AdvancedV2.AdvancedSearcher(), new(4, 18, 1500)),
        new("xo-ai-advanced-v3", "Advanced Search v3 (C#)", () => new AdvancedV3.AdvancedSearcher(), new(6, 22, 2000)),
        new("xo-ai-advanced-v4", "Advanced Search v4 (C#)", () => new AdvancedV4.AdvancedSearcher(), new(6, 22, 2000)),
        new("xo-ai-advanced-v5", "Advanced Search v5 (C#)", () => new AdvancedV5.AdvancedSearcher(), new(6, 22, 2000)),
        new("xo-ai-advanced-v6", "Advanced Search v6 (C#)", () => new AdvancedV6.AdvancedSearcher(), new(6, 22, 2000))
    ]);
}
