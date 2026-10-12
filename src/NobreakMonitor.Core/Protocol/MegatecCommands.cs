using System.Globalization;

namespace NobreakMonitor.Core.Protocol;

/// <summary>
/// Comandos do protocolo Megatec (serial 2400 8N1, terminados em CR),
/// o mesmo usado pelo TSApp Desktop para falar com nobreaks TS Shara.
/// </summary>
public static class MegatecCommands
{
    public const int BaudRate = 2400;

    /// <summary>Status: "(MMM.M NNN.N PPP.P QQQ RR.R S.SS TT.T b7..b0".</summary>
    public const string Status = "Q1\r";

    /// <summary>Valores nominais: "#MMM.M QQQ SS.SS RR.R".</summary>
    public const string Rating = "F\r";

    /// <summary>Identificação: "#Fabricante Modelo Versão".</summary>
    public const string Info = "I\r";

    /// <summary>Teste de bateria de 10 segundos.</summary>
    public const string Test10Seconds = "T\r";

    /// <summary>Teste até bateria baixa.</summary>
    public const string TestUntilLow = "TL\r";

    /// <summary>Cancela teste em andamento.</summary>
    public const string CancelTest = "CT\r";

    /// <summary>Liga/desliga o beep (alterna).</summary>
    public const string ToggleBeep = "Q\r";

    /// <summary>Cancela um desligamento do nobreak agendado com S.</summary>
    public const string CancelShutdown = "C\r";

    /// <summary>Teste de bateria por N minutos (1 a 99).</summary>
    public static string TestMinutes(int minutes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minutes, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minutes, 99);
        return $"T{minutes:00}\r";
    }

    /// <summary>
    /// Desliga a saída do nobreak após N minutos. O protocolo aceita .2 a .9
    /// (frações de minuto) e 01 a 10.
    /// </summary>
    public static string ShutdownUps(double minutes)
    {
        if (minutes is < 0.2 or > 10)
            throw new ArgumentOutOfRangeException(nameof(minutes), minutes, "Use de 0,2 a 10 minutos.");

        if (minutes < 1)
            return "S." + ((int)Math.Round(minutes * 10)).ToString(CultureInfo.InvariantCulture) + "\r";

        return $"S{(int)Math.Round(minutes):00}\r";
    }
}
