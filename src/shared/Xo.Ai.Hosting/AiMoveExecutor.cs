using Xo.Ai.Abstractions;
using Xo.Domain;

namespace Xo.Ai.Hosting;

public sealed class AiMoveException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public static class AiMoveExecutor
{
    public static async Task<Position> ChooseMoveAsync(
        IGameAi ai,
        GameState state,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        var moveTimeout = timeout ?? TimeSpan.FromSeconds(5);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(moveTimeout);
        try
        {
            var position = await Task.Run(() => ai.ChooseMove(state, deadline.Token), deadline.Token);
            deadline.Token.ThrowIfCancellationRequested();
            try
            {
                _ = state.ApplyMove(position, state.Revision);
            }
            catch (GameRuleException exception)
            {
                throw new AiMoveException($"AI returned an illegal move: {exception.Message}", exception);
            }

            return position;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new AiMoveException($"The AI exceeded the {moveTimeout.TotalSeconds:g} second move deadline.", exception);
        }
    }
}
