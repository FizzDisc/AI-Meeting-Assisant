using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiMeetingAssistant.Contracts;

public static class WorkerProtocol
{
    public const string CurrentVersion = "1.0";
}

public sealed record WorkerRequest(
    [property: JsonPropertyName("protocolVersion")] string ProtocolVersion,
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("payload")] JsonElement Payload);

public sealed record WorkerResponse(
    [property: JsonPropertyName("protocolVersion")] string ProtocolVersion,
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("payload")] JsonElement Payload,
    [property: JsonPropertyName("error")] WorkerError? Error = null);

public sealed record WorkerError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);

