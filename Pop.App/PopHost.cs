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
    private readonly OverlayStateTracker _overlayStateTracker = new();
    private readonly DiagnosticsLogService _diagnosticsLogService = new();
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
        _dragTracker.DragRejected += OnDragRejected;
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

        _dragTracker.DragRejected -= OnDragRejected;
        _dragTracker.DragStarted -= OnDragStarted;
        _dragTracker.DragUpdated -= OnDragUpdated;
        _dragTracker.DragCompleted -= OnDragCompleted;
        _dragTracker.Dispose();

        _overlayPresenter.Dispose();
        _diagnosticsLogService.Dispose();

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
        ApplyOverlayTransition(_overlayStateTracker.Reset());

        LogDiagnostics("drag-start", "Started tracking a potential throw.", new Dictionary<string, string?>
        {
            ["windowHandle"] = e.Session.WindowHandle.ToString("X"),
            ["monitorBounds"] = e.Session.MonitorInfo.WorkArea.ToString()
        });
    }

    private void OnDragUpdated(object? sender, DragSessionEventArgs e)
    {
        if (!_settings.Enabled)
        {
            ApplyOverlayTransition(_overlayStateTracker.Reset());
            return;
        }

        var decision = _snapDecider.Decide(e.Session, _settings);
        e.Session.CurrentPredictedTarget = decision.Target;
        ApplyOverlayTransition(_overlayStateTracker.Evaluate(decision, e.Session.MonitorInfo, _settings.ShowOverlay));
    }

    private async void OnDragCompleted(object? sender, DragSessionCompletedEventArgs e)
    {
        ApplyOverlayTransition(_overlayStateTracker.Reset());

        if (!_settings.Enabled)
        {
            return;
        }

        var decision = _snapDecider.Decide(e.Session, _settings);
        if (!decision.IsQualified)
        {
            LogDiagnostics(
                "drag-release",
                "Release did not qualify for snapping.",
                new Dictionary<string, string?>
                {
                    ["target"] = decision.Target.ToString(),
                    ["reason"] = decision.RejectionReason.ToString(),
                    ["velocityX"] = Math.Round(decision.HorizontalVelocityPxPerSec).ToString(),
                    ["velocityY"] = Math.Round(decision.VerticalVelocityPxPerSec).ToString(),
                    ["dominance"] = decision.HorizontalDominanceRatio.ToString("0.00")
                });
            return;
        }

        var tileBounds = TileLayoutCalculator.GetTileBounds(decision.Target, e.Session.MonitorInfo);
        if (tileBounds == Rectangle.Empty)
        {
            return;
        }

        e.Session.CurrentPredictedTarget = decision.Target;
        var plan = _windowAnimator.CreatePlan(
            e.Session.GetCurrentBoundsEstimate(),
            tileBounds,
            decision.HorizontalVelocityPxPerSec,
            _settings.GlideDurationMs);

        LogDiagnostics(
            "drag-release",
            "Snap qualified and animation plan generated.",
            new Dictionary<string, string?>
            {
                ["target"] = decision.Target.ToString(),
                ["reason"] = decision.RejectionReason.ToString(),
                ["velocityX"] = Math.Round(decision.HorizontalVelocityPxPerSec).ToString(),
                ["velocityY"] = Math.Round(decision.VerticalVelocityPxPerSec).ToString(),
                ["dominance"] = decision.HorizontalDominanceRatio.ToString("0.00"),
                ["frames"] = plan.Frames.Count.ToString(),
                ["overshootPx"] = plan.MaxOvershootPx.ToString()
            });

        try
        {
            await _windowAnimator.AnimateToTileAsync(e.Session.WindowHandle, plan, _disposeCancellation.Token);
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

    private void OnDragRejected(object? sender, DragSessionRejectedEventArgs e)
    {
        ApplyOverlayTransition(_overlayStateTracker.Reset());

        LogDiagnostics(
            "drag-ignored",
            "Pointer down did not start a Pop drag session.",
            new Dictionary<string, string?>
            {
                ["reason"] = e.InspectionResult.Eligibility.Reason.ToString(),
                ["detail"] = e.InspectionResult.Eligibility.Detail,
                ["point"] = e.ScreenPoint.ToString()
            });
    }

    private void UpdateMenuState()
    {
        _enabledMenuItem.Checked = _settings.Enabled;
        _overlayMenuItem.Checked = _settings.ShowOverlay;
        _launchAtStartupMenuItem.Checked = _settings.LaunchAtStartup;
        _notifyIcon.Text = _settings.Enabled ? "Pop - Enabled" : "Pop - Disabled";
    }

    private void ApplyOverlayTransition(OverlayTransition transition)
    {
        switch (transition.Action)
        {
            case OverlayTransitionAction.ShowOrUpdate:
                _overlayPresenter.Update(transition.Target, transition.Bounds);
                break;
            case OverlayTransitionAction.Hide:
                _overlayPresenter.Hide();
                break;
        }
    }

    private void LogDiagnostics(string category, string message, IReadOnlyDictionary<string, string?>? fields = null)
    {
        if (!_settings.EnableDiagnostics)
        {
            return;
        }

        _diagnosticsLogService.Write(new DiagnosticEvent(DateTimeOffset.Now, category, message, fields));
    }
}
