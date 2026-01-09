using System;

namespace GameScreenTranslator.Core.Configuration;

/// <summary>
/// Represents a parsed hotkey configuration
/// </summary>
public class HotkeyConfig
{
    public ModifierKeys Modifiers { get; set; }
    public VirtualKey Key { get; set; }
    
    /// <summary>
    /// Parses a hotkey string like "Ctrl+Alt+T" into a HotkeyConfig
    /// </summary>
    public static HotkeyConfig Parse(string hotkeyString)
    {
        if (string.IsNullOrWhiteSpace(hotkeyString))
            throw new ArgumentException("Hotkey string cannot be empty", nameof(hotkeyString));
        
        var parts = hotkeyString.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        
        if (parts.Length < 2)
            throw new ArgumentException("Hotkey must contain at least one modifier and one key", nameof(hotkeyString));
        
        var config = new HotkeyConfig();
        
        // Parse all parts except the last one as modifiers
        for (int i = 0; i < parts.Length - 1; i++)
        {
            config.Modifiers |= ParseModifier(parts[i]);
        }
        
        // Last part is the key
        config.Key = ParseKey(parts[^1]);
        
        return config;
    }
    
    private static ModifierKeys ParseModifier(string modifier)
    {
        return modifier.ToLowerInvariant() switch
        {
            "ctrl" or "control" => ModifierKeys.Control,
            "alt" => ModifierKeys.Alt,
            "shift" => ModifierKeys.Shift,
            "win" or "windows" => ModifierKeys.Windows,
            _ => throw new ArgumentException($"Unknown modifier: {modifier}")
        };
    }
    
    private static VirtualKey ParseKey(string key)
    {
        // Handle single characters
        if (key.Length == 1)
        {
            char c = char.ToUpperInvariant(key[0]);
            if (c >= 'A' && c <= 'Z')
                return (VirtualKey)(0x41 + (c - 'A')); // VK_A = 0x41
            if (c >= '0' && c <= '9')
                return (VirtualKey)(0x30 + (c - '0')); // VK_0 = 0x30
        }
        
        // Handle function keys and special keys
        return key.ToLowerInvariant() switch
        {
            "f1" => VirtualKey.F1,
            "f2" => VirtualKey.F2,
            "f3" => VirtualKey.F3,
            "f4" => VirtualKey.F4,
            "f5" => VirtualKey.F5,
            "f6" => VirtualKey.F6,
            "f7" => VirtualKey.F7,
            "f8" => VirtualKey.F8,
            "f9" => VirtualKey.F9,
            "f10" => VirtualKey.F10,
            "f11" => VirtualKey.F11,
            "f12" => VirtualKey.F12,
            "space" => VirtualKey.Space,
            "enter" or "return" => VirtualKey.Enter,
            "esc" or "escape" => VirtualKey.Escape,
            "tab" => VirtualKey.Tab,
            _ => throw new ArgumentException($"Unknown key: {key}")
        };
    }
}

/// <summary>
/// Modifier keys that can be combined with hotkeys
/// </summary>
[Flags]
public enum ModifierKeys
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8
}

/// <summary>
/// Virtual key codes for Windows API
/// Based on Windows VK_ constants
/// </summary>
public enum VirtualKey
{
    // Letters A-Z (0x41-0x5A)
    A = 0x41, B, C, D, E, F, G, H, I, J, K, L, M,
    N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    
    // Numbers 0-9 (0x30-0x39)
    D0 = 0x30, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    
    // Function keys (0x70-0x7B)
    F1 = 0x70, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    
    // Special keys
    Space = 0x20,
    Enter = 0x0D,
    Escape = 0x1B,
    Tab = 0x09
}
