using System.Text.Json;
using NobreakMonitor.Core.Config;
using NobreakMonitor.Core.Ipc;

namespace NobreakMonitor.Service.Storage;

/// <summary>Configuração persistida em %ProgramData%\NobreakMonitor\config.json.</summary>
public sealed class ConfigStore
{
    private readonly string _path;
    private readonly ILogger<ConfigStore> _logger;
    private readonly Lock _lock = new();
    private MonitorConfig _current;

    public ConfigStore(ILogger<ConfigStore> logger)
    {
        _logger = logger;
        _path = Path.Combine(AppPaths.DataDirectory, "config.json");
        _current = Load();
    }

    public MonitorConfig Current
    {
        get { lock (_lock) return _current; }
    }

    public MonitorConfig Save(MonitorConfig config)
    {
        var normalized = config.Normalize();
        lock (_lock)
        {
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(normalized, JsonWriteOptions));
            File.Move(tmp, _path, overwrite: true);
            _current = normalized;
        }
        return normalized;
    }

    private MonitorConfig Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var loaded = JsonSerializer.Deserialize<MonitorConfig>(File.ReadAllText(_path), IpcProtocol.JsonOptions);
                if (loaded is not null)
                    return loaded.Normalize();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao ler {Path}; usando configuração padrão", _path);
        }

        return new MonitorConfig().Normalize();
    }

    private static readonly JsonSerializerOptions JsonWriteOptions = new(IpcProtocol.JsonOptions) { WriteIndented = true };
}
