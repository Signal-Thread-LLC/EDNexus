using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using EDNexus.Plugins.Abstractions;

namespace EDNexus.Plugins.Hosting;

/// <summary>One file or directory inside a validated <c>.ednplugin</c> package.</summary>
/// <param name="Path">Path relative to the plugin folder, <c>/</c>-separated (directories end with <c>/</c>).</param>
/// <param name="IsDirectory">Whether the entry is a directory.</param>
/// <param name="Length">Declared uncompressed size in bytes.</param>
public sealed record PluginPackageEntry(string Path, bool IsDirectory, long Length);

/// <summary>The outcome of inspecting (or extracting) a <c>.ednplugin</c> package.</summary>
public sealed class PluginPackageInspection
{
    internal PluginPackageInspection(PluginManifest? manifest, IReadOnlyList<PluginPackageEntry> entries, IReadOnlyList<string> errors)
    {
        Errors = errors;
        Manifest = errors.Count == 0 ? manifest : null;
        Entries = errors.Count == 0 ? entries : [];
    }

    /// <summary>The package's validated manifest, or <see langword="null"/> when not <see cref="IsValid"/>.</summary>
    public PluginManifest? Manifest { get; }

    /// <summary>The package contents (relative to the plugin folder), or empty when not <see cref="IsValid"/>.</summary>
    public IReadOnlyList<PluginPackageEntry> Entries { get; }

    /// <summary>Every reason the package was rejected. Empty when <see cref="IsValid"/>.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Whether the package is safe to install.</summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>All <see cref="Errors"/> joined into one line, for logs and UI.</summary>
    public string ErrorSummary => string.Join("; ", Errors);

    internal static PluginPackageInspection Failure(params string[] errors) => new(null, [], errors);
}

/// <summary>
/// Reads and validates <c>.ednplugin</c> packages. A package is a zip of a plugin folder: it has
/// <c>plugin.json</c> at its root (or inside a single top-level folder, as produced by
/// "compress folder" tools), plus the entry assembly and its dependencies.
/// <para>
/// Nothing is trusted: every entry name is checked with <see cref="PluginPathRules"/> (rejecting
/// absolute paths, <c>..</c> traversal / zip-slip, drive letters, device names, case-colliding
/// duplicates), symlinks and encrypted entries are refused, and <see cref="PluginPackageLimits"/>
/// cap the archive size, entry count, and uncompressed size (including a compression-ratio
/// zip-bomb check, re-enforced on the actual bytes during extraction). No method here throws for
/// bad package content — problems come back as <see cref="PluginPackageInspection.Errors"/>.
/// </para>
/// </summary>
public static class PluginPackage
{
    /// <summary>Unix file-type bits stored in the high word of a zip entry's external attributes.</summary>
    private const int UnixFileTypeMask = 0xF000;
    private const int UnixSymlink = 0xA000;

    /// <summary>Validates the package at <paramref name="packagePath"/> without extracting it.</summary>
    public static PluginPackageInspection Inspect(string packagePath, PluginPackageLimits? limits = null)
        => WithArchive(packagePath, limits ?? PluginPackageLimits.Default, (archive, l) => Analyze(archive, l).Inspection);

    /// <summary>Validates the package in <paramref name="package"/> without extracting it. The stream is not disposed.</summary>
    public static PluginPackageInspection Inspect(Stream package, PluginPackageLimits? limits = null)
        => WithArchive(package, limits ?? PluginPackageLimits.Default, (archive, l) => Analyze(archive, l).Inspection);

    /// <summary>
    /// Validates the package at <paramref name="packagePath"/> and, only if it is valid, extracts
    /// it into <paramref name="destinationDirectory"/> (which must not exist yet). Files are
    /// written to a sibling <c>.extract-&lt;guid&gt;</c> folder that is renamed onto the destination
    /// only once complete; on any failure only that staging folder is removed, never the
    /// destination. If the process dies mid-extraction, a leftover <c>.extract-*</c> folder is
    /// only swept by <see cref="PluginInstaller.RecoverInterrupted"/> when it sits under the
    /// plugins root — direct callers extracting elsewhere own that cleanup.
    /// </summary>
    public static PluginPackageInspection ExtractTo(string packagePath, string destinationDirectory, PluginPackageLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(destinationDirectory);
        return WithArchive(packagePath, limits ?? PluginPackageLimits.Default,
            (archive, l) => Extract(archive, l, destinationDirectory));
    }

