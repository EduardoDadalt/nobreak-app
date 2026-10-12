using System.Text.Json;
using System.Text.Json.Serialization;
using NobreakMonitor.Core.Models;
using NobreakMonitor.Core.Shutdown;

namespace NobreakMonitor.Core.Ipc;

public static class IpcProtocol
{
    public const string PipeName = "NobreakMonitor";

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}

public static class IpcCommands
{
    public const string GetState = "getState";
    public const string GetConfig = "getConfig";
    public const string SetConfig = "setConfig";
    public const string GetEvents = "getEvents";
    public const string CancelShutdown = "cancelShutdown";
    public const string SendUpsCommand = "sendUpsCommand";
    public const string Rescan = "rescan";
}

/// <summary>Uma linha JSON enviada pelo app.</summary>
public sealed record IpcRequest(string Command, JsonElement? Payload = null);

/// <summary>Uma linha JSON de resposta do serviço.</summary>
public sealed record IpcResponse(bool Ok, string? Error = null, JsonElement? Payload = null);

public enum UpsCommandKind
{
    Test10Seconds,
    TestUntilLow,
    TestMinutes,
    CancelTest,
    ToggleBeep,
    CancelUpsShutdown,
}

public sealed record UpsCommandRequest(UpsCommandKind Kind, int Minutes = 0);

public sealed record GetEventsRequest(int Limit = 100);

public sealed record ShutdownCountdownState(ShutdownReason Reason, DateTimeOffset Deadline, int SecondsRemaining, bool Simulated);

public sealed record ServiceState
{
    public bool Connected { get; init; }
    public string? PortName { get; init; }
    public UpsInfo? Info { get; init; }
    public UpsRating? Rating { get; init; }
    public UpsStatus? Status { get; init; }
    public ShutdownCountdownState? Shutdown { get; init; }
    public string ServiceVersion { get; init; } = "";
    public DateTimeOffset Now { get; init; }
}
