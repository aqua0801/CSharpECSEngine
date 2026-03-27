using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ECSEngine
{
    /// <summary>
    /// ComponentPool interface to save extra castings.
    /// </summary>
    public interface IComponentPool : IDisposable
    {
        public bool Has(int entityId);
        void Remove(int entityId);
        int Count { get; }
    }

    /// <summary>
    /// Provides a pool for storing and managing components of type T, supporting efficient add, remove, and lookup
    /// operations by entity identifier.
    /// </summary>
    /// <remarks>ComponentPool is typically used in ECS architectures to
    /// associate components with entities using dense storage for performance. The pool automatically manages memory
    /// and resizes as needed. This class is not thread-safe.</remarks>
    /// <typeparam name="T">The value type of the component to be stored in the pool. Must be a struct.</typeparam>
    public class ComponentPool<T> : IComponentPool where T : struct
    {
        private static readonly ArrayPool<T> _compPool = ArrayPool<T>.Shared;
        private static readonly ArrayPool<int> _intPool = ArrayPool<int>.Shared;

        private static readonly bool _containsRefs = RuntimeHelpers.IsReferenceOrContainsReferences<T>();

        private T[] _components; 
        private int[] _entityToDense;
        private int[] _denseToEntity;
        private int _count = 0;
        private bool _disposed = false;

        public int Count
        {
            get { return _count; }
        }

        public ComponentPool(int initialCapacity = 256)
        {
            _components = _compPool.Rent(initialCapacity);
            _entityToDense = _intPool.Rent(initialCapacity);
            _denseToEntity = _intPool.Rent(initialCapacity);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void GrowDense()
        {
            int newSize = _components.Length * 2;

            var newComp = _compPool.Rent(newSize);
            var newD2E = _intPool.Rent(newSize);

            Array.Copy(_components, newComp, _count);
            Array.Copy(_denseToEntity, newD2E, _count);

            _compPool.Return(_components, clearArray: _containsRefs);
            _intPool.Return(_denseToEntity, clearArray: false);

            _components = newComp;
            _denseToEntity = newD2E;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void GrowSparse(int entityId)
        {
            int newSize = Math.Max(entityId + 1, _entityToDense.Length * 2);
            var next = _intPool.Rent(newSize);
            Array.Copy(_entityToDense, next, _entityToDense.Length);
            _intPool.Return(_entityToDense, clearArray: false);
            _entityToDense = next;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _compPool.Return(_components, clearArray: _containsRefs);
                _intPool.Return(_entityToDense, clearArray: false);
                _intPool.Return(_denseToEntity, clearArray: false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(int entityId, T component)
        {
            if (Has(entityId))
                throw new InvalidOperationException($"Entity {entityId} already has component {typeof(T).Name}");

            if ((uint)entityId >= (uint)_entityToDense.Length)
                GrowSparse(entityId);

            if (_count >= _components.Length)
                GrowDense();

            _entityToDense[entityId] = _count;
            _denseToEntity[_count] = entityId;
            _components[_count] = component;
            _count++;
        }

        public ref T Get(int entityId)
        {
            Debug.Assert(Has(entityId), $"Entity {entityId} does not have component {typeof(T).Name}");
            return ref _components[_entityToDense[entityId]];
        }

        public bool Has(int entityId)
        {
            if ((uint)entityId >= (uint)_entityToDense.Length) return false;
            int denseIndex = _entityToDense[entityId];
            if (denseIndex < 0) return false;
            return denseIndex < _count && _denseToEntity[denseIndex] == entityId;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool HasUnsafe(int entityId)
        {
            int denseIndex = _entityToDense[entityId];
            return denseIndex < _count && _denseToEntity[denseIndex] == entityId;
        }

        public ReadOnlySpan<int> ActiveEntities => _denseToEntity.AsSpan(0, _count);

        public void Remove(int entityId)
        {
            if (!Has(entityId)) return;

            int indexToRemove = _entityToDense[entityId];
            int lastIndex = _count - 1;

            if (indexToRemove < lastIndex)
            {
                int lastEntityId = _denseToEntity[lastIndex];
                _components[indexToRemove] = _components[lastIndex];
                _denseToEntity[indexToRemove] = lastEntityId;
                _entityToDense[lastEntityId] = indexToRemove;
            }

            _entityToDense[entityId] = -1;
            _count--;
        }

    }

    /// <summary>
    /// Provides contextual information for an ECS update cycle, including timing, frame
    /// count, and access to the ECS registry.
    /// </summary>
    /// <remarks>Use this struct to pass per-frame data and ECS state between systems during an update. The
    /// values are typically set by the ECS framework at the start of each update cycle.</remarks>
    public struct EcsContext
    {
        public float DeltaTime;
        public long FrameCount;
        public EcsRegistry Registry;
    }

    /// <summary>
    /// Provides a scheduler for managing and executing a sequence of ECS systems each frame.
    /// </summary>
    /// <remarks>The Scheduler coordinates the execution of registered systems, passing an updated context to
    /// each system on every tick. Systems are executed in the order they are added. This class is typically used in
    /// game loops or simulations to manage per-frame logic. Thread safety is not guaranteed; all operations should be
    /// performed on the same thread.</remarks>
    public class Scheduler
    {
        private readonly EcsRegistry _registry;
        private readonly List<Action<EcsRegistry, EcsContext>> _systems = new();
        private long _frameCount;

        public Scheduler(EcsRegistry registry) => _registry = registry;

        public Scheduler Add(Action<EcsRegistry, EcsContext> system)
        {
            _systems.Add(system);
            return this;
        }

        public void Tick(float deltaTime)
        {
            var ctx = new EcsContext
            {
                DeltaTime = deltaTime,
                FrameCount = _frameCount++,
                Registry = _registry
            };

            foreach (var system in _systems)
                system(_registry, ctx);

            _registry.PublishCommands();
        }
    }

}
