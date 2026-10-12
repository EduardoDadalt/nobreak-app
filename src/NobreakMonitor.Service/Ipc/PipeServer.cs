using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using NobreakMonitor.Core.Config;
using NobreakMonitor.Core.Events;
using NobreakMonitor.Core.Ipc;
using NobreakMonitor.Service.Storage;
using NobreakMonitor.Service.Ups;

namespace NobreakMonitor.Service.Ipc;

/// <summary>
/// Atende o app de configuração pelo named pipe \\.\pipe\NobreakMonitor.
/// Protocolo: uma requisição JSON por linha, uma resposta JSON por linha.
/// </summary>
public sealed class PipeServer(
    UpsMonitor monitor,
    ConfigStore configStore,
    EventStore events,
    ILogger<PipeServer> logger) : BackgroundService
{
    private const int MaxClients = 8;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = NamedPipeServerStreamAcl.Create(
                    IpcProtocol.PipeName, PipeDirection.InOut, MaxClients, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous, 0, 0, CreateSecurity());
            }
            catch (IOException ex)
            {
                // Todas as instâncias ocupadas: espera alguém desconectar.
                logger.LogDebug(ex, "Sem instância de pipe livre");
                await Task.Delay(1000, stoppingToken);
                continue;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Uma falha do pipe nunca pode derrubar o serviço (e com ele o monitor de desligamento).
                logger.LogError(ex, "Falha ao criar o named pipe");
                await Task.Delay(5000, stoppingToken);
                continue;
            }

            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                await pipe.DisposeAsync();
                if (stoppingToken.IsCancellationRequested)
                    return;
                logger.LogError(ex, "Falha aguardando conexão no named pipe");
                await Task.Delay(1000, stoppingToken);
                continue;
            }

            _ = Task.Run(() => HandleClientAsync(pipe, stoppingToken), stoppingToken);
        }
    }

    private static PipeSecurity CreateSecurity()
    {
        var security = new PipeSecurity();
        // A conta do próprio serviço precisa poder criar novas instâncias do pipe
        // (sem isso, rodando fora do LocalSystem, a 2ª instância falha com acesso negado).
        security.AddAccessRule(new PipeAccessRule(
            WindowsIdentity.GetCurrent().User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        return security;
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        await using var _ = pipe;
        var utf8 = new UTF8Encoding(false);
        using var reader = new StreamReader(pipe, utf8, false, 4096, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, utf8, 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };

        try
        {
            while (!ct.IsCancellationRequested && pipe.IsConnected)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null)
                    break;

                var response = Handle(line);
                await writer.WriteLineAsync(JsonSerializer.Serialize(response, IpcProtocol.JsonOptions).AsMemory(), ct);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // Cliente desconectou ou serviço parando.
        }
    }

    private IpcResponse Handle(string line)
    {
        try
        {
            var request = JsonSerializer.Deserialize<IpcRequest>(line, IpcProtocol.JsonOptions)
                ?? throw new InvalidDataException("Requisição vazia.");

            return request.Command switch
            {
                IpcCommands.GetState => Ok(monitor.GetState()),
                IpcCommands.GetConfig => Ok(configStore.Current),
                IpcCommands.SetConfig => Ok(SaveConfig(Payload<MonitorConfig>(request))),
                IpcCommands.GetEvents => Ok(events.Latest(Payload<GetEventsRequest>(request).Limit)),
                IpcCommands.CancelShutdown => Ok(monitor.CancelShutdown()),
                IpcCommands.SendUpsCommand => Ok(SendCommand(Payload<UpsCommandRequest>(request))),
                IpcCommands.Rescan => Ok(Rescan()),
                _ => new IpcResponse(false, $"Comando desconhecido: {request.Command}"),
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or InvalidOperationException or ArgumentException or IOException)
        {
            return new IpcResponse(false, ex.Message);
        }
    }

    private MonitorConfig SaveConfig(MonitorConfig config)
    {
        var saved = configStore.Save(config);
        events.Add(UpsEvent.Create("CONFIG_CHANGED", "Configuração alterada", EventSeverity.Info));
        return saved;
    }

    private bool SendCommand(UpsCommandRequest command)
    {
        monitor.SendUpsCommand(command);
        return true;
    }

    private bool Rescan()
    {
        monitor.RequestRescan();
        return true;
    }

    private static T Payload<T>(IpcRequest request) =>
        request.Payload is { } element
            ? element.Deserialize<T>(IpcProtocol.JsonOptions) ?? throw new InvalidDataException("Payload inválido.")
            : throw new InvalidDataException("Payload ausente.");

    private static IpcResponse Ok<T>(T value) =>
        new(true, null, JsonSerializer.SerializeToElement(value, IpcProtocol.JsonOptions));
}
