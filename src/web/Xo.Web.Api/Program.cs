using System.Text.Json.Serialization;
using Xo.Ai.Hosting;
using Xo.Domain;
using Xo.MatchRunner;
using Xo.Web.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
	options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();

builder.Services.AddSingleton(ComponentRegistry.CreateDefault());
builder.Services.AddSingleton(new GameArchive(capacity: 100));
builder.Services.AddSingleton<HumanGameStore>();
builder.Services.AddSingleton<AiRunManager>();

var app = builder.Build();
app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/components", (ComponentRegistry registry) =>
	registry.Components.Select(component => new
	{
		component.Id,
		component.DisplayName,
		component.Settings
	}));

app.MapPost("/api/human-games", async (
	CreateHumanGameRequest request,
	HumanGameStore games,
	CancellationToken cancellationToken) =>
{
	try
	{
		var game = await games.CreateAsync(request.AiId, request.HumanMark, cancellationToken);
		return Results.Created($"/api/human-games/{game.Id}", GameView.FromHuman(game));
	}
	catch (KeyNotFoundException exception)
	{
		return Results.NotFound(new { error = exception.Message });
	}
	catch (ArgumentOutOfRangeException exception)
	{
		return Results.BadRequest(new { error = exception.Message });
	}
	catch (AiMoveException exception)
	{
		return Results.Problem(detail: exception.Message, statusCode: 500, title: "AI move failed");
	}
});

app.MapGet("/api/human-games/{id:guid}", (Guid id, HumanGameStore games) =>
{
	var game = games.Get(id);
	return game is null ? Results.NotFound() : Results.Ok(GameView.FromHuman(game));
});

app.MapPost("/api/human-games/{id:guid}/moves", async (
	Guid id,
	HumanMoveRequest request,
	HumanGameStore games,
	CancellationToken cancellationToken) =>
{
	try
	{
		var game = await games.PlayAsync(id, new Position(request.X, request.Y), cancellationToken);
		return Results.Ok(GameView.FromHuman(game));
	}
	catch (KeyNotFoundException exception)
	{
		return Results.NotFound(new { error = exception.Message });
	}
	catch (GameRuleException exception)
	{
		return Results.Conflict(new { error = exception.Error.ToString(), detail = exception.Message });
	}
	catch (AiMoveException exception)
	{
		return Results.Problem(detail: exception.Message, statusCode: 500, title: "AI move failed");
	}
});

app.MapPost("/api/ai-runs", (CreateAiRunRequest request, AiRunManager runs) =>
{
	try
	{
		return Results.Accepted($"/api/ai-runs", runs.Start(request));
	}
	catch (Exception exception) when (exception is KeyNotFoundException or ArgumentOutOfRangeException)
	{
		return Results.BadRequest(new { error = exception.Message });
	}
});

app.MapGet("/api/ai-runs/{id:guid}", (Guid id, AiRunManager runs) =>
{
	var run = runs.Get(id);
	return run is null ? Results.NotFound() : Results.Ok(run);
});

app.MapGet("/api/games", (int? limit, GameArchive archive) =>
	archive.GetRecent(Math.Clamp(limit ?? 100, 1, 100)));

app.MapGet("/api/games/{id:guid}", (Guid id, GameArchive archive) =>
{
	var game = archive.Get(id);
	return game is null ? Results.NotFound() : Results.Ok(GameView.FromCompleted(game));
});

app.MapFallbackToFile("index.html");
app.Run();

public partial class Program;
