using Nostos.Win32.Interop;

namespace Nostos.Win32.Services;

/// <summary>An icon read off disk, as raw pixels the UI layer can wrap in whatever it draws with.</summary>
/// <param name="Width">Pixels across.</param>
/// <param name="Height">Pixels down.</param>
/// <param name="Bgra">Top-down BGRA, straight (not premultiplied) alpha. Length is Width * Height * 4.</param>
public sealed record IconPixels(int Width, int Height, byte[] Bgra);

/// <summary>
/// The icon Windows would draw for a file.
///
/// Deliberately returns pixels rather than any drawing type. This assembly has no UI framework
/// in it and should not acquire one, and the alternative -- System.Drawing -- is a large
/// dependency with its own ahead-of-time compilation caveats, brought in to do something that
/// is forty lines of GDI.
///
/// Everything here reads a file on disk. Nothing opens a process.
/// </summary>
public static class FileIcons
{
    /// <summary>
    /// Reads a file's icon, or null when it has none this can decode.
    ///
    /// Never throws for an ordinary failure. A startup entry can point at a file that has been
    /// uninstalled, sits on a disconnected drive, or is a .lnk to something long gone, and a row
    /// with no icon is a perfectly good answer to all of those.
    /// </summary>
    public static IconPixels? TryRead(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        // The file's own icon first, and the generic one for its extension if that fails.
        //
        // The fallback is not redundant. A Store app's launcher under WindowsApps is a
        // zero-length reparse point that the shell cannot open on the file's behalf, so asking
        // for its real icon returns nothing -- and Teams, which is exactly that, is the kind of
        // thing people most want to find in a startup list. SHGFI_USEFILEATTRIBUTES tells the
        // shell to answer from the name alone and never touch the file.
        return Read(path, NativeMethods.ShgfiIcon | NativeMethods.ShgfiLargeIcon)
               ?? Read(path, NativeMethods.ShgfiIcon | NativeMethods.ShgfiLargeIcon
                             | NativeMethods.ShgfiUseFileAttributes);
    }

    private static IconPixels? Read(string path, uint flags)
    {
        var info = new NativeMethods.ShFileInfo();
        var handle = NativeMethods.SHGetFileInfoW(
            path, 0x80 /* FILE_ATTRIBUTE_NORMAL */, ref info,
            (uint)System.Runtime.InteropServices.Marshal.SizeOf(info), flags);

        if (handle == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            return null;

        try
        {
            return FromIcon(info.hIcon);
        }
        catch (Exception e) when (e is InvalidOperationException or OutOfMemoryException)
        {
            return null;
        }
        finally
        {
            NativeMethods.DestroyIcon(info.hIcon);
        }
    }

    private static IconPixels? FromIcon(IntPtr icon)
    {
        if (!NativeMethods.GetIconInfo(icon, out var iconInfo))
            return null;

        try
        {
            if (iconInfo.hbmColor == IntPtr.Zero)
                return null;

            var bitmap = new NativeMethods.Bitmap();
            if (NativeMethods.GetObjectW(iconInfo.hbmColor, System.Runtime.InteropServices.Marshal.SizeOf(bitmap), ref bitmap) == 0)
                return null;

            var width = bitmap.bmWidth;
            var height = bitmap.bmHeight;
            if (width <= 0 || height <= 0 || width > 512 || height > 512)
                return null;

            var pixels = ReadBits(iconInfo.hbmColor, width, height);
            if (pixels is null)
                return null;

            // A 32-bit icon carries its own alpha. An older 1- or 8-bit one does not, and comes
            // back fully transparent -- an invisible icon rather than a missing one, which looks
            // like a bug in the list rather than an old icon. The AND mask is where the shape
            // lives in that case.
            if (IsFullyTransparent(pixels) && iconInfo.hbmMask != IntPtr.Zero)
                ApplyMask(iconInfo.hbmMask, width, height, pixels);

            return IsFullyTransparent(pixels) ? null : new IconPixels(width, height, pixels);
        }
        finally
        {
            if (iconInfo.hbmColor != IntPtr.Zero)
                NativeMethods.DeleteObject(iconInfo.hbmColor);

            if (iconInfo.hbmMask != IntPtr.Zero)
                NativeMethods.DeleteObject(iconInfo.hbmMask);
        }
    }

    private static byte[]? ReadBits(IntPtr bitmap, int width, int height)
    {
        var screen = NativeMethods.GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero)
            return null;

        try
        {
            var header = new NativeMethods.BitmapInfoHeader
            {
                biSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                biWidth = width,

                // Negative height asks GDI for a top-down bitmap, which is the order every UI
                // toolkit wants. Positive would come back upside down, and a flipped icon is
                // exactly the kind of thing that gets noticed after release.
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,
            };

            var pixels = new byte[width * height * 4];
            return NativeMethods.GetDIBits(screen, bitmap, 0, (uint)height, pixels, ref header, 0) == 0
                ? null
                : pixels;
        }
        finally
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>
    /// Fills in alpha from an icon's AND mask, for icons that predate 32-bit colour.
    ///
    /// The mask is 1 where the pixel should be transparent, which is the opposite way round from
    /// how alpha reads, hence the inversion.
    /// </summary>
    private static void ApplyMask(IntPtr mask, int width, int height, byte[] pixels)
    {
        var maskPixels = ReadBits(mask, width, height);
        if (maskPixels is null)
            return;

        for (var i = 0; i < pixels.Length; i += 4)
            pixels[i + 3] = maskPixels[i] == 0 ? (byte)255 : (byte)0;
    }

    private static bool IsFullyTransparent(byte[] pixels)
    {
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0)
                return false;
        }

        return true;
    }
}
