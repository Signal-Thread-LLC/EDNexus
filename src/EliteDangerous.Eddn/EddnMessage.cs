using System.Text.Json;
using System.Text.Json.Nodes;

namespace EliteDangerous.Eddn;

/// <summary>
/// A fully-formed EDDN upload: a <c>$schemaRef</c>, a header, and a schema-specific message body,
/// ready to be serialised and POSTed. Produced by <see cref="EddnJournalTransformer"/>.
/// </summary>
public sealed class EddnMessage
{
    /// <summary>The <c>$schemaRef</c> this message validates against (see <see cref="EddnSchemas"/>).</summary>
    public required string SchemaRef { get; init; }

    /// <summary>The complete <c>{ $schemaRef, header, message }</c> envelope.</summary>
    public required JsonObject Envelope { get; init; }

    /// <summary>Serialises the envelope to UTF-8 JSON bytes for upload.</summary>
    public byte[] ToUtf8Bytes() => JsonSerializer.SerializeToUtf8Bytes(Envelope);

    /// <summary>Serialises the envelope to a JSON string (handy for logging / tests).</summary>
    public override string ToString() => Envelope.ToJsonString();
}
