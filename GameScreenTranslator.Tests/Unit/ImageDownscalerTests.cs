using System;
using Xunit;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace GameScreenTranslator.Tests.Unit;

public class ImageDownscalerTests
{
    [Fact]
    public void DownscaleToWidth_PreservesAspectRatio()
    {
        // Arrange
        using var img = new Image<Rgba32>(width: 800, height: 600);
        // No need to draw for these unit tests; geometry is what matters.

        // Act
        using var resized = GameScreenTranslator.Core.Imaging.ImageDownscaler.DownscaleToWidth(img, targetWidth: 320);

        // Assert
        Assert.Equal(320, resized.Width);
        Assert.Equal(240, resized.Height); // 600 * (320/800)
    }

    [Fact]
    public void DownscaleToWidth_WhenAlreadySmall_ReturnsCloneOrSameSize()
    {
        // Arrange
        using var img = new Image<Rgba32>(width: 200, height: 100);

        // Act
        using var resized = GameScreenTranslator.Core.Imaging.ImageDownscaler.DownscaleToWidth(img, targetWidth: 320);

        // Assert
        Assert.Equal(200, resized.Width);
        Assert.Equal(100, resized.Height);
    }

    [Fact]
    public void EncodePng_ReturnsNonEmptyBytes()
    {
        // Arrange
        using var img = new Image<Rgba32>(width: 100, height: 50);

        // Act
        var bytes = GameScreenTranslator.Core.Imaging.ImageDownscaler.EncodePng(img);

        // Assert
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }
}
