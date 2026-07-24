using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Microsoft.Win32;

class Program
{
    const int WM_INPUTLANGCHANGEREQUEST = 0x50;
    static readonly IntPtr LAYOUT_ENGLISH = (IntPtr)0x0409;
    static readonly IntPtr LAYOUT_RUSSIAN = (IntPtr)0x0419;

    static bool loggingEnabled = false;
    static string logPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LangDaemon.log");

    [StructLayout(LayoutKind.Sequential)]
    struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);

    [DllImport("user32.dll")]
    static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll")]
    static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO pgui);

    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    static void Log(string msg)
    {
        if (!loggingEnabled) return;
        try
        {
            File.AppendAllText(logPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {msg}{Environment.NewLine}");
        }
        catch { }
    }

    static bool AlreadyRunning()
    {
        try
        {
            using (var client = new NamedPipeClientStream(".", "LangPipe", PipeDirection.In))
            {
                client.Connect(100);
                return client.IsConnected;
            }
        }
        catch
        {
            return false;
        }
    }

    static string? GetInstallDirFromRegistry()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\WinLangSwitch");
            return key?.GetValue("InstallLocation") as string;
        }
        catch
        {
            return null;
        }
    }

    static string ReadHotkeyFromConfig(string installDir)
    {
        try
        {
            var configPath = Path.Combine(installDir, "config", "settings.ini");
            if (!File.Exists(configPath))
                return "RAlt"; // дефолт

            foreach (var line in File.ReadAllLines(configPath))
            {
                if (line.StartsWith("hotkey=", StringComparison.OrdinalIgnoreCase))
                {
                    var value = line.Substring("hotkey=".Length).Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
            }
        }
        catch { }

        return "RAlt";
    }

    static Process? StartAhkForHotkey(string installDir, string hotkey)
    {
        try
        {
            string scriptName = hotkey.Equals("RCtrl", StringComparison.OrdinalIgnoreCase)
                ? "WinLangSwitch.RCtrl.ahk"
                : "WinLangSwitch.RAlt.ahk";

            string scriptPath = Path.Combine(installDir, "ahk", scriptName);

            if (!File.Exists(scriptPath))
            {
                Log($"AHK script not found: {scriptPath}");
                return null;
            }

            Log($"Starting AHK: {scriptPath}");

            string ahkExe = @"C:\Program Files\AutoHotkey\v2\AutoHotkey.exe";

            var psi = new ProcessStartInfo
            {
                FileName = ahkExe,
                Arguments = $"\"{scriptPath}\"",
                WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? installDir,
                UseShellExecute = false
            };

            return Process.Start(psi);
        }
        catch (Exception ex)
        {
            Log($"StartAhkForHotkey exception: {ex.Message}");
            return null;
        }
    }

    static void SetupConfigWatcher(string installDir)
    {
        try
        {
            var configDir = Path.Combine(installDir, "config");
            var configPath = Path.Combine(configDir, "settings.ini");

            if (!Directory.Exists(configDir))
                return;

            var watcher = new FileSystemWatcher(configDir)
            {
                Filter = "settings.ini",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
            };

            watcher.Changed += (s, e) =>
            {
                Log("Config changed, exiting for restart");
                Environment.Exit(0);
            };

            watcher.EnableRaisingEvents = true;

            Log("Config watcher enabled");
        }
        catch (Exception ex)
        {
            Log($"SetupConfigWatcher exception: {ex.Message}");
        }
    }

    static void ToggleLanguageCore(string tag)
    {
        try
        {
            IntPtr activeWindow = GetForegroundWindow();
            if (activeWindow == IntPtr.Zero)
            {
                Log($"{tag}: no active window");
                return;
            }

            uint threadId = GetWindowThreadProcessId(activeWindow, IntPtr.Zero);

            GUITHREADINFO gti = new GUITHREADINFO();
            gti.cbSize = Marshal.SizeOf(gti);

            if (GetGUIThreadInfo(threadId, ref gti) && gti.hwndFocus != IntPtr.Zero)
            {
                Log($"{tag}: hwndFocus={gti.hwndFocus}");
                threadId = GetWindowThreadProcessId(gti.hwndFocus, IntPtr.Zero);
            }
            else
            {
                Log($"{tag}: no hwndFocus, using activeWindow");
            }

            IntPtr currentLayout = GetKeyboardLayout(threadId);
            long langID = currentLayout.ToInt64() & 0xFFFF;

            Log($"{tag}: threadId={threadId}, langID=0x{langID:X4}");

            if (langID == 0x0409)
            {
                Log($"{tag}: switching to RU");
                PostMessage(activeWindow, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, LAYOUT_RUSSIAN);
            }
            else
            {
                Log($"{tag}: switching to EN");
                PostMessage(activeWindow, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, LAYOUT_ENGLISH);
            }
        }
        catch (Exception ex)
        {
            Log($"{tag}: exception: {ex.Message}");
        }
    }

    static void ToggleAltLogic()
    {
        ToggleLanguageCore("ALT");
    }

    static void ToggleCtrlLogic()
    {
        ToggleLanguageCore("CTRL");
    }

    static void Main()
    {
        if (AlreadyRunning())
            return;

        // 1. Получаем каталог установки из реестра
        var installDir = GetInstallDirFromRegistry();
        if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir))
        {
            Log("InstallDir not found or does not exist");
            return;
        }

        // 2. Читаем hotkey из конфига
        var hotkey = ReadHotkeyFromConfig(installDir);
        Log($"Hotkey from config: {hotkey}");

        // 3. Запускаем нужный AHK-скрипт
        var ahkProcess = StartAhkForHotkey(installDir, hotkey);

        // 4. Включаем watcher на конфиг — при изменении демон завершится и будет перезапущен системой
        SetupConfigWatcher(installDir);

        // 5. Основной цикл ожидания команд по named pipe
        while (true)
        {
            using (var server = new NamedPipeServerStream("LangPipe", PipeDirection.In))
            {
                server.WaitForConnection();

                using (var reader = new StreamReader(server))
                {
                    string cmd = reader.ReadLine();
                    if (cmd == null) continue;

                    switch (cmd.Trim())
                    {
                        case "toggle_alt":
                            Log("CMD: toggle_alt");
                            ToggleAltLogic();
                            break;

                        case "toggle_ctrl":
                            Log("CMD: toggle_ctrl");
                            ToggleCtrlLogic();
                            break;

                        case "log_on":
                            loggingEnabled = true;
                            Log("CMD: log_on (logging enabled)");
                            break;

                        case "log_off":
                            Log("CMD: log_off (logging disabled)");
                            loggingEnabled = false;
                            break;
                    }
                }
            }
        }
    }
}
