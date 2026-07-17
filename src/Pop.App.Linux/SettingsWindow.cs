using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Pop.Core.Models;

namespace Pop.App.Linux;

public sealed class SettingsWindow : Window
{
    private readonly Func<AppSettings, Task<bool>> _saveSettingsAsync;
    private AppSettings _currentSettings = AppSettings.Default;
    private readonly CheckBox _enabledCheckBox = new();
    private readonly CheckBox _diagnosticsCheckBox = new();
    private readonly TextBox _velocityTextBox = CreateNumberTextBox();
    private readonly TextBox _dominanceTextBox = CreateNumberTextBox();
    private readonly TextBlock _durationValueTextBlock = new()
    {
        FontWeight = FontWeight.SemiBold,
        HorizontalAlignment = HorizontalAlignment.Right,
        MinWidth = 72,
        TextAlignment = TextAlignment.Right
    };
    private readonly Slider _durationSlider = new()
    {
        Minimum = 50,
        Maximum = 1000,
        TickFrequency = 25,
        IsSnapToTickEnabled = true,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock _validationTextBlock = new()
    {
        Foreground = Brushes.Firebrick,
        TextWrapping = TextWrapping.Wrap,
        IsVisible = false
    };

    private bool _allowClose;

    public SettingsWindow(AppSettings settings, Func<AppSettings, Task<bool>> saveSettingsAsync)
    {
        _saveSettingsAsync = saveSettingsAsync;
        Title = "Pop Settings";
        Width = 560;
        Height = 460;
        MinWidth = 500;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = PopLinuxApp.LoadTrayIcon();
        Content = BuildContent();

        _durationSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
            {
                UpdateDurationText();
            }
        };

        Apply(settings);
    }

    public void ShowOrBringToFront(AppSettings settings)
    {
        Apply(settings);

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

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    private Control BuildContent()
    {
        var saveButton = new Button
        {
            Content = "Save",
            MinWidth = 96,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        saveButton.Click += SaveButton_OnClick;

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 96,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        cancelButton.Click += (_, _) => Hide();

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,*,Auto,Auto"),
            ColumnDefinitions = new ColumnDefinitions("180,*"),
            Margin = new Thickness(24),
            RowSpacing = 16,
            ColumnSpacing = 18
        };

        AddHeader(grid);
        AddFullWidth(grid, _enabledCheckBox, 2);
        AddSettingRow(grid, 3, "Throw velocity", "Minimum horizontal release speed.", _velocityTextBox);
        AddSettingRow(grid, 4, "Horizontal dominance", "How much more horizontal than vertical the throw must be.", _dominanceTextBox);
        AddSettingRow(grid, 5, "Animation duration", "Higher values make snapping feel slower and smoother.", CreateDurationControl());
        AddFullWidth(grid, _diagnosticsCheckBox, 6);
        AddFullWidth(grid, _validationTextBlock, 7);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Children = { cancelButton, saveButton }
        };
        AddFullWidth(grid, buttonPanel, 8);

        return new ScrollViewer
        {
            Content = grid,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }

    private static void AddHeader(Grid grid)
    {
        var title = new TextBlock
        {
            Text = "Pop Settings",
            FontSize = 24,
            FontWeight = FontWeight.SemiBold
        };
        AddFullWidth(grid, title, 0);

        var subtitle = new TextBlock
        {
            Text = "Tune snapping behavior for this desktop session.",
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap
        };
        AddFullWidth(grid, subtitle, 1);
    }

    private static void AddSettingRow(Grid grid, int row, string label, string detail, Control control)
    {
        var labelPanel = new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                },
                new TextBlock
                {
                    Text = detail,
                    Foreground = Brushes.Gray,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };

        Grid.SetRow(labelPanel, row);
        Grid.SetColumn(labelPanel, 0);
        grid.Children.Add(labelPanel);

        Grid.SetRow(control, row);
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
    }

    private static void AddFullWidth(Grid grid, Control control, int row)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, 0);
        Grid.SetColumnSpan(control, 2);
        grid.Children.Add(control);
    }

    private static TextBox CreateNumberTextBox()
    {
        return new TextBox
        {
            Width = 140,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center
        };
    }

    private Control CreateDurationControl()
    {
        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                _durationSlider,
                _durationValueTextBlock
            }
        };
    }

    private void Apply(AppSettings settings)
    {
        _currentSettings = settings;
        SetValidationMessage(string.Empty);
        _enabledCheckBox.Content = "Enable snapping";
        _enabledCheckBox.IsChecked = settings.Enabled;
        _diagnosticsCheckBox.Content = "Enable diagnostics logging";
        _diagnosticsCheckBox.IsChecked = settings.EnableDiagnostics;
        _velocityTextBox.Text = settings.ThrowVelocityThresholdPxPerSec.ToString("0.##");
        _dominanceTextBox.Text = settings.HorizontalDominanceRatio.ToString("0.##");
        _durationSlider.Value = settings.GlideDurationMs;
        UpdateDurationText();
    }

    private async void SaveButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!TryBuildSettings(out var settings, out var validationMessage))
        {
            SetValidationMessage(validationMessage);
            return;
        }

        var clickedButton = sender as Button;
        if (clickedButton is not null)
        {
            clickedButton.IsEnabled = false;
        }

        try
        {
            if (await _saveSettingsAsync(settings))
            {
                Hide();
            }
        }
        catch (Exception exception)
        {
            // async void: an unhandled exception here (e.g. a failed KWin reload or an unwritable
            // config dir) would escape onto the dispatcher and terminate the app.
            SetValidationMessage($"Pop couldn't save settings: {exception.Message}");
        }
        finally
        {
            if (clickedButton is not null)
            {
                clickedButton.IsEnabled = true;
            }
        }
    }

    private bool TryBuildSettings(out AppSettings settings, out string validationMessage)
    {
        settings = new AppSettings();

        if (!double.TryParse(_velocityTextBox.Text, out var throwVelocity) || throwVelocity < 100)
        {
            validationMessage = "Throw velocity must be a number greater than or equal to 100.";
            return false;
        }

        if (!double.TryParse(_dominanceTextBox.Text, out var dominanceRatio) || dominanceRatio < 1)
        {
            validationMessage = "Horizontal dominance must be a number greater than or equal to 1.";
            return false;
        }

        validationMessage = string.Empty;
        // Start from the current settings so contract fields not exposed in this UI (e.g.
        // LaunchAtStartup) are preserved rather than reset to their defaults on every save.
        settings = _currentSettings with
        {
            Enabled = _enabledCheckBox.IsChecked == true,
            EnableDiagnostics = _diagnosticsCheckBox.IsChecked == true,
            ThrowVelocityThresholdPxPerSec = throwVelocity,
            HorizontalDominanceRatio = dominanceRatio,
            GlideDurationMs = Math.Clamp((int)Math.Round(_durationSlider.Value), 50, 1000)
        };

        return true;
    }

    private void UpdateDurationText()
    {
        _durationValueTextBlock.Text = $"{Math.Round(_durationSlider.Value):0} ms";
        Grid.SetColumn(_durationValueTextBlock, 1);
    }

    private void SetValidationMessage(string message)
    {
        _validationTextBlock.Text = message;
        _validationTextBlock.IsVisible = !string.IsNullOrWhiteSpace(message);
    }
}
