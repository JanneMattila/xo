using System.Diagnostics;
using Xo.Ai.Abstractions;
using Xo.Ai.AdvancedV1;
using Xo.Ai.Hosting;
using Xo.Domain;
using Xo.MatchRunner;

var checks = new (string Name, Action Run)[]
{
	("domain detects freestyle overline", VerifyFreestyleOverline),
	("Advanced v1 opens board center", VerifyOpeningMove),
	("AI uses complete immutable move history", VerifyMoveHistory),
	("registry creates isolated C# AI instances", VerifyRegistry),
	("all AIs implement typed move contract", VerifyAiContract),
	("all AIs honor pre-cancellation", VerifyAiCancellation),
	("search cancellation interrupts and allows reuse", VerifySearchCancellation),
	("move deadline is cooperative and awaited", VerifyMoveDeadline),
	("cancellation is not scored as a forfeit", VerifyMatchCancellation),
	("illegal AI moves produce explicit forfeits", VerifyIllegalAiMove),
	("AI errors leave human turns unchanged", VerifyHumanRollback),
	("cancelled human turns can be retried", VerifyHumanCancellation),
	("parallel games have isolated state", VerifyConcurrentGames),
	("human winning turn skips AI and archives correctly", VerifyHumanWin),
	("match runner completes AI game", VerifyAiMatch),
	("human session applies AI response", VerifyHumanSession),
	("Advanced v1 human sessions support either mark", VerifyAdvancedHumanSessions),
	("game archive retains latest 100", VerifyArchiveCapacity),
	("Advanced v1 takes immediate win", VerifyAdvancedImmediateWin),
	("Advanced v1 blocks immediate loss", VerifyAdvancedImmediateBlock),
	("Advanced v2 takes immediate win", VerifyV2ImmediateWin),
	("Advanced v2 wins before defending", VerifyV2WinBeforeDefense),
	("Advanced v2 blocks immediate loss", VerifyV2ImmediateBlock),
	("Advanced v2 blocks broken four", VerifyV2BrokenFourBlock),
	("Advanced v2 creates a winning fork", VerifyV2WinningFork),
	("Advanced v2 avoids archived losing fork", VerifyV2ArchivedFork),
	("Advanced v2 supports shifted board coordinates", VerifyV2ShiftedBoard),
	("host executes Advanced v2 in process", VerifyHostedV2),
	("Advanced v3 wins before defending", VerifyV3WinBeforeDefense),
	("Advanced v3 blocks broken four", VerifyV3BrokenFourBlock),
	("Advanced v3 creates a winning fork", VerifyV3WinningFork),
	("Advanced v3 finds archived v2 forcing win", VerifyV3ArchivedWin),
	("Advanced v3 restores incremental search state", VerifyV3SearchBoard),
	("Advanced v3 supports shifted coordinates and bounds", VerifyV3ShiftedBoard),
	("host executes Advanced v3 in process", VerifyHostedV3),
	("opening suite pairs identical positions", VerifyOpeningPairs),
	("match runner plays even and odd seeded openings", VerifySeededMatches),
	("Advanced v4 preserves tactical priorities", VerifyV4Tactics),
	("Advanced v5 preserves tactical priorities", VerifyV5Tactics),
	("Advanced v6 preserves tactical priorities", VerifyV6Tactics)
};

var failures = 0;
foreach (var check in checks)
{
	try
	{
		check.Run();
		Console.WriteLine($"PASS {check.Name}");
	}
	catch (Exception exception)
	{
		failures++;
		Console.Error.WriteLine($"FAIL {check.Name}: {exception.Message}");
	}
}

Console.WriteLine($"{checks.Length - failures}/{checks.Length} checks passed.");
return failures == 0 ? 0 : 1;

static void VerifyFreestyleOverline()
{
	var game = GameState.Create();
	Position[] moves =
	[
		new(0, 0), new(0, 2),
		new(1, 0), new(2, 2),
		new(2, 0), new(4, 2),
		new(4, 0), new(6, 2),
		new(5, 0), new(8, 2),
		new(3, 0)
	];

	foreach (var position in moves)
	{
		game = game.ApplyMove(position, game.Revision);
	}

	Equal(GameStatus.XWon, game.Status);
}

static void VerifyOpeningMove()
{
	IGameAi ai = new AdvancedSearcher();
	Equal(new Position(9, 9), ai.ChooseMove(GameState.Create()));
	Equal(new Position(-8, -18), ai.ChooseMove(GameState.Create(new GameConfiguration(5, 5, -10, -20))));
}

static void VerifyMoveHistory()
{
	var state = ApplyMoves(new(10, 10), new(9, 9), new(11, 11));
	IGameAi ai = new AdvancedSearcher();
	var position = ai.ChooseMove(state);
	Equal(true, state.Contains(position));
	Equal(3L, state.Revision);
	Equal(Mark.Empty, state.GetMark(position));
}

