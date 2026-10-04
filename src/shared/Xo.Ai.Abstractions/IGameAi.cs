using Xo.Domain;

namespace Xo.Ai.Abstractions;

public interface IGameAi
{
    Position ChooseMove(GameState state, CancellationToken cancellationToken = default);
}
