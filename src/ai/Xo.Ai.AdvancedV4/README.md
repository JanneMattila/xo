# Advanced Search v4

Independent v3-based C# search library implementing
[`IGameAi`](../../shared/Xo.Ai.Abstractions/IGameAi.cs) inside the web process.
Earlier search strategies are preserved. The host supplies immutable state
and validates the returned position; cancellation is cooperative.

Changes:

- Principal-variation search: later moves use a null window and are searched
  again at full width whenever they can improve the current best score.
- History-based ordering of quiet moves that caused alpha-beta cutoffs.
- Mandatory single-threat defenses can extend the normal search, using the
  existing finite extension budget rather than consuming a quiet-search ply.
- Transposition entries track both normal and extension depth.
- Broader interior candidate coverage (minimum 10, rather than 6).

Defaults remain depth 6, 22 candidates, and 2000 ms, matching v3's compute
budget. It retains v3's reversible board and cached patterns. This is bounded
selective search, not a proof of perfect play.

Use Arena's **Varied paired openings** with 12 games and **Alternate sides**.
Each of the six seeded positions is played with the player identities
swapped. Opening moves are host-supplied, legal, and untimed; all subsequent
moves are chosen by the components.

Select **Advanced Search v4 (C#)** against **Advanced Search v3 (C#)**.

[The aggregate experiment report](../AI-EVOLUTION.md) records historical
pre-migration results and the recommended
version; a larger version number is not itself a strength claim.