static void VerifyRegistry()
{
	var registry = ComponentRegistry.CreateDefault();
	Equal(6, registry.Components.Count);
	Equal(true, registry.Components.Select(component => component.Id)
		.SequenceEqual(Enumerable.Range(1, 6).Select(version => $"xo-ai-advanced-v{version}")));
	foreach (var definition in registry.Components)
	{
		Equal(false, ReferenceEquals(definition.CreateAi(), definition.CreateAi()));
	}
	Equal(new AiSearchSettings(3, 14), registry.GetRequired("xo-ai-advanced-v1").Settings!);
	Equal("Advanced Search v1 (C#)", registry.GetRequired("xo-ai-advanced-v1").DisplayName);
	Throws<KeyNotFoundException>(() => registry.GetRequired("xo-ai-nearest"));
	Throws<KeyNotFoundException>(() => registry.GetRequired("xo-ai-advanced"));
	Equal(new AiSearchSettings(4, 18, 1500), registry.GetRequired("xo-ai-advanced-v2").Settings!);
	foreach (var version in new[] { 3, 4, 5, 6 })
	{
		Equal(new AiSearchSettings(6, 22, 2000), registry.GetRequired($"xo-ai-advanced-v{version}").Settings!);
	}
	Throws<KeyNotFoundException>(() => registry.GetRequired("unknown"));
	var duplicate = registry.Components[0];
	Throws<ArgumentException>(() => new ComponentRegistry([duplicate, duplicate]));
}

static void VerifyAiContract()
{
	var state = ApplyMoves(new(0, 0), new(0, 2), new(1, 0), new(1, 2),
		new(2, 0), new(2, 2), new(3, 0), new(3, 2));
	foreach (var definition in ComponentRegistry.CreateDefault().Components)
	{
		var ai = definition.CreateAi();
		var move = ai.ChooseMove(state);
		Equal(true, state.Contains(move));
		Equal(Mark.Empty, state.GetMark(move));
		Equal(8L, state.Revision);
		Equal(GameStatus.XWon, state.ApplyMove(move, state.Revision).Status);
		var ended = state.ApplyMove(new Position(4, 0), state.Revision);
		Throws<InvalidOperationException>(() => ai.ChooseMove(ended));
	}
}

static void VerifyAiCancellation()
{
	using var cancellation = new CancellationTokenSource();
	cancellation.Cancel();
	foreach (var definition in ComponentRegistry.CreateDefault().Components)
	{
		Throws<OperationCanceledException>(() => definition.CreateAi().ChooseMove(GameState.Create(), cancellation.Token));
	}
}

static void VerifySearchCancellation()
{
	IGameAi[] searches =
	[
		new AdvancedSearcher(5, 30),
		new Xo.Ai.AdvancedV2.AdvancedSearcher(5, 30, 3000),
		new Xo.Ai.AdvancedV3.AdvancedSearcher(8, 30, 3000),
		new Xo.Ai.AdvancedV4.AdvancedSearcher(8, 30, 3000),
		new Xo.Ai.AdvancedV5.AdvancedSearcher(8, 30, 3000),
		new Xo.Ai.AdvancedV6.AdvancedSearcher(8, 30, 3000)
	];
	var state = ApplyMoves(new(9, 9), new(10, 10), new(10, 8), new(8, 10));
	var winning = ApplyMoves(new(0, 0), new(0, 2), new(1, 0), new(1, 2),
		new(2, 0), new(2, 2), new(3, 0), new(3, 2));
	foreach (var ai in searches)
	{
		using var cancellation = new CancellationTokenSource();
		using var started = new ManualResetEventSlim();
		var search = Task.Run(() =>
		{
			started.Set();
			return ai.ChooseMove(state, cancellation.Token);
		});
		Equal(true, started.Wait(TimeSpan.FromSeconds(5)));
		var stopwatch = Stopwatch.StartNew();
		cancellation.CancelAfter(10);
		Throws<OperationCanceledException>(() => search.GetAwaiter().GetResult());
		Equal(true, stopwatch.Elapsed < TimeSpan.FromSeconds(2));
		Equal(4L, state.Revision);
		Equal(GameStatus.XWon, winning.ApplyMove(ai.ChooseMove(winning), winning.Revision).Status);
	}
}

static void VerifyMoveDeadline()
{
	var exited = false;
	var ai = new TestAi((_, token) =>
	{
		try
		{
			token.WaitHandle.WaitOne();
			token.ThrowIfCancellationRequested();
			throw new InvalidOperationException("Expected cancellation.");
		}
		finally
		{
			exited = true;
		}
	});
	var exception = Throws<AiMoveException>(() =>
		AiMoveExecutor.ChooseMoveAsync(ai, GameState.Create(), timeout: TimeSpan.FromMilliseconds(20))
			.GetAwaiter().GetResult());
	Equal(true, exception.Message.Contains("deadline", StringComparison.Ordinal));
	Equal(true, exited);
}

