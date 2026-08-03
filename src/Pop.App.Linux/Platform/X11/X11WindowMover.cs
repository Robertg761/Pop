using System.Diagnostics;
using System.Drawing;
using Pop.Core.Models;
using Pop.Platform.Abstractions.Windowing;

namespace Pop.App.Linux.Platform.X11;

public sealed class X11WindowMover : IWindowMover
{
    private const int StaticGravity = 10;
    private const int SourceApplication = 1;
    private const int MoveResizeFlags = (1 << 8) | (1 << 9) | (1 << 10) | (1 << 11);
    private readonly X11DisplayConnection _connection;

    public X11WindowMover(X11DisplayConnection connection)
    {
        _connection = connection;
    }

    public async Task MoveWindowAsync(IntPtr windowHandle, AnimationPlan plan, CancellationToken cancellationToken = default)
    {
        if (windowHandle == IntPtr.Zero || plan.FinalBounds == Rectangle.Empty)
        {
            return;
        }

        // Read once per move: the decoration extents are constant for the whole animation.
        var frameExtents = ReadFrameExtents(windowHandle);

        if (plan.Frames.Count == 0)
        {
            MoveResize(windowHandle, plan.FinalBounds, frameExtents);
            return;
        }

        Rectangle? previousBounds = null;
        var stopwatch = Stopwatch.StartNew();
        foreach (var frame in plan.Frames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remaining = frame.Offset - stopwatch.Elapsed;
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, cancellationToken);
            }

            if (previousBounds.HasValue && previousBounds.Value == frame.Bounds)
            {
                continue;
            }

            previousBounds = frame.Bounds;
            MoveResize(windowHandle, frame.Bounds, frameExtents);
        }

        if (!previousBounds.HasValue || previousBounds.Value != plan.FinalBounds)
        {
            MoveResize(windowHandle, plan.FinalBounds, frameExtents);
        }
    }

    private X11FrameExtents ReadFrameExtents(IntPtr windowHandle)
    {
        var values = X11PropertyReader.ReadLongArray(_connection, windowHandle, _connection.Atoms.NetFrameExtents);
        return values.Count >= 4
            ? new X11FrameExtents(values[0], values[1], values[2], values[3])
            : X11FrameExtents.None;
    }

    private void MoveResize(IntPtr windowHandle, Rectangle bounds, X11FrameExtents frameExtents)
    {
        // With StaticGravity _NET_MOVERESIZE_WINDOW positions the client window, so inset the
        // frame-rect target by _NET_FRAME_EXTENTS; otherwise server-side-decorated windows snap
        // with the title bar pushed under the top panel.
        var clientBounds = X11FrameGeometry.ToClientBounds(bounds, frameExtents);
        var flagsAndGravity = StaticGravity | MoveResizeFlags | (SourceApplication << 12);
        var ev = new X11Native.XClientMessageEvent
        {
            Type = X11Native.ClientMessage,
            Display = _connection.Display,
            Window = windowHandle,
            MessageType = _connection.Atoms.NetMoveresizeWindow,
            Format = 32,
            Data0 = new IntPtr(flagsAndGravity),
            Data1 = new IntPtr(clientBounds.X),
            Data2 = new IntPtr(clientBounds.Y),
            Data3 = new IntPtr(Math.Max(1, clientBounds.Width)),
            Data4 = new IntPtr(Math.Max(1, clientBounds.Height))
        };

        lock (_connection.SyncRoot)
        {
            if (_connection.IsDisposed)
            {
                // The display was closed during shutdown while this move was in flight.
                return;
            }

            X11Native.XSendEvent(
                _connection.Display,
                _connection.RootWindow,
                X11Native.False,
                new IntPtr(X11Native.SubstructureRedirectMask | X11Native.SubstructureNotifyMask),
                ref ev);
            X11Native.XFlush(_connection.Display);
        }
    }
}
