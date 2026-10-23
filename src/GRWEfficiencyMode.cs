// GRW Efficiency Mode
//
// One executable, two modes:
//   - Setup mode  : when the file name contains "Setup" (the downloaded file).
//                   Installs the app, creates the shortcuts, then exits.
//   - App mode    : the installed "GRW Efficiency Mode.exe".
//                   Launches Ghost Recon Wildlands (Steam or Ubisoft Connect,
//                   auto-detected), enables Windows Efficiency Mode (EcoQoS)
//                   on GRW.exe during startup, then gives control back to
//                   Windows. Process priority is never changed.
//
// Log: %LOCALAPPDATA%\GRW-Efficiency-Mode\GRW-Efficiency-Mode.log
//
// Written for the C# 5 compiler shipped with Windows (.NET Framework 4.x).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("GRW Efficiency Mode")]
[assembly: AssemblyDescription("Ghost Recon Wildlands - automatic Efficiency Mode")]
[assembly: AssemblyProduct("GRW Efficiency Mode")]
[assembly: AssemblyCopyright("MIT License")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

static class Program
{
    public const string AppName = "GRW Efficiency Mode";
    public const string SteamAppId = "460930";
    public const string UbisoftId = "1771";

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        string[] args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && string.Equals(args[1], "/uninstall", StringComparison.OrdinalIgnoreCase))
        {
            Setup.UninstallInstalledApp();
            return;
        }

        string self = Path.GetFileName(Application.ExecutablePath);
        if (self.IndexOf("setup", StringComparison.OrdinalIgnoreCase) >= 0)
            Setup.Run();
        else
            Launcher.Run();
    }
}

// =====================================================================
//  Game detection (shared by setup and launcher)
// =====================================================================
static class Game
{
    // Path to GRW.exe if found, otherwise null.
    public static string FindExecutable()
    {
        foreach (string lib in SteamLibraries())
        {
            string acf = Path.Combine(lib, @"steamapps\appmanifest_" + Program.SteamAppId + ".acf");
            if (!File.Exists(acf)) continue;
            Match m = Regex.Match(File.ReadAllText(acf), "\"installdir\"\\s+\"([^\"]+)\"");
            if (!m.Success) continue;
            string exe = Path.Combine(lib, @"steamapps\common\" + m.Groups[1].Value + @"\GRW.exe");
            if (File.Exists(exe)) return exe;
        }
        string dir = UbisoftInstallDir();
        if (dir != null)
        {
            string exe = Path.Combine(dir, "GRW.exe");
            if (File.Exists(exe)) return exe;
        }
        return null;
    }

    public static bool IsSteamVersionInstalled()
    {
        foreach (string lib in SteamLibraries())
        {
            if (File.Exists(Path.Combine(lib, @"steamapps\appmanifest_" + Program.SteamAppId + ".acf")))
                return true;
        }
        try
        {
            using (RegistryKey app = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\Apps\" + Program.SteamAppId))
            {
                object installed = app == null ? null : app.GetValue("Installed");
                return installed is int && (int)installed == 1;
            }
        }
        catch { return false; }
    }

    public static bool IsUbisoftVersionInstalled()
    {
        return UbisoftInstallDir() != null;
    }

    static List<string> SteamLibraries()
    {
        List<string> libraries = new List<string>();
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            {
                string steamPath = key == null ? null : key.GetValue("SteamPath") as string;
                if (string.IsNullOrEmpty(steamPath)) return libraries;
                libraries.Add(steamPath.Replace('/', '\\'));
                string vdf = Path.Combine(steamPath, @"steamapps\libraryfolders.vdf");
                if (File.Exists(vdf))
                {
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    {
                        string lib = m.Groups[1].Value.Replace(@"\\", @"\");
                        if (!libraries.Contains(lib)) libraries.Add(lib);
                    }
                }
            }
        }
        catch { }
        return libraries;
    }

    static string UbisoftInstallDir()
    {
        string[] keys = {
            @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs\" + Program.UbisoftId,
            @"SOFTWARE\Ubisoft\Launcher\Installs\" + Program.UbisoftId
        };
        foreach (string k in keys)
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(k))
                {
                    string dir = key == null ? null : key.GetValue("InstallDir") as string;
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
                }
            }
            catch { }
        }
        return null;
    }
}

// =====================================================================
//  App mode: launch the game and apply EcoQoS
// =====================================================================
static class Launcher
{
    const int RestoreDelaySeconds = 120;       // EcoQoS removed 120 s after the last new GRW.exe instance
    const int MaxWatchSeconds = 600;           // hard limit after first detection
    const int MaxWaitForGameSeconds = 300;     // give up if GRW.exe never shows up
    const int GraceAfterCloseSeconds = 15;     // GRW.exe gone for 15 s = game closed
    const int PollMilliseconds = 500;
    const string GameProcessName = "GRW";

