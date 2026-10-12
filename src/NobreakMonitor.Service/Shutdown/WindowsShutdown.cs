using System.Diagnostics;

namespace NobreakMonitor.Service.Shutdown;

public interface ISystemShutdown
{
    void Shutdown(string message, bool forceCloseApps);
}

/// <summary>Desliga o Windows via shutdown.exe (que já cuida do privilégio SeShutdownPrivilege).</summary>
public sealed class WindowsShutdown(ILogger<WindowsShutdown> logger) : ISystemShutdown
{
    // u:6:12 = não planejado, "Power Failure: Environment".
    private const string ReasonPowerFailure = "/d u:6:12";

    public void Shutdown(string message, bool forceCloseApps)
    {
        var comment = message.Replace("\"", "'");
        var baseArgs = $"/s /t 0 {(forceCloseApps ? "/f " : "")}/c \"{comment}\"";

        if (Run($"{baseArgs} {ReasonPowerFailure}") == 0)
            return;

        // Se o código de motivo for recusado, tenta sem ele.
        var exit = Run(baseArgs);
        if (exit != 0)
            logger.LogCritical("shutdown.exe retornou {ExitCode}; o computador pode não desligar", exit);
    }

    private int Run(string args)
    {
        var shutdownExe = Path.Combine(Environment.SystemDirectory, "shutdown.exe");
        logger.LogWarning("Executando {Exe} {Args}", shutdownExe, args);

        using var process = Process.Start(new ProcessStartInfo(shutdownExe, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        if (process is null)
            return -1;

        process.WaitForExit(10000);
        return process.HasExited ? process.ExitCode : 0;
    }
}