static void VerifyMatchCancellation()
{
	using var cancellation = new CancellationTokenSource();
	var definition = new AiComponentDefinition("cancel", "Cancel", () => new TestAi((_, token) =>
	{
		cancellation.Cancel();
		token.ThrowIfCancellationRequested();
		throw new InvalidOperationException("Expected cancellation.");
	}));
	var runner = new AiMatchRunner(new ComponentRegistry([definition]));
	Throws<OperationCanceledException>(() =>
		runner.PlayAsync("cancel", "cancel", true, cancellation.Token).GetAwaiter().GetResult());
}

static void VerifyIllegalAiMove()
{
	var definition = new AiComponentDefinition("illegal", "Illegal", () => new TestAi((_, _) => new(9, 9)));
	var runner = new AiMatchRunner(new ComponentRegistry([definition]));
	var game = runner.PlayAsync("illegal", "illegal", true).GetAwaiter().GetResult();
	Equal(GameOutcome.OForfeit, game.Outcome);
	Equal(1, game.Moves.Count);
	Equal(true, game.Detail?.Contains("illegal move", StringComparison.Ordinal) == true);
}

static void VerifyHumanRollback()
{
	var attempts = 0;
	var definition = new AiComponentDefinition("retry", "Retry", () => new TestAi((state, token) =>
		++attempts == 1 ? new Position(-1, -1) : ChooseFirstEmptyCell(state, token)));
	VerifyAsync().GetAwaiter().GetResult();
	async Task VerifyAsync()
	{
		await using var session = await HumanGameSession.CreateAsync(definition, Mark.X);
		var state = session.State;
		Throws<AiMoveException>(() => session.PlayHumanMoveAsync(new(10, 10)).GetAwaiter().GetResult());
		Equal(true, ReferenceEquals(state, session.State));
		Equal(0, session.RecordedMoves.Count);
		await session.PlayHumanMoveAsync(new(10, 10));
		Equal(2L, session.State.Revision);
		Throws<GameRuleException>(() => session.PlayHumanMoveAsync(new(10, 10)).GetAwaiter().GetResult());
		Equal(2, session.RecordedMoves.Count);
		await session.DisposeAsync();
		Throws<ObjectDisposedException>(() => session.PlayHumanMoveAsync(new(11, 11)).GetAwaiter().GetResult());
	}
}

static void VerifyHumanCancellation()
{
	using var cancellation = new CancellationTokenSource();
	var first = true;
	var definition = new AiComponentDefinition("cancel-retry", "Cancel retry", () => new TestAi((state, token) =>
	{
		if (first)
		{
			first = false;
			cancellation.Cancel();
			token.ThrowIfCancellationRequested();
		}
		return ChooseFirstEmptyCell(state, token);
	}));
	VerifyAsync().GetAwaiter().GetResult();
	async Task VerifyAsync()
	{
		await using var session = await HumanGameSession.CreateAsync(definition, Mark.X);
		Throws<OperationCanceledException>(() =>
			session.PlayHumanMoveAsync(new(10, 10), cancellation.Token).GetAwaiter().GetResult());
		Equal(0L, session.State.Revision);
		Equal(0, session.RecordedMoves.Count);
		await session.PlayHumanMoveAsync(new(10, 10));
		Equal(2L, session.State.Revision);
	}
}

static void VerifyConcurrentGames()
{
	VerifyAsync().GetAwaiter().GetResult();
	async Task VerifyAsync()
	{
		var registry = ComponentRegistry.CreateDefault();
		var runner = new AiMatchRunner(CreateTestRegistry());
		var games = await Task.WhenAll(
			runner.PlayAsync("test", "test", true),
			runner.PlayAsync("test", "test", false));
		Equal(true, games[0].Moves.Select(m => (m.X, m.Y, m.Mark))
			.SequenceEqual(games[1].Moves.Select(m => (m.X, m.Y, m.Mark))));
		Equal(1, games[0].XPlayerNumber);
		Equal(2, games[1].XPlayerNumber);
		var definition = registry.GetRequired("xo-ai-advanced-v4");
		await using var first = await HumanGameSession.CreateAsync(definition, Mark.X);
		await using var second = await HumanGameSession.CreateAsync(definition, Mark.X);
		await Task.WhenAll(first.PlayHumanMoveAsync(new(0, 0)), second.PlayHumanMoveAsync(new(19, 19)));
		Equal(Mark.X, first.State.GetMark(new(0, 0)));
		Equal(Mark.Empty, first.State.GetMark(new(19, 19)));
		Equal(Mark.X, second.State.GetMark(new(19, 19)));
		Equal(Mark.Empty, second.State.GetMark(new(0, 0)));
	}
}

