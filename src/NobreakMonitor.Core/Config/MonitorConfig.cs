namespace NobreakMonitor.Core.Config;

public sealed record MonitorConfig
{
    /// <summary>Porta COM fixa (ex.: "COM3"). Nulo/vazio = detecção automática.</summary>
    public string? PortName { get; init; }

    /// <summary>Intervalo entre leituras Q1, em milissegundos.</summary>
    public int PollIntervalMs { get; init; } = 2000;

    /// <summary>Desligar o computador quando o nobreak entrar na bateria.</summary>
    public bool ShutdownOnBattery { get; init; } = true;

    /// <summary>Segundos na bateria antes de desligar o computador.</summary>
    public int OnBatteryDelaySeconds { get; init; } = 120;

    /// <summary>Desligar o computador quando o nobreak sinalizar bateria baixa.</summary>
    public bool ShutdownOnLowBattery { get; init; } = true;

    /// <summary>Segundos após bateria baixa antes de desligar (encurta a contagem em curso).</summary>
    public int LowBatteryDelaySeconds { get; init; } = 10;

    /// <summary>Fecha programas à força no desligamento (evita que um "Salvar?" trave o processo).</summary>
    public bool ForceCloseApps { get; init; } = true;

    /// <summary>
    /// Minutos para o nobreak desligar a própria saída depois que o PC iniciar o desligamento
    /// (comando S do protocolo, 0,2 a 10). 0 = não enviar.
    /// </summary>
    public double UpsOffAfterShutdownMinutes { get; init; }

    /// <summary>Modo simulação: registra o desligamento sem desligar de fato.</summary>
    public bool SimulateShutdown { get; init; }

    public MonitorConfig Normalize() => this with
    {
        PortName = string.IsNullOrWhiteSpace(PortName) ? null : PortName.Trim().ToUpperInvariant(),
        PollIntervalMs = Math.Clamp(PollIntervalMs, 1000, 30000),
        OnBatteryDelaySeconds = Math.Clamp(OnBatteryDelaySeconds, 0, 86400),
        LowBatteryDelaySeconds = Math.Clamp(LowBatteryDelaySeconds, 0, 3600),
        UpsOffAfterShutdownMinutes = UpsOffAfterShutdownMinutes <= 0 ? 0 : Math.Clamp(UpsOffAfterShutdownMinutes, 0.2, 10),
    };
}
