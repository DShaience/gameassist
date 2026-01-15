using GameScreenTranslator.Core.Models;

namespace GameScreenTranslator.Core.Foundry;

/// <summary>
/// Parses and normalizes responses from the Foundry/OpenAI API.
/// Handles common model quirks like wrapped strings, JSON fragments, etc.
/// </summary>
public static class FoundryResponseParser
{
    /// <summary>
    /// Normalizes all fields in a TranslateCycleResponse.
    /// </summary>
    public static void NormalizeOutput(TranslateCycleResponse output)
    {
        if (output is null)
            return;

        output.SelectedLabel = CleanSimple(output.SelectedLabel);
        output.SourceText = CleanUiText(output.SourceText);
        output.TranslatedText = CleanUiText(output.TranslatedText);
        output.Explanation = output.Explanation is null ? null : CleanSimple(output.Explanation);
        output.Answer = output.Answer is null ? null : CleanSimple(output.Answer);
    }

    /// <summary>
    /// Basic cleanup: strip wrapping quotes/backticks and trim whitespace.
    /// </summary>
    public static string CleanSimple(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        return StripWrapping(value).Trim();
    }

    /// <summary>
    /// Cleans UI text with additional heuristics for model quirks.
    /// Handles JSON-like key/value fragments the model sometimes returns.
    /// </summary>
    public static string CleanUiText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var s = StripWrapping(value).Trim().TrimEnd(',');

        // Heuristic: model sometimes returns a JSON-like key/value fragment.
        // Example:  "GameTitle": "Super Adventure",
        // Convert to: GameTitle: Super Adventure
        if (TryExtractKeyValue(s, out var key, out var val))
        {
            key = StripWrapping(key).Trim();
            val = StripWrapping(val).Trim();
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(val))
                return $"{key}: {val}";
        }

        return s;
    }

    /// <summary>
    /// Attempts to extract a key/value pair from a string like '"Key": "Value"'.
    /// </summary>
    public static bool TryExtractKeyValue(string s, out string key, out string value)
    {
        key = "";
        value = "";

        if (string.IsNullOrWhiteSpace(s))
            return false;

        var idx = s.IndexOf(':');
        if (idx <= 0 || idx >= s.Length - 1)
            return false;

        // Avoid times like "12:34" by requiring at least one quote or letter around the split.
        var left = s[..idx].Trim();
        var right = s[(idx + 1)..].Trim().TrimEnd(',');

        if (left.Length == 0 || right.Length == 0)
            return false;

        // Require that the left side looks like a label (letters/quotes/underscores/spaces).
        // This is intentionally loose.
        key = left;
        value = right;
        return true;
    }

    /// <summary>
    /// Removes common wrapping characters (backticks, quotes) from strings.
    /// </summary>
    public static string StripWrapping(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return "";

        var t = s.Trim();

        // Remove wrapping backticks (rare but happens)
        if (t.Length >= 2 && t[0] == '`' && t[^1] == '`')
            t = t[1..^1].Trim();

        // Remove wrapping quotes
        if (t.Length >= 2 && t[0] == '"' && t[^1] == '"')
            t = t[1..^1].Trim();

        return t;
    }
}
