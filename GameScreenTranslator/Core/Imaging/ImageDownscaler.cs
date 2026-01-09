using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace GameScreenTranslator.Core.Imaging;

public static class ImageDownscaler
{
    /// <summary>
    /// Returns a resized clone of the image if its width exceeds targetWidth.
    /// If the image is already <= targetWidth, returns a clone with the same dimensions.
    ///
    /// Note: We accept Image<Rgba32> because ImageSharp's non-generic Image doesn't expose Clone().
    /// </summary>
    public static Image<Rgba32> DownscaleToWidth(Image<Rgba32> source, int targetWidth)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (targetWidth <= 0) throw new ArgumentOutOfRangeException(nameof(targetWidth));

        // Keep behavior deterministic for hashing: always return a new image instance.
        if (source.Width <= targetWidth)
        {
            return source.Clone();
        }

        var ratio = (double)targetWidth / source.Width;
        var targetHeight = (int)Math.Round(source.Height * ratio);
        if (targetHeight < 1) targetHeight = 1;

        return source.Clone(ctx =>
        {
            ctx.Resize(new ResizeOptions
            {
                Size = new Size(targetWidth, targetHeight),
                Mode = ResizeMode.Max
            });
        });
    }

    public static byte[] EncodePng(Image source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));

        using var ms = new MemoryStream();
        source.Save(ms, new PngEncoder());
        return ms.ToArray();
    }
}
