using System;
using System.Collections.Generic;

using GameScreenTranslator.Core.Abstractions;

namespace GameScreenTranslator.Core.State;

/// <summary>
/// Tracks state per "screen signature" to support cycling logic and
/// "don't repeat what was already spoken on this screen".
/// </summary>
public sealed class ScreenStateManager : IScreenStateManager
{
    private readonly Dictionary<ulong, ScreenState> _states = new();

    public ulong? CurrentSignature { get; private set; }

    public ScreenState Current
    {
        get
        {
            if (CurrentSignature is null)
                throw new InvalidOperationException("No current screen signature set. Call SetCurrentScreen(...) first.");

            return _states[CurrentSignature.Value];
        }
    }

    /// <summary>
    /// Sets the current screen signature, creating state if needed.
    /// If the signature changes, the new screen starts with cycle index 0 and empty spoken set.
    /// </summary>
    public void SetCurrentScreen(ulong signature)
    {
        if (!_states.ContainsKey(signature))
        {
            _states[signature] = new ScreenState();
        }

        CurrentSignature = signature;
    }

    public int GetCycleIndex() => Current.CycleIndex;

    public void AdvanceCycle()
    {
        Current.CycleIndex++;
    }

    public bool HasSpoken(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        return Current.SpokenKeys.Contains(key);
    }

    public void MarkSpoken(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        Current.SpokenKeys.Add(key);
    }

    public void ClearAll()
    {
        _states.Clear();
        CurrentSignature = null;
    }

    public sealed class ScreenState
    {
        public int CycleIndex { get; set; } = 0;

        // Key can be "region_id" or "normalized text" or "<label>:<bbox>" etc.
        public HashSet<string> SpokenKeys { get; } = new(StringComparer.Ordinal);
    }
}
