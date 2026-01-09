using System.Text;
using Xunit;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace GameScreenTranslator.Tests.Unit;

public class ScreenSignatureTests
{
    [Fact]
    public void Compute_IsDeterministic_ForSameTitleAndPixels()
    {
        // Arrange
        const string title = "Test Game Window";
        using var img = new Image<Rgba32>(width: 100, height: 50); // all zeros by default

        // Act
        var sig1 = GameScreenTranslator.Core.State.ScreenSignature.Compute(title, img);
        var sig2 = GameScreenTranslator.Core.State.ScreenSignature.Compute(title, img);

        // Assert
        Assert.Equal(sig1, sig2);
        Assert.NotEqual(0UL, sig1);
    }

    [Fact]
    public void Compute_Changes_WhenTitleChanges()
    {
        // Arrange
        using var img = new Image<Rgba32>(width: 100, height: 50);

        // Act
        var sig1 = GameScreenTranslator.Core.State.ScreenSignature.Compute("A", img);
        var sig2 = GameScreenTranslator.Core.State.ScreenSignature.Compute("B", img);

        // Assert
        Assert.NotEqual(sig1, sig2);
    }

    [Fact]
    public void Compute_Changes_WhenPixelsChange()
    {
        // Arrange
        const string title = "SameTitle";
        using var img1 = new Image<Rgba32>(width: 20, height: 10); // zeros
        using var img2 = new Image<Rgba32>(width: 20, height: 10);
        img2[0, 0] = new Rgba32(255, 0, 0, 255);

        // Act
        var sig1 = GameScreenTranslator.Core.State.ScreenSignature.Compute(title, img1);
        var sig2 = GameScreenTranslator.Core.State.ScreenSignature.Compute(title, img2);

        // Assert
        Assert.NotEqual(sig1, sig2);
    }

    [Fact]
    public void Compute_ThrowsOnEmptyTitle()
    {
        // Arrange
        using var img = new Image<Rgba32>(width: 10, height: 10);

        // Act + Assert
        Assert.Throws<ArgumentException>(() => GameScreenTranslator.Core.State.ScreenSignature.Compute("", img));
        Assert.Throws<ArgumentException>(() => GameScreenTranslator.Core.State.ScreenSignature.Compute("   ", img));
    }
}