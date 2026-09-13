namespace EDNexus.Core.Radio;

/// <summary>A single Icecast/MP3 live stream the radio player can tune to.</summary>
/// <param name="Id">Stable identifier, persisted in settings as <c>RadioLastStation</c>.</param>
/// <param name="Name">Display name.</param>
/// <param name="Description">Short blurb shown under the name in the UI.</param>
/// <param name="StreamUrl">Direct stream URL (MP3/Icecast).</param>
public sealed record RadioStation(string Id, string Name, string Description, string StreamUrl);

/// <summary>The static, built-in list of simulation/space radio stations offered by the player.</summary>
public static class RadioStationCatalog
{
    /// <summary>Every station EDNexus knows about, in display order.</summary>
    public static IReadOnlyList<RadioStation> Stations { get; } = new List<RadioStation>
    {
        new("radio-sidewinder", "Radio Sidewinder",
            "Elite Dangerous lore, music, and news",
            "https://radiosidewinder.out.airtime.pro:8000/radiosidewinder_b"),
        new("hutton-orbital-radio", "Hutton Orbital Radio",
            "Community news and talk",
            "https://quincy.torontocast.com/hutton"),
        new("simulator-radio", "Simulator Radio",
            "Gaming/simulator pop/dance music",
            "https://simulatorradio.stream/stream.mp3"),
        new("somafm-deep-space-one", "SomaFM - Deep Space One",
            "Deep ambient space music",
            "http://ice1.somafm.com/deepspaceone-128-mp3"),
        new("somafm-space-station-soma", "SomaFM - Space Station Soma",
            "Ambient/space music",
            "http://ice1.somafm.com/spacestation-128-mp3"),
        new("somafm-mission-control", "SomaFM - Mission Control",
            "Ambient space telemetry mix",
            "http://ice1.somafm.com/missioncontrol-128-mp3"),
    };

    /// <summary>Looks up a station by its stable id, or null if unknown (e.g. a station retired since it was saved).</summary>
    public static RadioStation? Find(string? id)
        => string.IsNullOrWhiteSpace(id) ? null : Stations.FirstOrDefault(s => s.Id == id);

    /// <summary>The station after <paramref name="currentId"/> in display order, wrapping around. Unknown/null id starts at the first station.</summary>
    public static RadioStation Next(string? currentId)
    {
        var idx = IndexOf(currentId);
        return Stations[idx < 0 ? 0 : (idx + 1) % Stations.Count];
    }

    /// <summary>The station before <paramref name="currentId"/> in display order, wrapping around. Unknown/null id starts at the first station.</summary>
    public static RadioStation Previous(string? currentId)
    {
        var idx = IndexOf(currentId);
        return Stations[idx < 0 ? 0 : (idx - 1 + Stations.Count) % Stations.Count];
    }

    private static int IndexOf(string? id)
    {
        for (var i = 0; i < Stations.Count; i++)
            if (Stations[i].Id == id) return i;
        return -1;
    }
}

/// <summary>Coarse playback state of the radio player.</summary>
public enum RadioPlaybackStatus
{
    /// <summary>Nothing loaded, or explicitly stopped.</summary>
    Stopped,

    /// <summary>Media is loading/connecting to the stream.</summary>
    Buffering,

    /// <summary>Audio is playing.</summary>
    Playing,

    /// <summary>Playback is paused (media stays loaded).</summary>
    Paused,

    /// <summary>The stream could not be played (bad URL, network failure, missing native VLC libs, etc.).</summary>
    Error,
}

/// <summary>Immutable snapshot of the radio player's current state, for UI consumption.</summary>
/// <param name="Enabled">Whether the radio feature is turned on.</param>
/// <param name="Station">The currently tuned station, if any.</param>
/// <param name="Status">Current playback status.</param>
/// <param name="Volume">Volume 0-100.</param>
/// <param name="Muted">Whether output is muted.</param>
/// <param name="LastError">The most recent playback error message, if <see cref="Status"/> is <see cref="RadioPlaybackStatus.Error"/>.</param>
public sealed record RadioPlayerSnapshot(
    bool Enabled,
    RadioStation? Station,
    RadioPlaybackStatus Status,
    int Volume,
    bool Muted,
    string? LastError);
