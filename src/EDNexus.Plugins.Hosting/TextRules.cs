using System.Globalization;

namespace EDNexus.Plugins.Hosting;

/// <summary>Shared checks for untrusted text (manifest display fields and package entry names).</summary>
internal static class TextRules
{
    /// <summary>
    /// Returns a reason (phrased to follow the field name, e.g. "contains control characters") if
    /// <paramref name="text"/> contains control characters, invisible <see cref="UnicodeCategory.Format"/>
    /// characters (bidi overrides, zero-width spaces/joiners, BOM), or unpaired surrogates;
    /// otherwise <see langword="null"/>.
    /// </summary>
    /// <param name="text">The text to check.</param>
    /// <param name="allowLineBreaks">Whether <c>\n</c>, <c>\r</c> and <c>\t</c> are permitted.</param>
    public static string? FindInvisibleOrInvalid(string text, bool allowLineBreaks)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(text, i) == UnicodeCategory.Format)
                    return $"contains an invisible formatting character (U+{char.ConvertToUtf32(ch, text[i + 1]):X4})";
                i++;
                continue;
            }
            if (char.IsSurrogate(ch))
                return "contains invalid Unicode (an unpaired surrogate)";
            if (char.IsControl(ch))
            {
                if (allowLineBreaks && ch is '\n' or '\r' or '\t')
                    continue;
                return "contains control characters";
            }
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.Format)
                return $"contains an invisible formatting character (U+{(int)ch:X4})";
        }
        return null;
    }
}
