using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Serilog;
using SPTInstaller.Models;

namespace SPTInstaller.Helpers;

/// <summary>
/// Creates .lnk shortcuts through the Windows shell's own ShellLink object, without PowerShell or WScript
/// </summary>
public static class ShortcutHelper
{
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    // Members must stay in the order the shell's vtable declares them, even the ones never called
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int size, IntPtr findData, int flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int size);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int size);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int size);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int size, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, int reserved);
        void Resolve(IntPtr window, int flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    public static string DesktopPath
    {
        get
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

            return string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop)
                ? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop")
                : desktop;
        }
    }

    /// <summary>
    ///     Create launcher and server shortcuts in a folder
    /// </summary>
    /// <param name="folder">Folder to create the shortcuts in</param>
    /// <param name="sptPath">Folder that has the launcher and server executables</param>
    /// <returns>Whether the shortcuts were created</returns>
    public static Result CreateSptShortcuts(string folder, string sptPath)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                return Result.FromError($"Could not find {folder}");
            }

            var extension = OperatingSystem.IsWindows() ? "exe" : "Linux";
            var launcher = Directory.GetFiles(sptPath, $"*.Launcher.{extension}").FirstOrDefault();
            var server = Directory.GetFiles(sptPath, $"*.Server.{extension}").FirstOrDefault();

            if (launcher == null || server == null)
            {
                return Result.FromError($"Could not find the launcher or server in {sptPath}");
            }

            if (OperatingSystem.IsWindows())
            {
                Create(Path.Join(folder, "SPT.Launcher.lnk"), launcher, sptPath);
                Create(Path.Join(folder, "SPT.Server.lnk"), server, sptPath);
            }
            else
            {
                CreateDesktopEntry(Path.Join(folder, "SPT.Launcher.desktop"), "SPT Launcher", launcher, sptPath, false);
                CreateDesktopEntry(Path.Join(folder, "SPT.Server.desktop"), "SPT Server", server, sptPath, true);
            }

            Log.Information("Created shortcuts in {folder}", folder);
            return Result.FromSuccess();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not create shortcuts in {folder}", folder);
            return Result.FromError($"Could not create shortcuts in {folder}: {ex.Message}");
        }
    }

    private static void CreateDesktopEntry(string shortcutPath, string name, string target, string workingDirectory, bool terminal)
    {
        File.WriteAllLines(shortcutPath,
        [
            "[Desktop Entry]",
            "Type=Application",
            $"Name={name}",
            $"Exec=\"{target}\"",
            $"Path={workingDirectory}",
            $"Terminal={(terminal ? "true" : "false")}",
        ]);

        // Desktops only offer to launch entries marked as executable
        FileHelper.MakeExecutable(shortcutPath);
    }

    private static void Create(string shortcutPath, string target, string workingDirectory)
    {
        var link = (IShellLinkW)new ShellLink();

        try
        {
            link.SetPath(target);
            link.SetWorkingDirectory(workingDirectory);
            ((IPersistFile)link).Save(shortcutPath, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }
}