    static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GRW-Efficiency-Mode", "GRW-Efficiency-Mode.log");

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetProcessInformation(IntPtr process, int infoClass,
        ref PROCESS_POWER_THROTTLING_STATE info, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr handle);

    const uint PROCESS_SET_INFORMATION = 0x0200;
    const int ProcessPowerThrottling = 4;
    const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 1;

    public static void Run()
    {
        bool owned;
        using (Mutex mutex = new Mutex(true, @"Local\GRW-Efficiency-Mode", out owned))
        {
            if (!LaunchGame()) return;

            if (!owned)
            {
                Log("Another watcher is already running; game launched, no second watcher.");
                return;
            }

            try { Watch(); }
            catch (Exception ex) { Log("Unexpected error: " + ex.Message); }
            finally
            {
                Log("Watcher stopped.");
                mutex.ReleaseMutex();
            }
        }
    }

    static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
            File.AppendAllText(LogPath,
                "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + message + Environment.NewLine);
        }
        catch { }
    }

    static bool LaunchGame()
    {
        try
        {
            if (Game.IsSteamVersionInstalled())
            {
                Log("Steam version detected; launching steam://rungameid/" + Program.SteamAppId + ".");
                Process.Start("steam://rungameid/" + Program.SteamAppId);
                return true;
            }
            if (Game.IsUbisoftVersionInstalled())
            {
                Log("Ubisoft Connect version detected; launching uplay://launch/" + Program.UbisoftId + "/0.");
                Process.Start("uplay://launch/" + Program.UbisoftId + "/0");
                return true;
            }
        }
        catch (Exception ex)
        {
            Log("Game launch failed: " + ex.Message);
            MessageBox.Show("The game could not be launched:\n" + ex.Message,
                Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        Log("No Ghost Recon Wildlands installation found.");
        MessageBox.Show("Ghost Recon Wildlands was not found (neither Steam nor Ubisoft Connect).",
            Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        return false;
    }

    static bool SetEcoQoS(Process process, bool enable)
    {
        IntPtr handle = OpenProcess(PROCESS_SET_INFORMATION, false, (uint)process.Id);
        if (handle == IntPtr.Zero)
        {
            Log("Cannot open PID " + process.Id + " (Win32 error " + Marshal.GetLastWin32Error() + ").");
            return false;
        }
        try
        {
            PROCESS_POWER_THROTTLING_STATE state = new PROCESS_POWER_THROTTLING_STATE();
            state.Version = 1;
            // enable: force EcoQoS on / disable: ControlMask = 0 gives control back to Windows
            state.ControlMask = enable ? PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0;
            state.StateMask = enable ? PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0;
            bool ok = SetProcessInformation(handle, ProcessPowerThrottling, ref state,
                (uint)Marshal.SizeOf(typeof(PROCESS_POWER_THROTTLING_STATE)));
            if (!ok)
                Log("EcoQoS change failed for PID " + process.Id + " (Win32 error " + Marshal.GetLastWin32Error() + ").");
            return ok;
        }
        finally { CloseHandle(handle); }
    }

    static void DisableAll(Process[] processes)
    {
        foreach (Process p in processes)
        {
            try { if (SetEcoQoS(p, false)) Log("EcoQoS disabled for PID " + p.Id + "."); }
            catch { }
        }
    }

    static void Watch()
    {
        Log("Watcher started. Game priority left unchanged.");
        DateTime start = DateTime.Now;
        HashSet<int> seenPids = new HashSet<int>();
        DateTime? firstDetection = null;
        DateTime? lastNewProcess = null;
        DateTime? lastSeen = null;

        while (true)
        {
            DateTime now = DateTime.Now;
            Process[] processes = Process.GetProcessesByName(GameProcessName);
            try
            {
                if (firstDetection == null && processes.Length == 0 &&
                    (now - start).TotalSeconds >= MaxWaitForGameSeconds)
                {
                    Log("GRW.exe not detected after " + MaxWaitForGameSeconds + " seconds; giving up.");
                    return;
                }

                if (processes.Length > 0) lastSeen = now;

                if (lastSeen != null && processes.Length == 0 &&
                    (now - lastSeen.Value).TotalSeconds >= GraceAfterCloseSeconds)
                {
                    Log("GRW.exe gone for " + GraceAfterCloseSeconds + " seconds; game considered closed.");
                    return;
                }

                foreach (Process p in processes)
                {
                    if (seenPids.Add(p.Id))
                    {
                        if (firstDetection == null) firstDetection = now;
                        lastNewProcess = now;
                        Log("New GRW.exe instance (PID " + p.Id + "). Enabling EcoQoS.");
                        if (SetEcoQoS(p, true)) Log("EcoQoS enabled for PID " + p.Id + ".");
                    }
                }

                if (firstDetection != null)
                {
                    if ((now - firstDetection.Value).TotalSeconds >= MaxWatchSeconds)
                    {
                        Log("Maximum watch time reached; disabling EcoQoS.");
                        DisableAll(processes);
                        return;
                    }
                    if (processes.Length > 0 && (now - lastNewProcess.Value).TotalSeconds >= RestoreDelaySeconds)
                    {
                        Log(RestoreDelaySeconds + " seconds since the last new instance; disabling EcoQoS.");
                        DisableAll(processes);
                        return;
                    }
                }
            }
            finally
            {
                foreach (Process p in processes) p.Dispose();
            }
            Thread.Sleep(PollMilliseconds);
        }
    }
}

// =====================================================================
//  Setup mode: install the app and create the shortcuts
// =====================================================================
static class Setup
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool DeleteFile(string path);

    public static void Run()
    {
        string defaultDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Programs\" + Program.AppName);

        using (SetupForm form = new SetupForm(defaultDir))
        {
            DialogResult result = form.ShowDialog();
            if (result == DialogResult.Cancel) return;
            if (result == DialogResult.Yes)
            {
                Uninstall(defaultDir);
                return;
            }
            try
            {
                Install(form.InstallDir, form.DesktopShortcut);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Installation failed:\n\n" + ex.Message +
                    "\n\nPick a folder you own (the default one never needs administrator rights).",
                    Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string msg = "Installed to:\n" + form.InstallDir + "\n";
            if (form.DesktopShortcut) msg += "\n- Shortcut added to the Desktop.";
            msg += "\n\nTip: right-click the shortcut > Pin to taskbar / Pin to Start.";
            MessageBox.Show(msg, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);

            if (form.DeleteInstaller && !ScheduleSelfDelete())
                MessageBox.Show("The setup file could not be scheduled for deletion. You can delete it manually.",
                    Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    static void Install(string dir, bool desktopShortcut)
    {
        Directory.CreateDirectory(dir);
        string target = Path.Combine(dir, Program.AppName + ".exe");

        // Stop a running copy so the .exe can be replaced
        foreach (Process p in Process.GetProcessesByName(Program.AppName))
        {
            try { p.Kill(); p.WaitForExit(3000); } catch { }
            p.Dispose();
        }

        File.Copy(Application.ExecutablePath, target, true);
        // Drop the "downloaded from the Internet" mark copied along with the file
        DeleteFile(target + ":Zone.Identifier");

        string iconLocation = target + ",0";
        CreateShortcut(Path.Combine(dir, Program.AppName + ".lnk"), target, dir, iconLocation, null);
        if (desktopShortcut)
        {
            CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Program.AppName + ".lnk"), target, dir, iconLocation, null);
        }

        string startMenuDir = StartMenuDirectory();
        Directory.CreateDirectory(startMenuDir);
        CreateShortcut(Path.Combine(startMenuDir, "Uninstall " + Program.AppName + ".lnk"),
            target, dir, target + ",0", "/uninstall");
    }

    static string StartMenuDirectory()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs", Program.AppName);
    }

    static void CreateShortcut(string path, string target, string workDir, string icon, string arguments)
    {
        Type shellType = Type.GetTypeFromProgID("WScript.Shell");
        object shell = Activator.CreateInstance(shellType);
        try
        {
            object lnk = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
            Type t = lnk.GetType();
            t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { target });
            t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { workDir });
            t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { icon });
            if (arguments != null)
                t.InvokeMember("Arguments", BindingFlags.SetProperty, null, lnk, new object[] { arguments });
            t.InvokeMember("Description", BindingFlags.SetProperty, null, lnk,
                new object[] { "Ghost Recon Wildlands - automatic Efficiency Mode" });
            t.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
            Marshal.FinalReleaseComObject(lnk);
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }

    static bool ScheduleSelfDelete()
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe",
                "/c ping 127.0.0.1 -n 2 >nul & del /f /q \"" + Application.ExecutablePath + "\"");
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            Process.Start(psi);
            return true;
        }
        catch { return false; }
    }

    public static void UninstallInstalledApp()
    {
        Uninstall(Path.GetDirectoryName(Application.ExecutablePath));
    }

    static void Uninstall(string installDir)
    {
        if (MessageBox.Show("Remove " + Program.AppName + " and its shortcuts?",
            Program.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        try
        {
            DeleteIfExists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Program.AppName + ".lnk"));
            string startMenuDir = StartMenuDirectory();
            if (Directory.Exists(startMenuDir)) Directory.Delete(startMenuDir, true);

            string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GRW-Efficiency-Mode");
            if (Directory.Exists(logDir)) Directory.Delete(logDir, true);

            string exePath = Path.Combine(installDir, Program.AppName + ".exe");
            string icoPath = Path.Combine(installDir, "GRW Efficiency Mode.ico");
            if (!ScheduleUninstallCleanup(exePath, icoPath, installDir))
                throw new IOException("Windows could not schedule the installed files for deletion.");

            MessageBox.Show("GRW Efficiency Mode has been uninstalled.",
                Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Uninstallation failed:\n\n" + ex.Message,
                Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    static bool ScheduleUninstallCleanup(string exePath, string icoPath, string installDir)
    {
        try
        {
            string command = "/c ping 127.0.0.1 -n 2 >nul & del /f /q \"" + exePath +
                "\" >nul 2>&1 & del /f /q \"" + icoPath +
                "\" >nul 2>&1 & rmdir \"" + installDir + "\" >nul 2>&1";
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", command);
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            Process.Start(psi);
            return true;
        }
        catch { return false; }
    }
}

// =====================================================================
//  Setup window
// =====================================================================
class SetupForm : Form
{
    readonly TextBox txtDir = new TextBox();
    readonly CheckBox cbDesktop = new CheckBox();
    readonly CheckBox cbDelete = new CheckBox();
    readonly Button btnUninstall = new Button();

    public string InstallDir { get { return txtDir.Text.Trim().TrimEnd('\\'); } }
    public bool DesktopShortcut { get { return cbDesktop.Checked; } }
    public bool DeleteInstaller { get { return cbDelete.Checked; } }

    public SetupForm(string defaultDir)
    {
        Text = Program.AppName + " - setup";
        ClientSize = new Size(460, 190);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        Label lblDir = new Label();
        lblDir.Text = "Install folder:";
        lblDir.SetBounds(15, 15, 430, 18);

        txtDir.Text = defaultDir;
        txtDir.SetBounds(15, 36, 340, 24);

        Button btnBrowse = new Button();
        btnBrowse.Text = "Browse...";
        btnBrowse.SetBounds(362, 34, 85, 26);
        btnBrowse.Click += delegate
        {
            using (FolderBrowserDialog fb = new FolderBrowserDialog())
            {
                fb.Description = "Where should " + Program.AppName + " be installed?";
                fb.ShowNewFolderButton = true;
                if (fb.ShowDialog(this) == DialogResult.OK)
                    txtDir.Text = Path.Combine(fb.SelectedPath, Program.AppName);
            }
        };

        Label lblOpt = new Label();
        lblOpt.Text = "A shortcut is always created in this folder. Also:";
        lblOpt.SetBounds(15, 76, 430, 18);

        cbDesktop.Text = "Add a shortcut to the Desktop";
        cbDesktop.SetBounds(15, 98, 430, 22);
        cbDesktop.Checked = true;

        cbDelete.Text = "Delete this setup file when done";
        cbDelete.SetBounds(15, 122, 430, 22);
        cbDelete.Checked = true;

        Button btnOk = new Button();
        btnOk.Text = "Install";
        btnOk.SetBounds(270, 152, 85, 28);
        btnOk.DialogResult = DialogResult.OK;

        btnUninstall.Text = "Uninstall";
        btnUninstall.SetBounds(15, 152, 85, 28);
        btnUninstall.Visible = File.Exists(Path.Combine(defaultDir, Program.AppName + ".exe"));
        btnUninstall.DialogResult = DialogResult.Yes;

        Button btnCancel = new Button();
        btnCancel.Text = "Cancel";
        btnCancel.SetBounds(362, 152, 85, 28);
        btnCancel.DialogResult = DialogResult.Cancel;

        Controls.AddRange(new Control[] { lblDir, txtDir, btnBrowse, lblOpt, cbDesktop, cbDelete,
            btnUninstall, btnOk, btnCancel });
        AcceptButton = btnOk;
        CancelButton = btnCancel;

        FormClosing += delegate(object sender, FormClosingEventArgs e)
        {
            if (DialogResult == DialogResult.OK && InstallDir.Length == 0)
            {
                MessageBox.Show(this, "Please choose an install folder.", Program.AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                e.Cancel = true;
            }
        };
    }
}
