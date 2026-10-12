namespace NobreakMonitor.Core.Models;

/// <summary>Leitura de uma resposta Q1.</summary>
public sealed record UpsStatus
{
    public double InputVoltage { get; init; }
    public double InputFaultVoltage { get; init; }
    public double OutputVoltage { get; init; }
    /// <summary>Campo QQQ do Q1 (carga em % da nominal).</summary>
    public int LoadPercent { get; init; }
    public double Frequency { get; init; }
    public double BatteryVoltage { get; init; }
    public int BatteryPercent { get; init; }
    public double Temperature { get; init; }

    /// <summary>Os 8 bits de status como recebidos (b7..b0).</summary>
    public string StatusBits { get; init; } = "00000000";

    /// <summary>b7: falha na rede elétrica (nobreak na bateria).</summary>
    public bool UtilityFail => Bit(0);
    /// <summary>b6: bateria baixa.</summary>
    public bool BatteryLow => Bit(1);
    /// <summary>b5: bypass / boost / buck ativo.</summary>
    public bool BypassActive => Bit(2);
    /// <summary>b4: falha no nobreak.</summary>
    public bool UpsFailed => Bit(3);
    /// <summary>b3: 1 = line-interactive/standby, 0 = online.</summary>
    public bool IsStandbyType => Bit(4);
    /// <summary>b2: teste em andamento.</summary>
    public bool TestInProgress => Bit(5);
    /// <summary>b1: desligamento do nobreak agendado.</summary>
    public bool ShutdownActive => Bit(6);
    /// <summary>b0: beep ligado.</summary>
    public bool BeeperOn => Bit(7);

    public DateTimeOffset Timestamp { get; init; }

    private bool Bit(int index) => StatusBits.Length > index && StatusBits[index] == '1';
}

/// <summary>Valores nominais (resposta F). A tensão é texto: modelos bivolt respondem "Univ.".</summary>
public sealed record UpsRating(string Voltage, double Current, double BatteryVoltage, double Frequency);

/// <summary>Identificação do equipamento (resposta I).</summary>
public sealed record UpsInfo(string Company, string Model, string Version);
