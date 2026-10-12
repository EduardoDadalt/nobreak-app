using NobreakMonitor.Core.Protocol;

namespace NobreakMonitor.Core.Tests;

public class MegatecProtocolTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ParseStatus_reads_all_fields()
    {
        var s = MegatecParser.ParseStatus("(127.0 127.0 126.0 034 60.0 13.5 30.0 00001001", Now);

        Assert.NotNull(s);
        Assert.Equal(127.0, s.InputVoltage);
        Assert.Equal(126.0, s.OutputVoltage);
        Assert.Equal(34, s.LoadPercent);
        Assert.Equal(60.0, s.Frequency);
        Assert.Equal(13.5, s.BatteryVoltage);
        Assert.Equal(100, s.BatteryPercent);
        Assert.Equal(30.0, s.Temperature);
        Assert.False(s.UtilityFail);
        Assert.True(s.IsStandbyType);
        Assert.True(s.BeeperOn);
    }

    [Fact]
    public void ParseStatus_reads_utility_fail_and_low_battery_bits()
    {
        var s = MegatecParser.ParseStatus("(000.0 000.0 120.0 040 60.0 11.0 30.0 11001001", Now);

        Assert.NotNull(s);
        Assert.True(s.UtilityFail);
        Assert.True(s.BatteryLow);
        Assert.False(s.TestInProgress);
    }

    [Fact]
    public void ParseStatus_accepts_trailing_parenthesis_used_in_original_app()
    {
        Assert.NotNull(MegatecParser.ParseStatus("(127.0 127.0 127.0 000 60.0 13.5 30.0 00000001)", Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("NAK")]
    [InlineData("Q1")]
    [InlineData("(127.0 127.0 127.0 000 60.0 13.5 30.0 0000000")]
    [InlineData("(127.0 127.0 127.0 000 60.0 13.5 30.0 0000000X")]
    public void ParseStatus_rejects_invalid_lines(string line)
    {
        Assert.Null(MegatecParser.ParseStatus(line, Now));
    }

    [Fact]
    public void ParseRating_reads_nominal_values()
    {
        var r = MegatecParser.ParseRating("#127.0 004 24.00 60.0");

        Assert.Equal(new Models.UpsRating("127.0", 4, 24.0, 60.0), r);
    }

    [Fact]
    public void Real_TS_Shara_Senoid_responses_parse()
    {
        // Respostas capturadas de um TS Shara Senoid (banco de 24 V, bivolt) em COM3.
        var info = MegatecParser.ParseInfo("#TS SHARA 221011 Senoid  18 V01020100F");
        var rating = MegatecParser.ParseRating("#Univ. 100 24.00 60.0");
        var status = MegatecParser.ParseStatus("(221.0 221.0 112.0 022 60.0 27.2 29.0 00001001", Now, rating?.BatteryVoltage);

        Assert.Equal(new Models.UpsInfo("TS SHARA 221011", "Senoid  18", "V01020100F"), info);
        Assert.Equal(new Models.UpsRating("Univ.", 100, 24.0, 60.0), rating);
        Assert.NotNull(status);
        Assert.Equal(112.0, status.OutputVoltage);
        Assert.Equal(22, status.LoadPercent);
        Assert.Equal(100, status.BatteryPercent);
        Assert.False(status.UtilityFail);
    }

    [Fact]
    public void ParseInfo_reads_fixed_width_fields()
    {
        // Fabricante (15) + espaço + modelo (10) + espaço + versão (10).
        var info = MegatecParser.ParseInfo("#" + "TS SHARA".PadRight(15) + " " + "UPS 1200".PadRight(10) + " " + "V1.0".PadRight(10));

        Assert.NotNull(info);
        Assert.Equal("TS SHARA", info.Company);
        Assert.Equal("UPS 1200", info.Model);
        Assert.Equal("V1.0", info.Version);
    }

    [Theory]
    [InlineData(13.5, 12, 100)]
    [InlineData(12.20, 12, 100)]
    [InlineData(11.64, 12, 50)]
    [InlineData(10.50, 12, 0)]
    [InlineData(23.28, 24, 50)]
    [InlineData(1.94, 2, 50)]
    public void BatteryCharge_matches_tsapp_table(double voltage, double nominal, int expected)
    {
        Assert.Equal(expected, BatteryCharge.Estimate(voltage, nominal));
    }

    [Theory]
    [InlineData(13.0, 12)]
    [InlineData(26.0, 24)]
    [InlineData(2.2, 2)]
    public void BatteryCharge_infers_nominal(double voltage, double expected)
    {
        Assert.Equal(expected, BatteryCharge.NominalFor(voltage));
    }

    [Theory]
    [InlineData(0.5, "S.5\r")]
    [InlineData(1, "S01\r")]
    [InlineData(10, "S10\r")]
    public void ShutdownUps_formats_like_protocol(double minutes, string expected)
    {
        Assert.Equal(expected, MegatecCommands.ShutdownUps(minutes));
    }

    [Fact]
    public void TestMinutes_pads_to_two_digits()
    {
        Assert.Equal("T05\r", MegatecCommands.TestMinutes(5));
    }
}
