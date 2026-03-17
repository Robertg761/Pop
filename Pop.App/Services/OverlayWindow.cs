using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using System.Windows.Media;
using Pop.Core.Models;

namespace Pop.App.Services;

public sealed class OverlayWindow : Window
{
    private readonly Border _outerBorder;
    private readonly Border _innerBorder;
    private bool _allowClose;
    private bool _isHiding;

    public OverlayWindow()
    {
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        IsHitTestVisible = false;
        Opacity = 0;

        _outerBorder = new Border
        {
            CornerRadius = new CornerRadius(24),
            BorderThickness = new Thickness(2),
            Background = CreateBrush(0x1E, 0xFB, 0xFD, 0xFF),
            BorderBrush = CreateBrush(0xFF, 0x8B, 0xC8, 0xFF)
        };

        _innerBorder = new Border
        {
            Margin = new Thickness(10),
            CornerRadius = new CornerRadius(18),
            BorderThickness = new Thickness(1),
            Background = CreateBrush(0x30, 0x38, 0x57, 0x90),
            BorderBrush = CreateBrush(0x50, 0xFF, 0xFF, 0xFF)
        };

        Content = new Grid
        {
            Margin = new Thickness(12),
            Children =
            {
                _outerBorder,
                _innerBorder
            }
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;
        var existingStyle = OverlayNativeMethods.GetWindowLongPtr(handle, OverlayNativeMethods.GwlExStyle).ToInt64();
        var updatedStyle = existingStyle |
                           OverlayNativeMethods.WsExTransparent |
                           OverlayNativeMethods.WsExToolWindow |
                           OverlayNativeMethods.WsExNoActivate;

        OverlayNativeMethods.SetWindowLongPtr(handle, OverlayNativeMethods.GwlExStyle, new IntPtr(updatedStyle));
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    public void UpdatePreview(Rectangle bounds, SnapTarget target)
    {
        Left = bounds.X;
        Top = bounds.Y;
        Width = bounds.Width;
        Height = bounds.Height;

        ApplyPalette(target);

        BeginAnimation(OpacityProperty, null);
        _isHiding = false;

        if (!IsVisible)
        {
            Show();
            Opacity = 0;
        }

        var fadeIn = new DoubleAnimation
        {
            To = 1,
            Duration = TimeSpan.FromMilliseconds(95),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        BeginAnimation(OpacityProperty, fadeIn, HandoffBehavior.SnapshotAndReplace);
    }

    public void HidePreview()
    {
        if (!IsVisible || _isHiding)
        {
            return;
        }

        _isHiding = true;
        BeginAnimation(OpacityProperty, null);

        var fadeOut = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(85),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };

        fadeOut.Completed += (_, _) =>
        {
            if (_isHiding)
            {
                Hide();
            }
        };

        BeginAnimation(OpacityProperty, fadeOut, HandoffBehavior.SnapshotAndReplace);
    }

    public void ClosePermanently()
    {
        _allowClose = true;
        Close();
    }

    private static SolidColorBrush CreateBrush(byte alpha, byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }

    private void ApplyPalette(SnapTarget target)
    {
        _outerBorder.Background = target switch
        {
            SnapTarget.LeftHalf => CreateBrush(0x22, 0x8B, 0xC8, 0xFF),
            SnapTarget.RightHalf => CreateBrush(0x22, 0xFF, 0xA9, 0x6B),
            _ => CreateBrush(0x18, 0xD7, 0xDF, 0xE8)
        };

        _outerBorder.BorderBrush = target switch
        {
            SnapTarget.LeftHalf => CreateBrush(0xFF, 0x9D, 0xD7, 0xFF),
            SnapTarget.RightHalf => CreateBrush(0xFF, 0xFF, 0xBF, 0x8C),
            _ => CreateBrush(0xFF, 0xE0, 0xE0, 0xE0)
        };

        _innerBorder.Background = target switch
        {
            SnapTarget.LeftHalf => CreateBrush(0x42, 0x1A, 0x3A, 0x68),
            SnapTarget.RightHalf => CreateBrush(0x42, 0x4B, 0x27, 0x18),
            _ => CreateBrush(0x30, 0x2A, 0x2A, 0x2A)
        };

        _innerBorder.BorderBrush = target switch
        {
            SnapTarget.LeftHalf => CreateBrush(0x55, 0xE6, 0xF4, 0xFF),
            SnapTarget.RightHalf => CreateBrush(0x55, 0xFF, 0xE7, 0xD1),
            _ => CreateBrush(0x40, 0xFF, 0xFF, 0xFF)
        };
    }
}
