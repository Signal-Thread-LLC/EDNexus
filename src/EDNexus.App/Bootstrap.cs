using System.Globalization;
using EDNexus.App.Telemetry;
using EDNexus.Core.Settings;
using IOverlay = EDNexus.Core.Overlay.IOverlay;
using IVoice = EDNexus.Core.Voice.IVoice;

namespace EDNexus.App;

/// <summary>Process-wide services created in <c>Program.Main</c> and read by the Avalonia app.</summary>
public sealed class Bootstrap
{
    public SettingsStore Store { get; }
    public AppSettings Settings { get; }
    public CrashReporting Crash { get; }

    /// <summary>Runtime developer-tools state (not persisted; off every launch).</summary>
    public DeveloperOptions Dev { get; } = new();

    /// <summary>The in-game HUD overlay — the Windows implementation, or a no-op elsewhere.</summary>
    public IOverlay Overlay { get; } = Services.Overlay.OverlayFactory.Create();

    /// <summary>Spoken callouts — Windows SAPI, or a no-op elsewhere.</summary>
    public IVoice Voice { get; } = Services.Voice.VoiceFactory.Create();

    public Bootstrap(SettingsStore store, AppSettings settings, CrashReporting crash)
    {
        Store = store;
        Settings = settings;
        Crash = crash;

        // Apply the saved voice choice up front so the very first callout already uses it.
        Voice.SetVoice(Settings.Voice.VoiceName);
        Voice.SetVolume(Settings.Voice.Volume);
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

    /// <summary>
    /// Roll the mining day over to <paramref name="now"/>'s local date if it has changed, freezing
    /// whatever totals stood as "last session" first. Idempotent — a no-op once already on today's
    /// date — so it is safe to call on every dashboard tick as well as every refined unit.
    /// </summary>
    public void EnsureMiningSessionDate(DateTimeOffset now)
    {
        var today = now.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var mining = Settings.Mining;
        if (mining.SessionDate == today) return;

        if (mining.SessionDate is not null)
        {
            mining.LastSessionDate = mining.SessionDate;
            mining.LastSessionValue = mining.SessionValue;
            mining.LastSessionUnits = mining.SessionUnits;
        }
        mining.SessionDate = today;
        mining.SessionValue = 0;
        mining.SessionUnits = 0;
        Store.Save(Settings);
    }

    /// <summary>
    /// Record one refined unit against today's running mining total (rolling the day over first if
    /// needed). <paramref name="credits"/> is 0 when the commodity's price isn't known yet — the unit
    /// still counts toward tonnage refined, just not toward the credit total.
    /// </summary>
    public void RecordMiningRefined(DateTimeOffset when, long credits)
    {
        EnsureMiningSessionDate(when);
        Settings.Mining.SessionValue += Math.Max(0, credits);
        Settings.Mining.SessionUnits += 1;
        Store.Save(Settings);
    }

    /// <summary>Persist the overlay's on/off state and show/hide the live window to match.</summary>
    public void ApplyOverlayChoice(bool enabled)
    {
        Settings.Overlay.Enabled = enabled;
        Store.Save(Settings);
        if (enabled) Overlay.Show();
        else Overlay.Hide();
    }

    /// <summary>
    /// Persist the voice-callout choices and apply the voice/volume live, so a change here doesn't
    /// need a restart to take effect.
    /// </summary>
    public void ApplyVoiceChoice(bool enabled, string? voiceName, int volume, IEnumerable<string> disabledCallouts)
    {
        Settings.Voice.Enabled = enabled;
        Settings.Voice.VoiceName = string.IsNullOrWhiteSpace(voiceName) ? null : voiceName;
        Settings.Voice.Volume = Math.Clamp(volume, 0, 100);
        Settings.Voice.DisabledCallouts = disabledCallouts.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Store.Save(Settings);

        Voice.SetVoice(Settings.Voice.VoiceName);
        Voice.SetVolume(Settings.Voice.Volume);
    }
}
