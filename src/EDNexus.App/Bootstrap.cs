using EDNexus.App.Telemetry;
using EDNexus.Core.Settings;

namespace EDNexus.App;

/// <summary>Process-wide services created in <c>Program.Main</c> and read by the Avalonia app.</summary>
public sealed class Bootstrap
{
    public SettingsStore Store { get; }
    public AppSettings Settings { get; }
    public CrashReporting Crash { get; }

    /// <summary>Runtime developer-tools state (not persisted; off every launch).</summary>
    public DeveloperOptions Dev { get; } = new();

    public Bootstrap(SettingsStore store, AppSettings settings, CrashReporting crash)
    {
        Store = store;
        Settings = settings;
        Crash = crash;
    }

    /// <summary>Persist the current consent choice and start/stop reporting to match.</summary>
    public void ApplyCrashReportingChoice(bool enabled)
    {
        Settings.CrashReportingEnabled = enabled;
        Store.Save(Settings);
        if (enabled) Crash.TryStart(Settings);
        else Crash.Stop();
    }

    /// <summary>
    /// Persist the EDDN/Inara opt-in choices. The reporters read these flags live, so no restart is
    /// needed for the change to take effect.
    /// </summary>
    public void ApplyReportingChoice(bool eddnEnabled, bool inaraEnabled, string inaraApiKey)
    {
        Settings.Reporting.Eddn.Enabled = eddnEnabled;
        Settings.Reporting.Inara.Enabled = inaraEnabled;
        Settings.Reporting.Inara.ApiKey = inaraApiKey.Trim();
        Store.Save(Settings);
    }

    /// <summary>Persist the user's auto-update preference.</summary>
    public void ApplyAutoDownloadChoice(bool enabled)
    {
        Settings.AutoDownloadUpdates = enabled;
        Store.Save(Settings);
        System.Diagnostics.Trace.TraceInformation($"Settings: AutoDownloadUpdates set to {enabled}");
    }

    /// <summary>Persist the pinned engineering blueprint (null id clears the pin).</summary>
    public void ApplyEngineeringPin(string? blueprintId, int grade)
    {
        Settings.Engineering.PinnedBlueprintId = blueprintId;
        Settings.Engineering.PinnedGrade = grade;
        Store.Save(Settings);
    }

    /// <summary>Persist the Ship / On-foot toggle for the Engineering card.</summary>
    public void ApplyEngineeringOnFootMode(bool onFootMode)
    {
        Settings.Engineering.OnFootMode = onFootMode;
        Store.Save(Settings);
    }

    /// <summary>Persist the dashboard arrangement (card order, visibility, width, collapse state).</summary>
    public void ApplyDashboardLayout(IEnumerable<CardLayout> layout)
    {
        Settings.Dashboard.Cards = layout.ToList();
        Store.Save(Settings);
    }

    /// <summary>Persist the pinned Odyssey suit/weapon upgrade (null id clears the pin).</summary>
    public void ApplyOnFootPin(string? kind, string? id, int grade)
    {
        Settings.Engineering.PinnedOnFootKind = kind;
        Settings.Engineering.PinnedOnFootId = id;
        Settings.Engineering.PinnedOnFootGrade = grade;
        Store.Save(Settings);
    }

    /// <summary>Persist the route plotter's last plotted route (or an empty one, to clear it).</summary>
    public void ApplySavedRoute(RouteSettings route)
    {
        Settings.Route = route;
        Store.Save(Settings);
    }

    /// <summary>Persist the mining card's "worth mining" credit threshold.</summary>
    public void ApplyMiningThreshold(int credits)
    {
        Settings.Mining.MinValueThreshold = Math.Max(0, credits);
        Store.Save(Settings);
    }

    /// <summary>
    /// Fold newly observed galactic-average prices into the learned price book. A commodity's average
    /// price is a fixed constant, so once learned it is written once and never touched again — this
    /// only ever adds unseen commodities or corrects one this build's table had wrong.
    /// </summary>
    public void LearnCommodityPrices(IEnumerable<(string Symbol, int MeanPrice)> prices)
    {
        var changed = false;
        foreach (var (symbol, mean) in prices)
        {
            if (mean <= 0 || string.IsNullOrEmpty(symbol)) continue;
            if (Settings.Mining.KnownPrices.TryGetValue(symbol, out var existing) && existing == mean) continue;
            Settings.Mining.KnownPrices[symbol] = mean;
            changed = true;
        }
        if (changed) Store.Save(Settings);
    }

}
