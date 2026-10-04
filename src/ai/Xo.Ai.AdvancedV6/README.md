# Advanced Search v6

Independent v4-based late-move-reduction C# library implementing
[`IGameAi`](../../shared/Xo.Ai.Abstractions/IGameAi.cs) inside the web process.
The host supplies immutable state, validates the position, and applies
cooperative cancellation. V5's compound-threat
changes are not included because they did not improve its paired score
against v4. Earlier search strategies are preserved.

Defaults remain depth 6, 22 candidates, and a 2000 ms budget.

- Broaden interior candidate coverage (minimum 12; decrease by one every
  two plies).
- After six moves have been considered, search late quiet moves one ply
  shallower using a null window.
- Never reduce immediate threats, open threes, or forced defensive replies.
- Re-search promising reduced moves at normal depth, then at full window
  whenever principal-variation search requires it.

This is selective search. A quiet move that looks bad at reduced depth can
still contain a deeper resource; full re-search of promising moves does
not establish exact minimax or unbeatable play.

Compare against v4 using 12 **Varied paired openings** Arena games with
**Alternate sides**. [The aggregate report](../AI-EVOLUTION.md) records the
historical pre-migration comparison: the replicated score was tied, so
v6 remains experimental.
