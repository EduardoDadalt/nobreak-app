namespace NobreakMonitor.Core.Protocol;

/// <summary>
/// Estimativa de carga da bateria por tensão, com a mesma tabela
/// de interpolação linear do TSApp Desktop (core/battery/calc-soc).
/// </summary>
public static class BatteryCharge
{
    private static readonly double[] Voltage12 = [12.20, 12.09, 11.98, 11.87, 11.76, 11.64, 11.53, 11.42, 11.31, 10.91, 10.50];
    private static readonly int[] Percent = [100, 90, 80, 70, 60, 50, 40, 30, 20, 10, 0];

    /// <summary>
    /// Deduz a tensão nominal do banco: o TSApp usa 24 V quando a leitura passa de 18 V.
    /// Leituras abaixo de 3 V são tratadas como tensão por célula (alguns nobreaks online).
    /// </summary>
    public static double NominalFor(double batteryVoltage, double? ratedBatteryVoltage = null)
    {
        if (ratedBatteryVoltage is > 0)
            return ratedBatteryVoltage.Value;
        if (batteryVoltage < 3)
            return 2;
        return batteryVoltage > 18 ? 24 : 12;
    }

    public static int Estimate(double batteryVoltage, double nominal)
    {
        if (batteryVoltage <= 0 || nominal <= 0)
            return 0;

        // Normaliza para a escala de 12 V (6 células).
        var scaled = batteryVoltage * 12 / nominal;

        if (scaled > Voltage12[0])
            return 100;
        if (scaled <= Voltage12[^1])
            return 0;

        for (var i = 0; i < Voltage12.Length - 1; i++)
        {
            if (scaled <= Voltage12[i] && scaled > Voltage12[i + 1])
            {
                var slope = (Percent[i] - Percent[i + 1]) / (Voltage12[i] - Voltage12[i + 1]);
                var soc = Percent[i] - slope * (Voltage12[i] - scaled);
                return (int)Math.Round(Math.Clamp(soc, 0, 100), MidpointRounding.AwayFromZero);
            }
        }

        return 0;
    }
}
