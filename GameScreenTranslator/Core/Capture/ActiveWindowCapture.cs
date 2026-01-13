using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using GameScreenTranslator.Core.Abstractions;
using GameScreenTranslator.Core.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace GameScreenTranslator.Core.Capture;

public sealed class ActiveWindowCapture : ICaptureService
{
    public WindowCaptureResult CaptureActiveWindow()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == nint.Zero)
            throw new InvalidOperationException("No foreground window found.");

        var title = GetWindowTitle(hwnd);

        _ = GetWindowThreadProcessId(hwnd, out var pid);

        string? processName = null;
        try
        {
            processName = Process.GetProcessById((int)pid).ProcessName;
        }
        catch
        {
            // ignore
        }

        using var bmp = CaptureWindowBitmap(hwnd);
        var img = ConvertBitmapToImageSharp(bmp);

        return new WindowCaptureResult(
            Hwnd: hwnd,
            WindowTitle: title,
            ProcessId: (int)pid,
            ProcessName: processName,
            Screenshot: img
        );
    }

    private static string GetWindowTitle(nint hwnd)
    {
        var sb = new StringBuilder(1024);
        _ = GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static Bitmap CaptureWindowBitmap(nint hwnd)
    {
        if (!GetWindowRect(hwnd, out var rect))
            throw new InvalidOperationException("GetWindowRect failed.");

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;

        if (width <= 0 || height <= 0)
            throw new InvalidOperationException($"Invalid window bounds: {width}x{height}");

        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        using var graphics = Graphics.FromImage(bmp);
        var hdc = graphics.GetHdc();

        var hdcReleased = false;

        try
        {
            // PW_RENDERFULLCONTENT = 0x00000002 (Win8.1+). Helps with some windows.
            var ok = PrintWindow(hwnd, hdc, 2);

            // Release HDC before any CopyFromScreen fallback.
            graphics.ReleaseHdc(hdc);
            hdcReleased = true;

            if (!ok)
            {
                // Fallback: screen copy (may capture occluded portions; acceptable for P0).
                // NOTE: On newer System.Drawing builds, combining flags (SourceCopy|CaptureBlt) can throw
                // InvalidEnumArgumentException. Retry with SourceCopy-only for compatibility.
                try
                {
                    graphics.CopyFromScreen(
                        rect.Left,
                        rect.Top,
                        0,
                        0,
                        new System.Drawing.Size(width, height),
                        CopyPixelOperation.SourceCopy | CopyPixelOperation.CaptureBlt);
                }
                catch (System.ComponentModel.InvalidEnumArgumentException)
                {
                    graphics.CopyFromScreen(
                        rect.Left,
                        rect.Top,
                        0,
                        0,
                        new System.Drawing.Size(width, height),
                        CopyPixelOperation.SourceCopy);
                }

                if (IsMostlyBlack(bmp))
                    Log.Warn("CopyFromScreen also looks black (window may be GPU/overlay protected)");

                return bmp;
            }

            // Some GPU-accelerated windows return "success" but the bitmap is black.
            // Detect that and fallback to a screen copy of the window bounds.
            if (IsMostlyBlack(bmp))
            {
                Log.Debug("PrintWindow returned black image; falling back to CopyFromScreen");
                try
                {
                    graphics.CopyFromScreen(
                        rect.Left,
                        rect.Top,
                        0,
                        0,
                        new System.Drawing.Size(width, height),
                        CopyPixelOperation.SourceCopy | CopyPixelOperation.CaptureBlt);
                }
                catch (System.ComponentModel.InvalidEnumArgumentException)
                {
                    graphics.CopyFromScreen(
                        rect.Left,
                        rect.Top,
                        0,
                        0,
                        new System.Drawing.Size(width, height),
                        CopyPixelOperation.SourceCopy);
                }

                if (IsMostlyBlack(bmp))
                    Log.Warn("CopyFromScreen also looks black (window may be GPU/overlay protected)");
            }

            return bmp;
        }
        finally
        {
            if (!hdcReleased)
            {
                try { graphics.ReleaseHdc(hdc); } catch { }
            }
        }
    }

    private static bool IsMostlyBlack(Bitmap bmp)
    {
        // Heuristic: sample pixels on a grid and check if most are near-black.
        var rect = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var stride = data.Stride;
            var height = data.Height;
            var width = data.Width;

            var bytes = new byte[stride * height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);

            // Sample a grid of points (bounded).
            var stepX = Math.Max(1, width / 40);
            var stepY = Math.Max(1, height / 40);

            var samples = 0;
            var dark = 0;

            for (var y = 0; y < height; y += stepY)
            {
                var rowOffset = y * stride;
                for (var x = 0; x < width; x += stepX)
                {
                    var i = rowOffset + x * 4;
                    if (i + 3 >= bytes.Length)
                        continue;

                    // BGRA
                    var b = bytes[i + 0];
                    var g = bytes[i + 1];
                    var r = bytes[i + 2];

                    var brightness = (r + g + b) / 3;
                    if (brightness < 8)
                        dark++;

                    samples++;
                    if (samples >= 2500)
                        break;
                }

                if (samples >= 2500)
                    break;
            }

            if (samples == 0)
                return false;

            var darkRatio = (double)dark / samples;
            return darkRatio >= 0.98;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    private static Image<Rgba32> ConvertBitmapToImageSharp(Bitmap bmp)
    {
        var rect = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            // Bitmap is BGRA; ImageSharp expects RGBA.
            // We'll copy and swizzle channels.
            var bytes = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);

            var img = new Image<Rgba32>(bmp.Width, bmp.Height);

            img.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    var srcOffset = y * data.Stride;
                    for (int x = 0; x < row.Length; x++)
                    {
                        var i = srcOffset + x * 4;
                        byte b = bytes[i + 0];
                        byte g = bytes[i + 1];
                        byte r = bytes[i + 2];
                        byte a = bytes[i + 3];
                        row[x] = new Rgba32(r, g, b, a);
                    }
                }
            });

            return img;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    #region Win32

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(nint hwnd, nint hdcBlt, uint nFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    #endregion
}
