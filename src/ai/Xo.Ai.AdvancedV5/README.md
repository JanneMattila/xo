# Advanced Search v5

Independent v4-based compound-threat C# library implementing
[`IGameAi`](../../shared/Xo.Ai.Abstractions/IGameAi.cs) inside the web process.
Earlier search strategies are preserved. The host supplies immutable state,
validates the returned position, and applies cooperative cancellation.
Defaults remain depth 6, 22 candidates, and a 2000 ms budget.

The hypothesis is that v4 stops quiet-threat analysis too early. V5:

- Includes double open-three attacks in the bounded threat continuation.
- Searches defenses against double threes and four-plus-three formations.
- Scores double threes and four-plus-three moves above isolated closed fours.

These formations are heuristic search priorities, **not** automatically
declared forced wins. Only actual five-in-a-row or distinct immediate
winning endpoints use mate scores. Candidate-limited defense and finite
search still mean there is no proof of perfect play.

Use 12 Arena games with **Varied paired openings** and **Alternate sides**
to compare against the current incumbent. [The aggregate report](../AI-EVOLUTION.md) records
whether this candidate improved performance in the historical pre-migration
experiment. It did not, so v5 remains experimental.