static void VerifyHumanWin()
{
	var calls = 0;
	var definition = new AiComponentDefinition("passive", "Passive",
		() => new TestAi((_, _) => new Position(2 * calls++, 2)));
	VerifyAsync().GetAwaiter().GetResult();
	async Task VerifyAsync()
	{
		await using var session = await HumanGameSession.CreateAsync(definition, Mark.X);
		for (var x = 0; x < 5; x++)
		{
			await session.PlayHumanMoveAsync(new(x, 0));
		}
		Equal(4, calls);
		Equal(GameStatus.XWon, session.State.Status);
		var completed = session.ToCompletedGame();
		Equal(GameOutcome.XWon, completed.Outcome);
		Equal(1, completed.XPlayerNumber);
		Equal(9, completed.Moves.Count);
	}
}

static void VerifyAiMatch()
{
	var runner = new AiMatchRunner(CreateTestRegistry());

	var game = runner.PlayAsync("test", "test", player1Starts: true)
		.GetAwaiter()
		.GetResult();

	var replay = GameState.Create();
	Equal(0L, game.Moves[0].ElapsedMilliseconds);
	foreach (var move in game.Moves)
	{
		replay = replay.ApplyMove(new Position(move.X, move.Y), replay.Revision);
		Equal(move.Mark, replay.Moves[^1].Mark);
	}

	var replayOutcome = replay.Status switch
	{
		GameStatus.XWon => GameOutcome.XWon,
		GameStatus.OWon => GameOutcome.OWon,
		GameStatus.Draw => GameOutcome.Draw,
		_ => throw new InvalidOperationException("AI match did not reach a terminal state.")
	};
	Equal(replayOutcome, game.Outcome);
}

static void VerifyHumanSession()
{
	var definition = CreateTestRegistry().GetRequired("test");

	VerifyAsync().GetAwaiter().GetResult();
	return;

	async Task VerifyAsync()
	{
		await using var session = await HumanGameSession.CreateAsync(definition, Mark.X);
		var state = await session.PlayHumanMoveAsync(new Position(10, 10));
		Equal(2L, state.Revision);
		Equal(Mark.X, state.GetMark(new Position(10, 10)));
		Equal(Mark.O, state.GetMark(new Position(0, 0)));
		Equal(0L, session.RecordedMoves[0].ElapsedMilliseconds);
	}
}

static void VerifyAdvancedHumanSessions()
{
	var registry = ComponentRegistry.CreateDefault();
	var definition = registry.GetRequired("xo-ai-advanced-v1");

	VerifyAsync().GetAwaiter().GetResult();
	return;

	async Task VerifyAsync()
	{
		foreach (var humanMark in new[] { Mark.X, Mark.O })
		{
			await using var session = await HumanGameSession.CreateAsync(definition, humanMark);
			Equal(humanMark == Mark.X ? 0L : 1L, session.State.Revision);
			Equal(humanMark, session.State.NextMark);
			var position = new Position(10, 10);
			var state = await session.PlayHumanMoveAsync(position);
			Equal(humanMark == Mark.X ? 2L : 3L, state.Revision);
			Equal(humanMark, state.GetMark(position));
			Equal(humanMark, state.NextMark);
		}
	}
}

static void VerifyArchiveCapacity()
{
	var archive = new GameArchive(capacity: 100);
	for (var index = 0; index < 101; index++)
	{
		archive.Add(new CompletedGame(
			Guid.NewGuid(),
			DateTimeOffset.UtcNow,
			DateTimeOffset.UtcNow,
			"one",
			"two",
			1,
			GameOutcome.Draw,
			[],
			index.ToString()));
	}

	var games = archive.GetRecent();
	Equal(100, games.Count);
	Equal("100", games[0].Detail ?? string.Empty);
	Equal("1", games[^1].Detail ?? string.Empty);
}

static void VerifyAdvancedImmediateWin()
{
	var game = ApplyMoves(
		new(0, 0), new(0, 2),
		new(1, 0), new(2, 2),
		new(2, 0), new(4, 2),
		new(3, 0), new(6, 2));

	var result = new AdvancedSearcher().ChooseMove(game, Mark.X);

	Equal(new Position(4, 0), result.Position);
}

static void VerifyAdvancedImmediateBlock()
{
	var game = ApplyMoves(
		new(10, 10), new(0, 0),
		new(10, 11), new(1, 0),
		new(12, 12), new(2, 0),
		new(14, 14), new(3, 0));

	var result = new AdvancedSearcher().ChooseMove(game, Mark.X);

	Equal(new Position(4, 0), result.Position);
}

