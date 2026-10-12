using NobreakMonitor.Core.Config;
using NobreakMonitor.Core.Events;
using NobreakMonitor.Core.Models;
using NobreakMonitor.Core.Shutdown;

namespace NobreakMonitor.Core.Tests;

public class ShutdownPolicyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    private static readonly MonitorConfig Config = new()
    {
        ShutdownOnBattery = true,
        OnBatteryDelaySeconds = 60,
        ShutdownOnLowBattery = true,
        LowBatteryDelaySeconds = 5,
    };

    private static UpsStatus Status(string bits) => new() { StatusBits = bits, Timestamp = T0 };

    private static readonly UpsStatus OnLine = Status("00001001");
    private static readonly UpsStatus OnBattery = Status("10001001");
    private static readonly UpsStatus LowBattery = Status("11001001");
    private static readonly UpsStatus TestOnBattery = Status("10001101");

    [Fact]
    public void Online_does_nothing()
    {
        var policy = new ShutdownPolicy();

        Assert.Equal(PolicyAction.None, policy.Evaluate(OnLine, Config, T0).Action);
        Assert.Null(policy.Countdown);
    }

    [Fact]
    public void On_battery_starts_countdown_and_executes_after_delay()
    {
        var policy = new ShutdownPolicy();

        var started = policy.Evaluate(OnBattery, Config, T0);
        Assert.Equal(PolicyAction.Started, started.Action);
        Assert.Equal(60, started.Countdown!.SecondsRemaining(T0));

        Assert.Equal(PolicyAction.None, policy.Evaluate(OnBattery, Config, T0.AddSeconds(59)).Action);
        Assert.Equal(PolicyAction.Execute, policy.Evaluate(OnBattery, Config, T0.AddSeconds(60)).Action);

        // Não repete enquanto continuar na bateria.
        Assert.Equal(PolicyAction.None, policy.Evaluate(OnBattery, Config, T0.AddSeconds(90)).Action);
    }

    [Fact]
    public void Power_return_cancels_countdown()
    {
        var policy = new ShutdownPolicy();
        policy.Evaluate(OnBattery, Config, T0);

        Assert.Equal(PolicyAction.Cancelled, policy.Evaluate(OnLine, Config, T0.AddSeconds(10)).Action);
        Assert.Null(policy.Countdown);
        Assert.Equal(PolicyAction.None, policy.Evaluate(OnLine, Config, T0.AddSeconds(120)).Action);
    }

    [Fact]
    public void Low_battery_shortens_countdown()
    {
        var policy = new ShutdownPolicy();
        policy.Evaluate(OnBattery, Config, T0);

        var shortened = policy.Evaluate(LowBattery, Config, T0.AddSeconds(10));
        Assert.Equal(PolicyAction.Shortened, shortened.Action);
        Assert.Equal(ShutdownReason.LowBattery, shortened.Countdown!.Reason);
        Assert.Equal(PolicyAction.Execute, policy.Evaluate(LowBattery, Config, T0.AddSeconds(15)).Action);
    }

    [Fact]
    public void Low_battery_triggers_even_when_on_battery_shutdown_disabled()
    {
        var config = Config with { ShutdownOnBattery = false };
        var policy = new ShutdownPolicy();

        Assert.Equal(PolicyAction.None, policy.Evaluate(OnBattery, config, T0).Action);
        Assert.Equal(PolicyAction.Started, policy.Evaluate(LowBattery, config, T0.AddSeconds(30)).Action);
        Assert.Equal(PolicyAction.Execute, policy.Evaluate(LowBattery, config, T0.AddSeconds(35)).Action);
    }

    [Fact]
    public void Zero_delay_executes_immediately()
    {
        var policy = new ShutdownPolicy();

        Assert.Equal(PolicyAction.Execute, policy.Evaluate(OnBattery, Config with { OnBatteryDelaySeconds = 0 }, T0).Action);
    }

    [Fact]
    public void Battery_test_is_ignored()
    {
        var policy = new ShutdownPolicy();

        Assert.Equal(PolicyAction.None, policy.Evaluate(TestOnBattery, Config, T0).Action);
        Assert.Null(policy.Countdown);
    }

    [Fact]
    public void Lost_communication_keeps_countdown_running()
    {
        var policy = new ShutdownPolicy();
        policy.Evaluate(OnBattery, Config, T0);

        Assert.Equal(PolicyAction.None, policy.Evaluate(null, Config, T0.AddSeconds(30)).Action);
        Assert.Equal(PolicyAction.Execute, policy.Evaluate(null, Config, T0.AddSeconds(61)).Action);
    }

    [Fact]
    public void User_cancel_holds_until_power_returns_but_low_battery_rearms()
    {
        var policy = new ShutdownPolicy();
        policy.Evaluate(OnBattery, Config, T0);

        Assert.True(policy.CancelByUser());
        Assert.Equal(PolicyAction.None, policy.Evaluate(OnBattery, Config, T0.AddSeconds(120)).Action);

        Assert.Equal(PolicyAction.Started, policy.Evaluate(LowBattery, Config, T0.AddSeconds(130)).Action);

        // Energia volta: tudo é rearmado para a próxima queda.
        policy.Evaluate(OnLine, Config, T0.AddSeconds(200));
        Assert.Equal(PolicyAction.Started, policy.Evaluate(OnBattery, Config, T0.AddSeconds(300)).Action);
    }

    [Fact]
    public void Disabling_option_cancels_running_countdown()
    {
        var policy = new ShutdownPolicy();
        policy.Evaluate(OnBattery, Config, T0);

        var decision = policy.Evaluate(OnBattery, Config with { ShutdownOnBattery = false }, T0.AddSeconds(5));
        Assert.Equal(PolicyAction.Cancelled, decision.Action);
        Assert.Null(policy.Countdown);
    }

    [Fact]
    public void Status_change_detector_reports_power_failure_and_restore()
    {
        var fail = StatusChangeDetector.Detect(OnBattery, OnLine);
        Assert.Contains(fail, e => e.Code == "UTILITY_FAIL");

        var restore = StatusChangeDetector.Detect(OnLine, OnBattery);
        Assert.Contains(restore, e => e.Code == "UTILITY_RESTORED");

        Assert.Empty(StatusChangeDetector.Detect(OnLine, null));
    }
}
