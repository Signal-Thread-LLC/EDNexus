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
}
