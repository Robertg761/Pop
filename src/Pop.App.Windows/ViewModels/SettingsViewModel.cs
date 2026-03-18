using System.ComponentModel;
using System.Runtime.CompilerServices;
using Pop.Core.Models;

namespace Pop.App.Windows.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private bool _enabled;
    private bool _launchAtStartup;
    private bool _enableDiagnostics;
    private string _throwVelocityThresholdText = string.Empty;
    private string _horizontalDominanceRatioText = string.Empty;
    private string _glideDurationText = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool Enabled
    {
        get => _enabled;
        set => SetField(ref _enabled, value);
    }

    public bool LaunchAtStartup
    {
        get => _launchAtStartup;
        set => SetField(ref _launchAtStartup, value);
    }

    public bool EnableDiagnostics
    {
        get => _enableDiagnostics;
        set => SetField(ref _enableDiagnostics, value);
    }

    public string ThrowVelocityThresholdText
    {
        get => _throwVelocityThresholdText;
        set => SetField(ref _throwVelocityThresholdText, value);
    }

    public string HorizontalDominanceRatioText
    {
        get => _horizontalDominanceRatioText;
        set => SetField(ref _horizontalDominanceRatioText, value);
    }

    public string GlideDurationText
    {
        get => _glideDurationText;
        set => SetField(ref _glideDurationText, value);
    }

    public static SettingsViewModel FromSettings(AppSettings settings)
    {
        var viewModel = new SettingsViewModel();
        viewModel.Apply(settings);
        return viewModel;
    }

    public void Apply(AppSettings settings)
    {
        Enabled = settings.Enabled;
        LaunchAtStartup = settings.LaunchAtStartup;
        EnableDiagnostics = settings.EnableDiagnostics;
        ThrowVelocityThresholdText = settings.ThrowVelocityThresholdPxPerSec.ToString("0.##");
        HorizontalDominanceRatioText = settings.HorizontalDominanceRatio.ToString("0.##");
        GlideDurationText = settings.GlideDurationMs.ToString();
    }

    public bool TryBuildSettings(out AppSettings settings, out string validationMessage)
    {
        settings = new AppSettings();

        if (!double.TryParse(ThrowVelocityThresholdText, out var throwVelocity) || throwVelocity < 100)
        {
            validationMessage = "Throw velocity must be a number greater than or equal to 100.";
            return false;
        }

        if (!double.TryParse(HorizontalDominanceRatioText, out var dominanceRatio) || dominanceRatio < 1)
        {
            validationMessage = "Horizontal dominance must be a number greater than or equal to 1.";
            return false;
        }

        if (!int.TryParse(GlideDurationText, out var glideDurationMs) || glideDurationMs < 50 || glideDurationMs > 1000)
        {
            validationMessage = "Glide duration must be an integer between 50 and 1000 milliseconds.";
            return false;
        }

        validationMessage = string.Empty;
        settings = new AppSettings
        {
            Enabled = Enabled,
            LaunchAtStartup = LaunchAtStartup,
            EnableDiagnostics = EnableDiagnostics,
            ThrowVelocityThresholdPxPerSec = throwVelocity,
            HorizontalDominanceRatio = dominanceRatio,
            GlideDurationMs = glideDurationMs
        };

        return true;
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
