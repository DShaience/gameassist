using System.Collections.Generic;

namespace GameScreenTranslator.Core.Configuration;

public static class PromptTemplate
{
    /// <summary>
    /// Minimal, dependency-free template renderer.
    /// Replaces {key} with values from the dictionary.
    /// Unknown placeholders are left as-is.
    /// </summary>
    public static string Render(string template, IReadOnlyDictionary<string, string?> values)
    {
        if (string.IsNullOrEmpty(template) || values.Count == 0)
            return template;

        var result = template;

        foreach (var kvp in values)
        {
            var key = kvp.Key;
            if (string.IsNullOrWhiteSpace(key))
                continue;

            var placeholder = "{" + key + "}";
            var value = kvp.Value ?? "";

            result = result.Replace(placeholder, value);
        }

        return result;
    }
}
