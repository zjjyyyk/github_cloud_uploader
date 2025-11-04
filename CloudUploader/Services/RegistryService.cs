using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Win32;
using Serilog;

namespace CloudUploader.Services
{
    public static class RegistryService
    {
        private const string AppName = "CloudUploader";
        private const string ContextMenuName = "上传到 zjjyyyk cloud";

        // 启动项注册表路径
        private const string StartupKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        // 右键菜单注册表路径
        private const string FileContextMenuPath = @"*\shell\CloudUploader";
        private const string DirectoryContextMenuPath = @"Directory\shell\CloudUploader";
        private const string DirectoryBackgroundContextMenuPath = @"Directory\Background\shell\CloudUploader";

        public static void EnableAutoStart()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(StartupKeyPath, true);
                if (key != null)
                {
                    var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                    key.SetValue(AppName, $"\"{exePath}\"");
                    Log.Information("Auto-start enabled");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to enable auto-start");
            }
        }

        public static void DisableAutoStart()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(StartupKeyPath, true);
                if (key != null && key.GetValue(AppName) != null)
                {
                    key.DeleteValue(AppName, false);
                    Log.Information("Auto-start disabled");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to disable auto-start");
            }
        }

        public static bool InstallContextMenu()
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                {
                    Log.Error("Failed to get executable path");
                    return false;
                }

                // 注册文件右键菜单
                RegisterContextMenu(Registry.ClassesRoot, FileContextMenuPath, exePath);

                // 注册文件夹右键菜单
                RegisterContextMenu(Registry.ClassesRoot, DirectoryContextMenuPath, exePath);

                // 注册文件夹背景右键菜单
                RegisterContextMenu(Registry.ClassesRoot, DirectoryBackgroundContextMenuPath, exePath);

                Log.Information("Context menu installed successfully");
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                Log.Error("Failed to install context menu: Administrator privileges required");
                return false;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to install context menu");
                return false;
            }
        }

        private static void RegisterContextMenu(RegistryKey baseKey, string path, string exePath)
        {
            using var key = baseKey.CreateSubKey(path);
            key?.SetValue("", ContextMenuName);
            key?.SetValue("Icon", exePath);

            using var commandKey = key?.CreateSubKey("command");
            commandKey?.SetValue("", $"\"{exePath}\" \"%1\"");
        }

        public static bool UninstallContextMenu()
        {
            try
            {
                // 删除文件右键菜单
                Registry.ClassesRoot.DeleteSubKeyTree(FileContextMenuPath, false);

                // 删除文件夹右键菜单
                Registry.ClassesRoot.DeleteSubKeyTree(DirectoryContextMenuPath, false);

                // 删除文件夹背景右键菜单
                Registry.ClassesRoot.DeleteSubKeyTree(DirectoryBackgroundContextMenuPath, false);

                Log.Information("Context menu uninstalled successfully");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to uninstall context menu");
                return false;
            }
        }

        public static bool IsContextMenuInstalled()
        {
            try
            {
                using var key = Registry.ClassesRoot.OpenSubKey(FileContextMenuPath);
                return key != null;
            }
            catch
            {
                return false;
            }
        }

        public static bool RunAsAdministrator()
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                    return false;

                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Verb = "runas", // 请求管理员权限
                    UseShellExecute = true
                };

                Process.Start(startInfo);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to run as administrator");
                return false;
            }
        }
    }
}
