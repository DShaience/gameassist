namespace GameScreenTranslator.Core.Configuration;

/// <summary>
/// Centralized definitions for all app hotkeys.
/// Provides default key combinations, names, and descriptions used throughout the app.
/// </summary>
public static class HotkeyDefinitions
{
    /// <summary>
    /// Translate/Cycle: Captures screenshot and translates visible text.
    /// </summary>
    public static readonly HotkeyDef Translate = new(
        Name: "Translate",
        DefaultShortcut: "Ctrl+Alt+T",
        Description: "Translate what's on screen"
    );

    /// <summary>
    /// Ask: Tap-to-talk mode. Captures screenshot, records voice question, answers via TTS.
    /// </summary>
    public static readonly HotkeyDef Ask = new(
        Name: "Ask",
        DefaultShortcut: "Ctrl+Alt+W",
        Description: "Ask a question about what's on screen"
    );

    /// <summary>
    /// Stop: Cancels current operation and stops TTS playback.
    /// </summary>
    public static readonly HotkeyDef Stop = new(
        Name: "Stop",
        DefaultShortcut: "Ctrl+Alt+X",
        Description: "Stop speaking"
    );

    /// <summary>
    /// ResetConversation: Clears conversation history and rolling summary.
    /// </summary>
    public static readonly HotkeyDef ResetConversation = new(
        Name: "ResetConversation",
        DefaultShortcut: "Ctrl+Alt+R",
        Description: "Clear conversation history"
    );

    /// <summary>
    /// All defined hotkeys in order.
    /// </summary>
    public static readonly HotkeyDef[] All = { Translate, Ask, Stop, ResetConversation };

    /// <summary>
    /// Formats a hotkey for display in logs or UI.
    /// Example: "Translate (Ctrl+Alt+T)"
    /// </summary>
    public static string FormatForLog(HotkeyDef def, string? actualShortcut = null)
    {
        var shortcut = actualShortcut ?? def.DefaultShortcut;
        return $"{def.Name} ({shortcut})";
    }
}

/// <summary>
/// Definition of a single hotkey action.
/// </summary>
/// <param name="Name">Short identifier used in logs and config.</param>
/// <param name="DefaultShortcut">Default key combination (e.g., "Ctrl+Alt+T").</param>
/// <param name="Description">User-friendly description of what this hotkey does.</param>
public sealed record HotkeyDef(string Name, string DefaultShortcut, string Description);
