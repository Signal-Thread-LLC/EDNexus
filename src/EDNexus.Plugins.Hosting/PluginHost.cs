using System.Reflection;
using EDNexus.Plugins.Abstractions;

namespace EDNexus.Plugins.Hosting;

/// <summary>
/// Discovers the plugins installed under a plugins root and loads each one into its own
/// collectible <see cref="System.Runtime.Loader.AssemblyLoadContext"/>.
/// <para>
/// The first <see cref="LoadAll"/> repairs interrupted installs
/// (<see cref="PluginInstaller.RecoverInterrupted"/>); then, on every pass, for each
/// <c>&lt;root&gt;/&lt;id&gt;/</c> folder the host parses and validates <c>plugin.json</c>, checks the
/// folder name matches the id and the id is unique, gates on SDK / app version, loads the entry
/// assembly, instantiates the entry type and calls <see cref="IEDNexusPlugin.Initialize"/> with a
/// context from the host's factory. A bad plugin never throws out of discovery or stops the others:
/// each folder gets a <see cref="PluginLoadResult"/> saying what happened and why.
/// </para>
/// <para>
/// Loading runs plugin code (constructors and <see cref="IEDNexusPlugin.Initialize"/>) on the
/// calling thread. Exceptions from that code are contained; a stack overflow or an
/// <see cref="Environment.FailFast(string)"/> cannot be, in-process.
/// </para>
/// <para>
/// A load context isolates dependency <em>versions</em>; it is <strong>not a security
/// boundary</strong>. Plugin code runs with the host's full trust, and any assembly a plugin does
/// not ship itself (including the host's own assemblies) resolves from the host process.
/// </para>
/// </summary>
public sealed class PluginHost : IDisposable
{
    private static readonly PluginRecoveryResult NoRecovery = new([], [], []);

    private readonly object _gate = new();
    private readonly Dictionary<string, LoadedPlugin> _loaded = new(StringComparer.Ordinal);
    private readonly Func<PluginManifest, IPluginContext> _contextFactory;
    private bool _disposed;
    private bool _loading;

    /// <param name="pluginsRoot">The plugins root (see <see cref="PluginPaths.Resolve()"/>). It need not exist.</param>
    /// <param name="appVersion">The running EDNexus version, for <see cref="PluginManifest.MinAppVersion"/>.</param>
    /// <param name="contextFactory">
    /// Builds the <see cref="IPluginContext"/> handed to each plugin's
    /// <see cref="IEDNexusPlugin.Initialize"/>. This is the only place a plugin's context is built.
    /// If the returned context is <see cref="IDisposable"/> the host disposes it when the plugin
    /// unloads or fails to initialise, so the bridge behind it can drop the plugin's subscriptions
    /// (which would otherwise keep the plugin in memory).
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

