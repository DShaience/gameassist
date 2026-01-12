using GameScreenTranslator.Core.Capture;
using GameScreenTranslator.Core.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace GameScreenTranslator.Core.Abstractions;

public interface IInteractionLogger
{
    void Append(InteractionLogEntry entry);

    string? EncodeScreenshotPngBase64(Image<Rgba32> screenshot);
}
