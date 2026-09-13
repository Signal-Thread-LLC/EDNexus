using System.Runtime.InteropServices;
using EDNexus.Core.Voice;

namespace EDNexus.App.Services.Voice;

/// <summary>
/// Windows text-to-speech via late-bound SAPI COM (<c>SAPI.SpVoice</c>). Late binding (rather than a
/// generated COM interop assembly or the <c>System.Speech</c> package) keeps this type loadable on
/// every platform the app builds for — instantiating the COM object simply fails on non-Windows
/// runtimes, same as the rest of this file's best-effort error handling, instead of the assembly
/// itself requiring a Windows-only target framework.
/// </summary>
public sealed class SapiVoice : IVoice, IDisposable
{
    // SVSFlagsAsync: Speak() returns immediately and plays on a worker thread inside SAPI.
    private const int SvsFlagsAsync = 1;

    private readonly object? _voice;
    private readonly List<string> _voiceNames = new();
    private readonly List<object> _voiceTokens = new();

    public bool IsAvailable => _voice is not null;
    public IReadOnlyList<string> AvailableVoices => _voiceNames;
    public string? SelectedVoice { get; private set; }

    public SapiVoice()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        try
        {
            var type = Type.GetTypeFromProgID("SAPI.SpVoice");
            if (type is null) return;
            _voice = Activator.CreateInstance(type);
            LoadVoices();
        }
        catch
        {
            // No SAPI on this machine (or it's misconfigured) — degrade to unavailable rather than throw.
            _voice = null;
        }
    }

    private void LoadVoices()
    {
        if (_voice is null) return;
        try
        {
            dynamic voice = _voice;
            var tokens = voice.GetVoices("", "");
            int count = tokens.Count;
            for (var i = 0; i < count; i++)
            {
                dynamic token = tokens.Item(i);
                string name = token.GetDescription();
                _voiceNames.Add(name);
                _voiceTokens.Add(token);
            }
        }
        catch
        {
            // Leave the voice list empty — the engine's default voice still speaks without one.
        }
    }

    public void SetVoice(string? name)
    {
        SelectedVoice = name;
        if (_voice is null || string.IsNullOrEmpty(name)) return;
        try
        {
            var index = _voiceNames.FindIndex(v => string.Equals(v, name, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return;
            dynamic voice = _voice;
            voice.Voice = _voiceTokens[index];
        }
        catch
        {
            // Best-effort: keep whatever voice was already selected.
        }
    }

    public void SetVolume(int percent)
    {
        if (_voice is null) return;
        try { ((dynamic)_voice).Volume = Math.Clamp(percent, 0, 100); }
        catch { /* best-effort */ }
    }

    public void Speak(string text)
    {
        if (_voice is null || string.IsNullOrWhiteSpace(text)) return;
        try { ((dynamic)_voice).Speak(text, SvsFlagsAsync); }
        catch { /* best-effort — a missed callout is not worth surfacing an error over */ }
    }

    public void Dispose()
    {
        if (_voice is null || !OperatingSystem.IsWindows()) return;
        if (!Marshal.IsComObject(_voice)) return;
        try { Marshal.FinalReleaseComObject(_voice); }
        catch { /* best-effort cleanup */ }
    }
}
