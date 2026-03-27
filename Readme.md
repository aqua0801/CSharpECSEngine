# ECSEngine

A lightweight, high-performance Entity Component System for .NET 10+.

Built around a **sparse set** architecture with zero heap allocation in the hot path, fluent query API, and deferred structural changes via command buffers.

[![NuGet](https://img.shields.io/nuget/v/ECSEngine.svg)](https://www.nuget.org/packages/ECSEngine)

---


## Quick Start

```csharp
// 1. Define components as structs
struct Position { public float X, Y; }
struct Velocity { public float X, Y, Speed; }
struct Dead { }  // tag component

// 2. Create registry and scheduler
var (registry, scheduler) = EcsRegistry.Create(reg =>
{
    reg.RegisterPool<Position>();
    reg.RegisterPool<Velocity>();
    reg.RegisterPool<Dead>();
});

// 3. Add systems
scheduler
    .Add(MovementSystem)
    .Add(DeathSystem);

// 4. Create entities
int id = registry.CreateEntity();
registry.AddComponent<Position>(id, new() { X = 0, Y = 0 });
registry.AddComponent<Velocity>(id, new() { X = 1, Y = 0, Speed = 5f });

// 5. Tick
while (running)
    scheduler.Tick(deltaTime);
```

---

## Components

Components must be **structs**. No base class or interface required.

```csharp
struct Health  { public float Current, Max; }
struct Armor   { public float Value; }
struct Frozen  { }  // zero-size tag components are valid
```

All component types must be registered before calling `Create()`:

```csharp
var (registry, scheduler) = EcsRegistry.Create(reg =>
{
    reg.RegisterPool<Health>();
    reg.RegisterPool<Armor>();
    reg.RegisterPool<Frozen>();
});
```

Registering after `Create()` throws `InvalidOperationException`.

---

## Entities

```csharp
int id = registry.CreateEntity();

registry.AddComponent<Health>(id, new() { Current = 100, Max = 100 });
registry.RemoveComponent<Health>(id);
bool hasHealth = registry.HasComponent<Health>(id);

registry.DestroyEntity(id);  // removes all components and recycles the ID
```

---

## Queries

Queries use a fluent builder. All queries are executed immediately — there is no deferred evaluation.

### Basic query

```csharp
reg.GetQueryBuilder()
   .With<Position>()
   .Execute(ctx, (id, ref Position pos, EcsContext c) =>
   {
       pos.X += 1f * c.DeltaTime;
   });
```

### Multi-component intersection

The engine automatically iterates the smallest pool to minimise work:

```csharp
reg.GetQueryBuilder()
   .With<Position>()
   .With<Velocity>()
   .Execute(ctx, (id, ref Position pos, ref Velocity vel, EcsContext c) =>
   {
       pos.X += vel.X * vel.Speed * c.DeltaTime;
       pos.Y += vel.Y * vel.Speed * c.DeltaTime;
   });
```

Up to **4 components** are supported in the fluent chain.

### Without filter

`Without<T>()` must be placed **before** any `With<T>()` call:

```csharp
reg.GetQueryBuilder()
   .Without<Dead>()
   .Without<Frozen>()
   .With<Health>()
   .With<Armor>()
   .Execute(ctx, (id, ref Health hp, ref Armor armor, EcsContext c) =>
   {
       hp.Current -= 10f * c.DeltaTime;
   });
```

Up to **4 Without filters** are supported. Filters are resolved at query-build time — there is no per-iteration dictionary lookup.

---

## EcsContext

`EcsContext` is passed to every action and contains frame state:

```csharp
public struct EcsContext
{
    public float DeltaTime;
    public long  FrameCount;
    public EcsRegistry Registry;  // for PostCommand inside actions
}
```

---

## Structural Changes

Adding, removing components, or destroying entities must not happen directly inside a query. Use `PostCommand` to defer these operations to the end of the tick:

```csharp
void DeathSystem(EcsRegistry reg, EcsContext ctx)
{
    reg.GetQueryBuilder()
       .Without<Dead>()
       .With<Health>()
       .Execute(ctx, (id, ref Health hp, EcsContext c) =>
       {
           if (hp.Current <= 0f)
           {
               int capturedId = id;
               c.Registry.PostCommand(r => r.AddComponent<Dead>(capturedId, new()));
           }
       });
}
```

Commands are flushed automatically at the end of `scheduler.Tick()`.

You can also flush manually:

```csharp
registry.PublishCommands();
```

---

## Zero-overhead Queries with ExecuteInline

By default, `Execute` uses a delegate, which incurs one indirect call per iteration. For performance-critical systems, use `ExecuteInline` with a struct implementing `IEcsAction<T...>`:

```csharp
struct MoveAction : IEcsAction<Position, Velocity>
{
    public void Execute(int id, ref Position pos, ref Velocity vel, EcsContext ctx)
    {
        pos.X += vel.X * vel.Speed * ctx.DeltaTime;
        pos.Y += vel.Y * vel.Speed * ctx.DeltaTime;
    }
}

reg.GetQueryBuilder()
   .With<Position>()
   .With<Velocity>()
   .ExecuteInline(ctx, new MoveAction());
```

The JIT specialises the iteration loop for each `TAction` struct, eliminating all virtual dispatch.

---

## Beyond 4 Components

For queries requiring more than 4 components, use `GetPoolUnsafe<T>()` to access pools directly and write your own iteration logic as an extension method:

```csharp
public static class MyQueryExtensions
{
    public static void Execute<T1, T2, T3, T4, T5>(
        this EcsRegistry reg,
        EcsContext ctx,
        MyAction<T1, T2, T3, T4, T5> action)
        where T1 : struct where T2 : struct where T3 : struct
        where T4 : struct where T5 : struct
    {
        var p1 = reg.GetPoolUnsafe<T1>();
        var p2 = reg.GetPoolUnsafe<T2>();
        var p3 = reg.GetPoolUnsafe<T3>();
        var p4 = reg.GetPoolUnsafe<T4>();
        var p5 = reg.GetPoolUnsafe<T5>();

        // iterate smallest pool, check intersection
        foreach (int id in p1.ActiveEntities)
            if (p2.Has(id) && p3.Has(id) && p4.Has(id) && p5.Has(id))
                action(id, ref p1.Get(id), ref p2.Get(id),
                           ref p3.Get(id), ref p4.Get(id), ref p5.Get(id), ctx);
    }
}
```

> `GetPoolUnsafe` is an escape hatch. You are responsible for valid entity IDs and correct iteration logic.

---

## Scheduler

The `Scheduler` is separate from the `EcsRegistry` by design — the same registry can be driven by multiple schedulers (e.g. a fixed-step physics loop alongside a per-frame game loop):

```csharp
var gameLoop    = new Scheduler(registry).Add(InputSystem).Add(RenderSystem);
var physicsLoop = new Scheduler(registry).Add(PhysicsSystem);

while (running)
{
    gameLoop.Tick(deltaTime);
    physicsLoop.TickFixed(1f / 60f);  // if you implement TickFixed
}
```

Systems are plain static methods or any `Action<EcsRegistry, EcsContext>`:

```csharp
static void MovementSystem(EcsRegistry reg, EcsContext ctx) { ... }

scheduler.Add(MovementSystem);
scheduler.Add((reg, ctx) => { /* inline system */ });
```

---

## Disposal

`EcsRegistry` implements `IDisposable`. Component pools use `ArrayPool<T>` internally and return all rented arrays on disposal:

```csharp
using var (registry, scheduler) = EcsRegistry.Create(reg => { ... });
```

---

## Design Notes

| Concern | Decision |
|---|---|
| Component storage | Sparse set — O(1) add/remove/lookup, cache-friendly iteration |
| Thread safety | Single-threaded by design. Structural changes must be deferred via `PostCommand` |
| Query arity limit | 4 built-in, extensible via `GetPoolUnsafe` |
| Without filter limit | 4 per query |
| Entity ID recycling | Retired IDs are reused via an internal queue |
| Pool resizing | Doubling strategy using `ArrayPool<T>`, separately for sparse and dense arrays |

---

## Requirements

- .NET 10 or later
- No external dependencies
