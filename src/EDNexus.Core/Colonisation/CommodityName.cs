using System.Text;

namespace EDNexus.Core.Colonisation;

/// <summary>
/// Reduces the several forms a commodity name arrives in — the journal symbol
/// (<c>$aluminium_name;</c>), a localised label (<c>"Fruit and Vegetables"</c>), or a bare
/// market symbol (<c>"aluminium"</c>) — to one canonical key so records from different events
/// (and the cargo hold) can be matched against each other.
/// </summary>
/// <remarks>
/// The game is not internally consistent: <c>ColonisationConstructionDepot</c> emits
/// <c>$aluminium_name;</c> while <c>ColonisationContribution</c> emits <c>$Aluminium_name;</c>,
/// and the <c>Cargo</c> event stores either the bare symbol or a localised label. Canonicalising
/// to lower-case alphanumerics collapses all of these to the same key.
/// </remarks>
public static class CommodityName
{
    /// <summary>
    /// Returns a stable lower-case, alphanumeric-only key for a commodity name in any of its
    /// journal forms. Returns an empty string for null/blank input.
    /// </summary>
    public static string Canonicalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var s = raw.Trim();

        // Strip the "$...._name;" wrapper the game uses for internal symbols.
        if (s.StartsWith('$'))
        {
            s = s.TrimStart('$');
            const string suffix = "_name;";
            if (s.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                s = s[..^suffix.Length];
        }

        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
            if (char.IsLetterOrDigit(ch))
                sb.Append(char.ToLowerInvariant(ch));

        return sb.ToString();
    }
}
