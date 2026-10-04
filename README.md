# XO

Five-in-a-row with a host-authoritative .NET game engine, in-process C# AI
libraries, and an ASP.NET Core/vanilla JavaScript web application.

## Build and verify

```bash
dotnet build Xo.slnx
dotnet run --project tests/Xo.Verification/Xo.Verification.csproj
```

The verification executable has no external package dependencies.

## AI implementations

- [Advanced Search v1](src/ai/Xo.Ai.AdvancedV1/README.md) explains
	tactical filtering, heuristic scoring, and alpha-beta search.
- [Advanced Search v2](src/ai/Xo.Ai.AdvancedV2/README.md) documents
	the archived self-play analysis, fork-aware threat extensions, and Arena
	comparisons against the unchanged original.
- [Advanced Search v3](src/ai/Xo.Ai.AdvancedV3/README.md) documents
	v2 self-play analysis, incremental search and transposition caching, and
	Arena comparisons against both previous versions.
- [Advanced Search v4](src/ai/Xo.Ai.AdvancedV4/README.md) explains
	principal-variation search and forced-defense extensions.
- [Advanced Search v5](src/ai/Xo.Ai.AdvancedV5/README.md) explores
	compound-threat continuation and scoring.
- [Advanced Search v6](src/ai/Xo.Ai.AdvancedV6/README.md) explores
	late quiet-move reductions and broader candidate coverage.

[AI evolution results](src/ai/AI-EVOLUTION.md) describe the historical
paired-opening experiments and their limits. V4 is the promoted incumbent
from those experiments; v5 and v6 remain experimental alternatives.

## In-process AI architecture

Every implementation is a C# class library implementing
[`IGameAi`](src/shared/Xo.Ai.Abstractions/IGameAi.cs):

```csharp
Position ChooseMove(GameState state, CancellationToken cancellationToken = default);
```

The game supplies the complete immutable board and history; the AI returns
a typed position for the side to move. The host alone validates and records
moves. Human games and Arena share the same execution path, with separate
AI instances for each game and each Arena player.

[`ComponentRegistry`](src/shared/Xo.Ai.Hosting/ComponentRegistry.cs) registers
the six implementations and their default search settings in C#. There
are no executable paths, wire protocols, stdin/stdout communication,
console match runner, or alternate-language adapters. The component IDs for
Play and Arena are `xo-ai-advanced-v1` through `xo-ai-advanced-v6`.
The original `xo-ai-advanced` ID has been replaced by `xo-ai-advanced-v1`;
clients should use the new ID.

CPU-bound search runs on a worker task within the web process. Cancellation
and the five-second move deadline are cooperative: bundled AIs check the
token during search, and the host awaits search termination before returning.
An illegal move or missed deadline is an explicit Arena forfeit. Request or
application cancellation is not a competitive loss. Unexpected exceptions
fail the run visibly. Human moves and AI replies commit together; on an AI
failure or cancellation, the unchanged board can be retried.

The in-process contract is for trusted C# implementations. It does not
provide process isolation or forcibly terminate code that ignores cancellation.

To add an AI, reference the abstractions project from a C# class library,
implement `IGameAi`, and register a factory that creates a fresh instance.
Constructor settings replace runtime protocol parameters. `/api/components`
returns IDs, display names, and typed search settings (`depth`,
`candidateLimit`, `timeBudgetMilliseconds`), not transport metadata.

## Web application

```bash
dotnet run --project src/web/Xo.Web.Api --urls http://0.0.0.0:8080
```

Open `http://localhost:8080`. The Play view supports human-vs-AI, Arena runs
AI-vs-AI loops, and Archive retains the latest 100 completed games for the
current server process.

The entire white Play panel (opponent, stone selection, new-game button,
and live statistics) can be toggled using **Game setup & statistics**.
It starts expanded and collapses when a new game starts successfully,
reclaiming the sidebar space for the board. Reopen it at any time to
change opponents, start another game, or see live statistics. The board
uses the normal arrow cursor.

Arena's **Statistics** panel also starts expanded and collapses when a
run starts successfully; reopen it at any time to see the live values.

Starting the web application builds and loads all bundled AI libraries.
Active games, runs, and the last 100 archived games are held in memory and
are cleared when the server restarts.

## Paired opening benchmarks

Arena's **Opening set** defaults to the original unseeded game. Select
**Varied paired openings**, enable **Alternate sides**, and run 12 games for
one full cycle of six opening positions with both player assignments.
Seeded moves are recorded with zero think time and identified in Archive.
Every subsequent decision receives the complete seeded host state through
the same C# interface used for standard games.

The API accepts `openingSet: "standard"` (default) or `openingSet: "varied"`
on `/api/ai-runs`, alongside `player1`, `player2`, `games`, and `alternateSides`.
This reduces single-opening overfitting but does not establish perfect or
unbeatable play.