using System.Collections;
using System.Collections.Concurrent;
using EDNexus.Core.Journal;
using EDNexus.Core.State;
using EDNexus.Plugins.Abstractions;
using EDNexus.Plugins.Hosting.Bridge;

namespace EDNexus.Plugins.Hosting.Tests;

public sealed class PluginBridgeTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private readonly JournalEventBus _bus = new();
    private readonly CommanderState _state = new();
    private readonly List<PluginBridgeSession> _sessions = [];

    public PluginBridgeTests() => _ = new StateTracker(_bus, _state);

    public void Dispose()
    {
        foreach (var session in _sessions) session.Dispose();
    }

    private static PluginManifest Manifest(string id, params string[] capabilities)
        => new(id, id, "1.0.0", PluginSdk.CurrentVersionString) { Capabilities = capabilities };

    private PluginBridgeSession Attach(PluginBridge bridge, string id, params string[] capabilities)
    {
        var session = bridge.Attach(Manifest(id, capabilities));
        _sessions.Add(session);
        return session;
    }

    private void Publish(string json, bool historical = false)
    {
        Assert.True(JournalEntry.TryParse(json, historical, out var entry));
        _bus.Publish(entry);
    }

    private const string Jump = """{"timestamp":"2026-09-26T10:00:00Z","event":"FSDJump","StarSystem":"Sol","Body":"Sol"}""";
    private const string Cargo = """{"timestamp":"2026-09-26T10:00:01Z","event":"Cargo","Vessel":"Ship","Inventory":[{"Name":"gold","Name_Localised":"Gold","Count":12}]}""";
    private const string Materials = """{"timestamp":"2026-09-26T10:00:02Z","event":"Materials","Raw":[{"Name":"iron","Count":40}],"Manufactured":[],"Encoded":[]}""";

    // ---- State: read-only, copies, no path back to CommanderState ----

    [Fact]
    public void State_ReflectsCommanderState_AndCollectionsAreFrozenCopies()
    {
        var session = Attach(new PluginBridge(_bus, _state), "a.state", PluginCapabilities.State);
        Publish(Jump);
        Publish(Cargo);
        Publish(Materials);

        var view = session.State;
        Assert.Equal("Sol", view.StarSystem);
        Assert.Equal(12, view.Cargo["Gold"]);
        Assert.Equal(40, view.RawMaterials["iron"]);

        // Neither the interface nor a cast back to a mutable type can reach the live collection.
        var cargo = view.Cargo;
        Assert.IsNotType<ConcurrentDictionary<string, int>>(cargo);
        Assert.False(cargo is ConcurrentDictionary<string, int>);
        Assert.NotSame(_state.Cargo, cargo);
        var asDictionary = Assert.IsAssignableFrom<IDictionary<string, int>>(cargo);
        Assert.Throws<NotSupportedException>(() => asDictionary["Gold"] = 999);
        Assert.Throws<NotSupportedException>(() => asDictionary.Add("Silver", 1));
        Assert.Throws<NotSupportedException>(() => asDictionary.Clear());
        if (cargo is IDictionary nonGeneric)
            Assert.Throws<NotSupportedException>(() => nonGeneric["Gold"] = 999);
        Assert.Equal(12, _state.Cargo["Gold"]);

        // A copy handed out earlier does not change under the plugin either.
        _state.Cargo["Gold"] = 1;
        Assert.Equal(12, cargo["Gold"]);
        Assert.Equal(1, view.Cargo["Gold"]);
    }

    [Fact]
    public void State_ExposesNoSetters()
    {
        var session = Attach(new PluginBridge(_bus, _state), "a.state", PluginCapabilities.State);
        var type = session.State.GetType();
        Assert.All(type.GetProperties(), p => Assert.Null(p.GetSetMethod(nonPublic: false)));
        Assert.All(session.State.Snapshot().GetType().GetProperties(), p => Assert.Null(p.GetSetMethod(nonPublic: false)));
        Assert.IsNotType<CommanderState>(session.State);
    }

    [Fact]
    public void Snapshot_IsPointInTime()
    {
        var session = Attach(new PluginBridge(_bus, _state), "a.state", PluginCapabilities.State);
        Publish(Jump);
        var snapshot = session.State.Snapshot();

        Publish("""{"timestamp":"2026-09-26T10:05:00Z","event":"FSDJump","StarSystem":"Achenar"}""");

        Assert.Equal("Sol", snapshot.StarSystem);
        Assert.Equal("Achenar", session.State.StarSystem);
        Assert.Same(snapshot, snapshot.Snapshot());
    }

    // ---- Events: dispatch, isolation, unsubscribe ----

    [Fact]
    public void Events_DeliverAdaptedEvents_WithHistoricalFlag_AfterStateIsUpdated()
    {
        var session = Attach(new PluginBridge(_bus, _state), "a.events", PluginCapabilities.Events, PluginCapabilities.State);
        var received = new BlockingCollection<(IJournalEvent Event, string? SystemInState)>();
        session.Events.Subscribe("FSDJump", e => received.Add((e, session.State.StarSystem)));

        Publish(Jump, historical: true);

        Assert.True(received.TryTake(out var got, Wait));
        Assert.Equal("FSDJump", got.Event.Event);
        Assert.True(got.Event.IsHistorical);
        Assert.False(got.Event.IsSimulated);
        Assert.Equal("Sol", got.Event.GetString("StarSystem"));
        Assert.Null(got.Event.GetString("NoSuchField"));
        Assert.Equal(new DateTimeOffset(2026, 9, 26, 10, 0, 0, TimeSpan.Zero), got.Event.Timestamp);
        Assert.Equal("Sol", got.SystemInState);
        Assert.IsNotType<JournalEntry>(got.Event);
    }

    [Fact]
    public void Events_NameMatchingIsOrdinal()
    {
        var session = Attach(new PluginBridge(_bus, _state), "a.events", PluginCapabilities.Events);
        var hits = new BlockingCollection<string>();
        session.Events.Subscribe("fsdjump", e => hits.Add("wrong-case"));
        session.Events.Subscribe("FSDJump", e => hits.Add("exact"));

        Publish(Jump);

        Assert.True(hits.TryTake(out var first, Wait));
        Assert.Equal("exact", first);
        DrainAndAssertIdle(session);
        Assert.Empty(hits);
    }

    [Fact]
    public void ThrowingHandler_IsReportedAndIsolated_FromItsSiblingsAndOtherPlugins()
    {
        var errors = new BlockingCollection<PluginHandlerError>();
        var bridge = new PluginBridge(_bus, _state, new PluginBridgeOptions { HandlerError = errors.Add });
        var bad = Attach(bridge, "bad.plugin", PluginCapabilities.Events);
        var good = Attach(bridge, "good.plugin", PluginCapabilities.Events);

        var badSibling = new BlockingCollection<string>();
        var goodSeen = new BlockingCollection<string>();
        bad.Events.SubscribeAny(_ => throw new InvalidOperationException("boom"));
        bad.Events.SubscribeAny(e => badSibling.Add(e.Event));
        good.Events.SubscribeAny(e => goodSeen.Add(e.Event));

        var engineSaw = 0;
        _bus.SubscribeAny(_ => engineSaw++);

        Publish(Jump);
        Publish(Cargo);

        Assert.True(errors.TryTake(out var error, Wait));
        Assert.Equal("bad.plugin", error.PluginId);
        Assert.Equal("FSDJump", error.EventName);
        Assert.IsType<InvalidOperationException>(error.Exception);

        Assert.Equal(new[] { "FSDJump", "Cargo" }, TakeN(badSibling, 2));
        Assert.Equal(new[] { "FSDJump", "Cargo" }, TakeN(goodSeen, 2));
        Assert.Equal(2, engineSaw);
        Assert.Equal("Sol", _state.StarSystem);
        Assert.True(SpinWait.SpinUntil(() => bad.HandlerErrorCount == 2, Wait));
        Assert.Equal(0, good.HandlerErrorCount);
    }

    [Fact]
    public async Task BlockedHandler_DoesNotBlockThePumpOrOtherPlugins()
    {
        var bridge = new PluginBridge(_bus, _state);
        var slow = Attach(bridge, "slow.plugin", PluginCapabilities.Events);
        var fast = Attach(bridge, "fast.plugin", PluginCapabilities.Events);

        using var release = new ManualResetEventSlim();
        var slowEntered = new ManualResetEventSlim();
        slow.Events.SubscribeAny(_ => { slowEntered.Set(); release.Wait(Wait); });
        var fastSeen = new BlockingCollection<string>();
        fast.Events.SubscribeAny(e => fastSeen.Add(e.Event));

        var publish = Task.Run(() => { Publish(Jump); Publish(Cargo); });
        var finished = await Task.WhenAny(publish, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.True(finished == publish, "Publish must not wait for plugin handlers.");
        Assert.True(slowEntered.Wait(Wait));
        Assert.Equal(new[] { "FSDJump", "Cargo" }, TakeN(fastSeen, 2));
        Assert.Equal(1, slow.PendingEventCount);   // Cargo waits behind the blocked FSDJump.

        release.Set();
        Assert.True(SpinWait.SpinUntil(() => slow.PendingEventCount == 0, Wait));
    }

    [Fact]
    public void Dispose_OnSubscription_StopsDelivery_AndIsIdempotent()
    {
        var session = Attach(new PluginBridge(_bus, _state), "a.events", PluginCapabilities.Events);
        var removable = new BlockingCollection<string>();
        var kept = new BlockingCollection<string>();
        var subscription = session.Events.On("FSDJump", e => removable.Add(e.Event));
        session.Events.OnAny(e => kept.Add(e.Event));

        Publish(Jump);
        Assert.True(removable.TryTake(out _, Wait));
        Assert.True(kept.TryTake(out _, Wait));

        subscription.Dispose();
        subscription.Dispose();
        Publish(Jump);

        Assert.True(kept.TryTake(out _, Wait));
        DrainAndAssertIdle(session);
        Assert.Empty(removable);
    }

    [Fact]
    public void DisposingTheSession_UnhooksFromTheBus_AndStopsItsThread()
    {
        var session = Attach(new PluginBridge(_bus, _state), "a.events", PluginCapabilities.Events);
        var seen = 0;
        session.Events.SubscribeAny(_ => Interlocked.Increment(ref seen));
        Publish(Jump);
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref seen) == 1, Wait));

        session.Dispose();
        Assert.True(session.WaitForDispatchExit(Wait));
        Publish(Jump);

        Assert.Equal(1, Volatile.Read(ref seen));
        Assert.Throws<ObjectDisposedException>(() => session.Events.SubscribeAny(_ => { }));
    }

    [Fact]
    public void FullQueue_DropsOldest_WithoutBlockingThePump()
    {
        var bridge = new PluginBridge(_bus, _state, new PluginBridgeOptions { QueueCapacity = 2 });
        var session = Attach(bridge, "slow.plugin", PluginCapabilities.Events);
        using var release = new ManualResetEventSlim();
        var entered = new ManualResetEventSlim();
        var seen = new BlockingCollection<string?>();
        session.Events.SubscribeAny(e =>
        {
            entered.Set();
            release.Wait(Wait);
            seen.Add(e.GetString("StarSystem"));
        });

        Publish("""{"timestamp":"2026-09-26T10:00:00Z","event":"FSDJump","StarSystem":"A"}""");
        Assert.True(entered.Wait(Wait));
        foreach (var system in new[] { "B", "C", "D" })
            Publish($$"""{"timestamp":"2026-09-26T10:00:00Z","event":"FSDJump","StarSystem":"{{system}}"}""");

        Assert.Equal(1, session.DroppedEventCount);
        release.Set();
        Assert.Equal(new[] { "A", "C", "D" }, TakeN(seen, 3));
    }

    // ---- Capability gating ----

    [Fact]
    public void WithoutEventsCapability_SubscriptionIsDenied()
    {
        var session = Attach(new PluginBridge(_bus, _state), "no.events", PluginCapabilities.State);

        var ex = Assert.Throws<UnauthorizedAccessException>(() => session.Events.Subscribe("FSDJump", _ => { }));
        Assert.Contains("no.events", ex.Message);
        Assert.Contains(PluginCapabilities.Events, ex.Message);
        Assert.Throws<UnauthorizedAccessException>(() => session.Events.SubscribeAny(_ => { }));
        Assert.Throws<UnauthorizedAccessException>(() => session.Events.On("FSDJump", _ => { }));
        Assert.Throws<UnauthorizedAccessException>(() => session.Events.OnAny(_ => { }));
    }

    [Fact]
    public void WithoutStateCapability_EveryReadIsDenied()
    {
        var session = Attach(new PluginBridge(_bus, _state), "no.state", PluginCapabilities.Events);
        var state = session.State;

        Assert.Throws<UnauthorizedAccessException>(() => state.Name);
        Assert.Throws<UnauthorizedAccessException>(() => state.StarSystem);
        Assert.Throws<UnauthorizedAccessException>(() => state.Body);   // a DIM member on the SDK side
        Assert.Throws<UnauthorizedAccessException>(() => state.Cargo);
        Assert.Throws<UnauthorizedAccessException>(() => state.Snapshot());
    }

    [Fact]
    public void GrantedCapabilities_NarrowTheDeclaredSet_AndCannotWidenIt()
    {
        var bridge = new PluginBridge(_bus, _state);
        var revoked = bridge.Attach(Manifest("revoked", PluginCapabilities.Events, PluginCapabilities.State), [PluginCapabilities.Events]);
        var widened = bridge.Attach(Manifest("widened", PluginCapabilities.Events), [PluginCapabilities.Events, PluginCapabilities.State]);
        _sessions.AddRange([revoked, widened]);

        Assert.Throws<UnauthorizedAccessException>(() => revoked.State.Name);
        Assert.Throws<UnauthorizedAccessException>(() => widened.State.Name);
        revoked.Events.SubscribeAny(_ => { });
    }

    // ---- Developer mode ----

    [Fact]
    public void DeveloperMode_FlagsEvents_AndWithholdsThemFromNetworkPlugins()
    {
        var simulated = true;
        var bridge = new PluginBridge(_bus, _state, new PluginBridgeOptions { IsSimulated = () => simulated });
        var local = Attach(bridge, "local.plugin", PluginCapabilities.Events, PluginCapabilities.State);
        var networked = Attach(bridge, "net.plugin", PluginCapabilities.Events, PluginCapabilities.State, PluginCapabilities.Network);

        var localSeen = new BlockingCollection<IJournalEvent>();
        var netSeen = new BlockingCollection<IJournalEvent>();
        local.Events.SubscribeAny(localSeen.Add);
        networked.Events.SubscribeAny(netSeen.Add);

        Publish(Jump);

        Assert.True(localSeen.TryTake(out var localEvent, Wait));
        Assert.True(localEvent.IsSimulated);
        Assert.Equal("Sol", local.State.StarSystem);
        Assert.Null(networked.State.StarSystem);          // fabricated state is hidden too
        Assert.Empty(networked.State.Cargo);
        DrainAndAssertIdle(networked);
        Assert.Empty(netSeen);

        simulated = false;
        Publish(Jump);
        Assert.True(netSeen.TryTake(out var live, Wait));
        Assert.False(live.IsSimulated);
        Assert.Equal("Sol", networked.State.StarSystem);
    }

    [Fact]
    public void ThrowingDeveloperModePredicate_FailsClosed()
    {
        var bridge = new PluginBridge(_bus, _state, new PluginBridgeOptions { IsSimulated = () => throw new InvalidOperationException() });
        var networked = Attach(bridge, "net.plugin", PluginCapabilities.Events, PluginCapabilities.State, PluginCapabilities.Network);
        var seen = new BlockingCollection<IJournalEvent>();
        networked.Events.SubscribeAny(seen.Add);

        Publish(Jump);

        DrainAndAssertIdle(networked);
        Assert.Empty(seen);
        Assert.Null(networked.State.StarSystem);
    }

    // ---- Adapter ----

    [Fact]
    public void Adapter_PrefersLocalised_AndDeserializeNeverThrows()
    {
        Assert.True(JournalEntry.TryParse(
            """{"timestamp":"2026-09-26T10:00:00Z","event":"Died","KillerShip":"python","KillerShip_Localised":"Python","Count":"nope"}""",
            false, out var entry));
        IJournalEvent adapted = new JournalEventAdapter(entry, isSimulated: false);

        Assert.Equal("Python", adapted.GetLocalised("KillerShip"));
        Assert.Null(adapted.GetInt64("Count"));
        Assert.Null(adapted.GetString(null!));
        Assert.Null(adapted.Deserialize<ShapeWithNumber>());
    }

    private sealed record ShapeWithNumber(int Count);

    // ---- Engine bus hook ----

    [Fact]
    public void BusCompletedHook_RunsAfterEngineHandlers_AndDisposes()
    {
        var bus = new JournalEventBus();
        var order = new List<string>();
        var hook = bus.SubscribeCompleted(_ => order.Add("completed"));
        bus.SubscribeAny(_ => order.Add("any"));
        bus.Subscribe("FSDJump", _ => order.Add("named"));

        Assert.True(JournalEntry.TryParse(Jump, false, out var entry));
        bus.Publish(entry);
        hook.Dispose();
        hook.Dispose();
        bus.Publish(entry);

        Assert.Equal(new[] { "any", "named", "completed", "any", "named" }, order);
    }

    // ---- helpers ----

    private static string[] TakeN<T>(BlockingCollection<T> source, int count)
    {
        var items = new List<string>();
        for (var i = 0; i < count; i++)
        {
            Assert.True(source.TryTake(out var item, Wait), $"Timed out waiting for item {i + 1} of {count}.");
            items.Add(item?.ToString() ?? "<null>");
        }
        return [.. items];
    }

    /// <summary>
    /// Waits until everything published so far has been through the plugin's queue, by publishing a
    /// sentinel and waiting for a probe handler to see it. Delivery is in order, so anything that
    /// should have arrived before the sentinel has.
    /// </summary>
    private void DrainAndAssertIdle(PluginBridgeSession session)
    {
        using var seen = new ManualResetEventSlim();
        using var probe = session.Events.On("DrainProbe", _ => seen.Set());
        var droppedBefore = session.DroppedEventCount;
        Publish("""{"timestamp":"2026-09-26T10:00:00Z","event":"DrainProbe"}""");
        // Network plugins never see the sentinel while simulated; fall back to an idle queue.
        Assert.True(seen.Wait(TimeSpan.FromMilliseconds(500)) || session.PendingEventCount == 0);
        Assert.Equal(droppedBefore, session.DroppedEventCount);
    }
}
