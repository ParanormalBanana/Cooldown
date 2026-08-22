using System.Diagnostics;

namespace Cooldown;

internal sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _tray;

    public TrayApp(string[] firstEvents)
    {
        var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        _tray = new NotifyIcon
        {
            Icon = icon,
            Text = "Cooldown",
            Visible = true,
            ContextMenuStrip = Menu(),
        };
        _tray.DoubleClick += (_, _) => OpenUi();
        Kick(firstEvents);
    }

    private ContextMenuStrip Menu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Cooldown", null, (_, _) => OpenUi());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Quit());
        return menu;
    }

    private static void Kick(string[] events)
    {
        Task.Run(() =>
        {
            try { Worker.Run(events); }
            catch (Exception ex) { Log.Error("Agent worker failed", ex); }
        });
    }

    private static void OpenUi()
    {
        try
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(dir, "Cooldown.exe"),
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Log.Error("Could not open Cooldown", ex);
        }
    }

    private void Quit()
    {
        _tray.Visible = false;
        _tray.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _tray.Dispose();
        base.Dispose(disposing);
    }
}
