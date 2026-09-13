using System.Text.Json;

namespace EDNexus.Ebs.Models;

/// <summary>The body of a <c>POST /api/update-state</c> request from the desktop client.</summary>
/// <param name="State">
/// The commander state snapshot to broadcast to viewers, opaque to the EBS. The broadcaster channel
/// is derived from the caller's verified JWT, never taken from the request body, so a compromised
/// client cannot spoof updates for a channel it does not own.
/// </param>
public sealed record UpdateStateRequest(JsonElement State);
