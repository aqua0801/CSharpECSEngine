using ECSEngine;
using System.Collections.Concurrent;

namespace ECSEngine
{
    /// <summary>
    /// Provides functionality for generating and recycling unique integer IDs in a thread-safe manner.
    /// </summary>
    /// <remarks>IDManager is intended for scenarios where unique identifiers are required and can be reused
    /// after entities are destroyed. It is thread-safe and suitable for concurrent environments.</remarks>
    internal class IDManager
    {
        private int _lastEntityId = -1;
        private readonly ConcurrentQueue<int> _recycledIds = new();

        public int AddNewId()
        {
            if (_recycledIds.TryDequeue(out int id))
            {
                return id;
            }

            return Interlocked.Increment(ref _lastEntityId);
        }

        public void DestroyEntity(int entityId)
        {
            _recycledIds.Enqueue(entityId);
        }

    }
}

