using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NobreakMonitor.Core.Config;
using NobreakMonitor.Core.Events;
using NobreakMonitor.Core.Ipc;
using NobreakMonitor.Core.Shutdown;

namespace NobreakMonitor.App.ViewModels;

public sealed record EventRow(string Time, string Message, EventSeverity Severity, Brush Brush);

public sealed class MainViewModel : ObservableObject
{
    private static readonly Brush Green = Freeze(new SolidColorBrush(Color.FromRgb(0x1e, 0x9a, 0x3a)));
    private static readonly Brush Orange = Freeze(new SolidColorBrush(Color.FromRgb(0xd9, 0x7a, 0x00)));
    private static readonly Brush Red = Freeze(new SolidColorBrush(Color.FromRgb(0xc6, 0x28, 0x28)));
    private static readonly Brush Gray = Freeze(new SolidColorBrush(Color.FromRgb(0x75, 0x75, 0x75)));

    private readonly PipeClient _client = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _polling;
    private int _ticks;
    private bool _configLoaded;
    private ServiceState? _last;

    public MainViewModel()
    {
        _timer.Tick += async (_, _) => await PollAsync();

        Test10Seconds = UpsCommand(UpsCommandKind.Test10Seconds);
        TestUntilLow = UpsCommand(UpsCommandKind.TestUntilLow);
        CancelTest = UpsCommand(UpsCommandKind.CancelTest);
        ToggleBeep = UpsCommand(UpsCommandKind.ToggleBeep);
        CancelUpsShutdown = UpsCommand(UpsCommandKind.CancelUpsShutdown);
        TestMinutes = new RelayCommand(() => RunAsync(() =>
            _client.SendUpsCommandAsync(new UpsCommandRequest(UpsCommandKind.TestMinutes, TestMinutesValue)), "Comando enviado."),
            () => Connected && TestMinutesValue is >= 1 and <= 99);
        CancelComputerShutdown = new RelayCommand(() => RunAsync(() => _client.CancelShutdownAsync(), "Desligamento cancelado."),
            () => CountdownActive);
        Rescan = new RelayCommand(() => RunAsync(() => _client.RescanAsync(), "Procurando o nobreak..."), () => ServiceOnline);
        SaveConfig = new RelayCommand(SaveConfigAsync, () => ServiceOnline);
        ReloadConfig = new RelayCommand(LoadConfigAsync, () => ServiceOnline);
        RefreshEvents = new RelayCommand(LoadEventsAsync, () => ServiceOnline);
    }

    /// <summary>Alerta para a bandeja: título, mensagem, crítico.</summary>
    public event Action<string, string, bool>? Alert;

    /// <summary>Pede para trazer a janela à frente (ex.: contagem de desligamento iniciada).</summary>
    public event Action? ShowRequested;

    public void Start()
    {
        _timer.Start();
        _ = PollAsync();
    }

    // ---- Estado do serviço / nobreak ----

    public bool ServiceOnline { get => _serviceOnline; private set => Set(ref _serviceOnline, value); }
    private bool _serviceOnline;

    public string Headline { get => _headline; private set => Set(ref _headline, value); }
    private string _headline = "Conectando ao serviço...";

    public string SubHeadline { get => _subHeadline; private set => Set(ref _subHeadline, value); }
    private string _subHeadline = "";

    public Brush HeadlineBrush { get => _headlineBrush; private set => Set(ref _headlineBrush, value); }
    private Brush _headlineBrush = Gray;

    public bool Connected { get => _connected; private set => Set(ref _connected, value); }
    private bool _connected;

    public string InputVoltage { get => _inputVoltage; private set => Set(ref _inputVoltage, value); }
    private string _inputVoltage = "—";

    public string OutputVoltage { get => _outputVoltage; private set => Set(ref _outputVoltage, value); }
    private string _outputVoltage = "—";

    public string Load { get => _load; private set => Set(ref _load, value); }
    private string _load = "—";

