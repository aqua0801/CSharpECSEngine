using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ECSEngine
{
    /// <summary>
    /// Provides delegate definitions for actions that operate on entities and their associated components within an ECS
    /// context.
    /// </summary>
    /// <remarks>The delegates defined in this class are intended for use with ECS frameworks, enabling
    /// strongly-typed operations on one or more components of a given entity. Each delegate includes the entity
    /// identifier, references to the relevant components, and the ECS context, allowing for flexible and type-safe
    /// manipulation of entity data.</remarks>
    public static class EcsActions
    {
        public delegate void EcsAction<T1>(int entityId, ref T1 c1, EcsContext ctx);
        public delegate void EcsAction<T1, T2>(int entityId, ref T1 c1, ref T2 c2, EcsContext ctx);
        public delegate void EcsAction<T1, T2, T3>(int entityId, ref T1 c1, ref T2 c2, ref T3 c3, EcsContext ctx);
        public delegate void EcsAction<T1, T2, T3, T4>(int entityId, ref T1 c1, ref T2 c2, ref T3 c3, ref T4 c4, EcsContext ctx);
    }

    public interface IEcsAction<T1>
    {
        void Execute(int entityId, ref T1 c1, EcsContext ctx);
    }
    public interface IEcsParallelAction<T1>
    {
        void Execute(int entityId, ref T1 c1, ParallelEcsContext ctx);
    }
    public interface IEcsAction<T1, T2>
    {
        void Execute(int entityId, ref T1 c1, ref T2 c2, EcsContext ctx);
    }
    public interface IEcsParallelAction<T1, T2>
    {
        void Execute(int entityId, ref T1 c1, ref T2 c2, ParallelEcsContext ctx);
    }
    public interface IEcsAction<T1, T2, T3>
    {
        void Execute(int entityId, ref T1 c1, ref T2 c2, ref T3 c3, EcsContext ctx);
    }
    public interface IEcsParallelAction<T1, T2, T3>
    {
        void Execute(int entityId, ref T1 c1, ref T2 c2, ref T3 p3, ParallelEcsContext ctx);
    }
    public interface IEcsAction<T1, T2, T3, T4>
    {
        void Execute(int entityId, ref T1 c1, ref T2 c2, ref T3 c3, ref T4 c4, EcsContext ctx);
    }
    public interface IEcsParallelAction<T1, T2, T3, T4>
    {
        void Execute(int entityId, ref T1 c1, ref T2 c2, ref T3 p3, ref T4 p4, ParallelEcsContext ctx);
    }

    public partial class EcsRegistry
    {
        /// <summary>
        /// Represents a fixed-size buffer containing four elements of type IComponentPool for use in internal pooling
        /// operations.
        /// </summary>
        /// <remarks>This structure is intended for internal use to optimize memory layout and access
        /// patterns when managing small collections of component pools. It leverages the InlineArray attribute to
        /// provide efficient, stack-allocated storage for exactly four elements.</remarks>
        [InlineArray(4)]
        internal struct PoolBuffer4
        {
            private IComponentPool _element;
        }

        [InlineArray(4)]
        internal struct BitsetBuffer4 { private ulong[] _element; }

        /// <summary>
        /// Provides a builder for constructing entity queries with optional component exclusion in an ECS registry.
        /// </summary>
        /// <remarks>Use this struct to fluently specify which components to include or exclude when
        /// building a query. The builder supports chaining of exclusion clauses before finalizing the query with the
        /// desired component types. This type is typically used in performance-critical scenarios where queries are
        /// constructed dynamically.</remarks>
        public struct QueryBuilder
        {
            private readonly EcsRegistry _reg;
            private PoolBuffer4 _withoutPools;
            private BitsetBuffer4 _withoutBitsets;
            private int _withoutCount;
            internal QueryBuilder(EcsRegistry reg) => _reg = reg;

            public Query<T1> With<T1>() where T1 : struct
                => new Query<T1>(_reg, _withoutBitsets, _withoutCount);

            public QueryBuilder Without<T>() where T : struct
            {
                if (_withoutCount < 4)
                {
                    var pool = _reg.GetPool<T>();
                    _withoutPools[_withoutCount] = pool;
                    _withoutBitsets[_withoutCount] = pool.BitsetArray;
                    _withoutCount++;
                }
                return this;
            }
        }

        /// <summary>
        /// Represents a query for entities that have a single component type in an ECS (Entity Component System)
        /// registry.
        /// </summary>
        /// <remarks>Use this struct to define and execute queries that operate on entities containing the
        /// specified component type. Additional component types can be included in the query by calling the With
        /// method. The query can be executed with user-defined actions for each matching entity.</remarks>
        /// <typeparam name="T1">The component type to include in the query. Must be a value type.</typeparam>
        public struct Query<T1> where T1 : struct
        {
            private readonly EcsRegistry _reg;
            private BitsetBuffer4 _withoutBitsets;
            private int _withoutCount;
            internal Query(EcsRegistry reg, BitsetBuffer4 withoutBitsets, int withoutCount)
            {
                _reg = reg;
                _withoutBitsets = withoutBitsets;
                _withoutCount = withoutCount;
            }

            public Query<T1, T2> With<T2>() where T2 : struct
                => new Query<T1, T2>(_reg, _withoutBitsets, _withoutCount);

            /// <summary>
            /// Executes the specified action for each active entity of type T1 that passes the filter within the given
            /// context.
            /// </summary>
            public void Execute(EcsContext ctx, EcsActions.EcsAction<T1> action)
            {
                var pool = _reg.GetPool<T1>();

                var entities = pool.ActiveEntities;
                int count = entities.Length;
                ref int entityRef = ref MemoryMarshal.GetReference(entities);

                for (int i = 0; i < count; i++)
                {
                    int id = Unsafe.Add(ref entityRef, i);
                    if (PassesFilter(id))
                        action(id, ref pool.Get(id), ctx);
                }
            }
            
            /// <summary>
            /// Less overhead of indirect call.
            /// </summary>
            public void ExecuteInline<TAction>(EcsContext ctx, TAction action)
                where TAction : struct , IEcsAction<T1>
            {
                var pool = _reg.GetPool<T1>();

                var entities = pool.ActiveEntities;
                int count = entities.Length;
                ref int entityRef = ref MemoryMarshal.GetReference(entities);

                for (int i = 0; i < count; i++)
                {
                    int id = Unsafe.Add(ref entityRef, i);
                    if (PassesFilter(id))
                        action.Execute(id, ref pool.Get(id), ctx);
                }
            }

            public void ExecuteParallel<TAction>(EcsContext ctx, TAction action)
                where TAction : struct, IEcsParallelAction<T1>
            {
                var pctx = ctx.AsParallel();
                var pool = _reg.GetPool<T1>();

                int[] entities = pool.DenseEntities;
                int count = pool.Count;
                int chunkSize = Math.Max(64 / Unsafe.SizeOf<T1>(), 1);

                var withoutBitsets = _withoutBitsets;
                int withoutCount = _withoutCount;

                Parallel.For(0, (count + chunkSize - 1) / chunkSize, chunk =>
                {
                    int start = chunk * chunkSize;
                    int end = Math.Min(start + chunkSize, count);
                    for (int i = start; i < end; i++)
                    {
                        int id = entities[i];

                        int word = id >> 6;
                        ulong bit = 1UL << (id & 63);
                        bool passes = true;
                        for (int j = 0; j < withoutCount; j++)
                        {
                            var bs = withoutBitsets[j];
                            if ((uint)word < (uint)bs.Length && (bs[word] & bit) != 0)
                            {
                                passes = false;
                                break;
                            }
                        }

                        if (passes)
                            action.Execute(id, ref pool.Get(id), pctx);
                    }
                });
            }


            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private bool PassesFilter(int id)
            {
                int word = id >> 6;
                ulong bit = 1UL << (id & 63);
                for (int i = 0; i < _withoutCount; i++)
                {
                    var bs = _withoutBitsets[i];
                    if ((uint)word < (uint)bs.Length && (bs[word] & bit) != 0)
                        return false;
                }
                return true;
            }
        }

        public struct Query<T1, T2> where T1 : struct where T2 : struct
        {
            private readonly EcsRegistry _reg;
            private BitsetBuffer4 _withoutBitsets;
            private int _withoutCount;
            internal Query(EcsRegistry reg, BitsetBuffer4 withoutBitsets, int withoutCount)
            {
                _reg = reg;
                _withoutBitsets = withoutBitsets;
                _withoutCount = withoutCount;
            }

            public Query<T1, T2, T3> With<T3>() where T3 : struct
                => new Query<T1, T2, T3>(_reg, _withoutBitsets, _withoutCount);

            public void Execute(EcsContext ctx, EcsActions.EcsAction<T1, T2> action)
            {
                var p1 = _reg.GetPool<T1>();
                var p2 = _reg.GetPool<T2>();

                ulong[] bs1 = p1.BitsetArray;
                ulong[] bs2 = p2.BitsetArray;
                int words = Math.Min(p1.BitsetWordCount, p2.BitsetWordCount);

                for (int w = 0; w < words; w++)
                {
                    ulong mask = bs1[w] & bs2[w];
                    if (mask == 0) continue;

                    for (int i = 0; i < _withoutCount; i++)
                    {
                        var wbs = _withoutBitsets[i];
                        if ((uint)w < (uint)wbs.Length)
                            mask &= ~wbs[w];
                    }

                    while (mask != 0)
                    {
                        int bit = BitOperations.TrailingZeroCount(mask);
                        int id = (w << 6) | bit;
                        action(id, ref p1.Get(id), ref p2.Get(id), ctx);
                        mask &= mask - 1; 
                    }
                }
            }

            public void ExecuteInline<TAction>(EcsContext ctx, TAction action)
                where TAction : struct , IEcsAction<T1,T2>
            {
                var p1 = _reg.GetPool<T1>();
                var p2 = _reg.GetPool<T2>();

                ulong[] bs1 = p1.BitsetArray;
                ulong[] bs2 = p2.BitsetArray;
                int words = Math.Min(p1.BitsetWordCount, p2.BitsetWordCount);

                for (int w = 0; w < words; w++)
                {
                    ulong mask = bs1[w] & bs2[w];
                    if (mask == 0) continue;

                    for (int i = 0; i < _withoutCount; i++)
                    {
                        var wbs = _withoutBitsets[i];
                        if ((uint)w < (uint)wbs.Length)
                            mask &= ~wbs[w];
                    }

                    while (mask != 0)
                    {
                        int bit = BitOperations.TrailingZeroCount(mask);
                        int id = (w << 6) | bit;
                        action.Execute(id, ref p1.Get(id), ref p2.Get(id), ctx);
                        mask &= mask - 1;
                    }
                }
            }

            public void ExecuteParallel<TAction>(EcsContext ctx, TAction action)
                where TAction : struct, IEcsParallelAction<T1, T2>
            {
                var p1 = _reg.GetPool<T1>();
                var p2 = _reg.GetPool<T2>();
                var pctx = ctx.AsParallel();

                ulong[] bs1 = p1.BitsetArray;
                ulong[] bs2 = p2.BitsetArray;
                int words = Math.Min(p1.BitsetWordCount, p2.BitsetWordCount);

                var withoutBitsets = _withoutBitsets;
                int withoutCount = _withoutCount;

                Parallel.For(0, words, w =>
                {
                    ulong mask = bs1[w] & bs2[w];
                    if (mask == 0) return;

                    for (int i = 0; i < withoutCount; i++)
                    {
                        var wbs = withoutBitsets[i];
                        if ((uint)w < (uint)wbs.Length)
                            mask &= ~wbs[w];
                    }
                    if (mask == 0) return;

                    while (mask != 0)
                    {
                        int bit = BitOperations.TrailingZeroCount(mask);
                        int id = (w << 6) | bit;
                        action.Execute(id, ref p1.Get(id), ref p2.Get(id), pctx);
                        mask &= mask - 1;
                    }
                });
            }
        }

        public struct Query<T1, T2, T3> where T1 : struct where T2 : struct where T3 : struct
        {
            private readonly EcsRegistry _reg;
            private BitsetBuffer4 _withoutBitsets;
            private int _withoutCount;
            internal Query(EcsRegistry reg, BitsetBuffer4 withoutBitsets, int withoutCount)
            {
                _reg = reg;
                _withoutBitsets = withoutBitsets;
                _withoutCount = withoutCount;
            }

            public Query<T1, T2, T3, T4> With<T4>() where T4 : struct
                => new Query<T1, T2, T3, T4>(_reg, _withoutBitsets, _withoutCount);

            public void Execute(EcsContext ctx, EcsActions.EcsAction<T1, T2, T3> action)
            {
                var p1 = _reg.GetPool<T1>();
                var p2 = _reg.GetPool<T2>();
                var p3 = _reg.GetPool<T3>();

                ulong[] bs1 = p1.BitsetArray;
                ulong[] bs2 = p2.BitsetArray;
                ulong[] bs3 = p3.BitsetArray;
                int words = Min(p1.BitsetWordCount, p2.BitsetWordCount, p3.BitsetWordCount);

                for (int w = 0; w < words; w++)
                {
                    ulong mask = bs1[w] & bs2[w] & bs3[w];
                    if (mask == 0) continue;

                    for (int i = 0; i < _withoutCount; i++)
                    {
                        var wbs = _withoutBitsets[i];
                        if ((uint)w < (uint)wbs.Length)
                            mask &= ~wbs[w];
                    }

                    while (mask != 0)
                    {
                        int bit = BitOperations.TrailingZeroCount(mask);
                        int id = (w << 6) | bit;
                        action(id, ref p1.Get(id), ref p2.Get(id), ref p3.Get(id), ctx);
                        mask &= mask - 1;
                    }
                }
            }

            public void ExecuteInline<TAction>(EcsContext ctx, TAction action)
                where TAction : struct , IEcsAction<T1,T2,T3>
            {
                var p1 = _reg.GetPool<T1>();
                var p2 = _reg.GetPool<T2>();
                var p3 = _reg.GetPool<T3>();

                ulong[] bs1 = p1.BitsetArray;
                ulong[] bs2 = p2.BitsetArray;
                ulong[] bs3 = p3.BitsetArray;
                int words = Min(p1.BitsetWordCount, p2.BitsetWordCount, p3.BitsetWordCount);

                for (int w = 0; w < words; w++)
                {
                    ulong mask = bs1[w] & bs2[w] & bs3[w];
                    if (mask == 0) continue;

                    for (int i = 0; i < _withoutCount; i++)
                    {
                        var wbs = _withoutBitsets[i];
                        if ((uint)w < (uint)wbs.Length)
                            mask &= ~wbs[w];
                    }

                    while (mask != 0)
                    {
                        int bit = BitOperations.TrailingZeroCount(mask);
                        int id = (w << 6) | bit;
                        action.Execute(id, ref p1.Get(id), ref p2.Get(id), ref p3.Get(id), ctx);
                        mask &= mask - 1;
                    }
                }
            }

            public void ExecuteParallel<TAction>(EcsContext ctx, TAction action)
                where TAction : struct, IEcsParallelAction<T1, T2, T3>
            {
                var p1 = _reg.GetPool<T1>();
                var p2 = _reg.GetPool<T2>();
                var p3 = _reg.GetPool<T3>();
                var pctx = ctx.AsParallel();

                ulong[] bs1 = p1.BitsetArray;
                ulong[] bs2 = p2.BitsetArray;
                ulong[] bs3 = p3.BitsetArray;
                int words = Min(p1.BitsetWordCount, p2.BitsetWordCount, p3.BitsetWordCount);

                var withoutBitsets = _withoutBitsets;
                int withoutCount = _withoutCount;

                Parallel.For(0, words, w =>
                {
                    ulong mask = bs1[w] & bs2[w] & bs3[w];
                    if (mask == 0) return;

                    for (int i = 0; i < withoutCount; i++)
                    {
                        var wbs = withoutBitsets[i];
                        if ((uint)w < (uint)wbs.Length)
                            mask &= ~wbs[w];
                    }
                    if (mask == 0) return;

                    while (mask != 0)
                    {
                        int bit = BitOperations.TrailingZeroCount(mask);
                        int id = (w << 6) | bit;
                        action.Execute(id, ref p1.Get(id), ref p2.Get(id), ref p3.Get(id), pctx);
                        mask &= mask - 1;
                    }
                });
            }
        }

        public struct Query<T1, T2, T3, T4> where T1 : struct where T2 : struct where T3 : struct where T4 : struct
        {
            private readonly EcsRegistry _reg;
            private BitsetBuffer4 _withoutBitsets;
            private int _withoutCount;
            internal Query(EcsRegistry reg, BitsetBuffer4 withoutBitsets, int withoutCount)
            {
                _reg = reg;
                _withoutBitsets = withoutBitsets;
                _withoutCount = withoutCount;
            }

            public void Execute(EcsContext ctx, EcsActions.EcsAction<T1, T2, T3, T4> action)
            {
                var p1 = _reg.GetPool<T1>();
                var p2 = _reg.GetPool<T2>();
                var p3 = _reg.GetPool<T3>();
                var p4 = _reg.GetPool<T4>();

                ulong[] bs1 = p1.BitsetArray;
                ulong[] bs2 = p2.BitsetArray;
                ulong[] bs3 = p3.BitsetArray;
                ulong[] bs4 = p4.BitsetArray;
                int words = Min(p1.BitsetWordCount, p2.BitsetWordCount, p3.BitsetWordCount , p4.BitsetWordCount);

                for (int w = 0; w < words; w++)
                {
                    ulong mask = bs1[w] & bs2[w] & bs3[w] & bs4[w];
                    if (mask == 0) continue;

                    for (int i = 0; i < _withoutCount; i++)
                    {
                        var wbs = _withoutBitsets[i];
                        if ((uint)w < (uint)wbs.Length)
                            mask &= ~wbs[w];
                    }

                    while (mask != 0)
                    {
                        int bit = BitOperations.TrailingZeroCount(mask);
                        int id = (w << 6) | bit;
                        action(id, ref p1.Get(id), ref p2.Get(id), ref p3.Get(id), ref p4.Get(id), ctx);
                        mask &= mask - 1;
                    }
                }
            }

            public void ExecuteInline<TAction>(EcsContext ctx, TAction action)
                where TAction : struct , IEcsAction<T1,T2,T3,T4>
            {
                var p1 = _reg.GetPool<T1>();
                var p2 = _reg.GetPool<T2>();
                var p3 = _reg.GetPool<T3>();
                var p4 = _reg.GetPool<T4>();

                ulong[] bs1 = p1.BitsetArray;
                ulong[] bs2 = p2.BitsetArray;
                ulong[] bs3 = p3.BitsetArray;
                ulong[] bs4 = p4.BitsetArray;
                int words = Min(p1.BitsetWordCount, p2.BitsetWordCount, p3.BitsetWordCount, p4.BitsetWordCount);

                for (int w = 0; w < words; w++)
                {
                    ulong mask = bs1[w] & bs2[w] & bs3[w] & bs4[w];
                    if (mask == 0) continue;

                    for (int i = 0; i < _withoutCount; i++)
                    {
                        var wbs = _withoutBitsets[i];
                        if ((uint)w < (uint)wbs.Length)
                            mask &= ~wbs[w];
                    }

                    while (mask != 0)
                    {
                        int bit = BitOperations.TrailingZeroCount(mask);
                        int id = (w << 6) | bit;
                        action.Execute(id, ref p1.Get(id), ref p2.Get(id), ref p3.Get(id), ref p4.Get(id), ctx);
                        mask &= mask - 1;
                    }
                }
            }
            public void ExecuteParallel<TAction>(EcsContext ctx, TAction action)
                where TAction : struct, IEcsParallelAction<T1, T2, T3, T4>
            {
                var p1 = _reg.GetPool<T1>();
                var p2 = _reg.GetPool<T2>();
                var p3 = _reg.GetPool<T3>();
                var p4 = _reg.GetPool<T4>();
                var pctx = ctx.AsParallel();

                ulong[] bs1 = p1.BitsetArray;
                ulong[] bs2 = p2.BitsetArray;
                ulong[] bs3 = p3.BitsetArray;
                ulong[] bs4 = p4.BitsetArray;
                int words = Min(p1.BitsetWordCount, p2.BitsetWordCount, p3.BitsetWordCount, p4.BitsetWordCount);

                var withoutBitsets = _withoutBitsets;
                int withoutCount = _withoutCount;

                Parallel.For(0, words, w =>
                {
                    ulong mask = bs1[w] & bs2[w] & bs3[w] & bs4[w];
                    if (mask == 0) return;

                    for (int i = 0; i < withoutCount; i++)
                    {
                        var wbs = withoutBitsets[i];
                        if ((uint)w < (uint)wbs.Length)
                            mask &= ~wbs[w];
                    }
                    if (mask == 0) return;

                    while (mask != 0)
                    {
                        int bit = BitOperations.TrailingZeroCount(mask);
                        int id = (w << 6) | bit;
                        action.Execute(id, ref p1.Get(id), ref p2.Get(id), ref p3.Get(id), ref p4.Get(id), pctx);
                        mask &= mask - 1;
                    }
                });
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Min(int a, int b , int c) =>
            a <= b ? (a <= c ? a : c) : (b <= c ? b : c);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Min(int a, int b, int c, int d) =>
            a <= b && a <= c && a <= d ? a :
            b <= c && b <= d ? b :
            c <= d ? c : d;
    }


    public struct ParallelEcsContext
    {
        public float DeltaTime;
        public long FrameCount;
        internal EcsRegistry _reg;
        public void PostCommand(Action<EcsRegistry> cmd) => _reg.PostCommand(cmd);
    }

    public static class QueryExtensions
    {
        public delegate void EcsAction<T1, T2, T3, T4, T5>(
            int entityId, ref T1 c1, ref T2 c2, ref T3 c3,ref T4 c4, ref T5 c5, EcsContext ctx);

        /// <summary>
        /// Executes a user-defined action for each entity that contains all specified component types in the registry.
        /// </summary>
        /// <remarks>This method allows for custom iteration logic over entities with five specific
        /// component types. It is intended for scenarios where built-in execution patterns are insufficient and custom
        /// processing is required.</remarks>
        public static void Execute<T1, T2, T3, T4, T5>(
            this EcsRegistry reg,
            EcsContext ctx,
            EcsAction<T1, T2, T3, T4, T5> action)
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
        {
            var p1 = reg.GetPoolUnsafe<T1>();
            var p2 = reg.GetPoolUnsafe<T2>();
            var p3 = reg.GetPoolUnsafe<T3>();
            var p4 = reg.GetPoolUnsafe<T4>();
            var p5 = reg.GetPoolUnsafe<T5>();

            var entities = MinIndex(p1.Count, p2.Count, p3.Count, p4.Count, p5.Count) switch
            {
                0 => p1.ActiveEntities,
                1 => p2.ActiveEntities,
                2 => p3.ActiveEntities,
                3 => p4.ActiveEntities,
                _ => p5.ActiveEntities
            };
            int count = entities.Length;
            ref int entityRef = ref MemoryMarshal.GetReference(entities);

            for (int i = 0; i < count; i++)
            {
                int id = Unsafe.Add(ref entityRef, i);
                if (p1.HasUnsafe(id) && p2.HasUnsafe(id) && p3.HasUnsafe(id) && p4.HasUnsafe(id) && p5.HasUnsafe(id))
                    action(id, ref p1.Get(id), ref p2.Get(id), ref p3.Get(id), ref p4.Get(id), ref p5.Get(id), ctx);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int MinIndex(int a, int b, int c, int d, int e) =>
            a <= b && a <= c && a <= d && a <= e ? 0 :
            b <= c && b <= d && b <= e ? 1 :
            c <= d && c <= e ? 2 :
            d <= e ? 3 : 4;

    }

}
