using System;
using GameScreenTranslator.Core.Configuration;

namespace GameScreenTranslator.Core.App;

public static class FallbackSpeech
{
    public static string ResolveGenericErrorMessage(AppSettings settings, string? targetLanguage)
    {
        if (settings is null) throw new ArgumentNullException(nameof(settings));

        var audio = settings.Audio ?? new AppSettings.AudioSettings();

        // Prefer the current target language key if present.
        if (!string.IsNullOrWhiteSpace(targetLanguage) &&
            audio.FallbackMessagesByLanguage.TryGetValue(targetLanguage.Trim(), out var byLang) &&
            !string.IsNullOrWhiteSpace(byLang))
        {
            return byLang;
        }

        // Otherwise use configured default key.
        var key = audio.GenericErrorMessageKey;
        if (!string.IsNullOrWhiteSpace(key) &&
            audio.FallbackMessagesByLanguage.TryGetValue(key.Trim(), out var byKey) &&
            !string.IsNullOrWhiteSpace(byKey))
        {
            return byKey;
        }

        // Hard fallback.
        return "Sorry, something went wrong. Please try again.";
    }
}