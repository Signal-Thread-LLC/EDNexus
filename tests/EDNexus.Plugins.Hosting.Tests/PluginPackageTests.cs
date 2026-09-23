using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using static EDNexus.Plugins.Hosting.Tests.TestPackages;

namespace EDNexus.Plugins.Hosting.Tests;

public class PluginPackageTests
{
    private static PluginPackageInspection Inspect(byte[] zip, PluginPackageLimits? limits = null)
        => PluginPackage.Inspect(new MemoryStream(zip), limits);

    private static void AssertRejected(PluginPackageInspection result, string expectedFragment)
    {
        Assert.False(result.IsValid);
        Assert.Null(result.Manifest);
        Assert.Empty(result.Entries);
        Assert.Contains(result.Errors, e => e.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Inspect_ValidPackage_ReturnsManifestAndEntries()
    {
        var result = Inspect(Valid(("lib/Dep.dll", Dll), ("lib/", [])));

        Assert.True(result.IsValid, result.ErrorSummary);
        Assert.Equal("com.acme.jumpcounter", result.Manifest!.Id);
        Assert.Contains(result.Entries, e => e.Path == "plugin.json" && !e.IsDirectory);
        Assert.Contains(result.Entries, e => e.Path == "Acme.JumpCounter.dll");
        Assert.Contains(result.Entries, e => e.Path == "lib/Dep.dll");
        Assert.Contains(result.Entries, e => e.Path == "lib/" && e.IsDirectory);
    }

    [Fact]
    public void Inspect_SingleWrappingFolder_IsTreatedAsPluginRoot()
    {
        var zip = Zip([
            ("com.acme.jumpcounter/", []),
            ("com.acme.jumpcounter/plugin.json", Utf8(ValidManifestJson)),
            ("com.acme.jumpcounter/Acme.JumpCounter.dll", Dll),
        ]);

        var result = Inspect(zip);

        Assert.True(result.IsValid, result.ErrorSummary);
        Assert.Equal(["plugin.json", "Acme.JumpCounter.dll"], result.Entries.Select(e => e.Path));
    }

    [Fact]
    public void Inspect_ReadsFromFilePath()
    {
        using var dir = new TempDir();
        var path = dir.Write("jump.ednplugin", Valid());

        Assert.True(PluginPackage.Inspect(path).IsValid);
    }

    [Fact]
    public void Inspect_NonSeekableStream_IsBufferedWithinLimit()
    {
        var result = PluginPackage.Inspect(new NonSeekableStream(Valid()));
        Assert.True(result.IsValid, result.ErrorSummary);
    }

    // --- Structural problems -------------------------------------------------------------------

    [Fact]
    public void Inspect_MissingFile_IsRejected()
        => AssertRejected(PluginPackage.Inspect(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ednplugin")), "does not exist");

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 2, 3, 4 })]
    [InlineData(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0, 0, 0 })] // truncated local header
    public void Inspect_NotAZip_IsRejected(byte[] bytes)
        => AssertRejected(Inspect(bytes), "not a readable zip");

    [Fact]
    public void Inspect_EmptyZip_IsRejected()
        => AssertRejected(Inspect(Zip([])), "empty");

    [Fact]
    public void Inspect_NoManifest_IsRejected()
        => AssertRejected(Inspect(Zip([("Acme.JumpCounter.dll", Dll)])), "no plugin.json");

    [Fact]
    public void Inspect_ManifestOnlyInNestedFolderWithSiblings_IsRejected()
        => AssertRejected(Inspect(Zip([("a/plugin.json", Utf8(ValidManifestJson)), ("b/Acme.JumpCounter.dll", Dll)])), "no plugin.json");

    [Fact]
    public void Inspect_WrongManifestCasing_IsRejected()
        => AssertRejected(Inspect(Zip([("Plugin.JSON", Utf8(ValidManifestJson)), ("Acme.JumpCounter.dll", Dll)])), "named exactly");

    [Fact]
    public void Inspect_InvalidManifest_IsRejectedWithManifestReasons()
    {
        var zip = Zip([("plugin.json", Utf8("""{ "id": "NOPE" }""")), ("Acme.JumpCounter.dll", Dll)]);
        AssertRejected(Inspect(zip), "plugin.json: 'id'");
    }

    [Fact]
    public void Inspect_EntryAssemblyMissing_IsRejected()
        => AssertRejected(Inspect(Zip([("plugin.json", Utf8(ValidManifestJson)), ("Other.dll", Dll)])), "is not in the package");