static void VerifyV2ImmediateWin()
{
	var game = ApplyMoves(
		new(0, 0), new(0, 2),
		new(1, 0), new(2, 2),
		new(2, 0), new(4, 2),
		new(3, 0), new(6, 2));
	var result = new Xo.Ai.AdvancedV2.AdvancedSearcher().ChooseMove(game, Mark.X);
	Equal(new Position(4, 0), result.Position);
	Equal(GameStatus.XWon, game.ApplyMove(result.Position, game.Revision).Status);
	Equal(8L, game.Revision);
	Equal(Mark.Empty, game.GetMark(result.Position));
}

static void VerifyV2BrokenFourBlock()
{
	var game = ApplyMoves(
		new(10, 10), new(0, 0),
		new(10, 11), new(1, 0),
		new(12, 12), new(3, 0),
		new(14, 14), new(4, 0));
	var result = new Xo.Ai.AdvancedV2.AdvancedSearcher().ChooseMove(game, Mark.X);
	Equal(new Position(2, 0), result.Position);
}

static void VerifyV2WinBeforeDefense()
{
	var game = ApplyMoves(
		new(0, 0), new(0, 2), new(1, 0), new(1, 2),
		new(2, 0), new(2, 2), new(3, 0), new(3, 2));
	var result = new Xo.Ai.AdvancedV2.AdvancedSearcher().ChooseMove(game, Mark.X);
	Equal(GameStatus.XWon, game.ApplyMove(result.Position, game.Revision).Status);
}

static void VerifyV2ImmediateBlock()
{
	var game = ApplyMoves(
		new(10, 10), new(0, 0), new(10, 11), new(1, 0),
		new(12, 12), new(2, 0), new(14, 14), new(3, 0));
	var result = new Xo.Ai.AdvancedV2.AdvancedSearcher().ChooseMove(game, Mark.X);
	Equal(new Position(4, 0), result.Position);
}

static void VerifyV2WinningFork()
{
	var game = ApplyMoves(
		new(8, 9), new(0, 0),
		new(9, 9), new(2, 0),
		new(10, 9), new(4, 0));
	var result = new Xo.Ai.AdvancedV2.AdvancedSearcher().ChooseMove(game, Mark.X);
	var next = game.ApplyMove(result.Position, game.Revision);
	Equal(true, WinningMoves(next, Mark.X).Count >= 2);
	Equal(true, result.Score > 9_000_000);
}

static void VerifyV2ArchivedFork()
{
	var game = ApplyMoves(
		new(9, 9), new(10, 10), new(10, 8), new(8, 10),
		new(9, 10), new(9, 11), new(9, 8), new(11, 9),
		new(12, 8), new(11, 8), new(8, 12), new(11, 10),
		new(11, 11), new(7, 9), new(9, 6), new(9, 7),
		new(6, 8), new(8, 8), new(10, 6), new(6, 10),
		new(5, 11), new(10, 12), new(11, 13), new(10, 13),
		new(10, 11), new(8, 9));
	var result = new Xo.Ai.AdvancedV2.AdvancedSearcher().ChooseMove(game, Mark.X);
	var next = game.ApplyMove(result.Position, game.Revision);
	var fork = new Position(8, 7);
	var stopsFork = next.GetMark(fork) != Mark.Empty ||
		WinningMoves(next, Mark.X).Count > 0 ||
		WinningMoves(next.ApplyMove(fork, next.Revision), Mark.O).Count < 2;
	Equal(true, stopsFork);
}

static void VerifyV2ShiftedBoard()
{
	var configuration = new GameConfiguration(5, 5, -10, -20);
	var game = GameState.Create(configuration);
	var result = new Xo.Ai.AdvancedV2.AdvancedSearcher(timeBudgetMilliseconds: 50).ChooseMove(game, Mark.X);
	Equal(new Position(-8, -18), result.Position);
	game = game.ApplyMove(result.Position, game.Revision);
	result = new Xo.Ai.AdvancedV2.AdvancedSearcher(timeBudgetMilliseconds: 50).ChooseMove(game, Mark.O);
	Equal(true, game.Contains(result.Position));
	Equal(Mark.Empty, game.GetMark(result.Position));
}

static void VerifyHostedV2()
{
	var registry = ComponentRegistry.CreateDefault();
	VerifyAsync().GetAwaiter().GetResult();
	return;

	async Task VerifyAsync()
	{
		await using var session = await HumanGameSession.CreateAsync(registry.GetRequired("xo-ai-advanced-v2"), Mark.O);
		Equal(1L, session.State.Revision);
		var state = await session.PlayHumanMoveAsync(new Position(10, 10));
		Equal(3L, state.Revision);
		Equal(Mark.O, state.NextMark);
	}
}

static void VerifyV3WinBeforeDefense()
{
	var game = ApplyMoves(
		new(0, 0), new(0, 2), new(1, 0), new(1, 2),
		new(2, 0), new(2, 2), new(3, 0), new(3, 2));
	var result = new Xo.Ai.AdvancedV3.AdvancedSearcher().ChooseMove(game, Mark.X);
	Equal(GameStatus.XWon, game.ApplyMove(result.Position, game.Revision).Status);
	Equal(8L, game.Revision);
	Equal(Mark.Empty, game.GetMark(result.Position));
}

