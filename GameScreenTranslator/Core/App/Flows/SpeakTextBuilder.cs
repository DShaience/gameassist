using GameScreenTranslator.Core.Models;

namespace GameScreenTranslator.Core.App.Flows;

public static class SpeakTextBuilder
{
    public static string Build(TranslateCycleResponse response)
    {
        // Avoid speaking just "." when translatedText is empty.
        if (response is null)
            return "";

        var translated = (response.TranslatedText ?? "").Trim();
        var explanation = (response.Explanation ?? "").Trim();
        var source = (response.SourceText ?? "").Trim();

        if (translated.Length == 0)
        {
            if (explanation.Length > 0)
                return explanation;

            if (source.Length > 0)
                return source;

            return "לא הצלחתי למצוא טקסט קריא במסך הזה.";
        }

        if (explanation.Length > 0)
            return $"{translated}. {explanation}";

        return translated;
    }
}