    [Fact]
    public void Inspect_EntryAssemblyCasingMismatch_IsRejected()
        => AssertRejected(Inspect(Zip([("plugin.json", Utf8(ValidManifestJson)), ("acme.jumpcounter.dll", Dll)])), "casing");

    [Fact]
    public void Inspect_EntryAssemblyAsDirectory_IsRejected()
        => AssertRejected(Inspect(Zip([("plugin.json", Utf8(ValidManifestJson)), ("Acme.JumpCounter.dll/", [])])), "is not in the package");

    // --- Hostile entry names (zip-slip & friends) ----------------------------------------------

    [Theory]
    [InlineData("../evil.dll")]
    [InlineData("../../../../etc/cron.d/evil")]
    [InlineData("lib/../../evil.dll")]
    [InlineData("..\\evil.dll")]
    [InlineData("lib\\..\\..\\evil.dll")]
    [InlineData("/etc/passwd")]
    [InlineData("\\Windows\\System32\\evil.dll")]
    [InlineData("\\\\server\\share\\evil.dll")]
    [InlineData("C:/Windows/evil.dll")]
    [InlineData("C:\\Windows\\evil.dll")]
    [InlineData("C:evil.dll")]
    [InlineData("Acme.JumpCounter.dll:Zone.Identifier")]
    [InlineData("lib/CON")]
    [InlineData("aux.dll")]
    [InlineData("lib./x.dll")]
    [InlineData("./plugin.json")]
    [InlineData("lib//x.dll")]
    [InlineData("evil\u0000.dll")]
    public void Inspect_UnsafeEntryName_IsRejected(string name)
        => AssertRejected(Inspect(Valid((name, Dll))), "is unsafe");

    [Fact]
    public void Inspect_UnsafeEntry_RejectsTheWholePackage()
    {
        // One bad entry among many good ones still poisons the package — no partial installs.
        var result = Inspect(Valid(("lib/good.dll", Dll), ("../evil.dll", Dll), ("lib/also-good.dll", Dll)));
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
    }

    [Theory]
    [InlineData("Acme.JumpCounter.dll", "ACME.JUMPCOUNTER.DLL")]
    [InlineData("lib/Dep.dll", "LIB/dep.dll")]
    [InlineData("lib/Dep.dll", "lib\\Dep.dll")]
    public void Inspect_CaseOrSeparatorCollidingDuplicates_AreRejected(string first, string second)
    {
        var zip = Zip([("plugin.json", Utf8(ValidManifestJson)), ("Acme.JumpCounter.dll", Dll), (first, Dll), (second, Dll)]);
        AssertRejected(Inspect(zip), "duplicated");
    }

    [Fact]
    public void Inspect_ExactDuplicateEntries_AreRejected()
    {
        // ZipArchive happily writes the same name twice; extraction would silently overwrite.
        var zip = Zip([("plugin.json", Utf8(ValidManifestJson)), ("Acme.JumpCounter.dll", Dll), ("Acme.JumpCounter.dll", [0xCC])]);
        AssertRejected(Inspect(zip), "duplicated");
    }

    [Fact]
    public void Inspect_FileAndDirectoryWithSamePath_AreRejected()
        => AssertRejected(Inspect(Valid(("lib", Dll), ("lib/x.dll", Dll))), "which is a file");

    [Fact]
    public void Inspect_SymlinkEntry_IsRejected()
    {
        var zip = Zip(
            [("plugin.json", Utf8(ValidManifestJson)), ("Acme.JumpCounter.dll", Dll), ("link", Utf8("/etc/passwd"))],
            entry =>
            {
                if (entry.FullName == "link")
                    entry.ExternalAttributes = unchecked((int)(0xA1FFu << 16)); // S_IFLNK | 0777
            });

        AssertRejected(Inspect(zip), "symbolic link");
    }

    [Fact]
    public void Inspect_RegularUnixFileMode_IsAccepted()
    {
        var zip = Zip(
            [("plugin.json", Utf8(ValidManifestJson)), ("Acme.JumpCounter.dll", Dll)],
            entry => entry.ExternalAttributes = unchecked((int)(0x81A4u << 16))); // S_IFREG | 0644

        Assert.True(Inspect(zip).IsValid);
    }

    [Fact]
    public void Inspect_EncryptedEntry_IsRejected()
    {
        var zip = Valid(("secret.dll", Dll));
        SetEncryptedFlag(zip, "secret.dll");

        AssertRejected(Inspect(zip), "encrypted");
    }

