using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace GameScreenTranslator.Core.App;

public sealed class TrayAppContext : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly CancellationTokenSource _shutdownCts;

    public TrayAppContext(CancellationTokenSource shutdownCts, string logsDirectory)
    {
        _shutdownCts = shutdownCts ?? throw new ArgumentNullException(nameof(shutdownCts));

        var menu = new ContextMenuStrip();

        menu.Items.Add("Open logs folder", null, (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(logsDirectory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = logsDirectory,
                    UseShellExecute = true
                });
            }
            catch
            {
                // ignore
            }
        });

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Exit", null, (_, _) => Exit());

        _icon = new NotifyIcon
        {
            Text = "Game Screen Translator",
            Visible = true,
            ContextMenuStrip = menu
        };

        // If the app has an icon embedded in the executable, it will show automatically.
        // Otherwise, it will fall back to a generic icon.
        try { _icon.Icon = System.Drawing.SystemIcons.Application; } catch { /* ignore */ }

        _icon.DoubleClick += (_, _) =>
        {
            // For now: open logs (most useful UX in P0).
            try
            {
                Directory.CreateDirectory(logsDirectory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = logsDirectory,
                    UseShellExecute = true
                });
            }
            catch
            {
                // ignore
            }
        };
    }

    private void Exit()
    {
        try { _shutdownCts.Cancel(); } catch { /* ignore */ }
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                _icon.Visible = false;
                _icon.Dispose();
            }
            catch
            {
                // ignore
            }
        }

        base.Dispose(disposing);
    }
}