using NobreakMonitor.Core.Models;

namespace NobreakMonitor.Core.Events;

/// <summary>
/// Compara duas leituras e gera eventos de transição dos bits de status,
/// equivalente ao core/domain/nb-status do TSApp Desktop.
/// </summary>
public static class StatusChangeDetector
{
    public static IReadOnlyList<UpsEvent> Detect(UpsStatus current, UpsStatus? previous)
    {
        if (previous is null)
            return [];

        var events = new List<UpsEvent>();

        if (current.UtilityFail != previous.UtilityFail)
            events.Add(current.UtilityFail
                ? UpsEvent.Create("UTILITY_FAIL", "Falha na rede elétrica — nobreak na bateria", EventSeverity.Warning, current)
                : UpsEvent.Create("UTILITY_RESTORED", "Rede elétrica restabelecida", EventSeverity.Success, current));

        if (current.BatteryLow != previous.BatteryLow)
            events.Add(current.BatteryLow
                ? UpsEvent.Create("LOW_BATTERY", "Bateria baixa", EventSeverity.Critical, current)
                : UpsEvent.Create("BATTERY_OK", "Bateria OK", EventSeverity.Info, current));

        if (current.BypassActive != previous.BypassActive)
            events.Add(UpsEvent.Create(current.BypassActive ? "BYPASS_ON" : "BYPASS_OFF",
                current.BypassActive ? "Bypass/regulação ativa" : "Bypass/regulação inativa", EventSeverity.Info, current));

        if (current.UpsFailed != previous.UpsFailed)
            events.Add(current.UpsFailed
                ? UpsEvent.Create("UPS_FAILED", "Nobreak sinalizou falha", EventSeverity.Critical, current)
                : UpsEvent.Create("UPS_OK", "Nobreak saiu do estado de falha", EventSeverity.Success, current));

        if (current.TestInProgress != previous.TestInProgress)
            events.Add(UpsEvent.Create(current.TestInProgress ? "TEST_STARTED" : "TEST_FINISHED",
                current.TestInProgress ? "Teste de bateria iniciado" : "Teste de bateria finalizado", EventSeverity.Info, current));

        if (current.ShutdownActive != previous.ShutdownActive)
            events.Add(UpsEvent.Create(current.ShutdownActive ? "UPS_SHUTDOWN_ARMED" : "UPS_SHUTDOWN_DISARMED",
                current.ShutdownActive ? "Desligamento do nobreak agendado" : "Desligamento do nobreak cancelado", EventSeverity.Info, current));

        if (current.BeeperOn != previous.BeeperOn)
            events.Add(UpsEvent.Create(current.BeeperOn ? "BEEP_ON" : "BEEP_OFF",
                current.BeeperOn ? "Beep ativado" : "Beep desativado", EventSeverity.Info, current));

        return events;
    }
}