    [Fact]
    public void Inspect_HostileNamesInErrors_AreSanitised()
    {
        var result = Inspect(Valid(("../\u001b[2Jevil.dll", Dll)));
        Assert.False(result.IsValid);
        Assert.DoesNotContain('\u001b', result.ErrorSummary);
    }

    // --- Size limits / zip bombs ----------------------------------------------------------------

    [Fact]
    public void Inspect_PackageOverSizeLimit_IsRejected()
    {
        var zip = Valid(("big.bin", RandomBytes(4096)));
        AssertRejected(Inspect(zip, new PluginPackageLimits { MaxPackageBytes = 1024 }), "over the 1024-byte limit");
    }

    [Fact]
    public void Inspect_FileOverSizeLimit_IsRejectedBeforeReading()
    {
        using var dir = new TempDir();
        var path = dir.Write("big.ednplugin", Valid(("big.bin", RandomBytes(4096))));

        AssertRejected(PluginPackage.Inspect(path, new PluginPackageLimits { MaxPackageBytes = 1024 }), "over the 1024-byte limit");
    }

    [Fact]
    public void Inspect_NonSeekableStreamOverLimit_IsRejectedWithoutBufferingEverything()
    {
        var stream = new NonSeekableStream(new byte[1024 * 1024]);
        AssertRejected(PluginPackage.Inspect(stream, new PluginPackageLimits { MaxPackageBytes = 4096 }), "over the 4096-byte limit");
        Assert.True(stream.Position <= 4096 + 1);
    }

    [Fact]
    public void Inspect_TooManyEntries_IsRejected()
    {
        var extras = Enumerable.Range(0, 20).Select(i => ($"f{i}.txt", new byte[] { 1 })).ToArray();
        AssertRejected(Inspect(Valid(extras), new PluginPackageLimits { MaxEntries = 10 }), "over the 10-entry limit");
    }

    [Fact]
    public void Inspect_EntryOverPerFileLimit_IsRejected()
        => AssertRejected(Inspect(Valid(("big.bin", RandomBytes(5000))), new PluginPackageLimits { MaxEntryBytes = 4096 }), "per-file limit");

    [Fact]
    public void Inspect_TotalOverUncompressedLimit_IsRejected()
    {
        var zip = Valid(("a.bin", RandomBytes(3000)), ("b.bin", RandomBytes(3000)));
        AssertRejected(Inspect(zip, new PluginPackageLimits { MaxTotalUncompressedBytes = 5000 }), "expands to");
    }

    [Fact]
    public void Inspect_HighlyCompressedEntry_IsRejectedAsZipBomb()
    {
        // 8 MiB of zeros deflates to a few KiB — far past the default 100:1 ratio.
        var zip = Valid(("bomb.bin", new byte[8 * 1024 * 1024]));

        Assert.True(zip.Length < 64 * 1024);
        AssertRejected(Inspect(zip), "zip bomb");
    }

    [Fact]
    public void Inspect_SmallCompressibleFiles_AreNotFlaggedAsBombs()
        => Assert.True(Inspect(Valid(("zeros.bin", new byte[512 * 1024]))).IsValid); // under the 1 MiB threshold

    // --- Extraction ------------------------------------------------------------------------------

    [Fact]
    public void ExtractTo_WritesThePluginTree()
    {
        using var dir = new TempDir();
        var dest = Path.Combine(dir.Path, "out");

        var result = PluginPackage.ExtractTo(new MemoryStream(Valid(("lib/Dep.dll", Dll), ("empty/", [])))!, dest);

        Assert.True(result.IsValid, result.ErrorSummary);
        Assert.Equal(ValidManifestJson, File.ReadAllText(Path.Combine(dest, "plugin.json")));
        Assert.Equal(Dll, File.ReadAllBytes(Path.Combine(dest, "lib", "Dep.dll")));
        Assert.True(Directory.Exists(Path.Combine(dest, "empty")));
    }

    [Fact]
    public void ExtractTo_WrappedPackage_StripsTheWrapperFolder()
    {
        using var dir = new TempDir();
        var dest = Path.Combine(dir.Path, "out");
        var zip = Zip([("wrap/plugin.json", Utf8(ValidManifestJson)), ("wrap/Acme.JumpCounter.dll", Dll)]);

        Assert.True(PluginPackage.ExtractTo(new MemoryStream(zip), dest).IsValid);
        Assert.True(File.Exists(Path.Combine(dest, "plugin.json")));
        Assert.False(Directory.Exists(Path.Combine(dest, "wrap")));
    }

