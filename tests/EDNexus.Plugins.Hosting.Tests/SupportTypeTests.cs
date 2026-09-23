using EDNexus.Plugins.Abstractions;

namespace EDNexus.Plugins.Hosting.Tests;

public class SemanticVersionTests
{
    [Fact]
    public void Parse_ExposesComponents()
    {
        var v = SemanticVersion.Parse("1.2.3-beta.4+build.5");

        Assert.Equal(1, v.Major);
        Assert.Equal(2, v.Minor);
        Assert.Equal(3, v.Patch);
        Assert.Equal("beta.4", v.PreRelease);
        Assert.Equal("build.5", v.Build);
        Assert.True(v.IsPreRelease);
        Assert.Equal("1.2.3-beta.4+build.5", v.ToString());
    }

    [Fact]
    public void Parse_Invalid_Throws_TryParse_DoesNot()
    {
        Assert.Throws<FormatException>(() => SemanticVersion.Parse("1.0"));
        Assert.False(SemanticVersion.TryParse(null, out var v));
        Assert.Null(v);
        Assert.False(SemanticVersion.TryParse(new string('1', 500) + ".0.0", out _));
    }

    [Fact]
    public void Precedence_FollowsSemVerSpec()
    {
        // The example ordering from semver.org §11.
        string[] ordered =
        [
            "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2",
            "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0", "1.0.1", "1.1.0", "2.0.0",
        ];

        for (var i = 1; i < ordered.Length; i++)
            Assert.True(SemanticVersion.Parse(ordered[i - 1]) < SemanticVersion.Parse(ordered[i]), $"{ordered[i - 1]} < {ordered[i]}");
    }

    [Fact]
    public void BuildMetadata_IsIgnoredForEquality()
    {
        Assert.Equal(SemanticVersion.Parse("1.0.0+a"), SemanticVersion.Parse("1.0.0+b"));
        Assert.True(SemanticVersion.Parse("1.0.0+a") >= SemanticVersion.Parse("1.0.0"));
    }
}

public class PluginPathRulesTests
{
    [Theory]
    [InlineData("plugin.json")]
    [InlineData("lib/net10.0/Dep.dll")]
    [InlineData("runtimes/linux-x64/native/libfoo.so")]
    [InlineData(".hidden")]
    [InlineData("a b/c.txt")]
    public void SafePaths_AreAccepted(string path)
        => Assert.Null(PluginPathRules.CheckRelativePath(path));

    [Theory]
    [InlineData("")]
    [InlineData("/etc/passwd")]
    [InlineData("//server/share/x")]
    [InlineData("../x")]
    [InlineData("a/../../x")]
    [InlineData("a/./x")]
    [InlineData("a//x")]
    [InlineData("a\\x")]
    [InlineData("C:/x")]
    [InlineData("x:ads")]
    [InlineData("a/b.")]
    [InlineData("a/b ")]
    [InlineData(" a")]
    [InlineData("CON")]
    [InlineData("lib/nul.dll")]
    [InlineData("COM1.txt")]
    [InlineData("lpt9")]
    [InlineData("a*b")]
    [InlineData("a?b")]
    [InlineData("a|b")]
    [InlineData("a\u0000b")]
    [InlineData("a\nb")]
    public void UnsafePaths_AreRejected(string path)
        => Assert.NotNull(PluginPathRules.CheckRelativePath(path));

    [Fact]
    public void TooLongOrTooDeep_IsRejected()
    {
        Assert.NotNull(PluginPathRules.CheckRelativePath(new string('a', PluginPathRules.MaxRelativePathLength + 1)));
        Assert.NotNull(PluginPathRules.CheckRelativePath(string.Join('/', Enumerable.Repeat("a", PluginPathRules.MaxDepth + 1))));
    }

    [Fact]
    public void TrailingSlash_OnlyAllowedWhenRequested()
    {
        Assert.Null(PluginPathRules.CheckRelativePath("lib/", allowTrailingSlash: true));
        Assert.NotNull(PluginPathRules.CheckRelativePath("lib/"));
        Assert.NotNull(PluginPathRules.CheckRelativePath("/", allowTrailingSlash: true));
    }

    [Fact]
    public void Normalize_TurnsBackslashTraversalIntoDetectableForm()
    {
        var normalized = PluginPathRules.Normalize("..\\..\\evil.dll");
        Assert.Equal("../../evil.dll", normalized);
        Assert.Contains("traversal", PluginPathRules.CheckRelativePath(normalized));
    }

