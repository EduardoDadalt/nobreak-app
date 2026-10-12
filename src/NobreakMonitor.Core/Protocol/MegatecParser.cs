using System.Globalization;
using System.Text.RegularExpressions;
using NobreakMonitor.Core.Models;

namespace NobreakMonitor.Core.Protocol;

public static partial class MegatecParser
{
    [GeneratedRegex(@"^[01]{8}$")]
    private static partial Regex StatusBitsRegex();

    /// <summary>
    /// Interpreta "(127.0 127.0 127.0 000 60.0 13.5 30.0 00001001".
    /// Retorna null se a linha não for um Q1 válido.
    /// </summary>
    public static UpsStatus? ParseStatus(string line, DateTimeOffset timestamp, double? ratedBatteryVoltage = null)
    {
        var parts = Clean(line).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 8 || !StatusBitsRegex().IsMatch(parts[7]))
            return null;

        if (!TryNum(parts[0], out var input) || !TryNum(parts[2], out var output) || !TryNum(parts[5], out var battery))
            return null;

        TryNum(parts[1], out var inputFault);
        TryNum(parts[3], out var load);
        TryNum(parts[4], out var freq);
        TryNum(parts[6], out var temp);

        var nominal = BatteryCharge.NominalFor(battery, ratedBatteryVoltage);

        return new UpsStatus
        {
            InputVoltage = input,
            InputFaultVoltage = inputFault,
            OutputVoltage = output,
            LoadPercent = (int)load,
            Frequency = freq,
            BatteryVoltage = battery,
            BatteryPercent = BatteryCharge.Estimate(battery, nominal),
            Temperature = temp,
            StatusBits = parts[7],
            Timestamp = timestamp,
        };
    }

    /// <summary>Interpreta "#127.0 004 12.00 60.0" ou, em bivolt, "#Univ. 100 24.00 60.0".</summary>
    public static UpsRating? ParseRating(string line)
    {
        if (!line.TrimStart().StartsWith('#'))
            return null;

        var parts = Clean(line).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
            return null;

        return TryNum(parts[1], out var c) && TryNum(parts[2], out var b) && TryNum(parts[3], out var f)
            ? new UpsRating(parts[0], c, b, f)
            : null;
    }

    /// <summary>
    /// Interpreta "#Fabricante(15) Modelo(10) Versão(10)". Campos de largura fixa
    /// segundo o protocolo; se a linha for menor, divide por espaços.
    /// </summary>
    public static UpsInfo? ParseInfo(string line)
    {
        // Só remove espaços à esquerda: os da direita fazem parte dos campos de largura fixa.
        var trimmed = line.TrimEnd('\r', '\n').TrimStart();
        if (!trimmed.StartsWith('#'))
            return null;

        var body = trimmed[1..];
        if (body.Length >= 27)
            return new UpsInfo(body[..15].Trim(), body[16..26].Trim(), body[27..].Trim());

        var parts = body.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        return new UpsInfo(
            parts.ElementAtOrDefault(0) ?? "",
            parts.ElementAtOrDefault(1) ?? "",
            parts.ElementAtOrDefault(2) ?? "");
    }

    private static string Clean(string line) => line.Replace("#", "").Replace("(", "").Replace(")", "").Trim();

    private static bool TryNum(string s, out double value) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
