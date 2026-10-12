using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace NobreakMonitor.Core.Ipc;

/// <summary>
/// Cliente do named pipe do serviço. Mantém uma conexão e reconecta
/// automaticamente na próxima chamada se ela cair.
/// </summary>
public sealed class PipeClient : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _pipeName;
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public PipeClient(string pipeName = IpcProtocol.PipeName) => _pipeName = pipeName;

    public Task<ServiceState> GetStateAsync(CancellationToken ct = default) =>
        RequestAsync<ServiceState>(IpcCommands.GetState, null, ct);

    public Task<Config.MonitorConfig> GetConfigAsync(CancellationToken ct = default) =>
        RequestAsync<Config.MonitorConfig>(IpcCommands.GetConfig, null, ct);

    public Task<Config.MonitorConfig> SetConfigAsync(Config.MonitorConfig config, CancellationToken ct = default) =>
        RequestAsync<Config.MonitorConfig>(IpcCommands.SetConfig, config, ct);

    public Task<List<Events.UpsEvent>> GetEventsAsync(int limit, CancellationToken ct = default) =>
        RequestAsync<List<Events.UpsEvent>>(IpcCommands.GetEvents, new GetEventsRequest(limit), ct);

    public Task CancelShutdownAsync(CancellationToken ct = default) =>
        RequestAsync<JsonElement>(IpcCommands.CancelShutdown, null, ct);

    public Task SendUpsCommandAsync(UpsCommandRequest command, CancellationToken ct = default) =>
        RequestAsync<JsonElement>(IpcCommands.SendUpsCommand, command, ct);

    public Task RescanAsync(CancellationToken ct = default) =>
        RequestAsync<JsonElement>(IpcCommands.Rescan, null, ct);

    private async Task<T> RequestAsync<T>(string command, object? payload, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await EnsureConnectedAsync(ct);

            var request = new IpcRequest(command,
                payload is null ? null : JsonSerializer.SerializeToElement(payload, IpcProtocol.JsonOptions));

            string? line;
            try
            {
                await _writer!.WriteLineAsync(JsonSerializer.Serialize(request, IpcProtocol.JsonOptions).AsMemory(), ct);
                line = await _reader!.ReadLineAsync(ct);
            }
            catch (IOException)
            {
                Disconnect();
                throw;
            }

            if (line is null)
            {
                Disconnect();
                throw new IOException("O serviço encerrou a conexão.");
            }

            var response = JsonSerializer.Deserialize<IpcResponse>(line, IpcProtocol.JsonOptions)
                ?? throw new InvalidDataException("Resposta vazia do serviço.");

            if (!response.Ok)
                throw new InvalidOperationException(response.Error ?? "Erro desconhecido no serviço.");

            return response.Payload is { } element
                ? element.Deserialize<T>(IpcProtocol.JsonOptions)!
                : default!;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_pipe is { IsConnected: true })
            return;

        Disconnect();
        var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(1500, ct);
        }
        catch
        {
            await pipe.DisposeAsync();
            throw;
        }

        _pipe = pipe;
        var utf8 = new UTF8Encoding(false);
        _reader = new StreamReader(pipe, utf8, false, 4096, leaveOpen: true);
        _writer = new StreamWriter(pipe, utf8, 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
    }

    private void Disconnect()
    {
        // Com o pipe quebrado, o flush do writer no Dispose pode lançar IOException.
        try { _writer?.Dispose(); } catch (IOException) { }
        _reader?.Dispose();
        _pipe?.Dispose();
        _reader = null;
        _writer = null;
        _pipe = null;
    }

    public ValueTask DisposeAsync()
    {
        Disconnect();
        _lock.Dispose();
        return ValueTask.CompletedTask;
    }
}
