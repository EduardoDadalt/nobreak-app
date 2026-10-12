using System.Collections.Concurrent;
using System.IO.Ports;
using System.Reflection;
using NobreakMonitor.Core.Config;
using NobreakMonitor.Core.Events;
using NobreakMonitor.Core.Ipc;
using NobreakMonitor.Core.Models;
using NobreakMonitor.Core.Protocol;
using NobreakMonitor.Core.Shutdown;
using NobreakMonitor.Service.Shutdown;
using NobreakMonitor.Service.Storage;

namespace NobreakMonitor.Service.Ups;

/// <summary>
/// Loop principal: encontra o nobreak, lê Q1 periodicamente, registra eventos
/// e aplica a <see cref="ShutdownPolicy"/>. Todo acesso à serial acontece neste loop.
/// </summary>
public sealed class UpsMonitor(
    ConfigStore configStore,
    EventStore events,
    ISystemShutdown systemShutdown,
    ILogger<UpsMonitor> logger) : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(8);
    private const int MaxConsecutiveFailures = 3;

    private static readonly string Version =
        typeof(UpsMonitor).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";

    private readonly Lock _lock = new();
    private readonly ShutdownPolicy _policy = new();
    private readonly ConcurrentQueue<string> _pendingCommands = new();

    private SerialUpsConnection? _connection;
    private UpsInfo? _info;
    private UpsRating? _rating;
    private UpsStatus? _status;
    private string? _lastPort;
    private string? _configuredPort;
    private bool _rescanRequested;
    private int _failures;

    public ServiceState GetState()
    {
        var now = DateTimeOffset.Now;
        lock (_lock)
        {
            var countdown = _policy.Countdown;
            return new ServiceState
            {
                Connected = _connection is not null,
                PortName = _connection?.PortName ?? _lastPort,
                Info = _info,
                Rating = _rating,
                Status = _status,
                Shutdown = countdown is null
                    ? null
                    : new ShutdownCountdownState(countdown.Reason, countdown.Deadline, countdown.SecondsRemaining(now), configStore.Current.SimulateShutdown),
                ServiceVersion = Version,
                Now = now,
            };
        }
    }

    public bool CancelShutdown()
    {
        bool cancelled;
        lock (_lock)
            cancelled = _policy.CancelByUser();

        if (cancelled)
            events.Add(UpsEvent.Create("SHUTDOWN_CANCELLED_BY_USER", "Desligamento do computador cancelado pelo usuário", EventSeverity.Warning));
        return cancelled;
    }

    public void SendUpsCommand(UpsCommandRequest request)
    {
        var (command, description) = request.Kind switch
        {
            UpsCommandKind.Test10Seconds => (MegatecCommands.Test10Seconds, "Teste de 10 segundos"),
            UpsCommandKind.TestUntilLow => (MegatecCommands.TestUntilLow, "Teste até bateria baixa"),
            UpsCommandKind.TestMinutes => (MegatecCommands.TestMinutes(request.Minutes), $"Teste de {request.Minutes} minuto(s)"),
            UpsCommandKind.CancelTest => (MegatecCommands.CancelTest, "Cancelar teste"),
            UpsCommandKind.ToggleBeep => (MegatecCommands.ToggleBeep, "Alternar beep"),
            UpsCommandKind.CancelUpsShutdown => (MegatecCommands.CancelShutdown, "Cancelar desligamento do nobreak"),
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };

        lock (_lock)
        {
            if (_connection is null)
                throw new InvalidOperationException("Nobreak não conectado.");
        }

        _pendingCommands.Enqueue(command);
        events.Add(UpsEvent.Create("COMMAND", $"Comando enviado: {description}", EventSeverity.Info));
    }

    public void RequestRescan() => _rescanRequested = true;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        events.Add(UpsEvent.Create("SERVICE_STARTED", $"Serviço iniciado (v{Version})", EventSeverity.Info));

        var nextScan = DateTimeOffset.MinValue;
        var nextPoll = DateTimeOffset.MinValue;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    (nextScan, nextPoll) = await RunIterationAsync(nextScan, nextPoll, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Erro inesperado no loop de monitoramento");
                    Disconnect(logEvent: true);
                }

                await Task.Delay(500, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            Disconnect(logEvent: false);
            events.Add(UpsEvent.Create("SERVICE_STOPPED", "Serviço parado", EventSeverity.Info));
        }
    }

    private async Task<(DateTimeOffset NextScan, DateTimeOffset NextPoll)> RunIterationAsync(
        DateTimeOffset nextScan, DateTimeOffset nextPoll, CancellationToken stoppingToken)
    {
        var config = configStore.Current;
        var now = DateTimeOffset.Now;

        if (_rescanRequested || config.PortName != _configuredPort)
        {
            _rescanRequested = false;
            _configuredPort = config.PortName;
            Disconnect(logEvent: false);
            nextScan = now;
        }

        if (_connection is null && now >= nextScan)
        {
            await Task.Run(() => TryConnect(config), stoppingToken);
            nextScan = now + ScanInterval;
            nextPoll = now;
        }

        UpsStatus? fresh = null;
        if (_connection is not null)
        {
            await Task.Run(DrainCommands, stoppingToken);

            if (now >= nextPoll)
            {
                nextPoll = now.AddMilliseconds(config.PollIntervalMs);
                fresh = await Task.Run(Poll, stoppingToken);
            }
        }

        ApplyPolicy(fresh, config, DateTimeOffset.Now);

        return (nextScan, nextPoll);
    }

    private void TryConnect(MonitorConfig config)
    {
        IEnumerable<string> candidates = config.PortName is not null
            ? [config.PortName]
            : SerialPort.GetPortNames()
                .OrderByDescending(p => p == _lastPort)
                .ThenBy(p => p.Length).ThenBy(p => p, StringComparer.OrdinalIgnoreCase);

        foreach (var portName in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            SerialUpsConnection? connection = null;
            try
            {
                connection = SerialUpsConnection.Open(portName);

                // O TSApp identifica o nobreak pela resposta de "I" contendo "TS";
                // aqui aceitamos qualquer equipamento que responda Q1 válido.
                var info = MegatecParser.ParseInfo(connection.Query(MegatecCommands.Info) ?? "");
                var status = MegatecParser.ParseStatus(connection.Query(MegatecCommands.Status) ?? "", DateTimeOffset.Now);
                if (status is null)
                {
                    connection.Dispose();
                    continue;
                }

                var rating = MegatecParser.ParseRating(connection.Query(MegatecCommands.Rating) ?? "");
                if (rating is not null)
                    status = MegatecParser.ParseStatus(connection.Query(MegatecCommands.Status) ?? "", DateTimeOffset.Now, rating.BatteryVoltage) ?? status;

                lock (_lock)
                {
                    _connection = connection;
                    _info = info;
                    _rating = rating;
                    _status = status;
                    _lastPort = portName;
                    _failures = 0;
                }

                var model = info is null ? "" : $" — {info.Company} {info.Model}".TrimEnd();
                events.Add(UpsEvent.Create("COMM_OK", $"Nobreak conectado em {portName}{model}", EventSeverity.Success, status));
                return;
            }
            catch (UnauthorizedAccessException)
            {
                logger.LogWarning("Porta {Port} em uso por outro programa (o TSApp Desktop está aberto?)", portName);
                connection?.Dispose();
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or ArgumentException)
            {
                logger.LogDebug(ex, "Porta {Port} não respondeu como nobreak", portName);
                connection?.Dispose();
            }
        }
    }

    private UpsStatus? Poll()
    {
        var connection = _connection;
        if (connection is null)
            return null;

        UpsStatus? status = null;
        try
        {
            status = MegatecParser.ParseStatus(connection.Query(MegatecCommands.Status) ?? "", DateTimeOffset.Now, _rating?.BatteryVoltage);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Erro lendo Q1");
        }

        if (status is null)
        {
            if (++_failures >= MaxConsecutiveFailures)
                Disconnect(logEvent: true);
            return null;
        }

        _failures = 0;
        UpsStatus? previous;
        lock (_lock)
        {
            previous = _status;
            _status = status;
        }

        foreach (var ev in StatusChangeDetector.Detect(status, previous))
            events.Add(ev);

        return status;
    }

    private void DrainCommands()
    {
        while (_connection is { } connection && _pendingCommands.TryDequeue(out var command))
        {
            try
            {
                connection.Send(command);
                Thread.Sleep(300);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
            {
                logger.LogWarning(ex, "Falha ao enviar {Command}", command.TrimEnd());
            }
        }
    }

    private void ApplyPolicy(UpsStatus? fresh, MonitorConfig config, DateTimeOffset now)
    {
        PolicyDecision decision;
        lock (_lock)
            decision = _policy.Evaluate(fresh, config, now);

        var simulated = config.SimulateShutdown ? " [SIMULAÇÃO]" : "";

        switch (decision.Action)
        {
            case PolicyAction.Started:
            case PolicyAction.Shortened:
                var seconds = decision.Countdown!.SecondsRemaining(now);
                var why = decision.Countdown.Reason == ShutdownReason.LowBattery ? "bateria baixa" : "nobreak na bateria";
                events.Add(UpsEvent.Create("SHUTDOWN_SCHEDULED",
                    $"Computador será desligado em {seconds}s ({why}){simulated}", EventSeverity.Critical, _status));
                break;

            case PolicyAction.Cancelled:
                events.Add(UpsEvent.Create("SHUTDOWN_CANCELLED", "Desligamento do computador cancelado", EventSeverity.Success, _status));
                break;

            case PolicyAction.Execute:
                ExecuteShutdown(decision.Countdown!, config);
                break;
        }
    }

    private void ExecuteShutdown(ShutdownCountdown countdown, MonitorConfig config)
    {
        var why = countdown.Reason == ShutdownReason.LowBattery ? "bateria baixa" : "falta de energia";

        if (config.UpsOffAfterShutdownMinutes > 0 && _connection is not null)
        {
            var command = MegatecCommands.ShutdownUps(config.UpsOffAfterShutdownMinutes);
            if (config.SimulateShutdown)
            {
                events.Add(UpsEvent.Create("UPS_OFF_SIMULATED", $"[SIMULAÇÃO] Enviaria {command.TrimEnd()} ao nobreak", EventSeverity.Info));
            }
            else
            {
                _pendingCommands.Enqueue(command);
                DrainCommands();
                events.Add(UpsEvent.Create("UPS_OFF_SCHEDULED",
                    $"Nobreak programado para desligar em {config.UpsOffAfterShutdownMinutes} min", EventSeverity.Warning));
            }
        }

        if (config.SimulateShutdown)
        {
            events.Add(UpsEvent.Create("SHUTDOWN_SIMULATED", $"[SIMULAÇÃO] O computador seria desligado agora ({why})", EventSeverity.Critical, _status));
            return;
        }

        events.Add(UpsEvent.Create("SHUTDOWN_EXECUTED", $"Desligando o computador ({why})", EventSeverity.Critical, _status));
        systemShutdown.Shutdown($"NobreakMonitor: desligando por {why}.", config.ForceCloseApps);
    }

    private void Disconnect(bool logEvent)
    {
        SerialUpsConnection? old;
        lock (_lock)
        {
            old = _connection;
            _connection = null;
            _failures = 0;
        }

        if (old is null)
            return;

        old.Dispose();
        if (logEvent)
            events.Add(UpsEvent.Create("COMM_LOST", $"Comunicação com o nobreak perdida ({old.PortName})", EventSeverity.Warning, _status));
    }
}
