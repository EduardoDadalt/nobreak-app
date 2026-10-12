using NobreakMonitor.Core.Config;
using NobreakMonitor.Core.Models;

namespace NobreakMonitor.Core.Shutdown;

public enum ShutdownReason
{
    OnBattery,
    LowBattery,
}

public sealed record ShutdownCountdown(ShutdownReason Reason, DateTimeOffset StartedAt, DateTimeOffset Deadline)
{
    public int SecondsRemaining(DateTimeOffset now) => Math.Max(0, (int)Math.Ceiling((Deadline - now).TotalSeconds));
}

public enum PolicyAction
{
    None,
    Started,
    Shortened,
    Cancelled,
    Execute,
}

public sealed record PolicyDecision(PolicyAction Action, ShutdownCountdown? Countdown = null)
{
    public static readonly PolicyDecision None = new(PolicyAction.None);
}

/// <summary>
/// Decide quando desligar o computador a partir das leituras do nobreak.
/// Sem dependência de relógio ou de E/S, para ser testável.
/// </summary>
/// <remarks>
/// - Falha de rede inicia a contagem de <see cref="MonitorConfig.OnBatteryDelaySeconds"/>.
/// - Bateria baixa (ainda na bateria) inicia ou encurta a contagem para <see cref="MonitorConfig.LowBatteryDelaySeconds"/>.
/// - Volta da rede cancela a contagem.
/// - Leituras durante teste de bateria são ignoradas.
/// - Sem leitura (comunicação perdida), a contagem em curso continua.
/// - O cancelamento pelo usuário vale até a energia voltar; bateria baixa ainda rearma.
/// </remarks>
public sealed class ShutdownPolicy
{
    private ShutdownCountdown? _countdown;
    private bool _userCancelledOnBattery;
    private bool _userCancelledLowBattery;
    private bool _executed;

    public ShutdownCountdown? Countdown => _countdown;

    public PolicyDecision Evaluate(UpsStatus? status, MonitorConfig config, DateTimeOffset now)
    {
        var action = PolicyAction.None;

        if (status is not null)
        {
            var onBattery = status.UtilityFail && !status.TestInProgress;

            if (!onBattery)
            {
                _executed = false;
                _userCancelledOnBattery = false;
                _userCancelledLowBattery = false;
                return CancelCountdown();
            }

            if (_executed)
                return PolicyDecision.None;

            if (_countdown?.Reason == ShutdownReason.OnBattery && !config.ShutdownOnBattery)
                action = Cancelled(CancelCountdown());

            if (_countdown?.Reason == ShutdownReason.LowBattery && !config.ShutdownOnLowBattery)
                action = Cancelled(CancelCountdown());

            if (status.BatteryLow && config.ShutdownOnLowBattery && !_userCancelledLowBattery)
            {
                var deadline = now.AddSeconds(config.LowBatteryDelaySeconds);
                if (_countdown is null)
                {
                    _countdown = new ShutdownCountdown(ShutdownReason.LowBattery, now, deadline);
                    action = PolicyAction.Started;
                }
                else if (deadline < _countdown.Deadline)
                {
                    _countdown = _countdown with { Reason = ShutdownReason.LowBattery, Deadline = deadline };
                    action = PolicyAction.Shortened;
                }
            }

            if (_countdown is null && config.ShutdownOnBattery && !_userCancelledOnBattery)
            {
                _countdown = new ShutdownCountdown(ShutdownReason.OnBattery, now, now.AddSeconds(config.OnBatteryDelaySeconds));
                action = PolicyAction.Started;
            }
        }
        else if (_executed)
        {
            return PolicyDecision.None;
        }

        if (_countdown is not null && now >= _countdown.Deadline)
        {
            var due = _countdown;
            _countdown = null;
            _executed = true;
            return new PolicyDecision(PolicyAction.Execute, due);
        }

        return action == PolicyAction.None ? PolicyDecision.None : new PolicyDecision(action, _countdown);
    }

    /// <summary>Cancela a contagem em curso a pedido do usuário.</summary>
    public bool CancelByUser()
    {
        if (_countdown is null)
            return false;

        _userCancelledOnBattery = true;
        if (_countdown.Reason == ShutdownReason.LowBattery)
            _userCancelledLowBattery = true;

        _countdown = null;
        return true;
    }

    private PolicyDecision CancelCountdown()
    {
        if (_countdown is null)
            return PolicyDecision.None;

        var cancelled = _countdown;
        _countdown = null;
        return new PolicyDecision(PolicyAction.Cancelled, cancelled);
    }

    private static PolicyAction Cancelled(PolicyDecision decision) =>
        decision.Action == PolicyAction.Cancelled ? PolicyAction.Cancelled : PolicyAction.None;
}
