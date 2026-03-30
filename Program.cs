using ECSEngine;
using System.Diagnostics;

// ── Registry setup ────────────────────────────────────────────
var (registry, scheduler) = EcsRegistry.Create(reg =>
{
    reg.RegisterPool<Position>();
    reg.RegisterPool<Velocity>();
    reg.RegisterPool<Acceleration>();
    reg.RegisterPool<Health>();
    reg.RegisterPool<Armor>();
    reg.RegisterPool<Dead>();
});

scheduler
    .Add(Systems.ApplyAcceleration)
    .Add(Systems.ApplyVelocity)
    .Add(Systems.DamageSystem)
    .Add(Systems.DeathSystem)
    .Add(Systems.ReviveSystem);

// ── Entity creation ───────────────────────────────────────────
var rng = new Random(42);

for (int i = 0; i < 500_000; i++)
{
    int id = registry.CreateEntity();
    registry.AddComponent<Position>(id, new() { X = rng.NextSingle() * 1000, Y = rng.NextSingle() * 1000 });
    registry.AddComponent<Velocity>(id, new() { X = rng.NextSingle() - 0.5f, Y = rng.NextSingle() - 0.5f, Speed = rng.NextSingle() * 10 });
    registry.AddComponent<Acceleration>(id, new() { X = (rng.NextSingle() - 0.5f) * 0.1f, Y = (rng.NextSingle() - 0.5f) * 0.1f });
    registry.AddComponent<Health>(id, new() { Current = 100, Max = 100 });
    registry.AddComponent<Armor>(id, new() { Value = rng.NextSingle() * 50 });
}

for (int i = 0; i < 300_000; i++)
{
    int id = registry.CreateEntity();
    registry.AddComponent<Position>(id, new() { X = rng.NextSingle() * 1000, Y = rng.NextSingle() * 1000 });
    registry.AddComponent<Velocity>(id, new() { X = rng.NextSingle() - 0.5f, Y = rng.NextSingle() - 0.5f, Speed = rng.NextSingle() * 10 });
    registry.AddComponent<Acceleration>(id, new() { X = (rng.NextSingle() - 0.5f) * 0.1f, Y = (rng.NextSingle() - 0.5f) * 0.1f });
}

Console.WriteLine("Warmup...");
for (int i = 0; i < 500; i++)
    scheduler.Tick(0.016f);

// ── Main loop ─────────────────────────────────────────────────
var sw = Stopwatch.StartNew();
var reportTimer = Stopwatch.StartNew();
double lastTime = 0;
long frameCount = 0;

while (true)
{
    double now = sw.Elapsed.TotalSeconds;
    float dt = (float)(now - lastTime);
    lastTime = now;

    scheduler.Tick(dt);
    frameCount++;

    if (reportTimer.Elapsed.TotalSeconds >= 1.0)
    {
        float cs = registry.Checksum<Position>(p => p.X + p.Y);
        Console.WriteLine($"FPS: {frameCount,4} | dt: {dt * 1000:F2}ms | pos checksum: {cs:F0}");
        frameCount = 0;
        reportTimer.Restart();
    }
}

// ── Components ────────────────────────────────────────────────
struct Position { public float X, Y; }
struct Velocity { public float X, Y, Speed; }
struct Acceleration { public float X, Y; }
struct Health { public float Current, Max; }
struct Armor { public float Value; }
struct Dead { }

// ── Systems ───────────────────────────────────────────────────
static class Systems
{
    public static void ApplyAcceleration(EcsRegistry reg, EcsContext ctx)
    {
        reg.GetQueryBuilder()
           .With<Velocity>()
           .With<Acceleration>()
           //.Execute(ctx, static (int id, ref Velocity vel, ref Acceleration acc, EcsContext c) =>
           //{
           //    vel.X += acc.X * c.DeltaTime;
           //    vel.Y += acc.Y * c.DeltaTime;
           //    vel.Speed = MathF.Min(vel.Speed + 0.1f * c.DeltaTime, 50f);

           //    float len = MathF.Sqrt(vel.X * vel.X + vel.Y * vel.Y);
  
           //    if (len > 1e-6f) { vel.X /= len; vel.Y /= len; }
           //})
           .ExecuteParallel(ctx,new AccelerationActionParallel())
           ;
    }

    public static void ApplyVelocity(EcsRegistry reg, EcsContext ctx)
    {
        reg.GetQueryBuilder()
           .With<Position>()
           .With<Velocity>()
           //.Execute(ctx, static (int id, ref Position pos, ref Velocity vel, EcsContext c) =>
           //{
           //    pos.X += vel.X * vel.Speed * c.DeltaTime;
           //    pos.Y += vel.Y * vel.Speed * c.DeltaTime;

           //    if (pos.X < 0) { pos.X = 0; vel.X = MathF.Abs(vel.X); }
           //    if (pos.X > 1000) { pos.X = 1000; vel.X = -MathF.Abs(vel.X); }
           //    if (pos.Y < 0) { pos.Y = 0; vel.Y = MathF.Abs(vel.Y); }
           //    if (pos.Y > 1000) { pos.Y = 1000; vel.Y = -MathF.Abs(vel.Y); }
           //})
           .ExecuteParallel(ctx,new VelocityActionParallel())
           ;
    }

