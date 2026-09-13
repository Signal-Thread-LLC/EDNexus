namespace EDNexus.Core.Voice;

/// <summary>The moments EDNexus can speak a callout for.</summary>
public enum VoiceCalloutKind
{
    /// <summary>Main tank fuel has dropped to a quarter of capacity or below.</summary>
    FuelLow,

    /// <summary>An exobiology three-sample run has just been completed (the "Analyse" step).</summary>
    ScanComplete,

    /// <summary>A colonisation shopping-list commodity is now fully covered by the cargo hold.</summary>
    ShoppingListItemAcquired,
}

/// <summary>One spoken callout: which kind of moment it was, and the text to speak.</summary>
public sealed record VoiceCallout(VoiceCalloutKind Kind, string Text);

/// <summary>
/// Platform abstraction for text-to-speech. Implementations that can't speak at all (an unsupported
/// OS, or no TTS engine installed) report <see cref="IsAvailable"/> as false and treat every other
/// call as a no-op — see <see cref="NullVoice"/>.
/// </summary>
public interface IVoice
{
    /// <summary>Whether this platform has a working TTS engine behind it.</summary>
    bool IsAvailable { get; }

    /// <summary>Installed voice names, if the engine can enumerate them. Empty when unknown/unavailable.</summary>
    IReadOnlyList<string> AvailableVoices { get; }

    /// <summary>The voice currently selected, or null for the engine's default.</summary>
    string? SelectedVoice { get; }

    /// <summary>Select a voice by name (from <see cref="AvailableVoices"/>), or null for the default.</summary>
    void SetVoice(string? name);

    /// <summary>Set playback volume, 0-100.</summary>
    void SetVolume(int percent);

    /// <summary>Speak the given text. Returns immediately; playback is asynchronous. A no-op when unavailable.</summary>
    void Speak(string text);
}

/// <summary>
/// No-op <see cref="IVoice"/> for platforms without a native TTS implementation (Linux, macOS) and
/// for the CLI harness, which has nothing to speak through.
/// </summary>
public sealed class NullVoice : IVoice
{
    public bool IsAvailable => false;
    public IReadOnlyList<string> AvailableVoices { get; } = Array.Empty<string>();
    public string? SelectedVoice => null;
    public void SetVoice(string? name) { }
    public void SetVolume(int percent) { }
    public void Speak(string text) { }
}
