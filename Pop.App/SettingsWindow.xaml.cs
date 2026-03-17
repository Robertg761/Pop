using System.ComponentModel;
using System.Windows;
using Pop.App.ViewModels;
using Pop.Core.Models;

namespace Pop.App;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private bool _allowClose;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _viewModel = SettingsViewModel.FromSettings(settings);
        DataContext = _viewModel;
    }

    public event EventHandler<AppSettings>? SettingsSaved;

    public void ShowOrBringToFront(AppSettings settings)
    {
        _viewModel.Apply(settings);

        if (!IsVisible)
        {
            Show();
        }

        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    public void ClosePermanently()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.TryBuildSettings(out var settings, out var validationMessage))
        {
            System.Windows.MessageBox.Show(this, validationMessage, "Invalid Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SettingsSaved?.Invoke(this, settings);
        Hide();
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        Hide();
    }
}
