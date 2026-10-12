using System.IO.Ports;
using System.Text;
using NobreakMonitor.Core.Protocol;

namespace NobreakMonitor.Service.Ups;

/// <summary>Porta serial aberta com um nobreak Megatec. Uso single-thread (pelo loop do monitor).</summary>
public sealed class SerialUpsConnection : IDisposable
{
    private readonly SerialPort _port;

    private SerialUpsConnection(SerialPort port) => _port = port;

    public string PortName => _port.PortName;

    public static SerialUpsConnection Open(string portName)
    {
        var port = new SerialPort(portName, MegatecCommands.BaudRate, Parity.None, 8, StopBits.One)
        {
            Encoding = Encoding.ASCII,
            NewLine = "\r",
            ReadTimeout = 1500,
            WriteTimeout = 1500,
            // Alguns cabos/conversores USB-serial de nobreak dependem de DTR/RTS ativos.
            DtrEnable = true,
            RtsEnable = true,
        };

        try
        {
            port.Open();
        }
        catch
        {
            port.Dispose();
            throw;
        }

        return new SerialUpsConnection(port);
    }

    /// <summary>Envia um comando e lê uma linha de resposta (terminada em CR). Null em timeout.</summary>
    public string? Query(string command, int timeoutMs = 1500)
    {
        _port.DiscardInBuffer();
        _port.Write(command);
        _port.ReadTimeout = timeoutMs;
        try
        {
            return _port.ReadLine().Trim();
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    /// <summary>Envia um comando sem resposta (T, TL, Q, S, C...).</summary>
    public void Send(string command)
    {
        _port.DiscardInBuffer();
        _port.Write(command);
    }

    public void Dispose()
    {
        try
        {
            if (_port.IsOpen)
                _port.Close();
        }
        catch (IOException)
        {
            // Cabo removido: a porta já não existe.
        }
        _port.Dispose();
    }
}
