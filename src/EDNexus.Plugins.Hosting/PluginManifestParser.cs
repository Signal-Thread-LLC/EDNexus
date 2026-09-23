using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EDNexus.Plugins.Abstractions;

namespace EDNexus.Plugins.Hosting;

/// <summary>The outcome of parsing a <c>plugin.json</c>: a valid manifest, or the reasons it was rejected.</summary>
public sealed class PluginManifestParseResult
{
    private PluginManifestParseResult(PluginManifest? manifest, IReadOnlyList<string> errors)
    {
        Manifest = manifest;
        Errors = errors;
    }

    /// <summary>The validated manifest, or <see langword="null"/> when <see cref="IsValid"/> is false.</summary>
    public PluginManifest? Manifest { get; }

    /// <summary>Every problem found, in document order. Empty when <see cref="IsValid"/>.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Whether the manifest parsed and passed validation.</summary>
    public bool IsValid => Manifest is not null;

    /// <summary>All <see cref="Errors"/> joined into one line, for logs and UI.</summary>
    public string ErrorSummary => string.Join("; ", Errors);

    internal static PluginManifestParseResult Success(PluginManifest manifest) => new(manifest, []);

    internal static PluginManifestParseResult Failure(IReadOnlyList<string> errors) => new(null, errors);

    internal static PluginManifestParseResult Failure(string error) => new(null, [error]);
}

/// <summary>
/// Parses and validates <c>plugin.json</c> (see <c>PLUGIN_FORMAT.md</c>). Parsing is defensive:
/// it never throws on bad input — malformed JSON, wrong types, missing required fields, bad
/// SemVer, unsafe paths and unknown capabilities all produce a rejected result with a clear
/// reason. Unknown extra fields are ignored so newer manifests still load on older hosts.
/// </summary>
public static partial class PluginManifestParser
{
    /// <summary>The manifest file name inside a plugin folder or package.</summary>
    public const string FileName = "plugin.json";

    /// <summary>Largest <c>plugin.json</c> accepted, in bytes (64 KiB).</summary>
    public const int MaxManifestBytes = 64 * 1024;

    internal const int MaxIdLength = 128;
    internal const int MaxNameLength = 100;
    internal const int MaxAuthorLength = 200;
    internal const int MaxDescriptionLength = 2000;
    internal const int MaxEntryTypeLength = 512;
    internal const int MaxCapabilities = 32;

    // Lowercase reverse-DNS: at least two dot-separated segments; each starts and ends with a
    // letter/digit and may contain '-' or '_' in between. Lowercase-only so ids are unambiguous
    // on case-insensitive filesystems (the id is the install folder name).
    [GeneratedRegex(@"^[a-z0-9](?:[a-z0-9_-]*[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9_-]*[a-z0-9])?)+$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    // A namespace-qualified CLR type name, optionally with nested types ('+'); no generics.
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*(?:\+[A-Za-z_][A-Za-z0-9_]*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex TypeNamePattern();

    // The SDK contract version: "major.minor" (the form PluginSdk.CurrentVersionString uses).
    [GeneratedRegex(@"^(0|[1-9]\d{0,8})\.(0|[1-9]\d{0,8})$", RegexOptions.CultureInvariant)]
    private static partial Regex SdkVersionPattern();

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = 16,
    };

    /// <summary>Parses manifest JSON text. Never throws.</summary>
    /// <param name="json">The contents of a <c>plugin.json</c>.</param>
    public static PluginManifestParseResult Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return PluginManifestParseResult.Failure("manifest is empty");
        if (Encoding.UTF8.GetByteCount(json) > MaxManifestBytes)
            return PluginManifestParseResult.Failure($"manifest is larger than {MaxManifestBytes} bytes");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, DocumentOptions);
        }
        catch (JsonException ex)
        {
            return PluginManifestParseResult.Failure($"manifest is not valid JSON: {ex.Message}");
        }

