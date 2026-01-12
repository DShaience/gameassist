using System.Threading.Tasks;
using GameScreenTranslator.Core.Configuration;

namespace GameScreenTranslator.Core.App.Commands;

/// <summary>
/// Simple command interface. Each CLI mode (--hotkey-mode, --capture-once, etc.) is a command.
/// Commands receive runtime context (or null if they don't need config) and extra args.
/// </summary>
public interface ICliCommand
{
    /// <summary>
    /// The primary CLI flag that triggers this command (e.g. "--hotkey-mode").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// True if this command needs full bootstrap (settings, prompts, speech config).
    /// Some lightweight commands (--capture-once, --cycle-demo) don't need config.
    /// </summary>
    bool RequiresFullBootstrap { get; }

    /// <summary>
    /// Run the command.
    /// </summary>
    Task RunAsync(RuntimeContext? ctx, string[] args);
}
