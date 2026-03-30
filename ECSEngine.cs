using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ECSEngine
{
    /// <summary>
    /// Central registry of the ECS engine.
    /// </summary>
    public partial class EcsRegistry : IDisposable
    {
        private FrozenDictionary<Type, IComponentPool>? _pools;
        private readonly Dictionary<Type, IComponentPool> _building = new();
        private readonly IDManager _idManager = new();
        internal EcsRegistry() 
        {

        }

        public static (EcsRegistry, Scheduler) Create(Action<EcsRegistry> configure)
        {
            var registry = new EcsRegistry();
            configure(registry);
            registry.Freeze();
            return (registry, new Scheduler(registry));
        }

        public float Checksum<T>(Func<T, float> selector) where T : struct
        {
            var pool = GetPool<T>();
            int count = pool.Count;
            float sum = 0;
            ref int entityRef = ref MemoryMarshal.GetReference(pool.ActiveEntities);

            for (int i = 0; i < count; i++)
            {
                int id = Unsafe.Add(ref entityRef, i);
                sum += selector(pool.Get(id));
            }

            return sum;
        }

        public void RegisterPool<T>() where T : struct
        {
            if (_pools is not null)
                throw new InvalidOperationException("Cannot register after Freeze()");
            _building[typeof(T)] = new ComponentPool<T>();
        }

        private void Freeze()
        {
            _pools = _building.ToFrozenDictionary();
            _building.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ComponentPool<T> GetPool<T>() where T : struct
        {
            if (_pools is null)
                throw new InvalidOperationException("Call EcsRegistry.Create() before using the registry");
            return (ComponentPool<T>)_pools[typeof(T)];
        }

        public ComponentPool<T> GetPoolUnsafe<T>() where T : struct
            => GetPool<T>();

        internal bool HasComponent(int entityId, Type type)
        {
            if (!_pools.TryGetValue(type, out var pool)) return false;
            return pool.Has(entityId);
        }

        public void AddComponent<T>(int entityId, T component) where T : struct
        {
            var pool = GetPool<T>();
            pool.Add(entityId, component);
        }

        public void RemoveComponent<T>(int entityId) where T: struct
        {
            var pool = GetPool<T>();
            pool.Remove(entityId);
        }

        public QueryBuilder GetQueryBuilder() => new QueryBuilder(this);

        private readonly ConcurrentQueue<Action<EcsRegistry>> _commands = new();

        public void PostCommand(Action<EcsRegistry> command)
        {
            _commands.Enqueue(command);
        }

        public void PublishCommands()
        {
            while (_commands.TryDequeue(out var command))
            {
                command(this);
            }
        }

        public int CreateEntity()
        {
            return _idManager.AddNewId();
        }

        public void DestroyEntity(int entityId)
        {
            _idManager.DestroyEntity(entityId);

            foreach (var pool in _pools.Values)
            {
                pool.Remove(entityId);
            }
        }

        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_pools is null) return;
            foreach (var pool in _pools.Values)
                pool.Dispose();
        }
    }

    
}