        using (document)
            return FromElement(document.RootElement);
    }

    /// <summary>
    /// Parses a manifest from <paramref name="stream"/>, reading at most
    /// <see cref="MaxManifestBytes"/> + 1 bytes. Never throws on bad content (I/O errors on the
    /// stream itself are reported as a rejection too).
    /// </summary>
    public static PluginManifestParseResult Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            var buffer = new byte[MaxManifestBytes + 1];
            var total = 0;
            int read;
            while (total < buffer.Length && (read = stream.Read(buffer, total, buffer.Length - total)) > 0)
                total += read;

            if (total > MaxManifestBytes)
                return PluginManifestParseResult.Failure($"manifest is larger than {MaxManifestBytes} bytes");

            string text;
            try
            {
                text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                    .GetString(buffer, 0, total);
            }
            catch (DecoderFallbackException)
            {
                return PluginManifestParseResult.Failure("manifest is not valid UTF-8");
            }

            // Tolerate a UTF-8 BOM, which Windows editors like to add.
            return Parse(text.TrimStart('﻿'));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or ObjectDisposedException)
        {
            return PluginManifestParseResult.Failure($"manifest could not be read: {ex.Message}");
        }
    }

    /// <summary>Reads and parses the manifest file at <paramref name="path"/>. Never throws.</summary>
    public static PluginManifestParseResult ParseFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            using var stream = File.OpenRead(path);
            return Parse(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return PluginManifestParseResult.Failure($"manifest '{path}' could not be read: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns every id that appears more than once in <paramref name="manifests"/>
    /// (compared case-insensitively, since ids are folder names). The host must refuse to load
    /// any plugin whose id is duplicated rather than picking one arbitrarily.
    /// </summary>
    public static IReadOnlyList<string> FindDuplicateIds(IEnumerable<PluginManifest> manifests)
    {
        ArgumentNullException.ThrowIfNull(manifests);
        return manifests
            .GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
    }

    /// <summary>Whether <paramref name="id"/> is a well-formed plugin id.</summary>
    public static bool IsValidId(string? id) => CheckId(id) is null;

    private static string? CheckId(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return "'id' is required";
        if (id.Length > MaxIdLength)
            return $"'id' is longer than {MaxIdLength} characters";
        if (!IdPattern().IsMatch(id))
            return $"'id' \"{id}\" must be lowercase reverse-DNS (e.g. \"com.acme.jumpcounter\"): letters, digits, '-' and '_' in at least two dot-separated segments";
        // The id is used verbatim as a folder name.
        var pathProblem = PluginPathRules.CheckRelativePath(id);
        return pathProblem is null ? null : $"'id' \"{id}\" is not usable as a folder name: {pathProblem}";
    }

    private static PluginManifestParseResult FromElement(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return PluginManifestParseResult.Failure($"manifest root must be a JSON object, not {Describe(root.ValueKind)}");

        var errors = new List<string>();

        // Index properties case-insensitively; a repeated key (in any casing) is ambiguous —
        // different JSON readers keep different copies — so it is an error, not last-one-wins.
        var properties = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in root.EnumerateObject())
        {
            if (!properties.TryAdd(property.Name, property.Value))
                errors.Add($"property '{property.Name}' appears more than once");
        }

        var id = ReadString(properties, "id", required: true, MaxIdLength, errors);
        if (id is not null && CheckId(id) is { } idProblem)
            errors.Add(idProblem);

        var name = ReadString(properties, "name", required: true, MaxNameLength, errors);
        if (name is not null)
            name = CheckDisplayText("name", name, errors);

        var version = ReadString(properties, "version", required: true, SemanticVersion.MaxLength, errors);
        if (version is not null && !SemanticVersion.TryParse(version, out _))
            errors.Add($"'version' \"{version}\" is not valid SemVer 2.0.0 (expected MAJOR.MINOR.PATCH, e.g. \"1.0.0\")");

        var author = ReadString(properties, "author", required: false, MaxAuthorLength, errors);
        if (author is not null)
            author = CheckDisplayText("author", author, errors);

        var description = ReadString(properties, "description", required: false, MaxDescriptionLength, errors);
        if (description is not null && description.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t'))
            errors.Add("'description' contains control characters");

        var sdkVersion = ReadString(properties, "sdkVersion", required: true, 32, errors);
        if (sdkVersion is not null && !SdkVersionPattern().IsMatch(sdkVersion))
            errors.Add($"'sdkVersion' \"{sdkVersion}\" must be \"major.minor\" (e.g. \"{PluginSdk.CurrentVersionString}\")");

        var minAppVersion = ReadString(properties, "minAppVersion", required: false, SemanticVersion.MaxLength, errors);
        if (minAppVersion is not null && !SemanticVersion.TryParse(minAppVersion, out _))
            errors.Add($"'minAppVersion' \"{minAppVersion}\" is not valid SemVer 2.0.0 (expected MAJOR.MINOR.PATCH)");

        var entryAssembly = ReadString(properties, "entryAssembly", required: true, PluginPathRules.MaxRelativePathLength, errors);
        if (entryAssembly is not null)
        {
            if (PluginPathRules.CheckRelativePath(entryAssembly) is { } pathProblem)
                errors.Add($"'entryAssembly' \"{entryAssembly}\" is not a safe relative path: {pathProblem}");
            else if (!entryAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                errors.Add($"'entryAssembly' \"{entryAssembly}\" must be a .dll");
        }

        var entryType = ReadString(properties, "entryType", required: true, MaxEntryTypeLength, errors);
        if (entryType is not null && !TypeNamePattern().IsMatch(entryType))
            errors.Add($"'entryType' \"{entryType}\" is not a valid full type name (e.g. \"Acme.JumpCounter.JumpCounterPlugin\")");

        var capabilities = ReadCapabilities(properties, errors);

        if (errors.Count > 0)
            return PluginManifestParseResult.Failure(errors);

        return PluginManifestParseResult.Success(new PluginManifest(id!, name!, version!, sdkVersion!, author, description)
        {
            MinAppVersion = minAppVersion,
            EntryAssembly = entryAssembly!,
            EntryType = entryType!,
            Capabilities = capabilities,
        });
    }

    private static string? ReadString(
        Dictionary<string, JsonElement> properties, string key, bool required, int maxLength, List<string> errors)
    {
        if (!properties.TryGetValue(key, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            if (required) errors.Add($"'{key}' is required");
            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            errors.Add($"'{key}' must be a string, not {Describe(element.ValueKind)}");
            return null;
        }

        var value = element.GetString()!.Trim();
        if (value.Length == 0)
        {
            // An empty optional field is treated as absent; an empty required one is missing.
            if (required) errors.Add($"'{key}' must not be empty");
            return null;
        }

        if (value.Length > maxLength)
        {
            errors.Add($"'{key}' is longer than {maxLength} characters");
            return null;
        }
        return value;
    }

    private static string? CheckDisplayText(string key, string value, List<string> errors)
    {
        if (value.Any(char.IsControl))
        {
            errors.Add($"'{key}' contains control characters");
            return null;
        }
        return value;
    }

    private static IReadOnlyList<string> ReadCapabilities(Dictionary<string, JsonElement> properties, List<string> errors)
    {
        if (!properties.TryGetValue("capabilities", out var element) || element.ValueKind == JsonValueKind.Null)
            return [];

        if (element.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"'capabilities' must be an array of strings, not {Describe(element.ValueKind)}");
            return [];
        }

        if (element.GetArrayLength() > MaxCapabilities)
        {
            errors.Add($"'capabilities' has more than {MaxCapabilities} entries");
            return [];
        }

        var result = new List<string>();
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                errors.Add($"'capabilities[{index}]' must be a string, not {Describe(item.ValueKind)}");
            }
            else
            {
                var capability = item.GetString()!;
                if (!PluginCapabilities.IsKnown(capability))
                    errors.Add($"'capabilities[{index}]' \"{Truncate(capability)}\" is not a known capability (expected one of: {string.Join(", ", PluginCapabilities.All.Order(StringComparer.Ordinal))})");
                else if (!result.Contains(capability, StringComparer.Ordinal))
                    result.Add(capability); // duplicates are harmless; keep the first
            }
            index++;
        }
        return result;
    }

    private static string Truncate(string value)
        => value.Length <= 64 ? value : value[..64] + "…";

    private static string Describe(JsonValueKind kind) => kind switch
    {
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "an array",
        JsonValueKind.String => "a string",
        JsonValueKind.Number => "a number",
        JsonValueKind.True or JsonValueKind.False => "a boolean",
        JsonValueKind.Null => "null",
        _ => kind.ToString().ToLower(CultureInfo.InvariantCulture),
    };
}
