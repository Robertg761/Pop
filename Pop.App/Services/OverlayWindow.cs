using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Pop.Core.Models;

namespace Pop.App.Services;

public sealed class OverlayWindow : Window
{
    private readonly Border _highlightBorder;
    private bool _allowClose;

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

        _highlightBorder = new Border
        {
            CornerRadius = new CornerRadius(18),
            BorderThickness = new Thickness(3),
            Background = CreateBrush(0x44, 0x54, 0x8E, 0xFF),
            BorderBrush = CreateBrush(0xFF, 0x9B, 0xC2, 0xFF)
        };

        Content = new Grid
        {
            Margin = new Thickness(10),
            Children =
            {
                _highlightBorder
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

    public void UpdateBounds(Rectangle bounds, SnapTarget target)
    {
        Left = bounds.X;
        Top = bounds.Y;
        Width = bounds.Width;
        Height = bounds.Height;

        _highlightBorder.Background = target switch
        {
            SnapTarget.LeftHalf => CreateBrush(0x40, 0x48, 0x7B, 0xFF),
            SnapTarget.RightHalf => CreateBrush(0x40, 0xFF, 0x7B, 0x54),
            _ => CreateBrush(0x30, 0x80, 0x80, 0x80)
        };

        _highlightBorder.BorderBrush = target switch
        {
            SnapTarget.LeftHalf => CreateBrush(0xFF, 0x89, 0xB4, 0xFF),
            SnapTarget.RightHalf => CreateBrush(0xFF, 0xFF, 0xB0, 0x7A),
            _ => CreateBrush(0xFF, 0xE0, 0xE0, 0xE0)
        };
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
}
