using System.Runtime.InteropServices;

namespace Pop.App.Linux.Platform.X11;

internal static class X11PropertyReader
{
    // Items requested per XGetWindowProperty call, in 32-bit multiples. Long properties (e.g.
    // _NET_CLIENT_LIST with hundreds of windows) are read in chunks via bytesAfterReturn.
    private const int ChunkLength = 1024;

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

        List<IntPtr>? values = null;
        long offset = 0;
        while (true)
        {
            int status;
            int actualFormat;
            nuint itemsCount;
            nuint bytesAfter;
            IntPtr data;
            lock (connection.SyncRoot)
            {
                if (connection.IsDisposed)
                {
                    return (IReadOnlyList<IntPtr>?)values ?? [];
                }

                status = X11Native.XGetWindowProperty(
                    connection.Display,
                    window,
                    property,
                    new IntPtr(offset),
                    new IntPtr(ChunkLength),
                    X11Native.False,
                    new IntPtr(propertyType),
                    out _,
                    out actualFormat,
                    out itemsCount,
                    out bytesAfter,
                    out data);
            }

            if (status != X11Native.Success || data == IntPtr.Zero)
            {
                return (IReadOnlyList<IntPtr>?)values ?? [];
            }

            try
            {
                if (itemsCount > 0)
                {
                    // Atom and window arrays are always format 32 (delivered as native long);
                    // any other format cannot hold valid handles.
                    if (actualFormat != 32)
                    {
                        return [];
                    }

                    var count = checked((int)itemsCount);
                    values ??= new List<IntPtr>(count);
                    for (var index = 0; index < count; index++)
                    {
                        values.Add(Marshal.ReadIntPtr(data, index * IntPtr.Size));
                    }
                }
            }
            finally
            {
                lock (connection.SyncRoot)
                {
                    X11Native.XFree(data);
                }
            }

            if (bytesAfter == 0 || itemsCount == 0)
            {
                return (IReadOnlyList<IntPtr>?)values ?? [];
            }

            // XGetWindowProperty's offset is in 32-bit multiples; one format-32 item each.
            offset += (long)itemsCount;
        }
    }

    public static IReadOnlyList<long> ReadLongArray(X11DisplayConnection connection, IntPtr window, IntPtr property)
    {
        if (property == IntPtr.Zero)
        {
            return [];
        }

        List<long>? values = null;
        long offset = 0;
        while (true)
        {
            int status;
            int actualFormat;
            nuint itemsCount;
            nuint bytesAfter;
            IntPtr data;
            lock (connection.SyncRoot)
            {
                if (connection.IsDisposed)
                {
                    return (IReadOnlyList<long>?)values ?? [];
                }

                status = X11Native.XGetWindowProperty(
                    connection.Display,
                    window,
                    property,
                    new IntPtr(offset),
                    new IntPtr(ChunkLength),
                    X11Native.False,
                    X11Native.AnyPropertyType == 0 ? IntPtr.Zero : new IntPtr(X11Native.AnyPropertyType),
                    out _,
                    out actualFormat,
                    out itemsCount,
                    out bytesAfter,
                    out data);
            }

            if (status != X11Native.Success || data == IntPtr.Zero)
            {
                return (IReadOnlyList<long>?)values ?? [];
            }

            try
            {
                if (itemsCount > 0)
                {
                    // X11 property formats are 8, 16 or 32 only. Format 32 arrives as native
                    // long (pointer-sized), 16 as short and 8 as byte.
                    if (actualFormat is not (8 or 16 or 32))
                    {
                        return [];
                    }

                    var count = checked((int)itemsCount);
                    values ??= new List<long>(count);
                    for (var index = 0; index < count; index++)
                    {
                        values.Add(actualFormat switch
                        {
                            32 => Marshal.ReadIntPtr(data, index * IntPtr.Size).ToInt64(),
                            16 => Marshal.ReadInt16(data, index * sizeof(short)),
                            _ => Marshal.ReadByte(data, index)
                        });
                    }
                }
            }
            finally
            {
                lock (connection.SyncRoot)
                {
                    X11Native.XFree(data);
                }
            }

            if (bytesAfter == 0 || itemsCount == 0 || actualFormat != 32)
            {
                // Only chunk format-32 reads: the offset unit is 32-bit multiples, which the
                // smaller formats do not map onto cleanly, and no property Pop reads uses them.
                return (IReadOnlyList<long>?)values ?? [];
            }

            offset += (long)itemsCount;
        }
    }
}
