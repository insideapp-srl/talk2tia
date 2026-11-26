using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Talk2Tia.Core.Openness;
using Talk2Tia.Core.Updates;

namespace Talk2Tia.Core.Tray;

/// <summary>
/// Talk2Tia icon in the Windows notification area, with version, links and the update command. Every MCP client
/// session starts its own talk2tia.exe: only one of them shows the icon (named mutex); when it exits, another takes over.
/// Runs on its own STA thread with a message loop, so it never touches the MCP stdio channel.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const string MutexName = @"Local\Talk2Tia.TrayIcon";

    private readonly UpdateService _updates;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _stopping = new(false);
    private ApplicationContext? _context;
    private NotifyIcon? _icon;
    private ToolStripMenuItem? _updateItem;
    private ToolStripMenuItem? _sessionsItem;
    private SynchronizationContext? _ui;

    private TrayIcon(UpdateService updates)
    {
        _updates = updates;
        _thread = new Thread(Run) { IsBackground = true, Name = "Talk2Tia tray" };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    /// <summary>Starts the icon unless the process has no interactive desktop (service, CI runner).</summary>
    public static TrayIcon? Start(UpdateService updates)
    {
        if (!Environment.UserInteractive)
        {
            return null;
        }

        var tray = new TrayIcon(updates);
        tray._thread.Start();
        return tray;
    }

    private void Run()
    {
        try
        {
            using var mutex = new Mutex(false, MutexName);
            try
            {
                // Wait until no other Talk2Tia process shows the icon, or until this process stops.
                while (!mutex.WaitOne(TimeSpan.FromSeconds(2)))
                {
                    if (_stopping.IsSet)
                    {
                        return;
                    }
                }
            }
            catch (AbandonedMutexException)
            {
                // The previous owner exited without releasing: this process takes over.
            }

            try
            {
                if (!_stopping.IsSet)
                {
                    ShowIcon();
                }
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.ExternalException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"[talk2tia] tray icon disabled: {ex.Message}");
        }
    }

    private void ShowIcon()
    {
        Application.EnableVisualStyles();
        _context = new ApplicationContext();
        using var icon = LoadIcon();
        var menu = new ContextMenuStrip();
        _ui = new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(_ui);

        var title = new ToolStripMenuItem($"Talk2Tia {UpdateService.Current.ToString(3)} — InsideApp") { Enabled = false, Font = new Font(menu.Font, FontStyle.Bold) };
        _sessionsItem = new ToolStripMenuItem(Text.Sessions(1)) { Enabled = false };
        _updateItem = new ToolStripMenuItem(Text.CheckUpdates, null, (_, _) => OnUpdateClicked());
        menu.Items.AddRange(new ToolStripItem[]
        {
            title,
            _sessionsItem,
            new ToolStripSeparator(),
            _updateItem,
            new ToolStripSeparator(),
            Link(Text.Documentation, Links.Docs),
            Link(Text.Website, Links.Site),
            Link("GitHub", Links.GitHub),
            Link("InsideApp", Links.InsideApp),
            Link(Text.Donate, Links.Donate),
            new ToolStripSeparator(),
            new ToolStripMenuItem(Text.WorkFolder, null, (_, _) => Open(Workspace.Root, createFolder: true)),
            new ToolStripMenuItem(Text.About, null, (_, _) => ShowAbout()),
        });

        _icon = new NotifyIcon { Icon = icon, ContextMenuStrip = menu, Visible = true };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowAbout();
            }
        };
        _icon.BalloonTipClicked += (_, _) => OnUpdateClicked();

        using var timer = new System.Windows.Forms.Timer { Interval = 5000 };
        timer.Tick += (_, _) => Refresh();
        timer.Start();
        _updates.Changed += OnUpdatesChanged;
        Refresh();

        Application.Run(_context);

        _updates.Changed -= OnUpdatesChanged;
        _icon.Visible = false;
        _icon.Dispose();
        menu.Dispose();
    }

    private static Icon LoadIcon()
    {
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream("Talk2Tia.Core.talk2tia.ico");
        return stream is null ? (Icon)SystemIcons.Application.Clone() : new Icon(stream, SystemInformation.SmallIconSize);
    }

    private static ToolStripMenuItem Link(string text, string url) => new(text, null, (_, _) => Open(url));

    private void OnUpdatesChanged() => _ui?.Post(_ => Refresh(notify: true), null);

    private void Refresh(bool notify = false)
    {
        if (_icon is null || _updateItem is null || _sessionsItem is null)
        {
            return;
        }

        var sessions = CountSessions();
        _sessionsItem.Text = Text.Sessions(sessions);
        var update = _updates.Available;
        string tooltip;
        if (!_updates.Enabled)
        {
            _updateItem.Visible = false;
            tooltip = $"Talk2Tia {UpdateService.Current.ToString(3)}";
        }
        else if (update?.LocalMsi is not null)
        {
            _updateItem.Text = _updates.InstallScheduled ? Text.UpdateScheduled(update.Version) : Text.InstallUpdate(update.Version);
            _updateItem.Enabled = !_updates.InstallScheduled;
            tooltip = $"Talk2Tia {UpdateService.Current.ToString(3)} — {Text.UpdateAvailableShort(update.Version)}";
            if (notify && !_updates.InstallScheduled)
            {
                _icon.ShowBalloonTip(10000, "Talk2Tia", Text.UpdateBalloon(update.Version), ToolTipIcon.Info);
            }
        }
        else
        {
            _updateItem.Text = Text.CheckUpdates;
            _updateItem.Enabled = true;
            tooltip = $"Talk2Tia {UpdateService.Current.ToString(3)} — {Text.Sessions(sessions)}";
        }

        _icon.Text = tooltip.Length > 63 ? tooltip.Substring(0, 63) : tooltip; // NotifyIcon limit
    }

    private void OnUpdateClicked()
    {
        if (_updates.Available?.LocalMsi is not null && !_updates.InstallScheduled)
        {
            var answer = MessageBox.Show(Text.ConfirmInstall(_updates.Available.Version), "Talk2Tia", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
            if (answer == DialogResult.OK)
            {
                try
                {
                    _updates.ScheduleInstall();
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    MessageBox.Show(ex.Message, "Talk2Tia", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            return;
        }

        if (_updates.Enabled)
        {
            _updates.CheckInBackground(force: true);
            _icon?.ShowBalloonTip(4000, "Talk2Tia", Text.Checking, ToolTipIcon.None);
        }
    }

    private static void ShowAbout() =>
        MessageBox.Show(
            $"Talk2Tia {UpdateService.Current.ToString(3)}\n{Text.Tagline}\n\n© 2026 InsideApp S.r.l. — MIT License\n{Links.Site}\n\n{Text.Sessions(CountSessions())}",
            "Talk2Tia", MessageBoxButtons.OK, MessageBoxIcon.Information);

    private static int CountSessions()
    {
        try
        {
            return Math.Max(1, Process.GetProcessesByName("talk2tia").Length);
        }
        catch (InvalidOperationException)
        {
            return 1;
        }
    }

    private static void Open(string target, bool createFolder = false)
    {
        try
        {
            if (createFolder)
            {
                System.IO.Directory.CreateDirectory(target);
            }

            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.IO.IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"[talk2tia] cannot open {target}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _stopping.Set();
        _context?.ExitThread();
        _thread.Join(TimeSpan.FromSeconds(3));
        _stopping.Dispose();
    }

    internal static class Links
    {
        public const string Site = "https://talk2tia.insideapp.it";
        public const string Docs = "https://talk2tia.insideapp.it/docs/";
        public const string GitHub = "https://github.com/insideapp-srl/talk2tia";
        public const string InsideApp = "https://www.insideapp.it";
        public const string Donate = "https://ko-fi.com/insideappsrl";
    }

    /// <summary>Menu texts in Italian or English, following the Windows display language.</summary>
    internal static class Text
    {
        private static readonly bool It = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "it";

        public static string Tagline => It ? "Server MCP per Siemens TIA Portal V21" : "MCP server for Siemens TIA Portal V21";
        public static string Documentation => It ? "Documentazione" : "Documentation";
        public static string Website => It ? "Sito web" : "Website";
        public static string Donate => It ? "❤ Dona su Ko-fi" : "❤ Donate on Ko-fi";
        public static string WorkFolder => It ? "Apri la cartella di lavoro" : "Open the work folder";
        public static string About => It ? "Informazioni" : "About";
        public static string CheckUpdates => It ? "Cerca aggiornamenti" : "Check for updates";
        public static string Checking => It ? "Ricerca aggiornamenti in corso…" : "Checking for updates…";
        public static string Sessions(int n) => It ? (n == 1 ? "1 sessione attiva" : $"{n} sessioni attive") : (n == 1 ? "1 active session" : $"{n} active sessions");
        public static string InstallUpdate(Version v) => It ? $"Installa l'aggiornamento {v.ToString(3)}" : $"Install update {v.ToString(3)}";
        public static string UpdateScheduled(Version v) => It ? $"Aggiornamento {v.ToString(3)} alla chiusura" : $"Update {v.ToString(3)} on exit";
        public static string UpdateAvailableShort(Version v) => It ? $"disponibile {v.ToString(3)}" : $"{v.ToString(3)} available";
        public static string UpdateBalloon(Version v) => It
            ? $"È disponibile Talk2Tia {v.ToString(3)}, firmato e verificato. Fai clic per installarlo."
            : $"Talk2Tia {v.ToString(3)} is available, signed and verified. Click to install it.";
        public static string ConfirmInstall(Version v) => It
            ? $"Talk2Tia {v.ToString(3)} verrà installato quando tutte le sessioni Talk2Tia saranno chiuse.\n\nChiudi e riapri il client MCP (Claude) per completare l'aggiornamento."
            : $"Talk2Tia {v.ToString(3)} will be installed when every Talk2Tia session has ended.\n\nClose and reopen the MCP client (Claude) to complete the update.";
    }
}