    public string Frequency { get => _frequency; private set => Set(ref _frequency, value); }
    private string _frequency = "—";

    public string BatteryVoltage { get => _batteryVoltage; private set => Set(ref _batteryVoltage, value); }
    private string _batteryVoltage = "—";

    public int BatteryPercent { get => _batteryPercent; private set => Set(ref _batteryPercent, value); }
    private int _batteryPercent;

    public string Temperature { get => _temperature; private set => Set(ref _temperature, value); }
    private string _temperature = "—";

    public string Flags { get => _flags; private set => Set(ref _flags, value); }
    private string _flags = "";

    public string DeviceInfo { get => _deviceInfo; private set => Set(ref _deviceInfo, value); }
    private string _deviceInfo = "";

    public bool CountdownActive { get => _countdownActive; private set => Set(ref _countdownActive, value); }
    private bool _countdownActive;

    public string CountdownText { get => _countdownText; private set => Set(ref _countdownText, value); }
    private string _countdownText = "";

    public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }
    private string _statusMessage = "";

    public ObservableCollection<EventRow> Events { get; } = [];

    // ---- Configuração ----

    public string PortName { get => _portName; set => Set(ref _portName, value); }
    private string _portName = "";

    public bool ShutdownOnBattery { get => _shutdownOnBattery; set => Set(ref _shutdownOnBattery, value); }
    private bool _shutdownOnBattery;

    public int OnBatteryDelaySeconds { get => _onBatteryDelaySeconds; set => Set(ref _onBatteryDelaySeconds, value); }
    private int _onBatteryDelaySeconds;

    public bool ShutdownOnLowBattery { get => _shutdownOnLowBattery; set => Set(ref _shutdownOnLowBattery, value); }
    private bool _shutdownOnLowBattery;

    public int LowBatteryDelaySeconds { get => _lowBatteryDelaySeconds; set => Set(ref _lowBatteryDelaySeconds, value); }
    private int _lowBatteryDelaySeconds;

    public bool ForceCloseApps { get => _forceCloseApps; set => Set(ref _forceCloseApps, value); }
    private bool _forceCloseApps;

    public double UpsOffAfterShutdownMinutes { get => _upsOffAfterShutdownMinutes; set => Set(ref _upsOffAfterShutdownMinutes, value); }
    private double _upsOffAfterShutdownMinutes;

    public bool SimulateShutdown { get => _simulateShutdown; set => Set(ref _simulateShutdown, value); }
    private bool _simulateShutdown;

    public int PollIntervalMs { get => _pollIntervalMs; set => Set(ref _pollIntervalMs, value); }
    private int _pollIntervalMs = 2000;

    public bool StartWithWindows
    {
        get => AutoStart.IsEnabled;
        set
        {
            AutoStart.Set(value);
            Set(ref _startWithWindows, value);
        }
    }
    private bool _startWithWindows;

    public int TestMinutesValue { get => _testMinutesValue; set => Set(ref _testMinutesValue, value); }
    private int _testMinutesValue = 1;

    // ---- Comandos ----

    public ICommand Test10Seconds { get; }
    public ICommand TestUntilLow { get; }
    public ICommand TestMinutes { get; }
    public ICommand CancelTest { get; }
    public ICommand ToggleBeep { get; }
    public ICommand CancelUpsShutdown { get; }
    public ICommand CancelComputerShutdown { get; }
    public ICommand Rescan { get; }
    public ICommand SaveConfig { get; }
    public ICommand ReloadConfig { get; }
    public ICommand RefreshEvents { get; }

    public Task CancelComputerShutdownAsync() => RunAsync(() => _client.CancelShutdownAsync(), "Desligamento cancelado.");

    private RelayCommand UpsCommand(UpsCommandKind kind) =>
        new(() => RunAsync(() => _client.SendUpsCommandAsync(new UpsCommandRequest(kind)), "Comando enviado."), () => Connected);

    private async Task PollAsync()
    {
        if (_polling)
            return;

        _polling = true;
        try
        {
            var state = await _client.GetStateAsync();
            var wasOnline = ServiceOnline;
            ServiceOnline = true;
            Apply(state);

            if (!_configLoaded)
                await LoadConfigAsync();
            if (!wasOnline || _ticks++ % 5 == 0)
                await LoadEventsAsync();
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            if (ServiceOnline || _last is null)
            {
                ServiceOnline = false;
                Connected = false;
                CountdownActive = false;
                Headline = "Serviço NobreakMonitor não está em execução";
                SubHeadline = "Instale/inicie o serviço (scripts\\install-service.ps1) e aguarde.";
                HeadlineBrush = Gray;
                _last = null;
            }
        }
        finally
        {
            _polling = false;
        }
    }

    private void Apply(ServiceState state)
    {
        var previous = _last;
        _last = state;
        Connected = state.Connected;

        var s = state.Status;
        DeviceInfo = string.Join(" · ", new[]
        {
            state.Info is { } i ? $"{i.Company} {i.Model} {i.Version}".Trim() : null,
            state.PortName,
            state.Rating is { } r ? $"Nominal {r.Voltage} / bateria {Num(r.BatteryVoltage)} V" : null,
            $"serviço v{state.ServiceVersion}",
        }.Where(x => !string.IsNullOrWhiteSpace(x)));

        if (!state.Connected || s is null)
        {
            Headline = "Nobreak não encontrado";
            SubHeadline = "Verifique o cabo USB. Feche o TSApp Desktop se ele estiver aberto (ele ocupa a porta).";
            HeadlineBrush = Gray;
            InputVoltage = OutputVoltage = Load = Frequency = BatteryVoltage = Temperature = "—";
            BatteryPercent = 0;
            Flags = "";
        }
        else
        {
            InputVoltage = $"{Num(s.InputVoltage)} V";
            OutputVoltage = $"{Num(s.OutputVoltage)} V";
            Load = $"{s.LoadPercent}%";
            Frequency = $"{Num(s.Frequency)} Hz";
            BatteryVoltage = $"{Num(s.BatteryVoltage)} V";
            BatteryPercent = s.BatteryPercent;
            Temperature = $"{Num(s.Temperature)} °C";

            if (s.UtilityFail)
            {
                Headline = s.BatteryLow ? "Na bateria — BATERIA BAIXA" : "Na bateria — falta de energia";
                HeadlineBrush = s.BatteryLow ? Red : Orange;
            }
            else
            {
                Headline = "Rede elétrica normal";
                HeadlineBrush = s.UpsFailed ? Red : Green;
            }
            SubHeadline = $"Bateria {s.BatteryPercent}% · carga {s.LoadPercent}% · atualizado {s.Timestamp.LocalDateTime:HH:mm:ss}";

            Flags = string.Join("   ", new[]
            {
                s.BeeperOn ? "🔔 Beep ligado" : "🔕 Beep desligado",
                s.TestInProgress ? "🧪 Teste em andamento" : null,
                s.BypassActive ? "↔ Bypass/regulação ativa" : null,
                s.ShutdownActive ? "⏻ Desligamento do nobreak agendado" : null,
                s.UpsFailed ? "⚠ Falha no nobreak" : null,
            }.Where(x => x is not null));

            if (previous?.Status is { } p && p.UtilityFail != s.UtilityFail)
                Alert?.Invoke("Nobreak", s.UtilityFail ? "Falta de energia — nobreak na bateria." : "Energia restabelecida.", s.UtilityFail);
        }

        if (previous is { Connected: true } && !state.Connected)
            Alert?.Invoke("Nobreak", "Comunicação com o nobreak perdida.", true);

        var countdown = state.Shutdown;
        CountdownActive = countdown is not null;
        if (countdown is not null)
        {
            var why = countdown.Reason == ShutdownReason.LowBattery ? "bateria baixa" : "falta de energia";
            CountdownText = $"O computador será desligado em {FormatSeconds(countdown.SecondsRemaining)} ({why})" +
                            (countdown.Simulated ? " — SIMULAÇÃO" : "");

            if (previous?.Shutdown is null)
            {
                Alert?.Invoke("Desligamento programado", CountdownText, true);
                ShowRequested?.Invoke();
            }
        }
    }

    private async Task LoadConfigAsync()
    {
        try
        {
            var c = await _client.GetConfigAsync();
            PortName = c.PortName ?? "";
            ShutdownOnBattery = c.ShutdownOnBattery;
            OnBatteryDelaySeconds = c.OnBatteryDelaySeconds;
            ShutdownOnLowBattery = c.ShutdownOnLowBattery;
            LowBatteryDelaySeconds = c.LowBatteryDelaySeconds;
            ForceCloseApps = c.ForceCloseApps;
            UpsOffAfterShutdownMinutes = c.UpsOffAfterShutdownMinutes;
            SimulateShutdown = c.SimulateShutdown;
            PollIntervalMs = c.PollIntervalMs;
            _configLoaded = true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException or System.Text.Json.JsonException)
        {
            StatusMessage = $"Erro ao ler configuração: {ex.Message}";
        }
    }

    private Task SaveConfigAsync() => RunAsync(async () =>
    {
        var saved = await _client.SetConfigAsync(new MonitorConfig
        {
            PortName = PortName,
            PollIntervalMs = PollIntervalMs,
            ShutdownOnBattery = ShutdownOnBattery,
            OnBatteryDelaySeconds = OnBatteryDelaySeconds,
            ShutdownOnLowBattery = ShutdownOnLowBattery,
            LowBatteryDelaySeconds = LowBatteryDelaySeconds,
            ForceCloseApps = ForceCloseApps,
            UpsOffAfterShutdownMinutes = UpsOffAfterShutdownMinutes,
            SimulateShutdown = SimulateShutdown,
        });
        // Reflete os valores ajustados pelo serviço (limites).
        OnBatteryDelaySeconds = saved.OnBatteryDelaySeconds;
        LowBatteryDelaySeconds = saved.LowBatteryDelaySeconds;
        UpsOffAfterShutdownMinutes = saved.UpsOffAfterShutdownMinutes;
        PortName = saved.PortName ?? "";
    }, "Configuração salva.");

    private async Task LoadEventsAsync()
    {
        try
        {
            var events = await _client.GetEventsAsync(200);
            if (Events.Count > 0 && events.Count > 0 && Events.Count == events.Count &&
                Events[0].Time == FormatTime(events[0]))
                return;

            Events.Clear();
            foreach (var e in events)
                Events.Add(new EventRow(FormatTime(e), e.Message, e.Severity, e.Severity switch
                {
                    EventSeverity.Critical => Red,
                    EventSeverity.Warning => Orange,
                    EventSeverity.Success => Green,
                    _ => Gray,
                }));
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException or System.Text.Json.JsonException)
        {
            // Mantém a lista atual; o próximo ciclo tenta de novo.
        }
    }

    private async Task RunAsync(Func<Task> action, string success)
    {
        try
        {
            await action();
            StatusMessage = success;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException or System.Text.Json.JsonException)
        {
            StatusMessage = $"Erro: {ex.Message}";
        }
        await PollAsync();
    }

    private static string FormatTime(UpsEvent e) => e.Timestamp.LocalDateTime.ToString("dd/MM/yyyy HH:mm:ss");

    private static string FormatSeconds(int seconds) =>
        seconds >= 60 ? $"{seconds / 60}min {seconds % 60:00}s" : $"{seconds}s";

    private static string Num(double v) => v.ToString("0.#", CultureInfo.CurrentCulture);

    private static Brush Freeze(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }
}
