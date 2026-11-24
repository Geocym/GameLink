using GameLink.Models;
namespace GameLink.Providers;
public interface IGameProvider : IDisposable
{
    event Action<GameEvent>? EventDetected;
    void Start();
}
