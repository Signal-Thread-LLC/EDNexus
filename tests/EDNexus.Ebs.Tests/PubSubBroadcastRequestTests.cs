using System.Text.Json;
using EDNexus.Ebs.Models;

namespace EDNexus.Ebs.Tests;

public class PubSubBroadcastRequestTests
{
    private static JsonElement ParseState(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Create_BuildsBroadcastTargetAndSerializedMessage()
    {
        var state = ParseState("""{"system":"Sol","ship":"Anaconda"}""");

        var request = PubSubBroadcastRequest.Create("998877", state);

        Assert.Equal(new[] { "broadcast" }, request.Target);
        Assert.Equal("998877", request.BroadcasterId);
        Assert.Equal("""{"system":"Sol","ship":"Anaconda"}""", request.Message);
    }

    [Fact]
    public void Create_SerializesMessageAsAJsonStringNotANestedObject()
    {
        var state = ParseState("""{"a":1}""");

        var request = PubSubBroadcastRequest.Create("1", state);

        // The wrapping envelope Twitch expects must itself deserialize with "message" as a string
        // property, not a nested object, per the Helix PubSub contract.
        var envelope = JsonSerializer.Serialize(request);
        using var parsed = JsonDocument.Parse(envelope);
        Assert.Equal(JsonValueKind.String, parsed.RootElement.GetProperty("message").ValueKind);
        Assert.Equal(JsonValueKind.String, parsed.RootElement.GetProperty("broadcaster_id").ValueKind);
        Assert.Equal(JsonValueKind.Array, parsed.RootElement.GetProperty("target").ValueKind);
    }

    [Fact]
    public void Create_ThrowsWhenBroadcasterIdIsMissing()
    {
        var state = ParseState("{}");

        Assert.Throws<ArgumentException>(() => PubSubBroadcastRequest.Create("", state));
    }

    [Fact]
    public void Create_ThrowsWhenSerializedPayloadExceedsSizeLimit()
    {
        var big = new string('x', 6000);
        var state = ParseState($$"""{"blob":"{{big}}"}""");

        var ex = Assert.Throws<PubSubPayloadTooLargeException>(() => PubSubBroadcastRequest.Create("1", state, maxMessageBytes: 5000));

        Assert.True(ex.ActualBytes > 5000);
        Assert.Equal(5000, ex.MaxBytes);
    }

    [Fact]
    public void Create_AllowsPayloadExactlyAtTheLimit()
    {
        // Build a payload whose serialized form is exactly `limit` bytes.
        const int limit = 64;
        var envelopeOverhead = JsonSerializer.Serialize(new { blob = "" }).Length;
        var fillerLength = limit - envelopeOverhead;
        var filler = new string('a', Math.Max(0, fillerLength));
        var state = ParseState($$"""{"blob":"{{filler}}"}""");

        var message = JsonSerializer.Serialize(state);
        var actualBytes = System.Text.Encoding.UTF8.GetByteCount(message);

        var request = PubSubBroadcastRequest.Create("1", state, maxMessageBytes: actualBytes);

        Assert.Equal(message, request.Message);
    }
}
