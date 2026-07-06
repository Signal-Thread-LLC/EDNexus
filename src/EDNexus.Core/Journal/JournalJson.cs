using System.Text.Json;
using System.Text.Json.Serialization;

namespace EDNexus.Core.Journal;

/// <summary>Shared JSON options for reading Elite Dangerous journal/status payloads.</summary>
public static class JournalJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        // ED events are PascalCase ("StarSystem") but envelope keys are lowercase
        // ("event", "timestamp"); case-insensitive covers both.
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };
}
