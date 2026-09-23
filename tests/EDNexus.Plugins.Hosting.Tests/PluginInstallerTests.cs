using static EDNexus.Plugins.Hosting.Tests.TestPackages;

namespace EDNexus.Plugins.Hosting.Tests;

public class PluginInstallerTests
{
    [Fact]
    public void Install_ValidPackage_LandsInRootSlashId()
    {
        using var dir = new TempDir();
        var package = dir.Write("jump.ednplugin", Valid(("lib/Dep.dll", Dll)));
        var root = Path.Combine(dir.Path, "plugins"); // created on demand

        var result = PluginInstaller.Install(package, root);

        Assert.True(result.Succeeded, result.ErrorSummary);
        Assert.Equal(Path.Combine(root, "com.acme.jumpcounter"), result.Directory);
        Assert.Equal("com.acme.jumpcounter", result.Manifest!.Id);
        Assert.True(File.Exists(Path.Combine(root, "com.acme.jumpcounter", "plugin.json")));
        Assert.True(File.Exists(Path.Combine(root, "com.acme.jumpcounter", "lib", "Dep.dll")));
        // No staging or backup folders are left behind.
        Assert.Equal(["com.acme.jumpcounter"], Directory.GetDirectories(root).Select(Path.GetFileName));
    }

    [Fact]
    public void Install_InstalledManifest_RoundTripsThroughTheParser()
    {
        using var dir = new TempDir();
        var root = Path.Combine(dir.Path, "plugins");
        var result = PluginInstaller.Install(dir.Write("p.ednplugin", Valid()), root);

        var reparsed = PluginManifestParser.ParseFile(Path.Combine(result.Directory!, PluginManifestParser.FileName));

        Assert.True(reparsed.IsValid, reparsed.ErrorSummary);
        Assert.Equal(result.Manifest!.Id, reparsed.Manifest!.Id);
    }

    [Fact]
    public void Install_DuplicateId_IsRejectedAndLeavesTheOriginalUntouched()
    {
        using var dir = new TempDir();
        var root = Path.Combine(dir.Path, "plugins");
        Assert.True(PluginInstaller.Install(dir.Write("v1.ednplugin", Valid(("v1.txt", [1]))), root).Succeeded);

        var second = PluginInstaller.Install(dir.Write("v2.ednplugin", Valid(("v2.txt", [2]))), root);

        Assert.False(second.Succeeded);
        Assert.Contains("already installed", second.ErrorSummary);
        Assert.True(File.Exists(Path.Combine(root, "com.acme.jumpcounter", "v1.txt")));
        Assert.False(File.Exists(Path.Combine(root, "com.acme.jumpcounter", "v2.txt")));
    }

    [Fact]
    public void Install_ReplaceExisting_SwapsInTheNewVersion()
    {
        using var dir = new TempDir();
        var root = Path.Combine(dir.Path, "plugins");
        Assert.True(PluginInstaller.Install(dir.Write("v1.ednplugin", Valid(("v1.txt", [1]))), root).Succeeded);

        var second = PluginInstaller.Install(dir.Write("v2.ednplugin", Valid(("v2.txt", [2]))), root, replaceExisting: true);

        Assert.True(second.Succeeded, second.ErrorSummary);
        Assert.False(File.Exists(Path.Combine(root, "com.acme.jumpcounter", "v1.txt")));
        Assert.True(File.Exists(Path.Combine(root, "com.acme.jumpcounter", "v2.txt")));
        Assert.Equal(["com.acme.jumpcounter"], Directory.GetDirectories(root).Select(Path.GetFileName));
    }

    [Fact]
    public void Install_MaliciousPackage_WritesNothing()
    {
        using var dir = new TempDir();
        var root = Path.Combine(dir.Path, "nested", "plugins");
        Directory.CreateDirectory(root);
        var package = dir.Write("evil.ednplugin", Valid(("../../pwned.dll", Dll), ("lib/ok.dll", Dll)));

        var result = PluginInstaller.Install(package, root);

        Assert.False(result.Succeeded);
        Assert.Null(result.Directory);
        Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        Assert.Empty(Directory.EnumerateFiles(dir.Path, "pwned.dll", SearchOption.AllDirectories));
    }

    [Fact]
    public void Install_ManifestIdWithTraversal_CannotEscapeTheRoot()
    {
        using var dir = new TempDir();
        var root = Path.Combine(dir.Path, "plugins");
        var zip = Zip([("plugin.json", Utf8(Manifest(id: "../../escape"))), ("Acme.JumpCounter.dll", Dll)]);

        var result = PluginInstaller.Install(dir.Write("evil.ednplugin", zip), root);

        Assert.False(result.Succeeded);
        Assert.Contains("'id'", result.ErrorSummary);
        Assert.False(Directory.Exists(Path.Combine(dir.Path, "escape")));
    }

    [Fact]
    public void Install_MissingPackage_FailsCleanly()
    {
        using var dir = new TempDir();
        var result = PluginInstaller.Install(Path.Combine(dir.Path, "nope.ednplugin"), Path.Combine(dir.Path, "plugins"));

        Assert.False(result.Succeeded);
        Assert.Contains("does not exist", result.ErrorSummary);
    }
}
