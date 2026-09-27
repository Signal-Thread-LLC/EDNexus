using System.Reflection;
using EDNexus.Plugins.Abstractions;

namespace EDNexus.Plugins.Hosting;

/// <summary>
/// Discovers the plugins installed under a plugins root and loads each one into its own
/// collectible <see cref="System.Runtime.Loader.AssemblyLoadContext"/>.
/// <para>
/// <see cref="LoadAll"/> first repairs interrupted installs
/// (<see cref="PluginInstaller.RecoverInterrupted"/>), then for every <c>&lt;root&gt;/&lt;id&gt;/</c>
/// folder: parses and validates <c>plugin.json</c>, checks the folder name matches the id and the id
/// is unique, gates on SDK / app version, loads the entry assembly, instantiates the entry type and
/// calls <see cref="IEDNexusPlugin.Initialize"/> with a context from the host's factory. A bad
/// plugin never throws out of discovery or stops the others: each folder gets a
/// <see cref="PluginLoadResult"/> saying what happened and why.
/// </para>
/// <para>
/// Loading runs plugin code (constructors and <see cref="IEDNexusPlugin.Initialize"/>) on the
/// calling thread. Exceptions from that code are contained; a stack overflow or an
/// <see cref="Environment.FailFast(string)"/> cannot be, in-process.
/// </para>
/// </summary>
public sealed class PluginHost : IDisposable
{
    private readonly object _gate = new();
    private readonly object _loadGate = new();
    private readonly Dictionary<string, LoadedPlugin> _loaded = new(StringComparer.Ordinal);
    private readonly Func<PluginManifest, IPluginContext> _contextFactory;
    private bool _disposed;

    /// <param name="pluginsRoot">The plugins root (see <see cref="PluginPaths.Resolve()"/>). It need not exist.</param>
    /// <param name="appVersion">The running EDNexus version, for <see cref="PluginManifest.MinAppVersion"/>.</param>
    /// <param name="contextFactory">
    /// Builds the <see cref="IPluginContext"/> handed to each plugin's
    /// <see cref="IEDNexusPlugin.Initialize"/>. If the returned context is <see cref="IDisposable"/>
    /// the host disposes it when the plugin unloads or fails to initialise, so the bridge behind it
    /// can drop the plugin's subscriptions (which would otherwise keep the plugin in memory).
    /// </param>
    public PluginHost(string pluginsRoot, SemanticVersion appVersion, Func<PluginManifest, IPluginContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(pluginsRoot);
        ArgumentNullException.ThrowIfNull(appVersion);
        ArgumentNullException.ThrowIfNull(contextFactory);
        PluginsRoot = Path.GetFullPath(pluginsRoot);
        AppVersion = appVersion;
        _contextFactory = contextFactory;
    }

    /// <summary>The (full) plugins root this host scans.</summary>
    public string PluginsRoot { get; }

    /// <summary>The app version plugins are gated against.</summary>
    public SemanticVersion AppVersion { get; }

    /// <summary>The SDK version plugins are gated against (overridable for tests).</summary>
    internal Version HostSdkVersion { get; init; } = PluginSdk.CurrentVersion;

    /// <summary>The report from the last <see cref="LoadAll"/>, or <see langword="null"/> before the first.</summary>
    public PluginDiscoveryReport? LastReport { get; private set; }

    /// <summary>A snapshot of the plugins currently loaded, in id order.</summary>
    public IReadOnlyList<LoadedPlugin> Loaded
    {
        get
        {
            lock (_gate)
                return _loaded.Values.OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
        }
    }

