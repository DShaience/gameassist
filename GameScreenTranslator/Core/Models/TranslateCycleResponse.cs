namespace GameScreenTranslator.Core.Models;

/// <summary>
/// Expected structured output from the vision+translation model call.
/// </summary>
public sealed class TranslateCycleResponse
{
    public string SelectedLabel { get; set; } = "";
    public BoundingBoxRect? BoundingBox { get; set; }

    public string SourceText { get; set; } = "";
    public string TranslatedText { get; set; } = "";
    public string? Explanation { get; set; }

    /// <summary>
    /// The answer to the user's question (used in Q&A mode).
    /// When present, this is what should be spoken by TTS.
    /// </summary>
    public string? Answer { get; set; }

    // Token usage info (populated from API response, not from model output)
    // These are set by AzureFoundryClient after parsing the response.
    /// <summary>
    /// Number of tokens in the prompt (input).
    /// </summary>
    public int? PromptTokens { get; set; }

    /// <summary>
    /// Number of tokens in the completion (output).
    /// </summary>
    public int? CompletionTokens { get; set; }

    public sealed class BoundingBoxRect
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }
}
