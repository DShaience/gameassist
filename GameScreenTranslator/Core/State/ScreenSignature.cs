using System;
using System.IO.Hashing;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using GameScreenTranslator.Core.Imaging;

namespace GameScreenTranslator.Core.State;

public static class ScreenSignature
{
    /// <summary>
    /// Computes a stable signature from (windowTitle + downscaled image bytes).
    /// Uses xxHash64 for speed.
    /// </summary>
    public static ulong Compute(string windowTitle, Image<Rgba32> screenshot)
    {
        if (string.IsNullOrWhiteSpace(windowTitle))
            throw new ArgumentException("Window title cannot be empty.", nameof(windowTitle));
        if (screenshot is null)
            throw new ArgumentNullException(nameof(screenshot));

        // Downscale for speed and stability.
        using var downscaled = ImageDownscaler.DownscaleToWidth(screenshot, targetWidth: 320);

        // Encode to PNG to get a deterministic byte sequence.
        var pngBytes = ImageDownscaler.EncodePng(downscaled);
        var titleBytes = Encoding.UTF8.GetBytes(windowTitle);

        // data = title + '\n' + pngBytes
        var data = new byte[titleBytes.Length + 1 + pngBytes.Length];
        Buffer.BlockCopy(titleBytes, 0, data, 0, titleBytes.Length);
        data[titleBytes.Length] = (byte)'\n';
        Buffer.BlockCopy(pngBytes, 0, data, titleBytes.Length + 1, pngBytes.Length);

        return XxHash64.HashToUInt64(data);
    }
}
