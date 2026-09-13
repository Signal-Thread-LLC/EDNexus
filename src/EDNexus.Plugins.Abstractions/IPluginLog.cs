namespace EDNexus.Plugins.Abstractions;

/// <summary>
/// A logging sink scoped to a single plugin. The host prefixes/tags output with the plugin's
/// identity so log output from misbehaving plugins is easy to attribute and to silence.
/// </summary>
public interface IPluginLog
{
    /// <summary>Logs a diagnostic-level message, for detail useful only while developing the plugin.</summary>
    void Debug(string message);

    /// <summary>Logs a routine informational message.</summary>
    void Info(string message);

    /// <summary>Logs a warning about a recoverable, unexpected condition.</summary>
    void Warn(string message);

    /// <summary>Logs an error, optionally with the exception that caused it.</summary>
    void Error(string message, Exception? exception = null);
}
