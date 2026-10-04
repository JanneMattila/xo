# Advanced Search v2

`xo-ai-advanced-v2` is an independent Advanced Search implementation.
It is now a C# class library implementing
[`IGameAi`](../../shared/Xo.Ai.Abstractions/IGameAi.cs) inside the web process.
The host supplies the immutable game state and validates the returned position.
The algorithms and default budgets are preserved; integration now uses typed
calls and cooperative cancellation instead of subprocess commands.

## Archived self-play analysis

The 10 original Advanced-vs-Advanced games inspected on October 3, 2026 had
the same 30-move sequence, all ending in an O win. Repetition is expected for
the original deterministic search: alternating player identities does not
change the X/O positions.

In the representative game `858b54dc-b040-4d20-b795-64ad4a6a4137`:

- O forced replies with moves 20 `(6,10)` and 22 `(10,12)`.
- X's move 27 `(11,6)` allowed O's move 28 `(8,7)` to complete a four with two
  distinct winning endpoints, `(8,6)` and `(8,11)`.
- X blocked `(8,6)` on move 29, but O won at `(8,11)` on move 30. The loss was
  already forced before that final block.

X's move 29 also created its own two winning endpoints, `(7,6)` and `(12,6)`.
Those attractive attack scores were irrelevant because O moved next and
could win first. The original leaf evaluation did not account for that
turn priority; v2 resolves immediate tactical wins before static evaluation.

The original evaluates hypothetical five-cell windows and stops at its
nominal depth. Window totals do not distinguish a four with one completion
from a fork with two completions reliably enough, and a forced continuation
can disappear past that search horizon.

The archived position before move 27 is a regression test; it is not an
opening book or a special case in the searcher.

## Search changes

- Count distinct winning replies, including broken fours, instead of
  double-counting overlapping windows that finish at the same cell.
- Prioritize immediate wins, mandatory blocks, and winning forks, in that
  order. A fork is only treated as a forced win when the opponent has no
  immediate winning move.
- Recognize straight and broken open threes and multiple open-three axes.
- Extend forcing fours and mandatory defenses up to six extra plies at
  the nominal search horizon.
- Use iterative deepening and retain only fully completed iterations.
- Rate attack and defense once per candidate per node, reusing those ratings
  for ordering and evaluation.

Candidate collection, center preference, coordinate tie-breaking, and
host-authoritative state updates remain based on the original implementation.
Timing budgets mean different machine loads can complete different depths;
this is bounded search, not a claim of perfect Gomoku play.

## Settings

| Constructor argument | Default | Clamped range |
| --- | ---: | --- |
| `depth` | 4 | 1-5 |
| `candidateLimit` | 18 | 4-30 |
| `timeBudgetMilliseconds` | 1500 | 50-3000 |

The time budget is checked between search nodes, not a hard real-time
interrupt. The 1500 ms default leaves headroom below the host's five-second
cooperative move deadline on the standard 20x20 board. `SearchResult.Depth` is the deepest
fully completed iteration, not the requested maximum.

## Reproduce a head-to-head run

### Measured Arena result (October 3, 2026)

These historical results were recorded before the in-process migration.
At that time, v1 source/project/documentation files were unchanged and the
host supervised separate AI executables. They are not a rerun of the
current deployment.

Two browser-started four-game runs were completed with alternating sides,
reversing the player slots for the second run:

| V2 side | Wins | Losses | Draws |
| --- | ---: | ---: | ---: |
| X | 4 | 0 | 0 |
| O | 4 | 0 | 0 |
| Total | 8 | 0 | 0 |

All eight archived games were replayed to verify legal alternating moves and
an actual five-in-a-row finish. There were no forfeits or errors. V2's longest
recorded move took 1517 ms, below the host's 5000 ms timeout. Games ended in
20-31 moves. The original's five source/project/documentation files retained
their pre-change SHA-256 hashes.

This is evidence for this opponent and configuration, not a general playing
strength rating or a guarantee of winning every future match.

### Running another comparison

Start the web application, select Arena, then:

1. Player 1: **Advanced Search v2 (C#)**.
2. Player 2: **Advanced Search v1 (C#)**.
3. Enable **Alternate sides**.
4. Run an even number of games and inspect the Archive for actual five-in-a-row
   wins, not timeouts or forfeits.

Run tactical and in-process integration regressions with:

```bash
dotnet run --project tests/Xo.Verification/Xo.Verification.csproj
```