    public static void DamageSystem(EcsRegistry reg, EcsContext ctx)
    {
        reg.GetQueryBuilder()
           .Without<Dead>()
           .With<Health>()
           .With<Armor>()
           //.Execute(ctx, static (int id, ref Health hp, ref Armor armor, EcsContext c) =>
           //{
           //    float raw = 10f * c.DeltaTime;
           //    float reduce = 1f - armor.Value / (armor.Value + 100f);
           //    hp.Current -= raw * reduce;
           //    hp.Current = MathF.Max(hp.Current, 0f);
           //})
           .ExecuteParallel(ctx,new DamageActionParallel())
           ;
    }

    public static void DeathSystem(EcsRegistry reg, EcsContext ctx)
    {
        reg.GetQueryBuilder()
           .Without<Dead>()
           .With<Health>()
           //.Execute(ctx, static (int id, ref Health hp, EcsContext c) =>
           //{
           //    if (hp.Current <= 0f)
           //    {
           //        int capturedId = id;
           //        c.Registry.PostCommand(r => r.AddComponent<Dead>(capturedId, new()));
           //    }
           //})
           .ExecuteParallel(ctx,new DeathActionParallel())
           ;
    }

    public static void ReviveSystem(EcsRegistry reg, EcsContext ctx)
    {
        reg.GetQueryBuilder()
           .With<Dead>()
           .With<Health>()
           //.Execute(ctx, static (int id, ref Dead _, ref Health hp, EcsContext c) =>
           //{
           //    if (c.FrameCount % 60 == 0)
           //    {
           //        hp.Current = hp.Max;
           //        int capturedId = id;
           //        c.Registry.PostCommand(r => r.RemoveComponent<Dead>(capturedId));
           //    }
           //})
           .ExecuteParallel(ctx,new ReviveActionParallel())
           ;
    }

    struct AccelerationActionParallel : IEcsParallelAction<Velocity, Acceleration>
    {
        public void Execute(int id, ref Velocity vel, ref Acceleration acc, ParallelEcsContext c)
        {
            vel.X += acc.X * c.DeltaTime;
            vel.Y += acc.Y * c.DeltaTime;
            vel.Speed = MathF.Min(vel.Speed + 0.1f * c.DeltaTime, 50f);

            float len = MathF.Sqrt(vel.X * vel.X + vel.Y * vel.Y);

            if (len > 1e-6f) { vel.X /= len; vel.Y /= len; }
        }
    }

    struct VelocityActionParallel : IEcsParallelAction<Position, Velocity>
    {
        public void Execute(int id, ref Position pos, ref Velocity vel, ParallelEcsContext c)
        {
            pos.X += vel.X * vel.Speed * c.DeltaTime;
            pos.Y += vel.Y * vel.Speed * c.DeltaTime;

            if (pos.X < 0) { pos.X = 0; vel.X = MathF.Abs(vel.X); }
            if (pos.X > 1000) { pos.X = 1000; vel.X = -MathF.Abs(vel.X); }
            if (pos.Y < 0) { pos.Y = 0; vel.Y = MathF.Abs(vel.Y); }
            if (pos.Y > 1000) { pos.Y = 1000; vel.Y = -MathF.Abs(vel.Y); }
        }
    }

    struct DamageActionParallel : IEcsParallelAction<Health, Armor>
    {
        public void Execute(int id, ref Health hp, ref Armor armor, ParallelEcsContext c)
        {
            float raw = 10f * c.DeltaTime;
            float reduce = 1f - armor.Value / (armor.Value + 100f);
            hp.Current -= raw * reduce;
            hp.Current = MathF.Max(hp.Current, 0f);
        }
    }

    struct DeathActionParallel : IEcsParallelAction<Health>
    {
        public void Execute(int id, ref Health hp, ParallelEcsContext c)
        {
            if (hp.Current <= 0f)
            {
                int capturedId = id;
                c.PostCommand(r => r.AddComponent<Dead>(capturedId, new()));
            }
        }
    }

    struct ReviveActionParallel : IEcsParallelAction<Dead, Health>
    {
        public void Execute(int id, ref Dead _, ref Health hp, ParallelEcsContext c)
        {
            if (c.FrameCount % 60 == 0)
            {
                hp.Current = hp.Max;
                int capturedId = id;
                c.PostCommand(r => r.RemoveComponent<Dead>(capturedId));
            }
        }
    }
}