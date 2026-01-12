using GameScreenTranslator.Core.Capture;

namespace GameScreenTranslator.Core.Abstractions;

public interface ICaptureService
{
    WindowCaptureResult CaptureActiveWindow();
}