static void VerifyV3BrokenFourBlock()
{
	var game = ApplyMoves(
		new(10, 10), new(0, 0), new(10, 11), new(1, 0),
		new(12, 12), new(3, 0), new(14, 14), new(4, 0));
	var result = new Xo.Ai.AdvancedV3.AdvancedSearcher().ChooseMove(game, Mark.X);
	Equal(new Position(2, 0), result.Position);
}

static void VerifyV3WinningFork()
{
	var game = ApplyMoves(
		new(8, 9), new(0, 0), new(9, 9), new(2, 0), new(10, 9), new(4, 0));
	var result = new Xo.Ai.AdvancedV3.AdvancedSearcher().ChooseMove(game, Mark.X);
	Equal(true, WinningMoves(game.ApplyMove(result.Position, game.Revision), Mark.X).Count >= 2);
	Equal(true, result.Score > 9_000_000);
}

static void VerifyV3ArchivedWin()
{
	var game = ApplyMoves(
		new(9, 9), new(10, 10), new(10, 8), new(8, 10),
		new(9, 10), new(9, 11), new(9, 6), new(10, 12),
		new(7, 9), new(9, 8), new(8, 9), new(11, 13),
		new(12, 14), new(10, 9), new(10, 11), new(7, 8),
		new(11, 12), new(12, 13), new(6, 9), new(5, 9),
		new(8, 8), new(10, 13), new(9, 13), new(12, 11),
		new(8, 7), new(11, 9), new(8, 12), new(14, 13),
		new(13, 13), new(8, 6), new(10, 14), new(13, 12),
		new(11, 10), new(12, 8), new(13, 7), new(15, 14),
		new(16, 15), new(11, 15));
	var searcher = new Xo.Ai.AdvancedV3.AdvancedSearcher();
	var result = searcher.ChooseMove(game, Mark.X);
	Equal(true, result.Score > 9_000_000);
	game = game.ApplyMove(result.Position, game.Revision);
	if (game.Status == GameStatus.XWon)
	{
		return;
	}

	var replies = WinningMoves(game, Mark.X);
	Equal(true, replies.Count > 0);
	if (replies.Count == 1)
	{
		game = game.ApplyMove(replies[0], game.Revision);
		result = searcher.ChooseMove(game, Mark.X);
		game = game.ApplyMove(result.Position, game.Revision);
		Equal(true, game.Status == GameStatus.XWon || WinningMoves(game, Mark.X).Count >= 2);
	}
}

static void VerifyV3SearchBoard()
{
	var game = ApplyMoves(new(9, 9), new(10, 10), new(10, 8), new(8, 10));
	var board = new Xo.Ai.AdvancedV3.SearchBoard(game);
	var history = new List<(int Index, GameState State)>();
	var random = new Random(42);
	for (var move = 0; move < 16; move++)
	{
		VerifyBoard(board, game);
		var candidates = board.Candidates.ToArray();
		var index = candidates[random.Next(candidates.Length)];
		history.Add((index, game));
		var position = board.ToPosition(index);
		board.Place(index, game.NextMark);
		game = game.ApplyMove(position, game.Revision);
		if (game.Status != GameStatus.InProgress)
		{
			break;
		}
	}

	VerifyBoard(board, game);
	foreach (var (index, previous) in history.AsEnumerable().Reverse())
	{
		board.Undo(index);
		game = previous;
		VerifyBoard(board, game);
	}

	static void VerifyBoard(Xo.Ai.AdvancedV3.SearchBoard actual, GameState state)
	{
		var expected = new Xo.Ai.AdvancedV3.SearchBoard(state);
		Equal(expected.Hash, actual.Hash);
		var candidates = actual.Candidates.Order().ToArray();
		Equal(true, candidates.SequenceEqual(expected.Candidates.Order()));
		foreach (var mark in new[] { Mark.X, Mark.O })
		{
			Equal(ReferenceWindowScore(state, mark), actual.PositionalScore(mark));
			var wins = WinningMoves(state, mark).ToHashSet();
			foreach (var index in candidates)
			{
				var potential = actual.GetPotential(index, mark);
				Equal(expected.GetPotential(index, mark), potential);
				Equal(wins.Contains(actual.ToPosition(index)), potential.IsWin);
			}
		}
	}
}