    /// <summary>
    /// Repairs interrupted installs, then discovers and loads every plugin under
    /// <see cref="PluginsRoot"/>. Never throws for a bad plugin or an unreadable root; see the
    /// returned report. With no plugins root, or an empty one, nothing is loaded.
    /// </summary>
    /// <exception cref="InvalidOperationException">Plugins are already loaded; <see cref="UnloadAll"/> first.</exception>
    /// <exception cref="ObjectDisposedException">The host has been disposed.</exception>
    public PluginDiscoveryReport LoadAll()
    {
        // _loadGate serialises whole discovery passes; _gate only guards the loaded set, so the UI
        // can read Loaded while a plugin's Initialize is still running.
        lock (_loadGate)
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_loaded.Count > 0)
                    throw new InvalidOperationException("Plugins are already loaded; call UnloadAll before loading again.");
            }

            // Must run before discovery: a replace interrupted by a crash leaves the plugin's
            // previous version in a hidden .replaced-* folder that only recovery puts back.
            var recovery = PluginInstaller.RecoverInterrupted(PluginsRoot);
            var errors = recovery.Errors.Select(e => "plugin install recovery: " + e).ToList();

            var results = new List<PluginLoadResult>();
            foreach (var candidate in Discover(errors))
            {
                var result = candidate.Reasons.Count > 0
                    ? new PluginLoadResult(candidate.Directory, candidate.Manifest, PluginLoadStatus.Rejected, candidate.Reasons, null)
                    : Load(candidate.Directory, candidate.Manifest!);
                if (result.Plugin is { } plugin)
                {
                    bool disposed;
                    lock (_gate)
                    {
                        disposed = _disposed;
                        if (!disposed)
                            _loaded[plugin.Id] = plugin;
                    }
                    // Disposed mid-pass (e.g. app shutdown): don't leave this one running.
                    if (disposed)
                        plugin.Unload();
                }
                results.Add(result);
            }

            var report = new PluginDiscoveryReport(PluginsRoot, recovery, errors, results);
            LastReport = report;
            return report;
        }
    }

    /// <summary>
    /// Shuts down and unloads plugin <paramref name="id"/>, or returns <see langword="null"/> when
    /// it is not loaded. The load context is collected once nothing references the plugin's types.
    /// </summary>
    public PluginUnloadResult? Unload(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        LoadedPlugin? plugin;
        lock (_gate)
        {
            if (!_loaded.Remove(id, out plugin))
                return null;
        }
        return plugin.Unload();
    }

    /// <summary>Shuts down and unloads every loaded plugin.</summary>
    public IReadOnlyList<PluginUnloadResult> UnloadAll()
    {
        List<LoadedPlugin> plugins;
        lock (_gate)
        {
            plugins = _loaded.Values.OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
            _loaded.Clear();
        }
        return plugins.Select(p => p.Unload()).ToList();
    }

    /// <summary>Unloads every plugin. Shutdown errors are swallowed; call <see cref="UnloadAll"/> to see them.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }
        UnloadAll();
    }

    // ---- discovery -------------------------------------------------------------------------

    private sealed record Candidate(string Directory, PluginManifest? Manifest, List<string> Reasons);

    /// <summary>Every plugin folder under the root, with its manifest and any static rejection reasons.</summary>
    private List<Candidate> Discover(List<string> errors)
    {
        string[] folders;
        try
        {
            if (!System.IO.Directory.Exists(PluginsRoot))
                return [];
            folders = System.IO.Directory.GetDirectories(PluginsRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            errors.Add($"plugins folder '{PluginsRoot}' could not be read: {TextRules.ForDisplay(ex.Message)}");
            return [];
        }

        var candidates = folders
            .Select(dir => (Dir: dir, Name: Path.GetFileName(dir)))
            // .staging-*, .replaced-*, .extract-* (installer/recovery work folders) and any other
            // hidden folder: ids can never start with '.', so none of these is a plugin.
            .Where(f => !f.Name.StartsWith('.'))
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .Select(f => Inspect(f.Dir, f.Name))
            .ToList();

        // An id claimed by more than one folder is ambiguous (usually a botched manual copy):
        // load none of them rather than guess, and name every folder involved.
        var parsed = candidates.Where(c => c.Manifest is not null).ToList();
        foreach (var id in PluginManifestParser.FindDuplicateIds(parsed.Select(c => c.Manifest!)))
        {
            var claimants = parsed.Where(c => string.Equals(c.Manifest!.Id, id, StringComparison.OrdinalIgnoreCase)).ToList();
            var names = string.Join(", ", claimants.Select(c => $"'{TextRules.ForDisplay(Path.GetFileName(c.Directory), 150)}'"));
            foreach (var claimant in claimants)
                claimant.Reasons.Add($"plugin id '{id}' is declared by more than one folder ({names}); none of them is loaded");
        }
        return candidates;
    }

    private static Candidate Inspect(string dir, string folderName)
    {
        var manifestPath = Path.Combine(dir, PluginManifestParser.FileName);
        if (!File.Exists(manifestPath))
            return new Candidate(dir, null, [$"no {PluginManifestParser.FileName} in the plugin folder"]);

        var parsed = PluginManifestParser.ParseFile(manifestPath);
        if (parsed.Manifest is not { } manifest)
            return new Candidate(dir, null, parsed.Errors.Select(e => "invalid manifest: " + e).ToList());

        var reasons = new List<string>();
        if (!string.Equals(manifest.Id, folderName, StringComparison.Ordinal))
            reasons.Add($"folder name '{TextRules.ForDisplay(folderName, 150)}' does not match plugin id '{manifest.Id}'");
        return new Candidate(dir, manifest, reasons);
    }

    // ---- loading ---------------------------------------------------------------------------

    private PluginLoadResult Load(string dir, PluginManifest manifest)
    {
        if (PluginCompatibility.Check(manifest, AppVersion, HostSdkVersion) is { } incompatible)
            return Result(PluginLoadStatus.Incompatible, incompatible);

        if (ResolveEntryAssembly(dir, manifest.EntryAssembly, out var entryPath) is { } badPath)
            return Result(PluginLoadStatus.Rejected, badPath);

        PluginLoadContext? loadContext = null;
        IPluginContext? context = null;
        var stage = "loading the entry assembly";
        try
        {
            loadContext = new PluginLoadContext(manifest.Id, entryPath!);
            Assembly assembly;
            try
            {
                assembly = loadContext.LoadFromAssemblyPath(entryPath!);
            }
            catch (BadImageFormatException)
            {
                return Unloaded(PluginLoadStatus.Rejected, $"entry assembly '{manifest.EntryAssembly}' is not a valid .NET assembly");
            }

            stage = "resolving the entry type";
            var type = assembly.GetType(manifest.EntryType, throwOnError: false, ignoreCase: false);
            if (CheckEntryType(type, manifest) is { } badType)
                return Unloaded(PluginLoadStatus.Rejected, badType);

            stage = "constructing the entry type";
            var instance = (IEDNexusPlugin)Activator.CreateInstance(type!)!;

            stage = "building the plugin context";
            context = _contextFactory(manifest);

            stage = "Initialize";
            instance.Initialize(context);

            var plugin = new LoadedPlugin(manifest, dir, entryPath!, loadContext, instance, context);
            return new PluginLoadResult(dir, manifest, PluginLoadStatus.Loaded, [], plugin);
        }
        catch (Exception ex)
        {
            return Unloaded(PluginLoadStatus.Failed, Describe($"{stage} failed", ex));
        }

        PluginLoadResult Unloaded(PluginLoadStatus status, string reason)
        {
            var reasons = new List<string> { reason };
            DisposeContext(context, reasons);
            loadContext?.Unload();
            return new PluginLoadResult(dir, manifest, status, reasons, null);
        }

        PluginLoadResult Result(PluginLoadStatus status, string reason)
            => new(dir, manifest, status, [reason], null);
    }

    /// <summary>
    /// Resolves <paramref name="entryAssembly"/> inside <paramref name="dir"/> with the same path
    /// rules the installer uses. Returns <see langword="null"/> and sets <paramref name="fullPath"/>
    /// when the file exists inside the folder; otherwise a reason.
    /// </summary>
    internal static string? ResolveEntryAssembly(string dir, string entryAssembly, out string? fullPath)
    {
        fullPath = null;
        // The parser already enforces this for real manifests; re-check so a hand-built manifest
        // cannot point the loader outside the plugin folder.
        if (PluginPathRules.CheckRelativePath(entryAssembly) is { } unsafePath)
            return $"entry assembly '{TextRules.ForDisplay(entryAssembly, 150)}' is not a safe relative path: {unsafePath}";
        if (PluginPathRules.ResolveInside(dir, entryAssembly) is not { } resolved)
            return $"entry assembly '{TextRules.ForDisplay(entryAssembly, 150)}' resolves outside the plugin folder";
        if (!File.Exists(resolved))
            return $"entry assembly '{entryAssembly}' was not found in the plugin folder";
        fullPath = resolved;
        return null;
    }

    private static string? CheckEntryType(Type? type, PluginManifest manifest)
    {
        var contract = typeof(IEDNexusPlugin);
        if (type is null)
            return $"entry type '{manifest.EntryType}' was not found in '{manifest.EntryAssembly}'";
        if (!contract.IsAssignableFrom(type))
        {
            // Same name, different type identity: the plugin defined (or somehow bound) its own copy.
            return type.GetInterfaces().Any(i => i.FullName == contract.FullName)
                ? $"entry type '{manifest.EntryType}' implements a different {contract.FullName} than the host's (from '{contract.Assembly.GetName().Name}')"
                : $"entry type '{manifest.EntryType}' does not implement {contract.FullName}";
        }
        if (type.IsAbstract || type.IsInterface || type.ContainsGenericParameters)
            return $"entry type '{manifest.EntryType}' is abstract or generic and cannot be instantiated";
        if (type.GetConstructor(Type.EmptyTypes) is null)
            return $"entry type '{manifest.EntryType}' has no public parameterless constructor";
        return null;
    }

    /// <summary>
    /// A one-line description of an exception from plugin code. Only text is kept: holding the
    /// exception would keep the plugin's types, and so its load context, alive.
    /// </summary>
    internal static string Describe(string what, Exception ex)
    {
        if (ex is TargetInvocationException { InnerException: { } inner })
            ex = inner;
        return $"{what}: {TextRules.ForDisplay(ex.GetType().FullName, 200)}: {TextRules.ForDisplay(ex.Message)}";
    }

    internal static void DisposeContext(IPluginContext? context, List<string> errors)
    {
        if (context is not IDisposable disposable)
            return;
        try
        {
            disposable.Dispose();
        }
        catch (Exception ex)
        {
            errors.Add(Describe("disposing the plugin context failed", ex));
        }
    }
}
