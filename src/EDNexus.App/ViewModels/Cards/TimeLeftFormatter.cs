namespace EDNexus.App.ViewModels;

/// <summary>
/// Shared "how long is left" formatting for cards with a countdown — Community Goals and Missions
/// both need the same compact "Xd Yh" / "Xh Ym" / "Xm" rendering, so it lives in one place rather
/// than being copied per card.
/// </summary>
internal static class TimeLeftFormatter
{
    /// <summary>Below this, a countdown is worth calling out as urgent (e.g. highlighted in the UI).</summary>
    public static readonly TimeSpan UrgentThreshold = TimeSpan.FromHours(2);

    public static string Format(TimeSpan left)
    {
        if (left.TotalDays >= 1) return $"{(int)left.TotalDays}d {left.Hours}h";
        if (left.TotalHours >= 1) return $"{(int)left.TotalHours}h {left.Minutes}m";
        return $"{(int)left.TotalMinutes}m";
    }
}
