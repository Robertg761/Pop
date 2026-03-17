using System.Drawing;
using Pop.App.Services;
using Pop.Core.Events;
using Pop.Core.Interfaces;
using Pop.Core.Models;
using Pop.Core.Services;
using Application = System.Windows.Application;
using Forms = System.Windows.Forms;

namespace Pop.App;

public sealed class PopHost : IDisposable
{
    private readonly ISettingsStore _settingsStore;
    private readonly StartupRegistrationService _startupRegistrationService;
    private readonly IDragTracker _dragTracker;
    private readonly ISnapDecider _snapDecider;
    private readonly IWindowAnimator _windowAnimator;
    private readonly WpfOverlayPresenter _overlayPresenter;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _enabledMenuItem;
    private readonly Forms.ToolStripMenuItem _launchAtStartupMenuItem;
    private readonly Forms.ToolStripMenuItem _overlayMenuItem;

    private AppSettings _settings = new();
    private SettingsWindow? _settingsWindow;

    public PopHost()
    {
        _settingsStore = new JsonSettingsStore();
        _startupRegistrationService = new StartupRegistrationService();
        _snapDecider = new SnapDecider();
        _windowAnimator = new WindowAnimator();
        _overlayPresenter = new WpfOverlayPresenter(Application.Current.Dispatcher);

        var windowInspector = new WindowInspector(new WindowEligibilityEvaluator());
        _dragTracker = new MouseHookDragTracker(windowInspector);
        _dragTracker.DragStarted += OnDragStarted;
        _dragTracker.DragUpdated += OnDragUpdated;
        _dragTracker.DragCompleted += OnDragCompleted;

        _enabledMenuItem = new Forms.ToolStripMenuItem("Enable Pop", null, async (_, _) => await ToggleEnabledAsync());
        _overlayMenuItem = new Forms.ToolStripMenuItem("Show Overlay", null, async (_, _) => await ToggleOverlayAsync());
        _launchAtStartupMenuItem = new Forms.ToolStripMenuItem("Launch At Startup", null, async (_, _) => await ToggleLaunchAtStartupAsync());

        var openSettingsMenuItem = new Forms.ToolStripMenuItem("Open Settings", null, (_, _) => OpenSettingsWindow());
        var exitMenuItem = new Forms.ToolStripMenuItem("Exit", null, (_, _) => Application.Current.Shutdown());

        var contextMenu = new Forms.ContextMenuStrip();
        contextMenu.Items.AddRange(
        [
            _enabledMenuItem,
            _overlayMenuItem,
            _launchAtStartupMenuItem,
            new Forms.ToolStripSeparator(),
            openSettingsMenuItem,
            new Forms.ToolStripSeparator(),
            exitMenuItem
        ]);

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "Pop",
            Icon = SystemIcons.Application,
            ContextMenuStrip = contextMenu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => OpenSettingsWindow();
    }

    public async Task InitializeAsync()
    {
        _settings = await _settingsStore.LoadAsync(_disposeCancellation.Token);
        _startupRegistrationService.SetLaunchAtStartup(_settings.LaunchAtStartup);
        UpdateMenuState();
        _dragTracker.Start();
    }

    public void Dispose()
    {
        _disposeCancellation.Cancel();

        _dragTracker.DragStarted -= OnDragStarted;
        _dragTracker.DragUpdated -= OnDragUpdated;
        _dragTracker.DragCompleted -= OnDragCompleted;
        _dragTracker.Dispose();

        _overlayPresenter.Dispose();

        if (_settingsWindow is not null)
        {
            _settingsWindow.SettingsSaved -= OnSettingsSaved;
            _settingsWindow.ClosePermanently();
            _settingsWindow = null;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _disposeCancellation.Dispose();
    }

    private void OnDragStarted(object? sender, DragSessionEventArgs e)
    {
        e.Session.CurrentPredictedTarget = SnapTarget.None;
        _overlayPresenter.Hide();
    }

    private void OnDragUpdated(object? sender, DragSessionEventArgs e)
    {
        if (!_settings.Enabled)
        {
            _overlayPresenter.Hide();
            return;
        }

        var decision = _snapDecider.Decide(e.Session, _settings);
        e.Session.CurrentPredictedTarget = decision.Target;

        if (!_settings.ShowOverlay || !decision.IsQualified)
        {
            _overlayPresenter.Hide();
            return;
        }

        var tileBounds = TileLayoutCalculator.GetTileBounds(decision.Target, e.Session.MonitorInfo);
        if (tileBounds == Rectangle.Empty)
        {
            _overlayPresenter.Hide();
            return;
        }

        _overlayPresenter.Show(decision.Target, tileBounds);
    }

    private async void OnDragCompleted(object? sender, DragSessionCompletedEventArgs e)
    {
        _overlayPresenter.Hide();

        if (!_settings.Enabled)
        {
            return;
        }

        var decision = _snapDecider.Decide(e.Session, _settings);
        if (!decision.IsQualified)
        {
            return;
        }

        var tileBounds = TileLayoutCalculator.GetTileBounds(decision.Target, e.Session.MonitorInfo);
        if (tileBounds == Rectangle.Empty)
        {
            return;
        }

        e.Session.CurrentPredictedTarget = decision.Target;
        try
        {
            await _windowAnimator.AnimateToTileAsync(
                e.Session.WindowHandle,
                tileBounds,
                decision.HorizontalVelocityPxPerSec,
                _settings.GlideDurationMs,
                _disposeCancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OpenSettingsWindow()
    {
        _settingsWindow ??= CreateSettingsWindow();
        _settingsWindow.ShowOrBringToFront(_settings);
    }

    private SettingsWindow CreateSettingsWindow()
    {
        var window = new SettingsWindow(_settings);
        window.SettingsSaved += OnSettingsSaved;
        return window;
    }

    private async void OnSettingsSaved(object? sender, AppSettings settings)
    {
        await ApplySettingsAsync(settings);
    }

    private async Task ToggleEnabledAsync()
    {
        await ApplySettingsAsync(_settings with { Enabled = !_settings.Enabled });
    }

    private async Task ToggleOverlayAsync()
    {
        await ApplySettingsAsync(_settings with { ShowOverlay = !_settings.ShowOverlay });
    }

    private async Task ToggleLaunchAtStartupAsync()
    {
        await ApplySettingsAsync(_settings with { LaunchAtStartup = !_settings.LaunchAtStartup });
    }

    private async Task ApplySettingsAsync(AppSettings settings)
    {
        _settings = settings;
        _startupRegistrationService.SetLaunchAtStartup(settings.LaunchAtStartup);
        UpdateMenuState();
        await _settingsStore.SaveAsync(settings, _disposeCancellation.Token);
    }

    private void UpdateMenuState()
    {
        _enabledMenuItem.Checked = _settings.Enabled;
        _overlayMenuItem.Checked = _settings.ShowOverlay;
        _launchAtStartupMenuItem.Checked = _settings.LaunchAtStartup;
        _notifyIcon.Text = _settings.Enabled ? "Pop - Enabled" : "Pop - Disabled";
    }
}
