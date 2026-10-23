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
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
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
    const ushort IconResourceType = 3;
    const ushort IconGroupResourceType = 14;
    const ushort IconGroupResourceId = 32512;
    const ushort FirstIconResourceId = 60000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool DeleteFile(string path);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr BeginUpdateResource(string fileName, bool deleteExistingResources);

    [DllImport("kernel32.dll", EntryPoint = "UpdateResourceW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool UpdateResource(IntPtr update, IntPtr type, IntPtr name, ushort language,
        byte[] data, uint dataSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool EndUpdateResource(IntPtr update, bool discard);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint PrivateExtractIcons(string file, int index, int cx, int cy,
        IntPtr[] icons, uint[] ids, uint count, uint flags);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);

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
                    "\n\nCheck that you can write to the install location.",
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
        string gameExe = Game.FindExecutable();
        if (gameExe != null)
        {
            string ico = Path.Combine(dir, "GRW Efficiency Mode.ico");
            WriteGameIcon(gameExe, ico);
            UpdateExecutableIcon(target, ico);
            iconLocation = ico + ",0";
        }

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

    static void UpdateExecutableIcon(string exePath, string icoPath)
    {
        byte[] ico = File.ReadAllBytes(icoPath);
        if (ico.Length < 6 || BitConverter.ToUInt16(ico, 0) != 0 ||
            BitConverter.ToUInt16(ico, 2) != 1)
            throw new InvalidDataException("The extracted game icon is not a valid .ico file.");

        ushort count = BitConverter.ToUInt16(ico, 4);
        if (count == 0 || count > ushort.MaxValue - FirstIconResourceId + 1 ||
            ico.Length < 6 + 16 * count)
            throw new InvalidDataException("The extracted game icon has an invalid image table.");

        IntPtr update = BeginUpdateResource(exePath, false);
        if (update == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the installed app to update its icon.");

        try
        {
            byte[] groupIcon = new byte[6 + 14 * count];
            Array.Copy(ico, 0, groupIcon, 0, 4);
            Array.Copy(BitConverter.GetBytes(count), 0, groupIcon, 4, 2);

            for (int i = 0; i < count; i++)
            {
                int entry = 6 + 16 * i;
                uint imageSize = BitConverter.ToUInt32(ico, entry + 8);
                uint imageOffset = BitConverter.ToUInt32(ico, entry + 12);
                if (imageOffset > ico.Length || imageSize > ico.Length - imageOffset)
                    throw new InvalidDataException("The extracted game icon contains an invalid image.");

                ushort resourceId = (ushort)(FirstIconResourceId + i);
                byte[] image = new byte[imageSize];
                Array.Copy(ico, (int)imageOffset, image, 0, image.Length);
                if (!UpdateResource(update, new IntPtr(IconResourceType), new IntPtr(resourceId),
                    0, image, (uint)image.Length))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not write an icon image to the installed app.");

                int groupEntry = 6 + 14 * i;
                Array.Copy(ico, entry, groupIcon, groupEntry, 8);
                Array.Copy(BitConverter.GetBytes(imageSize), 0, groupIcon, groupEntry + 8, 4);
                Array.Copy(BitConverter.GetBytes(resourceId), 0, groupIcon, groupEntry + 12, 2);
            }

            if (!UpdateResource(update, new IntPtr(IconGroupResourceType),
                new IntPtr(IconGroupResourceId), 0, groupIcon, (uint)groupIcon.Length))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not set the installed app icon.");

            IntPtr completedUpdate = update;
            update = IntPtr.Zero;
            if (!EndUpdateResource(completedUpdate, false))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not save the installed app icon.");
        }
        finally
        {
            if (update != IntPtr.Zero) EndUpdateResource(update, true);
        }
    }

    static void WriteGameIcon(string sourceExe, string outIco)
    {
        Bitmap source;
        IntPtr[] handles = new IntPtr[1];
        uint[] ids = new uint[1];
        if (PrivateExtractIcons(sourceExe, 0, 256, 256, handles, ids, 1, 0) >= 1 &&
            handles[0] != IntPtr.Zero)
        {
            try
            {
                using (Icon tmp = Icon.FromHandle(handles[0])) source = tmp.ToBitmap();
            }
            finally { DestroyIcon(handles[0]); }
        }
        else
        {
            using (Icon tmp = Icon.ExtractAssociatedIcon(sourceExe))
            {
                if (tmp == null) throw new InvalidOperationException("Could not extract the Ghost Recon icon.");
                source = tmp.ToBitmap();
            }
        }

        ColorizeGameIcon(source);

        int[] sizes = { 256, 48, 32, 16 };
        List<byte[]> images = new List<byte[]>();
        using (source)
        {
            foreach (int size in sizes)
            {
                using (Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
                {
                    using (Graphics gfx = Graphics.FromImage(bmp))
                    {
                        gfx.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        gfx.SmoothingMode = SmoothingMode.HighQuality;
                        gfx.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        gfx.Clear(Color.Transparent);
                        gfx.DrawImage(source, new Rectangle(0, 0, size, size),
                            0, 0, source.Width, source.Height, GraphicsUnit.Pixel);
                    }
                    using (MemoryStream ms = new MemoryStream())
                    {
                        bmp.Save(ms, ImageFormat.Png);
                        images.Add(ms.ToArray());
                    }
                }
            }
        }

        using (FileStream fs = File.Create(outIco))
        using (BinaryWriter writer = new BinaryWriter(fs))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)images.Count);
            int offset = 6 + 16 * images.Count;
            for (int i = 0; i < images.Count; i++)
            {
                byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                writer.Write(dim); writer.Write(dim); writer.Write((byte)0); writer.Write((byte)0);
                writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write((uint)images[i].Length); writer.Write((uint)offset);
                offset += images[i].Length;
            }
            foreach (byte[] image in images) writer.Write(image);
        }
    }

    static void ColorizeGameIcon(Bitmap image)
    {
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                Color pixel = image.GetPixel(x, y);
                int luminance = (pixel.R * 299 + pixel.G * 587 + pixel.B * 114) / 1000;
                image.SetPixel(x, y, Color.FromArgb(pixel.A,
                    Math.Min(255, 22 + luminance * 69 / 100),
                    Math.Min(255, 64 + luminance * 69 / 100),
                    Math.Min(255, 31 + luminance * 69 / 100)));
            }
        }
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
            string shortcutPath = Path.Combine(installDir, Program.AppName + ".lnk");
            string icoPath = Path.Combine(installDir, "GRW Efficiency Mode.ico");
            if (!ScheduleUninstallCleanup(exePath, shortcutPath, icoPath, installDir))
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

    static bool ScheduleUninstallCleanup(string exePath, string shortcutPath, string icoPath, string installDir)
    {
        try
        {
            string command = "/c ping 127.0.0.1 -n 2 >nul & del /f /q \"" + exePath +
                "\" >nul 2>&1 & del /f /q \"" + shortcutPath +
                "\" >nul 2>&1 & del /f /q \"" + icoPath +
                "\" >nul 2>&1 & rmdir \"" + installDir + "\" >nul 2>&1";
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", command);
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.WorkingDirectory = Path.GetTempPath();
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
    readonly string installDir;
    readonly CheckBox cbDesktop = new CheckBox();
    readonly CheckBox cbDelete = new CheckBox();
    readonly Button btnUninstall = new Button();

    public string InstallDir { get { return installDir; } }
    public bool DesktopShortcut { get { return cbDesktop.Checked; } }
    public bool DeleteInstaller { get { return cbDelete.Checked; } }

    public SetupForm(string defaultDir)
    {
        installDir = defaultDir;
        Text = Program.AppName + " - setup";
        ClientSize = new Size(460, 190);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        Label lblDir = new Label();
        lblDir.Text = "Install location:";
        lblDir.SetBounds(15, 15, 430, 18);

        txtDir.Text = defaultDir;
        txtDir.ReadOnly = true;
        txtDir.SetBounds(15, 36, 430, 24);

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

        Controls.AddRange(new Control[] { lblDir, txtDir, lblOpt, cbDesktop, cbDelete,
            btnUninstall, btnOk, btnCancel });
        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }
}
