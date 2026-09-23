using static EDNexus.Plugins.Hosting.Tests.TestPackages;

namespace EDNexus.Plugins.Hosting.Tests;

public class PluginInstallerTests(Xunit.Abstractions.ITestOutputHelper output)
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

    private const string Id = "com.acme.jumpcounter";

    /// <summary>Directory.Move, except calls matching <paramref name="fail"/> throw.</summary>
    private static Action<string, string> FailingMove(Func<string, string, bool> fail) => (from, to) =>
    {
        if (fail(from, to)) throw new IOException($"simulated failure moving '{Path.GetFileName(from)}'");
        Directory.Move(from, to);
    };

    private static bool IsStaging(string path) => Path.GetFileName(path).StartsWith(PluginInstaller.StagingPrefix, StringComparison.Ordinal);

    [Fact]
    public void Install_ReplaceFailsMidSwap_RollsBackToThePreviousVersion()
    {
        using var dir = new TempDir();
        var root = Path.Combine(dir.Path, "plugins");
        Assert.True(PluginInstaller.Install(dir.Write("v1.ednplugin", Valid(("v1.txt", [1]))), root).Succeeded);

        // Old version moves out fine; moving the new one in fails.
        var result = PluginInstaller.InstallCore(dir.Write("v2.ednplugin", Valid(("v2.txt", [2]))), root,
            replaceExisting: true, limits: null, FailingMove((from, _) => IsStaging(from)));

        Assert.False(result.Succeeded);
        Assert.Contains("could not move plugin into place", result.ErrorSummary);
        Assert.DoesNotContain("preserved at", result.ErrorSummary);
        Assert.True(File.Exists(Path.Combine(root, Id, "v1.txt")));
        Assert.False(File.Exists(Path.Combine(root, Id, "v2.txt")));
        Assert.Equal([Id], Directory.GetDirectories(root).Select(Path.GetFileName)); // no staging/backup left
    }

    [Fact]
    public void Install_RollbackAlsoFails_ReportsBackupPath_AndRecoveryRestoresIt()
    {
        using var dir = new TempDir();
        var root = Path.Combine(dir.Path, "plugins");
        Assert.True(PluginInstaller.Install(dir.Write("v1.ednplugin", Valid(("v1.txt", [1]))), root).Succeeded);
        var target = Path.Combine(root, Id);

        // Everything after the first move (old version -> backup) fails, including the restore.
        var result = PluginInstaller.InstallCore(dir.Write("v2.ednplugin", Valid(("v2.txt", [2]))), root,
            replaceExisting: true, limits: null, FailingMove((from, _) => from != target));

        Assert.False(result.Succeeded);
        Assert.Contains("preserved at", result.ErrorSummary);
        Assert.False(Directory.Exists(target));
        var backup = Assert.Single(Directory.GetDirectories(root));
        Assert.Contains(backup, result.ErrorSummary);
        Assert.True(File.Exists(Path.Combine(backup, "v1.txt")));

        var recovery = PluginInstaller.RecoverInterrupted(root);

        Assert.Equal([Id], recovery.Restored);
        Assert.Empty(recovery.Errors);
        Assert.True(File.Exists(Path.Combine(target, "v1.txt")));
        Assert.Equal([Id], Directory.GetDirectories(root).Select(Path.GetFileName));
    }

    [Fact]
    public void RecoverInterrupted_AfterCrashBetweenRenames_RestoresBackupAndDeletesStaging()
    {
        using var dir = new TempDir();
        var root = Path.Combine(dir.Path, "plugins");
        Assert.True(PluginInstaller.Install(dir.Write("v1.ednplugin", Valid(("v1.txt", [1]))), root).Succeeded);

        // Simulate a crash after "<id> -> backup" but before "staging -> <id>".
        Directory.Move(Path.Combine(root, Id), Path.Combine(root, PluginInstaller.BackupName(Id)));
        var staging = Path.Combine(root, PluginInstaller.StagingPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(staging, "lib"));
        File.WriteAllText(Path.Combine(staging, "lib", "half-written.dll"), "x");

        var recovery = PluginInstaller.RecoverInterrupted(root);

        Assert.Equal([Id], recovery.Restored);
        Assert.Contains(Path.GetFileName(staging), recovery.Removed);
        Assert.Empty(recovery.Errors);
        Assert.True(File.Exists(Path.Combine(root, Id, "v1.txt")));
        Assert.Equal([Id], Directory.GetDirectories(root).Select(Path.GetFileName));
    }

    [Fact]
    public void RecoverInterrupted_AfterCrashBeforeBackupCleanup_KeepsNewVersionAndDropsBackup()
    {
        using var dir = new TempDir();
        var root = Path.Combine(dir.Path, "plugins");
        Assert.True(PluginInstaller.Install(dir.Write("v2.ednplugin", Valid(("v2.txt", [2]))), root).Succeeded);
        var backup = Path.Combine(root, PluginInstaller.BackupName(Id));
        Directory.CreateDirectory(backup);
        File.WriteAllText(Path.Combine(backup, "v1.txt"), "old");

        var recovery = PluginInstaller.RecoverInterrupted(root);

        Assert.Empty(recovery.Restored);
        Assert.Contains(Path.GetFileName(backup), recovery.Removed);
        Assert.True(File.Exists(Path.Combine(root, Id, "v2.txt")));
        Assert.False(Directory.Exists(backup));
    }

    [Fact]
    public void RecoverInterrupted_StrayFileAtTarget_KeepsTheBackupAndReportsIt()
    {
        using var dir = new TempDir();
        var root = Path.Combine(dir.Path, "plugins");
        var backup = Path.Combine(root, PluginInstaller.BackupName(Id));
        Directory.CreateDirectory(backup);
        File.WriteAllText(Path.Combine(backup, "v1.txt"), "only copy");
        File.WriteAllText(Path.Combine(root, Id), "stray file, not a plugin folder");

        var recovery = PluginInstaller.RecoverInterrupted(root);

        Assert.Empty(recovery.Restored);
        Assert.Empty(recovery.Removed);
        var error = Assert.Single(recovery.Errors);
        Assert.Contains("a file is in the way", error);
        Assert.Contains(backup, error);
        Assert.Equal("only copy", File.ReadAllText(Path.Combine(backup, "v1.txt")));
        Assert.Equal("stray file, not a plugin folder", File.ReadAllText(Path.Combine(root, Id)));
    }

    [Fact]
    public void RecoverInterrupted_LeavesUnrelatedFoldersAlone()
    {
        using var dir = new TempDir();
        var root = Path.Combine(dir.Path, "plugins");
        string[] untouched = ["com.other.plugin", ".replaced-not-a-backup", ".replaced-Bad.Id." + Guid.NewGuid().ToString("N"), "notes"];
        foreach (var name in untouched)
            Directory.CreateDirectory(Path.Combine(root, name));

        var recovery = PluginInstaller.RecoverInterrupted(root);

        Assert.Empty(recovery.Restored);
        Assert.Empty(recovery.Removed);
        Assert.Equal(untouched.Order(StringComparer.Ordinal), Directory.GetDirectories(root).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void RecoverInterrupted_MissingRoot_IsANoOp()
    {
        var recovery = PluginInstaller.RecoverInterrupted(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        Assert.Empty(recovery.Restored);
        Assert.Empty(recovery.Removed);
        Assert.Empty(recovery.Errors);
    }

    [Theory]
    [InlineData("com.acme.x", true)]
    [InlineData("Com.Acme", false)]
    [InlineData("..", false)]
    public void BackupNames_RoundTripTheId(string id, bool valid)
    {
        if (valid)
            Assert.Equal(id, PluginInstaller.TryParseBackupId(PluginInstaller.BackupName(id)));
        else
            Assert.Null(PluginInstaller.TryParseBackupId(".replaced-" + id + "." + Guid.NewGuid().ToString("N")));
        Assert.Null(PluginInstaller.TryParseBackupId(".replaced-" + id));
        Assert.Null(PluginInstaller.TryParseBackupId(".replaced-" + id + ".not-a-guid"));
        Assert.Null(PluginInstaller.TryParseBackupId(id));
    }

    [Fact]
    public void Install_TargetIsALinkToAnotherFolder_NeverTouchesTheLinkedFolder()
    {
        using var dir = new TempDir();
        var root = Path.Combine(dir.Path, "plugins");
        Directory.CreateDirectory(root);
        var outside = Path.Combine(dir.Path, "precious");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "keep.txt"), "do not delete");
        var target = Path.Combine(root, Id);
        if (!TryCreateDirectoryLink(target, outside))
        {
            output.WriteLine("SKIPPED: directory symlinks/junctions unavailable in this environment");
            return; // e.g. a restricted CI account
        }
        output.WriteLine($"link created: {new DirectoryInfo(target).LinkTarget}");

        var refused = PluginInstaller.Install(dir.Write("a.ednplugin", Valid()), root);
        var replaced = PluginInstaller.Install(dir.Write("b.ednplugin", Valid(("v2.txt", [2]))), root, replaceExisting: true);

        Assert.False(refused.Succeeded);
        Assert.Contains("already installed", refused.ErrorSummary);
        Assert.True(replaced.Succeeded, replaced.ErrorSummary);
        // The link was swapped out, not followed: the linked folder is intact and untouched...
        Assert.Equal("do not delete", File.ReadAllText(Path.Combine(outside, "keep.txt")));
        Assert.False(File.Exists(Path.Combine(outside, "v2.txt")));
        // ...and the plugin is now a real folder.
        Assert.Null(new DirectoryInfo(target).LinkTarget);
        Assert.True(File.Exists(Path.Combine(target, "v2.txt")));
    }

    [Fact]
    public void RecoverInterrupted_StagingThatIsALink_RemovesOnlyTheLink()
    {
        using var dir = new TempDir();
        var root = Path.Combine(dir.Path, "plugins");
        Directory.CreateDirectory(root);
        var outside = Path.Combine(dir.Path, "precious");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "keep.txt"), "do not delete");
        var staging = Path.Combine(root, PluginInstaller.StagingPrefix + Guid.NewGuid().ToString("N"));
        if (!TryCreateDirectoryLink(staging, outside))
        {
            output.WriteLine("SKIPPED: directory symlinks/junctions unavailable in this environment");
            return;
        }
        output.WriteLine($"link created: {new DirectoryInfo(staging).LinkTarget}");

        PluginInstaller.RecoverInterrupted(root);

        Assert.False(Directory.Exists(staging));
        Assert.Equal("do not delete", File.ReadAllText(Path.Combine(outside, "keep.txt")));
    }

    /// <summary>A directory symlink, or on Windows without symlink rights, a junction.</summary>
    private static bool TryCreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
        }

        if (!OperatingSystem.IsWindows())
            return false;
        try
        {
            using var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
            {
                ArgumentList = { "/c", "mklink", "/J", link, target },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            mklink!.WaitForExit(10_000);
            return mklink.ExitCode == 0 && Directory.Exists(link);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
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
