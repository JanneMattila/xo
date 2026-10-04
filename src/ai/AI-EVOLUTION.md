# AI evolution: v4 through v6

## Historical execution context

The results below were recorded on October 3, 2026 **before** the migration
to in-process C# AI libraries. They used separate AI executables and the
former stateful command protocol. File-preservation and verification counts
describe that experiment, not a checksum claim about today's migrated files.

All versions now implement
[`IGameAi`](../shared/Xo.Ai.Abstractions/IGameAi.cs) in the web process.
The original Advanced implementation is now named `Xo.Ai.AdvancedV1` and
registered as `xo-ai-advanced-v1`. Historical references to v1 mean that
same search strategy, formerly registered as `xo-ai-advanced`.
Search strategies and default budgets are retained; integration, cancellation,
project paths, and documentation were updated across versions. A direct
interface receives the complete immutable host state. There is no console
match runner or executable registry. The historical scores have not been
remeasured for this runtime; wall-clock budgeting can affect completed depth.

## Recommendation

**V4 is the promoted incumbent for the tested paired-opening suite.**
V5 and v6 remain available experimental alternatives; a larger version
number does not mean a stronger player.

This is a measured local plateau, not an unbeatable algorithm. V4 lost
four of its twelve paired games against v3. None of these implementations
has a perfect-play proof.

## Preserved versions and experiments

Each candidate has its own project and component identifier. V1-v3 source
files were unchanged throughout this experiment. V4 and v5 were frozen
before subsequent candidates were created. SHA-256 comparisons confirmed
all 28 protected source, project, and README files remained unchanged.

- [V4](./Xo.Ai.AdvancedV4/README.md) derives from v3: principal-variation
  search, quiet-move history ordering, forced-defense extensions,
  extension-aware transposition entries, and broader interior coverage.
- [V5](./Xo.Ai.AdvancedV5/README.md) derives from v4: compound-threat
  continuation and additional double-three/four-plus-three scoring.
- [V6](./Xo.Ai.AdvancedV6/README.md) derives from v4, not v5: broader
  interior coverage and late quiet-move reductions, with full-depth
  re-search for promising reduced moves.

V3-v6 all use depth 6, a root candidate limit of 22, and a nominal
2000 ms search budget. Search-budget checks occur between nodes, so this
is not a hard real-time deadline. The historical host command timeout was 5 seconds.
The current host instead uses a five-second cooperative move deadline.
V1's original settings and v2's 1500 ms budget were retained.

## Paired-opening methodology

The host plays freestyle five-in-a-row on a 20 x 20 board. A full varied
cycle contains twelve games: six chronological seeded openings, each
played with both component color assignments.

| Opening | Coordinates in move order |
| --- | --- |
| adjacent-diagonal | (9,9), (10,10) |
| adjacent-orthogonal | (9,9), (9,10) |
| spaced-diagonal | (9,9), (11,11) |
| spaced-orthogonal | (9,9), (9,11) |
| cross-four | (9,9), (10,10), (10,8), (8,10) |
| wide-four | (9,9), (11,10), (8,10), (10,8) |

Seeded moves have zero recorded think time. In the historical experiment,
components received chronological move batches through the former protocol.
Current games pass the complete seeded state directly to `IGameAi`.
The host remains authoritative for legality and outcomes in both cases.

The practical promotion threshold was at least **60% paired score**
against the incumbent: `(wins + 0.5 * draws) / games`. V6's marginal
first cycle was repeated with player slots reversed before deciding
whether to promote it. These small samples are not statistical certainty
or exhaustive opening coverage. Time-bounded search can also vary across
runs even with identical openings.

## Paired Arena results

Results below are from the candidate's perspective.

| Candidate | Opponent | Games | Wins | Losses | Draws | Score | Decision |
| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |
| v4 | v3 | 12 | 8 | 4 | 0 | 66.7% | Promoted |
| v5 | v4 | 12 | 6 | 6 | 0 | 50.0% | Not promoted |
| v6, first cycle | v4 | 12 | 7 | 5 | 0 | 58.3% | Repeat required |
| v6, reversed slots | v4 | 12 | 5 | 7 | 0 | 41.7% | Not promoted |
| v6, combined | v4 | 24 | 12 | 12 | 0 | 50.0% | Not promoted |

The combined row summarizes the preceding two v6 rows; it is not another
24-game experiment. There are **48 completed paired games** in total.
Independent replay confirmed legal alternating moves, exact seeded
prefixes, paired color coverage, and actual winning lines. No forfeits or
error outcomes contributed to these scores. The maximum recorded move
time across these games was 3182 ms.

## Standard-opening qualification

V4 also played four original, unseeded Arena games against each earlier
version, alternating sides with two games as each mark.

| Candidate | Opponent | Games | Wins | Losses | Draws |
| --- | --- | ---: | ---: | ---: | ---: |
| v4 | v3 | 4 | 2 | 2 | 0 |
| v4 | v2 | 4 | 4 | 0 | 0 |
| v4 | v1 | 4 | 4 | 0 | 0 |

These twelve games were independently replayed, with no forfeits. The
v3 tie reinforces that the paired-opening promotion does not establish
universal dominance. Repeated standard openings are qualification checks,
not twelve additional distinct opening positions. The v1/v2 comparisons
retain their original, unequal resource settings.

An earlier v4-v3 qualification attempt failed with a 5-second command
timeout before completing any game. It was excluded from scoring and
retained as an unscored failure in the aggregate evidence. A fresh
four-game retry completed successfully. Completed-game timings are not
evidence that command timeouts can never occur.

## Why iteration stopped, and why this is not unbeatable

Two subsequent ideas failed to demonstrate a large improvement over v4
at the same search budget. V5 tied it, and v6's apparent first-cycle
advantage disappeared in the reversed-slot repeat. New versions therefore
stop at v6 rather than treating every new hypothesis as a stronger player.

This stopping rule only establishes a local plateau for the tested ideas,
opponents, openings, and resource budget. Other search algorithms,
evaluation functions, opening suites, or resource budgets could improve
the result.

An explicit counterexample to unbeatable v4 play is its 17-move
`adjacent-diagonal` loss against v3, archived with game identifier
`a6b08dcb-4aa2-4d31-9e27-a937dde63837`. The recorded legal final five was
independently verified. Three other openings also produced v4 losses.

Finite candidate pruning, heuristic leaf evaluations, selective threat
extensions, and fixed time budgets do not constitute an exact minimax
solution. Even a hypothetical undefeated Arena sample would not establish
a game-theoretic guarantee.

## Reproduction

Build from the repository root, then use Arena at `http://localhost:8080`:
choose the two components, select **Varied paired openings**, enable
**Alternate sides**, and run twelve games. Reverse the player slots for
a repeat. Archive labels identify the opening used in each game.

To start the current web application on Windows:

```powershell
dotnet run --project .\src\web\Xo.Web.Api --urls http://localhost:8080
```

Select **Standard opening** for the original unseeded game. The Arena API
accepts `openingSet: "standard"` or `"varied"` on `POST /api/ai-runs`.

## Historical experiment verification

- Full solution build: zero warnings and errors.
- Dependency-free verification executable: 39/39 checks passed.
- Frontend JavaScript syntax check passed.
- Console smoke test: two completed varied games with the same opening
  and reversed X assignments.
- All 60 completed Arena games independently replayed: 48 paired games
  and 12 standard-opening qualification games.
- All 28 protected v1-v5 file hashes matched their frozen snapshots.
- Live component settings confirmed equal v3-v6 search budgets.

These historical checks establish replay and experiment consistency, not
perfect play or a mathematical strength guarantee.
