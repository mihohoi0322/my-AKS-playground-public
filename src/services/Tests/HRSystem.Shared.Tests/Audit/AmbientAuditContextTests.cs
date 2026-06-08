using HRSystem.Shared.Audit;

namespace HRSystem.Shared.Tests.Audit;

public sealed class AmbientAuditContextTests
{
    private static AuditAmbient Sample(string actorId) => new(
        Actor: new AuditActor(actorId, "user"),
        ActingAs: null,
        DelegationPolicySnapshot: null,
        ClientIpHash: null,
        UserAgent: null,
        Traceparent: null);

    [Fact]
    public async Task Current_IsNullByDefault()
    {
        // Run in a fresh task so AsyncLocal state from other tests cannot leak in.
        await Task.Run(() => Assert.Null(AmbientAuditContext.Current));
    }

    [Fact]
    public async Task Push_SetsCurrent_AndDisposeRestoresPrevious()
    {
        await Task.Run(() =>
        {
            Assert.Null(AmbientAuditContext.Current);
            using (AmbientAuditContext.Push(Sample("a")))
            {
                Assert.Equal("a", AmbientAuditContext.Current!.Actor.ObjectId);
                using (AmbientAuditContext.Push(Sample("b")))
                {
                    Assert.Equal("b", AmbientAuditContext.Current!.Actor.ObjectId);
                }
                Assert.Equal("a", AmbientAuditContext.Current!.Actor.ObjectId);
            }
            Assert.Null(AmbientAuditContext.Current);
        });
    }

    [Fact]
    public async Task Require_ThrowsWhenAbsent()
    {
        await Task.Run(() => Assert.Throws<InvalidOperationException>(() => AmbientAuditContext.Require()));
    }

    [Fact]
    public async Task ConcurrentTasks_DoNotLeakAmbient()
    {
        // Each concurrent task pushes its own ambient and verifies isolation.
        const int parallelism = 32;
        var tasks = Enumerable.Range(0, parallelism).Select(async i =>
        {
            using var _ = AmbientAuditContext.Push(Sample($"actor-{i}"));
            await Task.Yield();
            await Task.Delay(1);
            return AmbientAuditContext.Current!.Actor.ObjectId;
        }).ToArray();

        var results = await Task.WhenAll(tasks);
        for (var i = 0; i < parallelism; i++)
        {
            Assert.Equal($"actor-{i}", results[i]);
        }
    }

    [Fact]
    public async Task TaskWithoutPush_SeesNoAmbient_EvenWhenSiblingPushed()
    {
        var pushed = new TaskCompletionSource();
        var observed = new TaskCompletionSource<AuditAmbient?>();

        var pushing = Task.Run(async () =>
        {
            using var _ = AmbientAuditContext.Push(Sample("pusher"));
            pushed.SetResult();
            await Task.Delay(50);
        });

        var observing = Task.Run(async () =>
        {
            await pushed.Task;
            observed.SetResult(AmbientAuditContext.Current);
        });

        await Task.WhenAll(pushing, observing);
        Assert.Null(await observed.Task);
    }
}