    /// <summary>
    /// What install recovery did on the first <see cref="LoadAll"/>, or <see langword="null"/>
    /// before it. Recovery runs once per host.
    /// </summary>
    public PluginRecoveryResult? Recovery { get; private set; }

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
    /// Discovers and loads every plugin under <see cref="PluginsRoot"/>. The first call on this
    /// host first runs <see cref="PluginInstaller.RecoverInterrupted"/>, which must not run
    /// concurrently with <see cref="PluginInstaller.Install"/> on the same root: do not install
    /// plugins while the first load is in progress. Later calls (after <see cref="UnloadAll"/>)
    /// do not repeat recovery. Never throws for a bad plugin or an unreadable root; see the
    /// returned report. With no plugins root, or an empty one, nothing is loaded.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Plugins are already loaded (call <see cref="UnloadAll"/> first), or a load pass is already
    /// in progress (including a call from plugin code or the context factory during a pass).
    /// </exception>
    /// <exception cref="ObjectDisposedException">The host has been disposed.</exception>
    public PluginDiscoveryReport LoadAll()
    {
        // _gate only guards state, never plugin code, so the UI can read Loaded (and a plugin's
        // Initialize can't deadlock the host) while a pass is running. _loading makes a second or
        // re-entrant pass fail fast instead.
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_loading)
                throw new InvalidOperationException("A plugin load pass is already in progress.");
            if (_loaded.Count > 0)
                throw new InvalidOperationException("Plugins are already loaded; call UnloadAll before loading again.");
            _loading = true;
        }

        try
        {
            // Must run before discovery: a replace interrupted by a crash leaves the plugin's
            // previous version in a hidden .replaced-* folder that only recovery puts back.
            var recovery = NoRecovery;
            if (Recovery is null)
            {
                recovery = PluginInstaller.RecoverInterrupted(PluginsRoot);
                Recovery = recovery;
            }
            var errors = recovery.Errors.Select(e => "plugin install recovery: " + e).ToList();

            var results = new List<PluginLoadResult>();
            foreach (var candidate in Discover(errors))
                results.Add(LoadCandidate(candidate));

            var report = new PluginDiscoveryReport(PluginsRoot, recovery, errors, results);
            LastReport = report;
            return report;
        }
        finally
        {
            lock (_gate)
                _loading = false;
        }
    }

    private PluginLoadResult LoadCandidate(Candidate candidate)
    {
        PluginLoadResult result;
        try
        {
            result = candidate.Reasons.Count > 0
                ? new PluginLoadResult(candidate.Directory, candidate.Manifest, PluginLoadStatus.Rejected, candidate.Reasons, null)
                : Load(candidate.Directory, candidate.Manifest!);
        }
        catch (Exception ex)
        {
            // Backstop: Load contains plugin failures itself, so this is a host bug, but one
            // folder must still never stop the rest.
            return new PluginLoadResult(candidate.Directory, candidate.Manifest, PluginLoadStatus.Failed,
                [Describe("the host failed while loading this plugin", ex)], null);
        }

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
        return result;
    }

    /// <summary>
    /// Shuts down and unloads plugin <paramref name="id"/>, or returns <see langword="null"/> when
    /// it is not loaded. Never throws for plugin misbehaviour. The load context is collected once
    /// nothing references the plugin's types — note that something outside the host (a static
    /// event such as <see cref="AppDomain.ProcessExit"/>, a running thread or timer the plugin
    /// started) can keep it alive indefinitely.
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

    /// <summary>
    /// Shuts down and unloads every loaded plugin. Every plugin is unloaded even if others
    /// misbehave; problems are in each result's <see cref="PluginUnloadResult.Errors"/>.
    /// </summary>
    public IReadOnlyList<PluginUnloadResult> UnloadAll()
    {
        List<LoadedPlugin> plugins;
        lock (_gate)
        {
            plugins = _loaded.Values.OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
            _loaded.Clear();
        }
        var results = new List<PluginUnloadResult>(plugins.Count);
        foreach (var plugin in plugins)
            results.Add(plugin.Unload()); // LoadedPlugin.Unload never throws
        return results;
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

    /// <summary>
    /// Loads one validated plugin. A <see cref="PluginLoadStatus.Failed"/> result means the host
    /// has released everything it holds for the plugin (context disposed, load context unloaded)
    /// after a best-effort <see cref="IEDNexusPlugin.Shutdown"/>; it cannot guarantee that plugin
    /// code has stopped (threads, timers or static subscriptions it started may still run).
    /// </summary>
    private PluginLoadResult Load(string dir, PluginManifest manifest)
    {
        if (PluginCompatibility.Check(manifest, AppVersion, HostSdkVersion) is { } incompatible)
            return Result(PluginLoadStatus.Incompatible, incompatible);

        if (ResolveEntryAssembly(dir, manifest.EntryAssembly, out var entryPath) is { } badPath)
            return Result(PluginLoadStatus.Rejected, badPath);

        PluginLoadContext? loadContext = null;
        IPluginContext? context = null;
        IEDNexusPlugin? initializing = null;
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
                return Unloaded(PluginLoadStatus.Rejected, $"entry assembly '{Show(manifest.EntryAssembly)}' is not a valid .NET assembly");
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
            initializing = instance;
            instance.Initialize(context);
            initializing = null;

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
            try
            {
                // Initialize may have started work before throwing; give the plugin its one
                // chance to stop it before its context goes away.
                if (initializing is not null)
                    TryShutdown(initializing, reasons);
            }
            finally
            {
                try
                {
                    DisposeContext(context, reasons);
                }
                finally
                {
                    loadContext?.Unload();
                }
            }
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
        // cannot name a path outside the plugin folder. This is a lexical check
        // (PluginPathRules.ResolveInside does not resolve links): a symlink or junction someone
        // placed inside the folder by hand is followed. That crosses no trust boundary — whoever
        // can create links in the plugins root can already put any DLL there.
        if (PluginPathRules.CheckRelativePath(entryAssembly) is { } unsafePath)
            return $"entry assembly '{Show(entryAssembly)}' is not a safe relative path: {unsafePath}";
        if (PluginPathRules.ResolveInside(dir, entryAssembly) is not { } resolved)
            return $"entry assembly '{Show(entryAssembly)}' resolves outside the plugin folder";
        if (!File.Exists(resolved))
            return $"entry assembly '{Show(entryAssembly)}' was not found in the plugin folder";
        fullPath = resolved;
        return null;
    }

    private static string? CheckEntryType(Type? type, PluginManifest manifest)
    {
        var contract = typeof(IEDNexusPlugin);
        var entryType = Show(manifest.EntryType);
        if (type is null)
            return $"entry type '{entryType}' was not found in '{Show(manifest.EntryAssembly)}'";
        if (!contract.IsAssignableFrom(type))
        {
            // Same name, different type identity: the plugin defined (or somehow bound) its own copy.
            return type.GetInterfaces().Any(i => i.FullName == contract.FullName)
                ? $"entry type '{entryType}' implements a different {contract.FullName} than the host's (from '{contract.Assembly.GetName().Name}')"
                : $"entry type '{entryType}' does not implement {contract.FullName}";
        }
        if (type.IsAbstract || type.IsInterface || type.ContainsGenericParameters)
            return $"entry type '{entryType}' is abstract or generic and cannot be instantiated";
        if (type.GetConstructor(Type.EmptyTypes) is null)
            return $"entry type '{entryType}' has no public parameterless constructor";
        return null;
    }

    private static string Show(string text) => TextRules.ForDisplay(text, 200);

    /// <summary>
    /// A one-line description of an exception from plugin code. Never throws: a plugin exception
    /// can override <see cref="Exception.Message"/> (or <see cref="object.ToString"/>) to throw,
    /// so neither is read unguarded. Only text is kept: holding the exception would keep the
    /// plugin's types, and so its load context, alive.
    /// </summary>
    internal static string Describe(string what, Exception ex)
    {
        string typeName = "an exception";
        try
        {
            if (ex is TargetInvocationException { InnerException: { } inner })
                ex = inner;
            typeName = TextRules.ForDisplay(ex.GetType().FullName, 200);
            string message;
            try
            {
                message = TextRules.ForDisplay(ex.Message);
            }
            catch
            {
                message = $"<message unavailable: {typeName}>";
            }
            return $"{what}: {typeName}: {message}";
        }
        catch
        {
            return $"{what}: {typeName}: <message unavailable>";
        }
    }

    /// <summary>Calls <see cref="IEDNexusPlugin.Shutdown"/>, recording (never throwing) a failure.</summary>
    internal static void TryShutdown(IEDNexusPlugin instance, List<string> errors)
    {
        try
        {
            instance.Shutdown();
        }
        catch (Exception ex)
        {
            errors.Add(Describe("Shutdown threw", ex));
        }
    }

    /// <summary>Disposes <paramref name="context"/> when it is disposable, recording (never throwing) a failure.</summary>
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
