namespace EDNexus.Plugins.Abstractions.Tests;

public class PluginManifestTests
{
    private static PluginManifest Make(params string[] capabilities)
        => new("com.acme.x", "X", "1.0.0", "1.0", "Acme", "Desc")
        {
            MinAppVersion = "0.1.0",
            EntryAssembly = "X.dll",
            EntryType = "X.Plugin",
            Capabilities = capabilities,
        };

    [Fact]
    public void Capabilities_AreCopiedOnInit_SoTheCallersListCannotChangeThem()
    {
        var source = new List<string> { PluginCapabilities.Events };
        var manifest = new PluginManifest("a.b", "N", "1.0.0", "1.0") { Capabilities = source };

        source.Add(PluginCapabilities.Network);

        Assert.Equal([PluginCapabilities.Events], manifest.Capabilities);
        Assert.False(manifest.Declares(PluginCapabilities.Network));
    }

    [Fact]
    public void Capabilities_CannotBeMutatedByDowncasting()
    {
        // A plugin receives this record via IPluginContext.Manifest; it must not be able to grant
        // itself a capability by casting the list back to something writable.
        var manifest = Make(PluginCapabilities.Events);

        Assert.IsNotType<List<string>>(manifest.Capabilities);
        Assert.IsNotType<string[]>(manifest.Capabilities);
        var asCollection = Assert.IsAssignableFrom<ICollection<string>>(manifest.Capabilities);
        Assert.True(asCollection.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => asCollection.Add(PluginCapabilities.Network));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)manifest.Capabilities)[0] = PluginCapabilities.Network);
        Assert.False(manifest.Declares(PluginCapabilities.Network));
    }

    [Fact]
    public void Capabilities_DefaultAndNull_AreEmpty()
    {
        Assert.Empty(new PluginManifest("a.b", "N", "1.0.0", "1.0").Capabilities);
        Assert.Empty(new PluginManifest("a.b", "N", "1.0.0", "1.0") { Capabilities = null! }.Capabilities);
    }

    [Fact]
    public void Equality_ComparesCapabilitiesByValue()
    {
        var a = Make(PluginCapabilities.Events, PluginCapabilities.State);
        var b = Make(PluginCapabilities.Events, PluginCapabilities.State);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equality_DetectsDifferentCapabilities()
    {
        Assert.NotEqual(Make(PluginCapabilities.Events), Make(PluginCapabilities.Events, PluginCapabilities.Network));
        Assert.NotEqual(Make(PluginCapabilities.Events), Make(PluginCapabilities.State));
    }

    [Fact]
    public void Equality_StillCoversEveryOtherField()
    {
        var baseline = Make(PluginCapabilities.Events);

        Assert.NotEqual(baseline, baseline with { Id = "com.acme.y" });
        Assert.NotEqual(baseline, baseline with { Author = null });
        Assert.NotEqual(baseline, baseline with { MinAppVersion = "9.9.9" });
        Assert.NotEqual(baseline, baseline with { EntryAssembly = "Y.dll" });
        Assert.NotEqual(baseline, baseline with { EntryType = "Y.Plugin" });
        Assert.Equal(baseline, baseline with { });
    }
}
