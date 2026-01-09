using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace GameScreenTranslator.Core.Capture;

public sealed record WindowCaptureResult(
    nint Hwnd,
    string WindowTitle,
    int ProcessId,
    string? ProcessName,
    Image<Rgba32> Screenshot
);