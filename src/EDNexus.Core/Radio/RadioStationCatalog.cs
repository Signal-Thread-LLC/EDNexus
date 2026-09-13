namespace EDNexus.Core.Radio;

/// <summary>
/// The static registry of community space-radio and simulator-radio stations the radio player ships
/// with. This is deliberately a fixed list (not user-editable yet) — every station here is a plain
/// MP3/Icecast stream verified to work directly with LibVLC.
/// </summary>
public static class RadioStationCatalog
{
    public static IReadOnlyList<RadioStation> Stations { get; } = new[]
    {
        new RadioStation(
            "radio-sidewinder",
            "Radio Sidewinder",
            "https://radiosidewinder.out.airtime.pro:8000/radiosidewinder_b",
            "Elite Dangerous lore, music, and community news.",
            "https://www.radiosidewinder.com/"),
        new RadioStation(
            "hutton-orbital-radio",
            "Hutton Orbital Radio",
            "https://quincy.torontocast.com/hutton",
            "Community news and talk for the long haul out to Hutton Orbital.",
            "http://huttonorbital.com/"),
        new RadioStation(
            "simulator-radio",
            "Simulator Radio",
            "https://simulatorradio.stream/stream.mp3",
            "Gaming and simulator community pop/dance music.",
            "https://simulatorradio.com/"),
        new RadioStation(
            "somafm-deep-space-one",
            "SomaFM — Deep Space One",
            "http://ice1.somafm.com/deepspaceone-128-mp3",
            "Deep ambient space music. Sit back, drift, and be not entertained.",
            "https://somafm.com/deepspaceone/"),
        new RadioStation(
            "somafm-space-station-soma",
            "SomaFM — Space Station Soma",
            "http://ice1.somafm.com/spacestation-128-mp3",
            "Ambient, space, and mid-tempo electronica for the flight deck.",
            "https://somafm.com/spacestation/"),
        new RadioStation(
            "somafm-mission-control",
            "SomaFM — Mission Control",
            "http://ice1.somafm.com/missioncontrol-128-mp3",
            "Celebrating NASA and the history of space exploration — mission audio and ambient telemetry mix.",
            "https://somafm.com/missioncontrol/"),
    };

    /// <summary>Looks up a station by <see cref="RadioStation.Id"/>; null when unknown or <paramref name="id"/> is empty.</summary>
    public static RadioStation? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        foreach (var station in Stations)
            if (string.Equals(station.Id, id, StringComparison.OrdinalIgnoreCase))
                return station;
        return null;
    }
}
