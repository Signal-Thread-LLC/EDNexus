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
    /// Random, locally-generated correlation id. It is the only stable key attached to reports and
    /// maps to nothing outside this machine — it is not derived from the commander or the OS user.
    /// </summary>
    public string InstallId { get; set; } = "";

    /// <summary>Opt-in configuration for the EDDN and Inara data reporters. Both default to off.</summary>
    public ReportingSettings Reporting { get; set; } = new();
}

/// <summary>Per-service opt-in for outbound data reporting. Nothing is sent unless enabled.</summary>
public sealed class ReportingSettings
{
    public EddnSettings Eddn { get; set; } = new();
    public InaraSettings Inara { get; set; } = new();
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
