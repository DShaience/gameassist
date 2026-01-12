namespace GameScreenTranslator.Core.Abstractions;

public interface IScreenStateManager
{
    void SetCurrentScreen(ulong signature);

    int GetCycleIndex();

    void AdvanceCycle();

    bool HasSpoken(string key);

    void MarkSpoken(string key);

    void ClearAll();
}