    [Fact]
    public void ResolveInside_ContainsPathsToTheRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "root");

        Assert.Equal(Path.Combine(root, "a", "b.dll"), PluginPathRules.ResolveInside(root, "a/b.dll"));
        Assert.Null(PluginPathRules.ResolveInside(root, "../evil.dll"));
        Assert.Null(PluginPathRules.ResolveInside(root, "a/../../evil.dll"));
        Assert.Null(PluginPathRules.ResolveInside(root, ""));
        Assert.Null(PluginPathRules.ResolveInside(root, "../root-sibling/x.dll")); // prefix-match trap
        var absolute = Path.Combine(Path.GetTempPath(), "elsewhere", "x.dll");
        Assert.Null(PluginPathRules.ResolveInside(root, absolute));
    }
}

public class PluginCompatibilityTests
{
    private static PluginManifest Manifest(string sdk = "1.0", string? minApp = null)
        => new("a.b", "N", "1.0.0", sdk) { MinAppVersion = minApp, EntryAssembly = "A.dll", EntryType = "A.P" };

    private static readonly SemanticVersion App = SemanticVersion.Parse("1.5.0");

    [Fact]
    public void CurrentSdk_IsCompatible()
        => Assert.Null(PluginCompatibility.Check(Manifest(PluginSdk.CurrentVersionString), App));

    [Theory]
    [InlineData("1.0", 1, 0, true)]
    [InlineData("1.0", 1, 3, true)]   // host has a newer minor — additive, fine
    [InlineData("1.4", 1, 3, false)]  // plugin needs members the host lacks
    [InlineData("2.0", 1, 9, false)]  // breaking major
    [InlineData("0.9", 1, 0, false)]
    public void SdkVersion_IsGatedByMajorAndMinor(string declared, int hostMajor, int hostMinor, bool compatible)
    {
        var reason = PluginCompatibility.Check(Manifest(declared), App, new Version(hostMajor, hostMinor));
        Assert.Equal(compatible, reason is null);
        if (!compatible) Assert.Contains("SDK", reason);
    }

    [Theory]
    [InlineData("1.5.0", true)]
    [InlineData("1.4.9", true)]
    [InlineData("1.5.1", false)]
    [InlineData("1.5.0-rc.1", true)]
    [InlineData("2.0.0", false)]
    public void MinAppVersion_IsEnforced(string minApp, bool compatible)
    {
        var reason = PluginCompatibility.Check(Manifest(minApp: minApp), App);
        Assert.Equal(compatible, reason is null);
        if (!compatible) Assert.Contains("requires EDNexus", reason);
    }

    [Fact]
    public void UnreadableVersions_AreReportedNotThrown()
    {
        Assert.Contains("unreadable SDK", PluginCompatibility.Check(Manifest("banana"), App));
        Assert.Contains("unreadable minimum", PluginCompatibility.Check(Manifest(minApp: "soon"), App));
    }
}

public class PluginPathsTests
{
    [Fact]
    public void DefaultRoot_IsUnderUserAppData_NotTheInstallDirectory()
    {
        var root = PluginPaths.DefaultRoot();

        Assert.NotNull(root);
        Assert.True(Path.IsPathFullyQualified(root));
        Assert.Equal("plugins", Path.GetFileName(root));
        Assert.Equal("EDNexus", Path.GetFileName(Path.GetDirectoryName(root)));
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(appData))
            Assert.StartsWith(Path.GetFullPath(appData), root, StringComparison.OrdinalIgnoreCase);
        Assert.False(root.StartsWith(Path.GetFullPath(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Resolve_UsesEnvOverride_EvenIfItDoesNotExistYet()
    {
        var custom = Path.Combine(Path.GetTempPath(), "ednexus-dev-plugins-" + Guid.NewGuid().ToString("N"));

        var root = PluginPaths.Resolve(name => name == PluginPaths.OverrideEnvVar ? custom : null);

        Assert.Equal(Path.GetFullPath(custom), root);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_BlankOverride_FallsBackToDefault(string? value)
        => Assert.Equal(PluginPaths.DefaultRoot(), PluginPaths.Resolve(_ => value));

    [Fact]
    public void Resolve_RelativeOverride_IsMadeAbsolute()
    {
        var root = PluginPaths.Resolve(_ => "dev-plugins");
        Assert.True(Path.IsPathFullyQualified(root!));
    }

    [Fact]
    public void PluginDirectory_IsRootPlusId_AndRejectsBadIds()
    {
        var root = Path.Combine(Path.GetTempPath(), "plugins");

        Assert.Equal(Path.Combine(root, "com.acme.x"), PluginPaths.PluginDirectory(root, "com.acme.x"));
        Assert.Throws<ArgumentException>(() => PluginPaths.PluginDirectory(root, "../evil"));
        Assert.Throws<ArgumentException>(() => PluginPaths.PluginDirectory(root, "Com.Acme"));
    }
}
