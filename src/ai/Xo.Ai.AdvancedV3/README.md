# Advanced Search v3

`xo-ai-advanced-v3` is an independent C# search library implementing
[`IGameAi`](../../shared/Xo.Ai.Abstractions/IGameAi.cs) inside the web process.
It uses a more efficient search engine than [v2](../Xo.Ai.AdvancedV2/README.md).
Each game has a separate search instance. The host supplies the complete
immutable state; there is no protocol session or duplicate game history.

## V2 self-play analysis

Two browser-started v2 self-play games were inspected on October 3, 2026.
Both followed the same sequence and ended in X wins after 43 moves.

In representative game `6b51a676-0cb2-4ca3-bcee-e12ef0b94111`, O's quiet move
38 `(11,15)` allowed a forcing sequence:

1. X played `(6,10)`, threatening `(7,11)`.
2. O had to block at `(7,11)`.
3. X played `(5,11)`, creating winning endpoints `(9,7)` and `(4,12)`.
4. O blocked one endpoint; X won at the other.

V2 already recognizes immediate forks and extends forcing fours. Its main
constraint is how much search it can complete within its time budget: every
node creates immutable states and rebuilds every nearby candidate's patterns.
V3 targets that repeated work so longer tactical sequences can be searched
before choosing a quiet move.

The archived forcing position is covered by a regression test, not by a
special-case move or an opening book.

## Search changes

- Use a private reversible board for search; the host's `GameState` remains
  unchanged.
- Precompute all 6,561 relative eight-neighbor axis patterns. Border cells and
  opposing stones block the same windows.
- Cache ratings and invalidate only cells on the four changed axes.
- Maintain the radius-two candidate frontier incrementally.
- Maintain five-cell positional window scores incrementally, adding broader
  board evaluation to v2's best-candidate attack/defense evaluation.
- Use negamax alpha-beta and a bounded transposition table with exact/lower/
  upper bounds, keyed by board occupancy and side to move.
- Search up to six nominal plies by default and eight extra forcing plies.
- Retain iterative deepening, immediate-win priority, mandatory blocks,
  distinct winning endpoints, and the last fully completed iteration.
- Undo every speculative move in `finally`, including when the time budget
  interrupts an iteration.

The reversible board, frontier, cached ratings, hash, and incremental scores
are checked against reconstructed boards and independent window scoring
through nested move/undo sequences.

## Constructor settings and search results

| Constructor argument | Default | Clamped range |
| --- | ---: | --- |
| `depth` | 6 | 1-8 |
| `candidateLimit` | 22 | 4-30 |
| `timeBudgetMilliseconds` | 2000 | 50-3000 |

V1 and v2 retain their original settings. V3's default budget is 2000 ms,
compared with v2's 1500 ms. This is a comparison of the registered components,
not an equal-compute benchmark.

The search-specific overload returns `Position`, `Score`, `Nodes`, completed
`Depth`, and `CacheHits`. `IGameAi` returns just the position, and the host
records elapsed time. Cancellation and the budget are checked between nodes rather than being a
hard real-time interrupt. Machine load can change the completed depth.
The searcher explicitly supports freestyle five-in-a-row.

## Arena comparisons

### Measured result (October 3, 2026)

These are historical subprocess-based results from before the in-process
migration, not new measurements of the current deployment. Earlier source
versions were frozen during that experiment; all versions now use the
shared typed interface and cooperative cancellation.

Browser-started Arena runs used alternating sides and both player-slot
arrangements. The initial v3 candidate passed the expanded comparison:

| Opponent | V3 wins as X | V3 wins as O | Total wins | Losses | Draws |
| --- | ---: | ---: | ---: | ---: | ---: |
| Advanced v2 | 4 | 4 | 8 | 0 | 0 |
| Advanced v1 | 4 | 4 | 8 | 0 | 0 |
| Total | 8 | 8 | 16 | 0 | 0 |

All 16 archived games were independently replayed to check legal coordinates,
alternating marks, and an actual five-in-a-row at the final move. There were
no forfeits or error details. V3's maximum recorded think time was 2021 ms,
below the host's 5000 ms timeout. V2 games ended in 21-25 moves; v1 games ended
in 26-43 moves.

SHA-256 fingerprints of all 10 v1/v2 source, project, and README files matched
their pre-v3 values. The complete solution built without warnings or errors,
and all 33 verification checks passed. Play was also checked with the human
using either mark.

These are results against these registered opponents and defaults, not a
general Gomoku strength rating or a guarantee of winning every future game.

### Running another comparison

Start the app and open Arena. Select **Advanced Search v3 (C#)** against
**Advanced Search v2 (C#)** or **Advanced Search v1 (C#)**, enable
**Alternate sides**, and run an even number of games. Reverse player slots
for a second run. Inspect Archive for actual five-in-a-row finishes, not
timeouts or forfeits.

Run regressions with:

```bash
dotnet run --project tests/Xo.Verification/Xo.Verification.csproj
```
