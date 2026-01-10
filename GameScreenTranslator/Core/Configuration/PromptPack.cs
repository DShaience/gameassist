namespace GameScreenTranslator.Core.Configuration;

public sealed class PromptPack
{
    public FoundryPrompts Foundry { get; set; } = new();

    public sealed class FoundryPrompts
    {
        public string System { get; set; } = "";
        public string TranslateUser { get; set; } = "";
        public string QuestionUser { get; set; } = "";
    }
}
