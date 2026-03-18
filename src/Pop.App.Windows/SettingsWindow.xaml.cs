using System.ComponentModel;
using System.Windows;
using Pop.App.Windows.Services;
using Pop.App.Windows.ViewModels;
using Pop.Core.Models;

namespace Pop.App.Windows;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private readonly IUpdateService _updateService;
    private bool _allowClose;

    internal SettingsWindow(AppSettings settings, IUpdateService updateService)
    {
        InitializeComponent();
        Icon = AppIconProvider.CreateWindowIcon();
        _viewModel = SettingsViewModel.FromSettings(settings);
        _updateService = updateService;
        DataContext = _viewModel;
        _updateService.StateChanged += OnUpdateStateChanged;
        ApplyUpdateState(_updateService.CurrentState);
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
        _updateService.StateChanged -= OnUpdateStateChanged;
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

    private async void CheckUpdatesButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _updateService.CheckNowAsync();
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, exception.Message, "Update Check Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void InstallUpdateButton_OnClick(object sender, RoutedEventArgs e)
    {
        _updateService.ApplyPendingUpdateAndRestart();
    }

    private void OnUpdateStateChanged(object? sender, UpdateStateChangedEventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            ApplyUpdateState(e.State);
            return;
        }

        Dispatcher.Invoke(() => ApplyUpdateState(e.State));
    }

    private void ApplyUpdateState(UpdateState state)
    {
        CurrentVersionTextBlock.Text = $"Version {state.CurrentVersion}";
        UpdateStatusTextBlock.Text = state.Message;
        CheckUpdatesButton.IsEnabled = state.CanCheck;

        if (state.Status == UpdateStatus.Downloading && state.DownloadProgressPercent is int progress)
        {
            UpdateProgressPanel.Visibility = Visibility.Visible;
            UpdateProgressBar.Value = progress;
            UpdateProgressTextBlock.Text = $"{progress}% downloaded";
        }
        else
        {
            UpdateProgressPanel.Visibility = Visibility.Collapsed;
            UpdateProgressBar.Value = 0;
            UpdateProgressTextBlock.Text = "0%";
        }

        InstallUpdateButton.Visibility = state.CanInstall ? Visibility.Visible : Visibility.Collapsed;
        InstallUpdateButton.IsEnabled = state.CanInstall;
        InstallUpdateButton.Content = state.CanInstall && !string.IsNullOrWhiteSpace(state.AvailableVersion)
            ? $"Install v{state.AvailableVersion}"
            : "Install Update";
    }
}
