using System.Runtime.InteropServices;

namespace Pop.App.Linux.Platform.X11;

internal static class X11PropertyReader
{
    public static bool HasAtom(X11DisplayConnection connection, IntPtr window, IntPtr property, IntPtr atom)
    {
        return ReadIntPtrArray(connection, window, property, X11Native.AnyPropertyType).Contains(atom);
    }

    public static IReadOnlyList<IntPtr> ReadIntPtrArray(X11DisplayConnection connection, IntPtr window, IntPtr property, long propertyType)
    {
        if (property == IntPtr.Zero)
        {
            return [];
        }

        var status = X11Native.XGetWindowProperty(
            connection.Display,
            window,
            property,
            IntPtr.Zero,
            new IntPtr(1024),
            X11Native.False,
            new IntPtr(propertyType),
            out _,
            out var actualFormat,
            out var itemsCount,
            out _,
            out var data);

        if (status != X11Native.Success || data == IntPtr.Zero || itemsCount == 0)
        {
            return [];
        }

        try
        {
            var count = checked((int)itemsCount);
            var values = new IntPtr[count];
            if (actualFormat == 32)
            {
                for (var index = 0; index < count; index++)
                {
                    values[index] = Marshal.ReadIntPtr(data, index * IntPtr.Size);
                }
            }
            else if (actualFormat == 64)
            {
                for (var index = 0; index < count; index++)
                {
                    values[index] = new IntPtr(Marshal.ReadInt64(data, index * sizeof(long)));
                }
            }

            return values;
        }
        finally
        {
            X11Native.XFree(data);
        }
    }

    public static IReadOnlyList<long> ReadLongArray(X11DisplayConnection connection, IntPtr window, IntPtr property)
    {
        if (property == IntPtr.Zero)
        {
            return [];
        }

        var status = X11Native.XGetWindowProperty(
            connection.Display,
            window,
            property,
            IntPtr.Zero,
            new IntPtr(1024),
            X11Native.False,
            X11Native.AnyPropertyType == 0 ? IntPtr.Zero : new IntPtr(X11Native.AnyPropertyType),
            out _,
            out var actualFormat,
            out var itemsCount,
            out _,
            out var data);

        if (status != X11Native.Success || data == IntPtr.Zero || itemsCount == 0)
        {
            return [];
        }

        try
        {
            var count = checked((int)itemsCount);
            var values = new long[count];
            if (actualFormat == 32)
            {
                for (var index = 0; index < count; index++)
                {
                    values[index] = Marshal.ReadIntPtr(data, index * IntPtr.Size).ToInt64();
                }
            }
            else if (actualFormat == 64)
            {
                for (var index = 0; index < count; index++)
                {
                    values[index] = Marshal.ReadInt64(data, index * sizeof(long));
                }
            }

            return values;
        }
        finally
        {
            X11Native.XFree(data);
        }
    }
}