    /// <summary>As <see cref="ExtractTo(string, string, PluginPackageLimits?)"/>, reading from a stream (not disposed).</summary>
    public static PluginPackageInspection ExtractTo(Stream package, string destinationDirectory, PluginPackageLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(destinationDirectory);
        return WithArchive(package, limits ?? PluginPackageLimits.Default,
            (archive, l) => Extract(archive, l, destinationDirectory));
    }

    private static PluginPackageInspection WithArchive(
        string packagePath, PluginPackageLimits limits, Func<ZipArchive, PluginPackageLimits, PluginPackageInspection> action)
    {
        ArgumentNullException.ThrowIfNull(packagePath);
        try
        {
            var info = new FileInfo(packagePath);
            if (!info.Exists)
                return PluginPackageInspection.Failure($"package '{packagePath}' does not exist");
            if (info.Length > limits.MaxPackageBytes)
                return PluginPackageInspection.Failure($"package is {info.Length} bytes, over the {limits.MaxPackageBytes}-byte limit");

            using var stream = info.OpenRead();
            return WithArchive(stream, limits, action);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return PluginPackageInspection.Failure($"package '{packagePath}' could not be read: {ex.Message}");
        }
    }

    private static PluginPackageInspection WithArchive(
        Stream package, PluginPackageLimits limits, Func<ZipArchive, PluginPackageLimits, PluginPackageInspection> action)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(limits);
        MemoryStream? buffered = null;
        try
        {
            Stream source = package;
            if (package.CanSeek)
            {
                if (package.Length - package.Position > limits.MaxPackageBytes)
                    return PluginPackageInspection.Failure($"package is over the {limits.MaxPackageBytes}-byte limit");
            }
            else
            {
                // ZipArchive needs to seek to the central directory; buffer, but never past the limit.
                buffered = new MemoryStream();
                if (!BoundedCopy(package, buffered, limits.MaxPackageBytes))
                    return PluginPackageInspection.Failure($"package is over the {limits.MaxPackageBytes}-byte limit");
                buffered.Position = 0;
                source = buffered;
            }

            // Check the declared entry count before ZipArchive parses the central directory into
            // one object per entry. (The runtime then verifies the directory really holds that
            // many entries, so a header understating the count fails to open rather than slipping
            // past this check; Analyze re-checks the materialised count regardless.)
            var declaredEntries = ReadDeclaredEntryCount(source);
            if (declaredEntries is null)
                return PluginPackageInspection.Failure("package is not a readable zip archive: no unambiguous end of central directory record");
            if (declaredEntries > limits.MaxEntries)
                return PluginPackageInspection.Failure($"package declares {declaredEntries} entries, over the {limits.MaxEntries}-entry limit");

            using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
            return action(archive, limits);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException or ArgumentException)
        {
            return PluginPackageInspection.Failure($"package is not a readable zip archive: {ex.Message}");
        }
        finally
        {
            buffered?.Dispose();
        }
    }

    /// <summary>
    /// Reads the "total entries" field from the zip's End Of Central Directory record, without
    /// parsing the directory. Like <see cref="ZipArchive"/>, it follows the Zip64 locator when
    /// the disk number, entry count, or central-directory offset is saturated.
    /// Returns <see langword="null"/> when no unambiguous EOCD record is found: none at all, or
    /// the last one's comment length doesn't end exactly at the end of the stream (a decoy
    /// signature inside the comment, or trailing bytes). Callers must reject the package then.
    /// Restores the stream position.
    /// </summary>
    internal static long? ReadDeclaredEntryCount(Stream stream)
    {
        const int EocdSize = 22;
        const int MaxCommentLength = ushort.MaxValue;
        const uint EocdSignature = 0x06054B50;

        var origin = stream.Position;
        try
        {
            var length = stream.Length;
            if (length < EocdSize)
                return null;

            var tailLength = (int)Math.Min(length, EocdSize + MaxCommentLength);
            var tail = new byte[tailLength];
            stream.Position = length - tailLength;
            stream.ReadExactly(tail);

            // Scan backwards for the EOCD signature (the archive comment may follow it).
            for (var i = tailLength - EocdSize; i >= 0; i--)
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i)) != EocdSignature)
                    continue;
                // The last EOCD signature is the one ZipArchive uses, and it does not check that the
                // record's comment runs exactly to the end of the file. If ours doesn't, the two
                // readers could disagree (e.g. a decoy record inside the comment with a different
                // count, or trailing bytes after the archive) — refuse rather than guess.
                if (BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 20)) != tailLength - i - EocdSize)
                    return null;

                var diskNumber = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 4));
                long count = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 10));
                var directoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i + 16));

                // Follow the Zip64 record under exactly the conditions ZipArchive does — any of
                // these fields saturated — so both readers settle on the same entry count.
                var zip64Signalled = diskNumber == ushort.MaxValue
                    || count == ushort.MaxValue
                    || directoryOffset == uint.MaxValue;
                if (!zip64Signalled)
                    return count;

                var zip64Count = TryReadZip64EntryCount(stream, length, length - tailLength + i);
                if (zip64Count is not null)
                    return zip64Count;
                // No readable Zip64 record: the classic count stands unless it is itself the
                // "see Zip64" placeholder, in which case the true count is unknowable.
                return count != ushort.MaxValue ? count : null;
            }
            return null;
        }
        finally
        {
            stream.Position = origin;
        }
    }

    /// <summary>
    /// Reads the total entry count from the Zip64 End Of Central Directory record, located via
    /// the 20-byte Zip64 locator immediately before the classic EOCD at
    /// <paramref name="eocdPosition"/>. Returns <see langword="null"/> when there is no locator
    /// or the record it points at is missing, out of range, or has the wrong signature.
    /// </summary>
    private static long? TryReadZip64EntryCount(Stream stream, long length, long eocdPosition)
    {
        const uint Zip64LocatorSignature = 0x07064B50;
        const uint Zip64EocdSignature = 0x06064B50;
        const int LocatorSize = 20;
        const int Zip64EocdMinSize = 56;

        if (eocdPosition < LocatorSize)
            return null;
        var locator = new byte[LocatorSize];
        stream.Position = eocdPosition - LocatorSize;
        stream.ReadExactly(locator);
        if (BinaryPrimitives.ReadUInt32LittleEndian(locator) != Zip64LocatorSignature)
            return null;

        var zip64Position = BinaryPrimitives.ReadUInt64LittleEndian(locator.AsSpan(8));
        if (length < Zip64EocdMinSize || zip64Position > (ulong)(length - Zip64EocdMinSize))
            return null;
        var zip64 = new byte[Zip64EocdMinSize];
        stream.Position = (long)zip64Position;
        stream.ReadExactly(zip64);
        if (BinaryPrimitives.ReadUInt32LittleEndian(zip64) != Zip64EocdSignature)
            return null;

        var total = BinaryPrimitives.ReadUInt64LittleEndian(zip64.AsSpan(32));
        return total > long.MaxValue ? long.MaxValue : (long)total;
    }

    private sealed record Analysis(
        PluginPackageInspection Inspection,
        IReadOnlyList<(ZipArchiveEntry Entry, string RelativePath)> Files);

    private static Analysis Analyze(ZipArchive archive, PluginPackageLimits limits)
    {
        var errors = new List<string>();
        var rawEntries = archive.Entries;

        if (rawEntries.Count == 0)
            return Fail("package is empty");
        if (rawEntries.Count > limits.MaxEntries)
            return Fail($"package has {rawEntries.Count} entries, over the {limits.MaxEntries}-entry limit");

        // Pass 1: every entry name must be safe on every OS, and entries must not collide.
        var normalized = new List<(ZipArchiveEntry Entry, string Name, bool IsDirectory)>(rawEntries.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalUncompressed = 0;
        foreach (var entry in rawEntries)
        {
            var name = PluginPathRules.Normalize(entry.FullName);
            var display = Display(entry.FullName);
            var isDirectory = name.EndsWith('/');

            if (PluginPathRules.CheckRelativePath(name, allowTrailingSlash: true) is { } pathProblem)
            {
                errors.Add($"entry \"{display}\" is unsafe: {pathProblem}");
                continue;
            }

            // Compare case-insensitively on the NFC form: macOS (and some Linux setups) treat
            // precomposed e-acute (U+00E9) and "e" + combining acute (U+0301) as the same file,
            // so they must collide here too.
            var key = CollisionKey(isDirectory ? name[..^1] : name);
            if (!seen.Add(key))
            {
                errors.Add($"entry \"{display}\" is duplicated (names are compared case-insensitively after Unicode normalisation)");
                continue;
            }

            if (((entry.ExternalAttributes >> 16) & UnixFileTypeMask) == UnixSymlink)
            {
                errors.Add($"entry \"{display}\" is a symbolic link, which packages may not contain");
                continue;
            }

            if (entry.IsEncrypted)
            {
                errors.Add($"entry \"{display}\" is encrypted");
                continue;
            }

            if (isDirectory)
            {
                if (entry.Length != 0)
                    errors.Add($"directory entry \"{display}\" has content");
            }
            else
            {
                if (entry.Length < 0 || entry.CompressedLength < 0)
                {
                    errors.Add($"entry \"{display}\" has an invalid size");
                    continue;
                }
                if (entry.Length > limits.MaxEntryBytes)
                {
                    errors.Add($"entry \"{display}\" is {entry.Length} bytes, over the {limits.MaxEntryBytes}-byte per-file limit");
                    continue;
                }
                if (entry.Length > limits.CompressionRatioThresholdBytes
                    && (entry.CompressedLength == 0 || entry.Length / entry.CompressedLength > limits.MaxCompressionRatio))
                {
                    errors.Add($"entry \"{display}\" has a suspicious compression ratio (possible zip bomb)");
                    continue;
                }
                totalUncompressed += entry.Length;
            }

            normalized.Add((entry, name, isDirectory));
        }

        if (totalUncompressed > limits.MaxTotalUncompressedBytes)
            errors.Add($"package expands to {totalUncompressed} bytes, over the {limits.MaxTotalUncompressedBytes}-byte limit");

        // A file and a directory may not share a path ("lib" the file vs "lib/x.dll").
        var files = new HashSet<string>(normalized.Where(e => !e.IsDirectory).Select(e => CollisionKey(e.Name)), StringComparer.OrdinalIgnoreCase);
        foreach (var (_, name, _) in normalized)
        {
            var trimmed = name.TrimEnd('/');
            for (var slash = trimmed.IndexOf('/'); slash >= 0; slash = trimmed.IndexOf('/', slash + 1))
            {
                if (files.Contains(CollisionKey(trimmed[..slash])))
                {
                    errors.Add($"entry \"{Display(name)}\" is nested under \"{trimmed[..slash]}\", which is a file");
                    break;
                }
            }
        }

        if (errors.Count > 0)
            return Fail([.. errors]);

        // Pass 2: find the plugin root (archive root, or a single wrapping folder) and its manifest.
        var prefix = FindRootPrefix(normalized.Select(e => e.Name).ToList());
        if (prefix is null)
            return Fail($"package has no {PluginManifestParser.FileName} at its root (or inside a single top-level folder)");

        var manifestEntry = normalized.First(e => !e.IsDirectory && string.Equals(e.Name, prefix + PluginManifestParser.FileName, StringComparison.OrdinalIgnoreCase));
        if (!string.Equals(manifestEntry.Name, prefix + PluginManifestParser.FileName, StringComparison.Ordinal))
            return Fail($"manifest must be named exactly \"{PluginManifestParser.FileName}\" (found \"{Display(manifestEntry.Name)}\")");
        if (manifestEntry.Entry.Length > PluginManifestParser.MaxManifestBytes)
            return Fail($"{PluginManifestParser.FileName} is larger than {PluginManifestParser.MaxManifestBytes} bytes");

        PluginManifestParseResult parsed;
        using (var manifestStream = manifestEntry.Entry.Open())
            parsed = PluginManifestParser.Parse(manifestStream);
        if (!parsed.IsValid)
            return Fail([.. parsed.Errors.Select(e => $"{PluginManifestParser.FileName}: {e}")]);
        var manifest = parsed.Manifest!;

        var entries = new List<PluginPackageEntry>();
        var fileEntries = new List<(ZipArchiveEntry, string)>();
        foreach (var (entry, name, isDirectory) in normalized)
        {
            var relative = name[prefix.Length..];
            if (relative.Length == 0)
                continue; // the wrapping folder's own directory entry
            entries.Add(new PluginPackageEntry(relative, isDirectory, entry.Length));
            if (!isDirectory)
                fileEntries.Add((entry, relative));
        }

        // The entry assembly must ship in the package, with exactly the declared casing so it
        // also resolves on case-sensitive filesystems.
        var entryAssembly = fileEntries.FirstOrDefault(f => string.Equals(f.Item2, manifest.EntryAssembly, StringComparison.OrdinalIgnoreCase));
        if (entryAssembly.Item1 is null)
            return Fail($"entryAssembly \"{manifest.EntryAssembly}\" is not in the package");
        if (!string.Equals(entryAssembly.Item2, manifest.EntryAssembly, StringComparison.Ordinal))
            return Fail($"entryAssembly \"{manifest.EntryAssembly}\" differs in casing from the packaged file \"{entryAssembly.Item2}\"");

        return new Analysis(new PluginPackageInspection(manifest, entries, []), fileEntries);

        static Analysis Fail(params string[] reasons) => new(new PluginPackageInspection(null, [], reasons), []);
    }

    /// <summary>
    /// "" when <c>plugin.json</c> sits at the archive root; <c>"folder/"</c> when every entry is
    /// under one top-level folder that contains it; otherwise <see langword="null"/>.
    /// </summary>
    private static string? FindRootPrefix(IReadOnlyList<string> names)
    {
        if (names.Any(n => string.Equals(n, PluginManifestParser.FileName, StringComparison.OrdinalIgnoreCase)))
            return "";

        var firstSlash = names[0].IndexOf('/');
        if (firstSlash <= 0)
            return null;
        var prefix = names[0][..(firstSlash + 1)];
        if (!names.All(n => n.StartsWith(prefix, StringComparison.Ordinal)))
            return null;

        return names.Any(n => string.Equals(n, prefix + PluginManifestParser.FileName, StringComparison.OrdinalIgnoreCase))
            ? prefix
            : null;
    }

    private static PluginPackageInspection Extract(ZipArchive archive, PluginPackageLimits limits, string destinationDirectory)
    {
        var analysis = Analyze(archive, limits);
        if (!analysis.Inspection.IsValid)
            return analysis.Inspection;

        string destination;
        try
        {
            // Trim a trailing separator so "…\out\" has parent "…" (not "…\out" itself).
            destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationDirectory));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return PluginPackageInspection.Failure($"destination '{destinationDirectory}' is not a valid path: {ex.Message}");
        }
        // Checked up front only for a clear error message; the Move below is what actually claims it.
        if (Directory.Exists(destination) || File.Exists(destination))
            return PluginPackageInspection.Failure($"destination '{destination}' already exists");

        var parent = Path.GetDirectoryName(destination);
        if (string.IsNullOrEmpty(parent))
            return PluginPackageInspection.Failure($"destination '{destination}' has no parent folder");

        // Extract into a sibling folder with an unguessable name that this call creates, then
        // rename it onto the destination. Directory.Move refuses an existing destination, so a
        // folder someone else created there (even an empty one) is never claimed — and on any
        // failure only our own staging folder is deleted, never the destination.
        var staging = Path.Combine(parent, ExtractStagingPrefix + Guid.NewGuid().ToString("N"));
        var stagingCreated = false;
        try
        {
            Directory.CreateDirectory(parent);
            if (Directory.Exists(staging))
                throw new PackageRejectedException($"staging folder '{staging}' unexpectedly exists");
            Directory.CreateDirectory(staging);
            stagingCreated = true;

            foreach (var entry in analysis.Inspection.Entries.Where(e => e.IsDirectory))
            {
                var dir = PluginPathRules.ResolveInside(staging, entry.Path.TrimEnd('/'))
                    ?? throw new PackageRejectedException($"entry \"{entry.Path}\" resolves outside the plugin folder");
                Directory.CreateDirectory(dir);
            }

            long written = 0;
            foreach (var (zipEntry, relative) in analysis.Files)
            {
                // Defence in depth: lexically re-check containment of the combined path (a string
                // check — links can't occur because packages containing them were rejected).
                var target = PluginPathRules.ResolveInside(staging, relative)
                    ?? throw new PackageRejectedException($"entry \"{relative}\" resolves outside the plugin folder");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                using var input = zipEntry.Open();
                using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                // Never trust the declared size: stop at declared length + 1 byte. The runtime may
                // also silently truncate at the declared size, so verify the CRC of what was
                // written — a header that lies about the size then fails instead of leaving a
                // truncated (corrupt) file behind.
                var crc = new Crc32();
                var copied = CopyAtMost(input, output, zipEntry.Length + 1, crc);
                if (copied != zipEntry.Length)
                    throw new PackageRejectedException($"entry \"{relative}\" decompressed to a different size than declared");
                if (crc.Value != zipEntry.Crc32)
                    throw new PackageRejectedException($"entry \"{relative}\" failed its CRC check (corrupt or tampered)");
                written += copied;
                if (written > limits.MaxTotalUncompressedBytes)
                    throw new PackageRejectedException($"package expands past the {limits.MaxTotalUncompressedBytes}-byte limit");
            }

            // The claim: Directory.Move fails if the destination now exists. (On Unix the runtime
            // checks before calling rename(2), which could itself replace an *empty* directory
            // created in that instant — nothing is lost then, since it held no data and is
            // never deleted by us.)
            Directory.Move(staging, destination);
            return analysis.Inspection;
        }
        catch (Exception ex) when (ex is PackageRejectedException or IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or ArgumentException)
        {
            if (stagingCreated)
                TryDeleteDirectory(staging);
            if (ex is not PackageRejectedException && (Directory.Exists(destination) || File.Exists(destination)))
                return PluginPackageInspection.Failure($"destination '{destination}' already exists");
            return PluginPackageInspection.Failure(ex is PackageRejectedException
                ? ex.Message
                : $"extraction failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Prefix of the sibling folder <see cref="ExtractTo(string, string, PluginPackageLimits?)"/>
    /// extracts into before renaming it onto the destination. It starts with '.', so it is never a
    /// valid plugin id; leftovers under the plugins root are removed by
    /// <see cref="PluginInstaller.RecoverInterrupted"/>.
    /// </summary>
    internal const string ExtractStagingPrefix = ".extract-";

    private static long CopyAtMost(Stream input, Stream output, long maxBytes, Crc32? crc = null)
    {
        var buffer = new byte[81920];
        long total = 0;
        while (total < maxBytes)
        {
            var read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, maxBytes - total));
            if (read == 0) break;
            output.Write(buffer, 0, read);
            crc?.Append(buffer.AsSpan(0, read));
            total += read;
        }
        return total;
    }

    /// <summary>The zip CRC-32 (IEEE 802.3, reflected, polynomial 0xEDB88320).</summary>
    private sealed class Crc32
    {
        private static readonly uint[] Table = BuildTable();
        private uint _state = 0xFFFFFFFFu;

        public uint Value => ~_state;

        public void Append(ReadOnlySpan<byte> data)
        {
            var state = _state;
            foreach (var b in data)
                state = Table[(state ^ b) & 0xFF] ^ (state >> 8);
            _state = state;
        }

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < table.Length; i++)
            {
                var c = i;
                for (var k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            return table;
        }
    }

    /// <summary>Copies <paramref name="input"/> to <paramref name="output"/>; false if it exceeds <paramref name="maxBytes"/>.</summary>
    private static bool BoundedCopy(Stream input, Stream output, long maxBytes)
        => CopyAtMost(input, output, maxBytes + 1) <= maxBytes;

    internal static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort; a leftover staging folder is ignored by discovery (not a valid id).
        }
    }

    /// <summary>
    /// The key two entry names collide on: NFC form, compared case-insensitively by the caller.
    /// Names have already passed <see cref="PluginPathRules.CheckRelativePath"/>, which rejects
    /// unpaired surrogates, so normalisation cannot throw.
    /// </summary>
    private static string CollisionKey(string name) => name.Normalize(NormalizationForm.FormC);

    /// <summary>Entry names are attacker-controlled; strip control characters before echoing them.</summary>
    private static string Display(string name)
    {
        var clean = new string(name.Select(c => char.IsControl(c) ? '?' : c).ToArray());
        return clean.Length <= 120 ? clean : clean[..120] + "…";
    }

    private sealed class PackageRejectedException(string message) : Exception(message);
}