static long ReferenceWindowScore(GameState game, Mark mark)
{
	long[] weights = [0, 2, 20, 300, 8000, 1_000_000];
	long xScore = 0, oScore = 0;
	var configuration = game.Configuration;
	for (var y = configuration.OriginY; y < configuration.OriginY + configuration.Height; y++)
	{
		for (var x = configuration.OriginX; x < configuration.OriginX + configuration.Width; x++)
		{
			foreach (var (dx, dy) in new[] { (1, 0), (0, 1), (1, 1), (1, -1) })
			{
				if (!game.Contains(new Position(x + 4 * dx, y + 4 * dy)))
				{
					continue;
				}

				var xs = 0;
				var os = 0;
				for (var offset = 0; offset < 5; offset++)
				{
					var cell = game.GetMark(new Position(x + offset * dx, y + offset * dy));
					xs += cell == Mark.X ? 1 : 0;
					os += cell == Mark.O ? 1 : 0;
				}

				xScore += os == 0 ? weights[xs] : 0;
				oScore += xs == 0 ? weights[os] : 0;
			}
		}
	}

	return mark == Mark.X ? xScore - oScore : oScore - xScore;
}

static void VerifyV3ShiftedBoard()
{
	var game = GameState.Create(new GameConfiguration(5, 5, -10, -20));
	var searcher = new Xo.Ai.AdvancedV3.AdvancedSearcher(timeBudgetMilliseconds: 50);
	var result = searcher.ChooseMove(game, Mark.X);
	Equal(new Position(-8, -18), result.Position);
	game = game.ApplyMove(result.Position, game.Revision);
	result = searcher.ChooseMove(game, Mark.O);
	Equal(true, game.Contains(result.Position));
	Equal(Mark.Empty, game.GetMark(result.Position));
	Throws<InvalidOperationException>(() => searcher.ChooseMove(game, Mark.X));
	Throws<NotSupportedException>(() => searcher.ChooseMove(GameState.Create(new GameConfiguration(winLength: 4)), Mark.X));
}

static void VerifyHostedV3()
{
	var registry = ComponentRegistry.CreateDefault();
	VerifyAsync().GetAwaiter().GetResult();
	return;

	async Task VerifyAsync()
	{
		await using var session = await HumanGameSession.CreateAsync(registry.GetRequired("xo-ai-advanced-v3"), Mark.O);
		Equal(1L, session.State.Revision);
		var state = await session.PlayHumanMoveAsync(new Position(10, 10));
		Equal(3L, state.Revision);
		Equal(Mark.O, state.NextMark);
	}
}

static void VerifyOpeningPairs()
{
	var suite = OpeningSuite.GetRequired("varied");
	Equal(6, suite.Count);
	for (var index = 0; index < 12; index += 2)
	{
		Equal(suite[index / 2], OpeningSuite.Select("varied", index, true));
		Equal(suite[index / 2], OpeningSuite.Select("varied", index + 1, true));
		var game = GameState.Create();
		foreach (var position in suite[index / 2].Moves)
		{
			game = game.ApplyMove(position, game.Revision);
		}
		Equal(GameStatus.InProgress, game.Status);
	}
	Equal(0, OpeningSuite.Select("standard", 0, true).Moves.Count);
	Throws<ArgumentOutOfRangeException>(() => OpeningSuite.GetRequired("invalid"));
}

static void VerifySeededMatches()
{
	var runner = new AiMatchRunner(CreateTestRegistry());
	foreach (var opening in OpeningSuite.GetRequired("varied"))
	{
		foreach (var starts in new[] { true, false })
		{
			var game = runner.PlayAsync("test", "test", starts, opening)
				.GetAwaiter().GetResult();
			Equal(opening.Id, game.OpeningId);
			Equal(opening.Moves.Count, game.OpeningMoveCount);
			Equal(true, game.Outcome is GameOutcome.XWon or GameOutcome.OWon or GameOutcome.Draw);
			var replay = GameState.Create();
			for (var index = 0; index < game.Moves.Count; index++)
			{
				var move = game.Moves[index];
				var position = new Position(move.X, move.Y);
				replay = replay.ApplyMove(position, replay.Revision);
				Equal(move.Mark, replay.Moves[^1].Mark);
				if (index < opening.Moves.Count)
				{
					Equal(opening.Moves[index], position);
					Equal(0L, move.ElapsedMilliseconds);
				}
			}
			Equal(true, replay.Status != GameStatus.InProgress);
		}
	}
	var invalid = new AiOpening("invalid", [new(0, 0)]);
	Throws<ArgumentException>(() => runner.PlayAsync("test", "test", true, invalid)
		.GetAwaiter().GetResult());
	var odd = new AiOpening("odd-prefix", [new(9, 9), new(10, 10), new(10, 8)]);
	foreach (var starts in new[] { true, false })
	{
		var game = runner.PlayAsync("test", "test", starts, odd).GetAwaiter().GetResult();
		Equal(3, game.OpeningMoveCount);
		Equal(true, game.Outcome is GameOutcome.XWon or GameOutcome.OWon or GameOutcome.Draw);
		var replay = GameState.Create();
		foreach (var move in game.Moves)
		{
			replay = replay.ApplyMove(new Position(move.X, move.Y), replay.Revision);
			Equal(move.Mark, replay.Moves[^1].Mark);
		}
		Equal(true, replay.Status != GameStatus.InProgress);
	}
}

