using System.Text.Json;
using NobreakMonitor.Core.Events;
using NobreakMonitor.Core.Ipc;

namespace NobreakMonitor.Service.Storage;

/// <summary>
/// Histórico de eventos em %ProgramData%\NobreakMonitor\events.jsonl (uma linha JSON por evento).
/// Mantém os mais recentes em memória e compacta o arquivo quando ele cresce demais.
/// </summary>
public sealed class EventStore
{
    private const int MemoryLimit = 1000;
    private const int FileLimit = 5000;

    private readonly string _path;
    private readonly ILogger<EventStore> _logger;
    private readonly Lock _lock = new();
    private readonly LinkedList<UpsEvent> _recent = new();
    private int _fileLines;

    public EventStore(ILogger<EventStore> logger)
    {
        _logger = logger;
        _path = Path.Combine(AppPaths.DataDirectory, "events.jsonl");
        LoadRecent();
    }

    public void Add(UpsEvent ev)
    {
        var level = ev.Severity switch
        {
            EventSeverity.Critical => LogLevel.Critical,
            EventSeverity.Warning => LogLevel.Warning,
            _ => LogLevel.Information,
        };
        _logger.Log(level, "[{Code}] {Message}", ev.Code, ev.Message);

        lock (_lock)
        {
            _recent.AddLast(ev);
            while (_recent.Count > MemoryLimit)
                _recent.RemoveFirst();

            try
            {
                File.AppendAllText(_path, JsonSerializer.Serialize(ev, IpcProtocol.JsonOptions) + "\n");
                if (++_fileLines > FileLimit)
                    Compact();
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Falha ao gravar o histórico");
            }
        }
    }

    public List<UpsEvent> Latest(int limit)
    {
        lock (_lock)
            return _recent.Reverse().Take(Math.Clamp(limit, 1, MemoryLimit)).ToList();
    }

    private void LoadRecent()
    {
        if (!File.Exists(_path))
            return;

        try
        {
            var lines = File.ReadAllLines(_path);
            _fileLines = lines.Length;
            foreach (var line in lines.TakeLast(MemoryLimit))
            {
                try
                {
                    if (JsonSerializer.Deserialize<UpsEvent>(line, IpcProtocol.JsonOptions) is { } ev)
                        _recent.AddLast(ev);
                }
                catch (JsonException)
                {
                    // Linha corrompida (ex.: queda de energia no meio da escrita): ignora.
                }
            }
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Falha ao ler o histórico");
        }
    }

    private void Compact()
    {
        var tmp = _path + ".tmp";
        File.WriteAllLines(tmp, _recent.Select(e => JsonSerializer.Serialize(e, IpcProtocol.JsonOptions)));
        File.Move(tmp, _path, overwrite: true);
        _fileLines = _recent.Count;
    }
}
