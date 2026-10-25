using System.Collections.Concurrent;

namespace Bork2d.Common;

public class EntityList<T> where T : class, IEntity
{
    private readonly ConcurrentDictionary<uint, WeakReference<T>> _entities = new();
    private uint _id = 0;

    public uint GetId() => Interlocked.Increment(ref _id);

    public void RegisterEntity(T entity)
    {
        _entities.TryAdd(entity.Id, new WeakReference<T>(entity));
    }

    public void UnregisterEntity(uint id)
    {
        _entities.TryRemove(id, out _);
    }

    public IEnumerable<T> GetEntities()
    {
        foreach (var entity in _entities.Values)
        {
            if (entity.TryGetTarget(out var target))
            {
                yield return target;
            }
        }
    }
}
