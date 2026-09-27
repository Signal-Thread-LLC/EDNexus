namespace EDNexus.Plugins.Abstractions.Tests;

public class ReadOnlyCommanderStateTests
{
    // A minimal implementation exercising only the required members, to prove the
    // optional properties' default interface implementations kick in without an override.
    private sealed class MinimalState : IReadOnlyCommanderState
    {
        public string? Name => "CMDR Test";
        public long Balance => 1000;
        public string? Ship => "sidewinder";
        public string? StarSystem => "Sol";
        public bool Docked => false;
        public DateTimeOffset LastUpdated => DateTimeOffset.UnixEpoch;
    }

    [Fact]
    public void OptionalMembers_DefaultToNull_WhenNotOverridden()
    {
        IReadOnlyCommanderState state = new MinimalState();

        Assert.Null(state.ShipName);
        Assert.Null(state.Body);
        Assert.Null(state.StationDisplayName);
    }

    [Fact]
    public void RequiredMembers_ReturnImplementedValues()
    {
        IReadOnlyCommanderState state = new MinimalState();

        Assert.Equal("CMDR Test", state.Name);
        Assert.Equal(1000, state.Balance);
        Assert.Equal("sidewinder", state.Ship);
        Assert.Equal("Sol", state.StarSystem);
        Assert.False(state.Docked);
        Assert.Equal(DateTimeOffset.UnixEpoch, state.LastUpdated);
    }

    [Fact]
    public void AddedMembers_HaveSafeDefaults_ForOlderImplementations()
    {
        IReadOnlyCommanderState state = new MinimalState();

        Assert.Empty(state.Cargo);
        Assert.Empty(state.RawMaterials);
        Assert.Empty(state.ManufacturedMaterials);
        Assert.Empty(state.EncodedMaterials);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, int>)state.Cargo).Add("gold", 1));
        Assert.Same(state, state.Snapshot());
    }

    [Fact]
    public void JournalEventAndEvents_AddedMembers_HaveSafeDefaults()
    {
        IJournalEvent journalEvent = new FakeJournalEvent("FSDJump", DateTimeOffset.UnixEpoch, isHistorical: false);
        IPluginEvents events = new FakePluginEvents();

        Assert.False(journalEvent.IsSimulated);
        Assert.Throws<NotSupportedException>(() => events.On("FSDJump", _ => { }));
        Assert.Throws<NotSupportedException>(() => events.OnAny(_ => { }));
    }
}
