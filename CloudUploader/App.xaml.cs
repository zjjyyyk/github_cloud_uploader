using System;
using System.Windows;
using CloudUploader.Services;
using Serilog;

namespace CloudUploader
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 初始化日志系统
            LoggerService.Initialize();
            Log.Information("Application starting...");

            // 加载配置
            ConfigService.Load();

            // 创建主窗口（必须创建，否则程序会退出）
            Log.Information("Creating main window...");
            try
            {
                MainWindow = new MainWindow();
                Log.Information("Main window created successfully");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to create main window");
                MessageBox.Show($"启动失败: {ex.Message}\n\n{ex.StackTrace}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            // 检查是否从右键菜单启动
            if (e.Args.Length > 0)
            {
                Log.Information($"Launched from context menu with {e.Args.Length} arguments");
                // 处理右键菜单传入的文件路径
                HandleContextMenuArgs(e.Args);
            }

            // 默认显示主窗口（不再默认最小化）
            Log.Information("Showing main window");
            MainWindow.Show();
        }

        private void HandleContextMenuArgs(string[] args)
        {
            // 获取主窗口实例并处理文件
            if (MainWindow is MainWindow mainWindow)
            {
                mainWindow.ProcessFilesFromContextMenu(args);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("Application exiting...");
            ConfigService.Save();
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
