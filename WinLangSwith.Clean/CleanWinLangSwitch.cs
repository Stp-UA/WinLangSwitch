using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using Microsoft.Win32;

class CleanWinLangSwitch
{
    static bool SilentMode = false;
    static bool ErrorOccurred = false;

    static void Main(string[] args)
    {
        ParseArguments(args);

        if (!SilentMode)
        {
            Console.WriteLine("=== WinLangSwitch Cleanup Tool ===");
            Console.WriteLine("Administrator privileges are required.\n");
        }

        if (!IsAdministrator())
        {
            LogError("Administrator privileges are required to run this tool.");
            Exit(1);
        }

        KillProcesses();
        DeleteFolders();
        DeleteRegistry();

        if (!SilentMode)
        {
            Console.WriteLine("\nCleanup complete.");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        Exit(ErrorOccurred ? 1 : 0);
    }

    // Parse command-line arguments
    static void ParseArguments(string[] args)
    {
        foreach (var arg in args)
        {
            if (arg.Equals("--silent", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-s", StringComparison.OrdinalIgnoreCase))
            {
                SilentMode = true;
            }
        }
    }

    // Check administrator privileges
    static bool IsAdministrator()
    {
        var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    // Unified logging helpers
    static void Log(string msg)
    {
        if (!SilentMode)
            Console.WriteLine(msg);
    }

    static void LogOk(string msg)
    {
        if (!SilentMode)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(msg);
            Console.ResetColor();
        }
    }

    static void LogError(string msg)
    {
        ErrorOccurred = true;
        if (!SilentMode)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(msg);
            Console.ResetColor();
        }
    }

    static void Exit(int code)
    {
        Environment.Exit(code);
    }

    // Kill WinLangSwitch processes
    static void KillProcesses()
    {
        Log("=== Processes ===");

        string[] procs = { "WinLangSwitch.Daemon", "WinLangSwitch.Setup" };

        foreach (var p in procs)
        {
            try
            {
                var list = Process.GetProcessesByName(p);
                if (list.Length == 0)
                {
                    Log($"No running processes found: {p}");
                    continue;
                }

                foreach (var proc in list)
                {
                    Log($"Found process: {p} (PID {proc.Id}) — terminating...");
                    try
                    {
                        proc.Kill();
                        proc.WaitForExit(3000);
                        LogOk($"[OK] Process {p} (PID {proc.Id}) terminated.");
                    }
                    catch (Exception ex)
                    {
                        LogError($"[ERROR] Could not terminate {p} (PID {proc.Id}): {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"[ERROR] Could not enumerate processes {p}: {ex.Message}");
            }
        }

        Log("");
    }

    // Delete installation folders and Start Menu folders
    static void DeleteFolders()
    {
        Log("=== Folders & Shortcuts ===");

        string appDataRoaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string appDataLocal   = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // Installation folders + Start Menu folders
        string[] folders =
        {
            @"C:\Program Files\WinLangSwitch",
            @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\WinLangSwitch",
            Path.Combine(appDataLocal, "WinLangSwitch"),
            Path.Combine(appDataRoaming, @"Microsoft\Windows\Start Menu\Programs\WinLangSwitch")
        };

        foreach (var folder in folders)
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    Log($"Deleting folder: {folder}");
                    Directory.Delete(folder, true);
                    LogOk($"[OK] Deleted folder: {folder}");
                }
                else
                {
                    Log($"Folder not found: {folder}");
                }
            }
            catch (Exception ex)
            {
                LogError($"[ERROR] Could not delete folder {folder}: {ex.Message}");
            }
        }

        // Startup shortcuts (no folder)
        string userStartup    = Path.Combine(appDataRoaming, @"Microsoft\Windows\Start Menu\Programs\Startup");
        string machineStartup = @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Startup";

        string[] shortcuts =
        {
            Path.Combine(userStartup, "WinLangSwitch Daemon.lnk"),
            Path.Combine(machineStartup, "WinLangSwitch Daemon.lnk")
        };

        foreach (var shortcut in shortcuts)
        {
            try
            {
                if (File.Exists(shortcut))
                {
                    Log($"Deleting shortcut: {shortcut}");
                    File.Delete(shortcut);
                    LogOk($"[OK] Deleted shortcut: {shortcut}");
                }
                else
                {
                    Log($"Shortcut not found: {shortcut}");
                }
            }
            catch (Exception ex)
            {
                LogError($"[ERROR] Could not delete shortcut {shortcut}: {ex.Message}");
            }
        }

        Log("");
    }

    // Delete uninstall keys and settings keys
    static void DeleteRegistry()
    {
        Log("=== Registry ===");

        DeleteUninstallKeys(Registry.LocalMachine, "HKLM");
        DeleteUninstallKeys(Registry.CurrentUser, "HKCU");

        DeleteKey(Registry.LocalMachine, @"Software\WinLangSwitch", "HKLM");
        DeleteKey(Registry.CurrentUser, @"Software\WinLangSwitch", "HKCU");

        Log("");
    }

    // Delete MSI uninstall keys by DisplayName
    static void DeleteUninstallKeys(RegistryKey root, string rootName)
    {
        const string uninstallPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

        using (var uninstall = root.OpenSubKey(uninstallPath))
        {
            if (uninstall == null)
            {
                Log($"{rootName}\\{uninstallPath} not found.");
                return;
            }

            foreach (var subKeyName in uninstall.GetSubKeyNames())
            {
                using (var subKey = uninstall.OpenSubKey(subKeyName))
                {
                    if (subKey == null) continue;

                    string displayName = subKey.GetValue("DisplayName") as string;

                    if (displayName != null && displayName.Contains("WinLangSwitch"))
                    {
                        string fullPath = $"{uninstallPath}\\{subKeyName}";
                        try
                        {
                            Log($"Deleting {rootName}\\{fullPath}");
                            root.DeleteSubKeyTree(fullPath, false);
                            LogOk($"[OK] Deleted {rootName}\\{fullPath}");
                        }
                        catch (Exception ex)
                        {
                            LogError($"[ERROR] Could not delete {rootName}\\{fullPath}: {ex.Message}");
                        }
                    }
                }
            }
        }
    }

    // Delete custom registry keys
    static void DeleteKey(RegistryKey root, string key, string rootName)
    {
        try
        {
            using (var k = root.OpenSubKey(key))
            {
                if (k == null)
                {
                    Log($"{rootName}\\{key} not found.");
                }
                else
                {
                    Log($"Deleting {rootName}\\{key}");
                    root.DeleteSubKeyTree(key, false);
                    LogOk($"[OK] Deleted {rootName}\\{key}");
                }
            }
        }
        catch (Exception ex)
        {
            LogError($"[ERROR] Could not delete {rootName}\\{key}: {ex.Message}");
        }
    }
}
