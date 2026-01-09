namespace GameScreenTranslator.Core.Models;

/// <summary>
/// Expected structured output from the vision+translation model call.
/// P0 keeps this minimal; later we can extend with token usage, confidence, etc.
/// </summary>
public sealed class TranslateCycleResponse
{
    public string SelectedLabel { get; set; } = "";
    public BoundingBoxRect? BoundingBox { get; set; }

    public string SourceText { get; set; } = "";
    public string TranslatedText { get; set; } = "";
    public string? Explanation { get; set; }

    public sealed class BoundingBoxRect
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }
}
