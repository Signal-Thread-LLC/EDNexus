using System.Reflection;

namespace EDNexus.App.Telemetry;

internal static class SentryConfig
{
    /// <summary>Dev/local override — set this env var to a DSN to enable reporting without a release build.</summary>
    public const string DsnEnvVar = "EDNEXUS_SENTRY_DSN";

    /// <summary>Assembly-metadata key the release build bakes the DSN into (see EDNexus.App.csproj).</summary>
    public const string DsnMetadataKey = "SentryDsn";

    /// <summary>
    /// Resolve the Sentry DSN. Priority: env var (dev) → assembly metadata injected at release build
    /// time from a CI secret. The DSN is never committed to the repo, so a plain source build returns
    /// <c>null</c> and reporting stays disabled.
    /// </summary>
    public static string? ResolveDsn()
    {
        var fromEnv = Environment.GetEnvironmentVariable(DsnEnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv.Trim();

        var meta = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == DsnMetadataKey);

        return string.IsNullOrWhiteSpace(meta?.Value) ? null : meta.Value.Trim();
    }
}
