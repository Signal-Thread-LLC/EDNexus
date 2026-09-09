namespace EDNexus.Core.Settings;

/// <summary>Persisted user settings. Kept deliberately small and UI-free.</summary>
public sealed class AppSettings
{
    /// <summary>
    /// Crash/error reporting consent. <c>null</c> = not yet asked (show the first-run prompt);
    /// <c>false</c> = declined (the default effect — nothing is sent); <c>true</c> = opted in.
    /// </summary>
    public bool? CrashReportingEnabled { get; set; }

    /// <summary>
    /// When true, downloaded updates are automatically fetched at startup (platform-specific asset
    /// from the GitHub Releases feed). Default is false to avoid surprise network activity.
    /// </summary>
    public bool AutoDownloadUpdates { get; set; } = false;

    /// <summary>
    /// Random, locally-generated correlation id. It is the only stable key attached to reports and
    /// maps to nothing outside this machine — it is not derived from the commander or the OS user.
    /// </summary>
    public string InstallId { get; set; } = "";

    /// <summary>Opt-in configuration for the EDDN and Inara data reporters. Both default to off.</summary>
    public ReportingSettings Reporting { get; set; } = new();

    /// <summary>The commander's pinned engineering goal, if any.</summary>
    public EngineeringSettings Engineering { get; set; } = new();

    /// <summary>
    /// The commander's dashboard arrangement — card order, visibility, width and collapse state.
    /// Empty until they first customise it, so a fresh install uses the shipped layout.
    /// </summary>
    public DashboardSettings Dashboard { get; set; } = new();

    /// <summary>The route plotter's last plotted route, so it survives a restart. Empty until one is plotted.</summary>
    public RouteSettings Route { get; set; } = new();

    /// <summary>The mining card's price threshold and learned galactic-average prices.</summary>
    public MiningSettings Mining { get; set; } = new();
}

/// <summary>
/// Settings for the mining card's "worth mining" highlight.
/// </summary>
/// <remarks>
/// Frontier doesn't expose a commodity's galactic average price anywhere outside of a station's
/// market screen, and there's no live API for it either — so rather than ship a table of numbers that
/// will drift out of date, EDNexus learns it the same way a commander would: every <c>MeanPrice</c>
/// seen in a docked market is remembered here, keyed by commodity, and never needs re-learning because
/// that figure is a fixed per-commodity constant, not something that fluctuates station to station.
/// </remarks>
public sealed class MiningSettings
{
    /// <summary>
    /// Galactic-average credit threshold: a material at or above this value is called out as worth
    /// mining. 0 (the default) means unset — nothing is highlighted until the commander picks a value.
    /// </summary>
    public int MinValueThreshold { get; set; }

    /// <summary>Learned galactic-average price per commodity (canonical symbol → credits).</summary>
    public Dictionary<string, int> KnownPrices { get; set; } = new();
}

/// <summary>
/// The route plotter card's last plotted route: inputs enough to redisplay it (and re-plot it)
/// without a network round-trip, plus where the stepper was left. A null/empty <see cref="From"/>
/// means no route is saved — the card starts blank, as before this existed.
/// </summary>
public sealed class RouteSettings
{
    public string? From { get; set; }
    public string? To { get; set; }

    /// <summary>Name of a <c>RouteMode</c> member (e.g. "NeutronHighway"), stored as text so it's a no-op to add modes later.</summary>
    public string Mode { get; set; } = "NeutronHighway";

    /// <summary>The neutron plot's jump-range text field, kept verbatim so a restore doesn't lose the commander's exact input.</summary>
    public string JumpRangeText { get; set; } = "50";

    /// <summary>Which hop the stepper was pointing at.</summary>
    public int StepIndex { get; set; }

    public List<SavedRouteHop> Hops { get; set; } = new();
}

/// <summary>
/// A persisted copy of a plotted waypoint — deliberately its own shape (not the engine's <c>RouteHop</c>)
/// so a change to the live route model never breaks deserializing an old saved route.
/// </summary>
public sealed class SavedRouteHop
{
    public string System { get; set; } = "";
    public int Jumps { get; set; }
    public bool IsNeutron { get; set; }
    public double DistanceJumpedLy { get; set; }
    public double DistanceRemainingLy { get; set; }
    public double? FuelUsed { get; set; }
    public double? FuelInTank { get; set; }
    public bool IsScoopable { get; set; }
    public bool MustRestock { get; set; }
    public double? RestockAmount { get; set; }
    public bool HasIcyRing { get; set; }
}

/// <summary>The single pinned blueprint the Engineering card focuses on. Null id means nothing pinned.</summary>
public sealed class EngineeringSettings
{
    /// <summary>Blueprint id from the engineering catalog (e.g. "fsd_increased_range"), or null.</summary>
    public string? PinnedBlueprintId { get; set; }

    /// <summary>Target grade for the pinned blueprint, 1–5.</summary>
    public int PinnedGrade { get; set; } = 5;

    /// <summary>When true, the Engineering card shows the on-foot (Odyssey) panel instead of the ship panel.</summary>
    public bool OnFootMode { get; set; }

    /// <summary>"suit" or "weapon" — which Odyssey catalog <see cref="PinnedOnFootId"/> refers to.</summary>
    public string? PinnedOnFootKind { get; set; }

    /// <summary>Suit or weapon id from the Odyssey catalog, or null if nothing pinned.</summary>
    public string? PinnedOnFootId { get; set; }

    /// <summary>Target grade for the pinned suit/weapon, 1–5.</summary>
    public int PinnedOnFootGrade { get; set; } = 5;
}

/// <summary>Per-service opt-in for outbound data reporting. Nothing is sent unless enabled.</summary>
public sealed class ReportingSettings
{
    public EddnSettings Eddn { get; set; } = new();
    public InaraSettings Inara { get; set; } = new();

    /// <summary>
    /// When true, the reporting log additionally records the (redacted) JSON payload of every EDDN
    /// and Inara upload — verbose, for validating exactly what was sent. The per-attempt summary
    /// (schema/status/result) is always logged regardless. Default off to keep the log compact.
    /// </summary>
    public bool LogPayloads { get; set; }
}

/// <summary>EDDN reporter settings. Uploads are anonymized by the relay.</summary>
public sealed class EddnSettings
{
    /// <summary>When true, contribute anonymized market/scan/travel data to EDDN.</summary>
    public bool Enabled { get; set; }
}

/// <summary>Inara reporter settings. Requires the commander's personal Inara API key.</summary>
public sealed class InaraSettings
{
    /// <summary>When true, sync commander travel/identity to Inara using <see cref="ApiKey"/>.</summary>
    public bool Enabled { get; set; }

    /// <summary>The commander's personal Inara API key (from their Inara account).</summary>
    public string ApiKey { get; set; } = "";
}
