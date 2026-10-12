using NobreakMonitor.Core.Models;

namespace NobreakMonitor.Core.Events;

public enum EventSeverity
{
    Info,
    Success,
    Warning,
    Critical,
}

public sealed record UpsEvent(
    DateTimeOffset Timestamp,
    string Code,
    string Message,
    EventSeverity Severity,
    double? InputVoltage = null,
    double? BatteryVoltage = null,
    string? StatusBits = null)
{
    public static UpsEvent Create(string code, string message, EventSeverity severity, UpsStatus? status = null) =>
        new(DateTimeOffset.Now, code, message, severity, status?.InputVoltage, status?.BatteryVoltage, status?.StatusBits);
}
