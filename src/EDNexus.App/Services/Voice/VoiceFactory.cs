using System.Runtime.InteropServices;
using EDNexus.Core.Voice;

namespace EDNexus.App.Services.Voice;

/// <summary>Picks the right <see cref="IVoice"/> implementation for the current OS.</summary>
public static class VoiceFactory
{
    /// <summary>Windows gets SAPI text-to-speech; every other platform gets a no-op.</summary>
    public static IVoice Create() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? new SapiVoice() : new NullVoice();
}
