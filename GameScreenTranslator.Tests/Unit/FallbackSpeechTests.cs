using GameScreenTranslator.Core.App;
using GameScreenTranslator.Core.Configuration;
using Xunit;

namespace GameScreenTranslator.Tests.Unit;

public sealed class FallbackSpeechTests
{
    [Fact]
    public void ResolveGenericErrorMessage_PrefersExactTargetLanguageKey()
    {
        var settings = new AppSettings
        {
            Audio = new AppSettings.AudioSettings
            {
                GenericErrorMessageKey = "Hebrew",
                FallbackMessagesByLanguage =
                {
                    ["Hebrew"] = "HE",
                    ["Russian"] = "RU"
                }
            }
        };

        var msg = FallbackSpeech.ResolveGenericErrorMessage(settings, "Russian");
        Assert.Equal("RU", msg);
    }

    [Fact]
    public void ResolveGenericErrorMessage_FallsBackToGenericKey()
    {
        var settings = new AppSettings
        {
            Audio = new AppSettings.AudioSettings
            {
                GenericErrorMessageKey = "Hebrew",
                FallbackMessagesByLanguage =
                {
                    ["Hebrew"] = "HE"
                }
            }
        };

        var msg = FallbackSpeech.ResolveGenericErrorMessage(settings, "UnknownLanguage");
        Assert.Equal("HE", msg);
    }

    [Fact]
    public void ResolveGenericErrorMessage_HasHardFallback()
    {
        var settings = new AppSettings
        {
            Audio = new AppSettings.AudioSettings
            {
                GenericErrorMessageKey = "Missing",
                FallbackMessagesByLanguage = { }
            }
        };

        var msg = FallbackSpeech.ResolveGenericErrorMessage(settings, null);
        Assert.False(string.IsNullOrWhiteSpace(msg));
    }
}