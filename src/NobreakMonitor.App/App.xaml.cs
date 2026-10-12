using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using NobreakMonitor.App.ViewModels;
using Forms = System.Windows.Forms;

namespace NobreakMonitor.App;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\NobreakMonitor.App";
    private const string ShowEventName = @"Local\NobreakMonitor.App.Show";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showEvent;
    private Forms.NotifyIcon? _tray;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Só uma instância por usuário: a segunda apenas pede para a primeira aparecer.
        _instanceMutex = new Mutex(true, InstanceMutexName, out var isFirst);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!isFirst)
        {
            _showEvent.Set();
            Shutdown();
            return;
        }

        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "Nobreak Monitor — erro inesperado", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // Faz bindings numéricos usarem a cultura do Windows (ex.: "0,5" em pt-BR).
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

        var viewModel = new MainViewModel();
        _window = new MainWindow(viewModel);
        CreateTray(viewModel);

        viewModel.Alert += (title, message, critical) =>
            _tray?.ShowBalloonTip(5000, title, message, critical ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);
        viewModel.ShowRequested += () => _window.ShowAndActivate();

        new Thread(WaitForShowRequests) { IsBackground = true, Name = "ShowRequests" }.Start();

        if (!e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase))
            _window.ShowAndActivate();

        viewModel.Start();
    }

    private void CreateTray(MainViewModel viewModel)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir painel", null, (_, _) => _window?.ShowAndActivate());
        menu.Items.Add("Cancelar desligamento", null, async (_, _) => await viewModel.CancelComputerShutdownAsync());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Sair do painel", null, (_, _) => ExitApp());

        var iconStream = GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))!.Stream;
        _tray = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(iconStream),
            Text = "Nobreak Monitor",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
                _window?.ShowAndActivate();
        };
    }

    private void WaitForShowRequests()
    {
        while (_showEvent is not null)
        {
            try
            {
                _showEvent.WaitOne();
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            Dispatcher.BeginInvoke(() => _window?.ShowAndActivate());
        }
    }

    private void ExitApp()
    {
        if (_window is not null)
        {
            _window.AllowClose = true;
            _window.Close();
        }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        var showEvent = _showEvent;
        _showEvent = null;
        showEvent?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