    [Fact]
    public void ExtractTo_ZipSlip_WritesNothingAnywhere()
    {
        using var dir = new TempDir();
        var dest = Path.Combine(dir.Path, "a", "b", "out");

        var result = PluginPackage.ExtractTo(new MemoryStream(Valid(("../../../escaped.dll", Dll), ("..\\..\\escaped2.dll", Dll)))!, dest);

        Assert.False(result.IsValid);
        Assert.False(Directory.Exists(dest));
        Assert.Empty(Directory.EnumerateFileSystemEntries(dir.Path, "escaped*", SearchOption.AllDirectories));
    }

    [Fact]
    public void ExtractTo_ExistingDestination_IsRejected()
    {
        using var dir = new TempDir();
        var result = PluginPackage.ExtractTo(new MemoryStream(Valid()), dir.Path);
        Assert.False(result.IsValid);
        Assert.Contains("already exists", result.ErrorSummary);
    }

    [Fact]
    public void ExtractTo_EntryThatDecompressesPastItsDeclaredSize_IsRejectedAndCleanedUp()
    {
        using var dir = new TempDir();
        var dest = Path.Combine(dir.Path, "out");
        var zip = Valid(("payload.bin", RandomBytes(2000)));
        // Lie in both headers: claim 10 bytes while the deflate stream holds 2000.
        SetDeclaredUncompressedSize(zip, "payload.bin", 10);

        var result = PluginPackage.ExtractTo(new MemoryStream(zip), dest);

        Assert.False(result.IsValid);
        Assert.False(Directory.Exists(dest));
    }

