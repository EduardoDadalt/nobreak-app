using System.ComponentModel;
using System.Windows;
using NobreakMonitor.App.ViewModels;

namespace NobreakMonitor.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>Quando verdadeiro, fechar a janela encerra o app em vez de escondê-la na bandeja.</summary>
    public bool AllowClose { get; set; }

    public void ShowAndActivate()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
}