static void VerifyV4Tactics() => VerifyCandidateTactics((game, mark) =>
{
	var result = new Xo.Ai.AdvancedV4.AdvancedSearcher().ChooseMove(game, mark);
	return (result.Position, result.Score);
});

static void VerifyCandidateTactics(Func<GameState, Mark, (Position Position, int Score)> choose)
{
	var game = ApplyMoves(new(0, 0), new(0, 2), new(1, 0), new(1, 2),
		new(2, 0), new(2, 2), new(3, 0), new(3, 2));
	var result = choose(game, Mark.X);
	Equal(GameStatus.XWon, game.ApplyMove(result.Position, game.Revision).Status);
	Equal(8L, game.Revision);
	Equal(Mark.Empty, game.GetMark(result.Position));
	game = ApplyMoves(new(10, 10), new(0, 0), new(10, 11), new(1, 0),
		new(12, 12), new(3, 0), new(14, 14), new(4, 0));
	result = choose(game, Mark.X);
	Equal(new Position(2, 0), result.Position);
	game = ApplyMoves(new(8, 9), new(0, 0), new(9, 9), new(2, 0), new(10, 9), new(4, 0));
	result = choose(game, Mark.X);
	Equal(true, WinningMoves(game.ApplyMove(result.Position, game.Revision), Mark.X).Count >= 2);
	Equal(true, result.Score > 9_000_000);
	game = GameState.Create(new GameConfiguration(5, 5, -10, -20));
	result = choose(game, Mark.X);
	Equal(new Position(-8, -18), result.Position);
}

static void VerifyV5Tactics() => VerifyCandidateTactics((game, mark) =>
{
	var result = new Xo.Ai.AdvancedV5.AdvancedSearcher().ChooseMove(game, mark);
	return (result.Position, result.Score);
});

static void VerifyV6Tactics() => VerifyCandidateTactics((game, mark) =>
{
	var result = new Xo.Ai.AdvancedV6.AdvancedSearcher().ChooseMove(game, mark);
	return (result.Position, result.Score);
});

static List<Position> WinningMoves(GameState game, Mark mark)
{
	var result = new List<Position>();
	var configuration = game.Configuration;
	for (var y = configuration.OriginY; y < configuration.OriginY + configuration.Height; y++)
	{
		for (var x = configuration.OriginX; x < configuration.OriginX + configuration.Width; x++)
		{
			var position = new Position(x, y);
			if (game.GetMark(position) != Mark.Empty)
			{
				continue;
			}

			foreach (var (dx, dy) in new[] { (1, 0), (0, 1), (1, 1), (1, -1) })
			{
				var count = 1;
				foreach (var sign in new[] { -1, 1 })
				{
					var current = new Position(x + sign * dx, y + sign * dy);
					while (game.Contains(current) && game.GetMark(current) == mark)
					{
						count++;
						current = new Position(current.X + sign * dx, current.Y + sign * dy);
					}
				}

				if (count >= configuration.WinLength)
				{
					result.Add(position);
					break;
				}
			}
		}
	}

	return result;
}

static GameState ApplyMoves(params Position[] positions)
{
	var game = GameState.Create();
	foreach (var position in positions)
	{
		game = game.ApplyMove(position, game.Revision);
	}

	return game;
}

static ComponentRegistry CreateTestRegistry() => new(
[
	new("test", "First empty cell test fixture", () => new TestAi(ChooseFirstEmptyCell))
]);

static Position ChooseFirstEmptyCell(GameState state, CancellationToken cancellationToken)
{
	var configuration = state.Configuration;
	for (var y = configuration.OriginY; y < configuration.OriginY + configuration.Height; y++)
	{
		cancellationToken.ThrowIfCancellationRequested();
		for (var x = configuration.OriginX; x < configuration.OriginX + configuration.Width; x++)
		{
			var position = new Position(x, y);
			if (state.GetMark(position) == Mark.Empty)
			{
				return position;
			}
		}
	}

	throw new InvalidOperationException("The test board has no legal move.");
}

static void Equal<T>(T expected, T actual)
	where T : notnull
{
	if (!EqualityComparer<T>.Default.Equals(expected, actual))
	{
		throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
	}
}

static TException Throws<TException>(Action action)
	where TException : Exception
{
	try
	{
		action();
	}
	catch (TException exception)
	{
		return exception;
	}

	throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

sealed class TestAi(Func<GameState, CancellationToken, Position> choose) : IGameAi
{
	public Position ChooseMove(GameState state, CancellationToken cancellationToken = default) =>
		choose(state, cancellationToken);
}