    [Fact]
    public void ExtractTo_CrcMismatchWithCorrectSize_IsRejectedAndCleanedUp()
    {
        using var dir = new TempDir();
        var dest = Path.Combine(dir.Path, "out");
        var zip = Valid(("payload.bin", RandomBytes(2000)));
        // Same size, wrong checksum: the content was altered (or the headers were).
        PatchHeaders(zip, "payload.bin", (span, isCentral) =>
        {
            var offset = isCentral ? 16 : 14;
            BinaryPrimitives.WriteUInt32LittleEndian(span[offset..], BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]) ^ 0xDEADBEEF);
        });

        var result = PluginPackage.ExtractTo(new MemoryStream(zip), dest);

        Assert.False(result.IsValid);
        Assert.Contains("CRC", result.ErrorSummary, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(dest));
    }

    [Fact]
    public void ExtractTo_PreExistingDestination_IsNeverDeleted()
    {
        using var dir = new TempDir();
        var dest = Path.Combine(dir.Path, "mine");
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "keep.txt"), "user data");

        var bad = PluginPackage.ExtractTo(new MemoryStream(Valid(("../evil.dll", Dll))), dest);
        var good = PluginPackage.ExtractTo(new MemoryStream(Valid()), dest);

        Assert.False(bad.IsValid);
        Assert.False(good.IsValid);
        Assert.Equal("user data", File.ReadAllText(Path.Combine(dest, "keep.txt")));
    }

    [Fact]
    public void ExtractTo_DestinationIsAFile_FailsWithoutDeletingIt()
    {
        using var dir = new TempDir();
        var dest = dir.Write("occupied", [1, 2, 3]);

        var result = PluginPackage.ExtractTo(new MemoryStream(Valid()), dest);

        Assert.False(result.IsValid);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(dest));
    }

    // --- Unicode aliasing -------------------------------------------------------------------------

    [Fact]
    public void Inspect_NfcAndNfdSpellingsOfTheSameName_AreDuplicates()
    {
        // "caf\u00E9.dll" precomposed (U+00E9) vs decomposed ("e" + U+0301): one file on macOS.
        var zip = Valid(("caf\u00E9.dll", Dll), ("cafe\u0301.dll", Dll));
        AssertRejected(Inspect(zip), "duplicated");
    }

    [Fact]
    public void Inspect_NfdDirectoryAndNfcFile_Collide()
        => AssertRejected(Inspect(Valid(("caf\u00E9", Dll), ("cafe\u0301/x.dll", Dll))), "which is a file");

    [Fact]
    public void Inspect_BidiOverrideInEntryName_IsRejected()
        => AssertRejected(Inspect(Valid(("evil\u202Elld.exe", Dll))), "invisible formatting");

    // --- Entry count read from the End Of Central Directory ---------------------------------------

    [Fact]
    public void ReadDeclaredEntryCount_MatchesTheArchive()
    {
        var zip = Valid(("a.txt", [1]), ("b/", []));
        Assert.Equal(4, PluginPackage.ReadDeclaredEntryCount(new MemoryStream(zip)));
    }

    [Fact]
    public void ReadDeclaredEntryCount_IgnoresTrailingComment_AndRestoresPosition()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.Comment = "PK\u0005\u0006 decoy signature inside the comment";
            archive.CreateEntry("x.txt");
        }
        buffer.Position = 3;

        Assert.Equal(1, PluginPackage.ReadDeclaredEntryCount(buffer));
        Assert.Equal(3, buffer.Position);
    }

    [Fact]
    public void ReadDeclaredEntryCount_NoEocd_IsNull()
        => Assert.Null(PluginPackage.ReadDeclaredEntryCount(new MemoryStream(new byte[100])));

    [Fact]
    public void Inspect_DeclaredEntryCountOverLimit_IsRejectedBeforeParsingEntries()
    {
        var zip = Valid();
        SetEocdEntryCount(zip, 60_000);

        AssertRejected(Inspect(zip, new PluginPackageLimits { MaxEntries = 10 }), "declares 60000 entries");
    }

    [Fact]
    public void Inspect_EocdUnderstatingTheEntryCount_IsRejected()
    {
        // Claims 1 entry but the central directory holds 3: must not slip past the count check.
        var zip = Valid(("extra.txt", [1]));
        SetEocdEntryCount(zip, 1);

        Assert.False(Inspect(zip, new PluginPackageLimits { MaxEntries = 2 }).IsValid);
    }

    // --- Helpers ---------------------------------------------------------------------------------

    private static void SetEocdEntryCount(byte[] zip, ushort count)
    {
        for (var i = zip.Length - 22; i >= 0; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(i)) != 0x06054B50) continue;
            BinaryPrimitives.WriteUInt16LittleEndian(zip.AsSpan(i + 8), count);  // entries on this disk
            BinaryPrimitives.WriteUInt16LittleEndian(zip.AsSpan(i + 10), count); // total entries
            return;
        }
        Assert.Fail("no EOCD record");
    }

    private static byte[] RandomBytes(int count)
    {
        var bytes = new byte[count];
        new Random(count).NextBytes(bytes);
        return bytes;
    }

    /// <summary>Sets general-purpose flag bit 0 (encrypted) on the named entry's local and central headers.</summary>
    private static void SetEncryptedFlag(byte[] zip, string name)
        => PatchHeaders(zip, name, (span, isCentral) =>
        {
            var offset = isCentral ? 8 : 6;
            var flags = BinaryPrimitives.ReadUInt16LittleEndian(span[offset..]);
            BinaryPrimitives.WriteUInt16LittleEndian(span[offset..], (ushort)(flags | 1));
        });

    private static void SetDeclaredUncompressedSize(byte[] zip, string name, uint size)
        => PatchHeaders(zip, name, (span, isCentral) =>
            BinaryPrimitives.WriteUInt32LittleEndian(span[(isCentral ? 24 : 22)..], size));

    /// <summary>Finds every local (PK\3\4) and central (PK\1\2) header for <paramref name="name"/> and patches it.</summary>
    private static void PatchHeaders(byte[] zip, string name, SpanAction patch)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var patched = 0;
        for (var i = 0; i + 46 < zip.Length; i++)
        {
            if (zip[i] != 0x50 || zip[i + 1] != 0x4B) continue;
            var isLocal = zip[i + 2] == 3 && zip[i + 3] == 4;
            var isCentral = zip[i + 2] == 1 && zip[i + 3] == 2;
            if (!isLocal && !isCentral) continue;

            var nameLengthOffset = isCentral ? 28 : 26;
            var nameOffset = isCentral ? 46 : 30;
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(i + nameLengthOffset));
            if (nameLength != nameBytes.Length || i + nameOffset + nameLength > zip.Length) continue;
            if (!zip.AsSpan(i + nameOffset, nameLength).SequenceEqual(nameBytes)) continue;

            patch(zip.AsSpan(i), isCentral);
            patched++;
        }
        Assert.Equal(2, patched);
    }

    private delegate void SpanAction(Span<byte> header, bool isCentral);

    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
