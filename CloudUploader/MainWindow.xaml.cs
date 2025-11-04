using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CloudUploader.Services;
using Microsoft.Win32;
using Serilog;
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;

namespace CloudUploader
{
    public partial class MainWindow : Window
    {
        private UploadQueueService? _uploadQueueService;
        private GitService? _gitService;
        private ObservableCollection<UploadTask> _uploadTasks = new();

        public MainWindow()
        {
            InitializeComponent();
            InitializeServices();
            LoadConfiguration();
            
            // 设置托盘图标
            SetTrayIcon();

            // 默认显示主窗口（不再自动隐藏）
            Log.Information("Main window initialized");
        }

        private void SetTrayIcon()
        {
            try
            {
                // 从文件加载图标
                string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "app.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    TrayIcon.Icon = new System.Drawing.Icon(iconPath);
                    Log.Information($"Tray icon loaded from: {iconPath}");
                }
                else
                {
                    Log.Warning($"Tray icon file not found: {iconPath}");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load tray icon");
            }
        }

        private void InitializeServices()
        {
            _uploadQueueService = new UploadQueueService(
                ConfigService.Instance.TargetFolder,
                ConfigService.Instance.GitRepoFolder);

            _gitService = new GitService(ConfigService.Instance.GitRepoFolder);

            // 订阅上传队列事件
            _uploadQueueService.TaskAdded += OnTaskAdded;
            _uploadQueueService.TaskUpdated += OnTaskUpdated;
            _uploadQueueService.TaskCompleted += OnTaskCompleted;
        }

        private void LoadConfiguration()
        {
            TargetFolderTextBox.Text = ConfigService.Instance.TargetFolder;
            AutoStartCheckBox.IsChecked = ConfigService.Instance.AutoStart;
            StartMinimizedCheckBox.IsChecked = ConfigService.Instance.StartMinimized;
        }

        public void ProcessFilesFromContextMenu(string[] filePaths)
        {
            Log.Information($"Processing {filePaths.Length} files from context menu");
            
            // 显示窗口
            ShowWindow();

            // 添加到上传队列（自动执行 Git 操作）
            _uploadQueueService?.EnqueueMultiple(filePaths, autoGit: true);

            AppendGitOutput($"[{DateTime.Now:HH:mm:ss}] 开始处理 {filePaths.Length} 个项目\n");
        }

        private void OnTaskAdded(object? sender, UploadTask task)
        {
            Dispatcher.Invoke(() =>
            {
                _uploadTasks.Add(task);
                AppendLog($"任务添加: {task.FileName}");
            });
        }

        private void OnTaskUpdated(object? sender, UploadTask task)
        {
            Dispatcher.Invoke(() =>
            {
                // ListView 会自动更新，因为 UploadTask 的属性变化
                var existingTask = _uploadTasks.FirstOrDefault(t => t.Id == task.Id);
                if (existingTask != null)
                {
                    int index = _uploadTasks.IndexOf(existingTask);
                    _uploadTasks[index] = task;
                }
            });
        }

        private void OnTaskCompleted(object? sender, UploadTask task)
        {
            Dispatcher.Invoke(() =>
            {
                AppendLog($"任务完成: {task.FileName} - {task.Message}");
                
                if (task.Status == UploadStatus.Failed)
                {
                    MessageBox.Show(
                        $"上传失败: {task.FileName}\n错误: {task.Message}",
                        "上传失败",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            });
        }

        private void BrowseTargetFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog();
            dialog.SelectedPath = ConfigService.Instance.TargetFolder;
            
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                TargetFolderTextBox.Text = dialog.SelectedPath;
                ConfigService.UpdateTargetFolder(dialog.SelectedPath);
                
                // 重新初始化服务
                InitializeServices();
                
                AppendLog($"目标文件夹已更改为: {dialog.SelectedPath}");
            }
        }

        private void OpenTargetFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string targetFolder = ConfigService.Instance.TargetFolder;
                if (System.IO.Directory.Exists(targetFolder))
                {
                    System.Diagnostics.Process.Start("explorer.exe", targetFolder);
                    AppendLog($"已打开文件夹: {targetFolder}");
                }
                else
                {
                    MessageBox.Show("目标文件夹不存在，请先设置有效的目标文件夹。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    AppendLog("错误: 目标文件夹不存在");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开文件夹: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Log.Error(ex, "Failed to open target folder");
            }
        }

        private void AutoStartCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (AutoStartCheckBox.IsChecked.HasValue)
            {
                ConfigService.UpdateAutoStart(AutoStartCheckBox.IsChecked.Value);
                AppendLog($"开机启动: {(AutoStartCheckBox.IsChecked.Value ? "已启用" : "已禁用")}");
            }
        }

        private void StartMinimizedCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (StartMinimizedCheckBox.IsChecked.HasValue)
            {
                ConfigService.UpdateStartMinimized(StartMinimizedCheckBox.IsChecked.Value);
                AppendLog($"启动后最小化: {(StartMinimizedCheckBox.IsChecked.Value ? "已启用" : "已禁用")}");
            }
        }

        private void InstallContextMenu_Click(object sender, RoutedEventArgs e)
        {
            if (RegistryService.IsContextMenuInstalled())
            {
                var result = MessageBox.Show(
                    "右键菜单已安装。是否要卸载？",
                    "右键菜单",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    if (RegistryService.UninstallContextMenu())
                    {
                        MessageBox.Show("右键菜单卸载成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                        AppendLog("右键菜单已卸载");
                    }
                    else
                    {
                        MessageBox.Show("卸载失败，请以管理员权限运行程序。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            else
            {
                if (RegistryService.InstallContextMenu())
                {
                    MessageBox.Show("右键菜单安装成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    AppendLog("右键菜单已安装");
                }
                else
                {
                    var result = MessageBox.Show(
                        "安装失败，需要管理员权限。是否以管理员权限重启程序？",
                        "需要管理员权限",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        RegistryService.RunAsAdministrator();
                        Application.Current.Shutdown();
                    }
                }
            }
        }

        private void DropZone_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                DropZoneBorder.Background = new SolidColorBrush(Color.FromRgb(220, 240, 255));
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void DropZone_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void DropZone_DragLeave(object sender, DragEventArgs e)
        {
            DropZoneBorder.Background = new SolidColorBrush(Color.FromRgb(245, 245, 245));
        }

        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            DropZoneBorder.Background = new SolidColorBrush(Color.FromRgb(245, 245, 245));

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                _uploadQueueService?.EnqueueMultiple(files, autoGit: false); // 拖拽不自动执行 Git
                
                // 显示成功提示
                int fileCount = 0;
                int folderCount = 0;
                foreach (var path in files)
                {
                    if (System.IO.Directory.Exists(path))
                        folderCount++;
                    else if (System.IO.File.Exists(path))
                        fileCount++;
                }
                
                string message = $"成功添加 {fileCount} 个文件";
                if (folderCount > 0)
                    message += $" 和 {folderCount} 个文件夹";
                message += " 到上传队列";
                
                AppendLog(message);
                MessageBox.Show(message, "上传成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ClearCompleted_Click(object sender, RoutedEventArgs e)
        {
            var completedTasks = _uploadTasks.Where(t => t.Status == UploadStatus.Completed).ToList();
            foreach (var task in completedTasks)
            {
                _uploadTasks.Remove(task);
            }
            
            _uploadQueueService?.ClearCompletedTasks();
            AppendLog($"清除 {completedTasks.Count} 个已完成任务");
        }

        private async void GitPull_Click(object sender, RoutedEventArgs e)
        {
            AppendGitOutput($"[{DateTime.Now:HH:mm:ss}] 执行 git pull...\n");
            var result = await _gitService!.PullAsync();
            AppendGitOutput($"{result.output}\n");
            AppendLog($"Git Pull: {(result.success ? "成功" : "失败")}");
        }

        private async void GitAdd_Click(object sender, RoutedEventArgs e)
        {
            AppendGitOutput($"[{DateTime.Now:HH:mm:ss}] 执行 git add -A...\n");
            var result = await _gitService!.AddAllAsync();
            AppendGitOutput($"{result.output}\n");
            AppendLog($"Git Add: {(result.success ? "成功" : "失败")}");
        }

        private async void GitCommit_Click(object sender, RoutedEventArgs e)
        {
            AppendGitOutput($"[{DateTime.Now:HH:mm:ss}] 执行 git commit...\n");
            var result = await _gitService!.CommitAsync();
            AppendGitOutput($"{result.output}\n");
            AppendLog($"Git Commit: {(result.success ? "成功" : "失败")}");
        }

        private async void GitPush_Click(object sender, RoutedEventArgs e)
        {
            AppendGitOutput($"[{DateTime.Now:HH:mm:ss}] 执行 git push...\n");
            var result = await _gitService!.PushAsync();
            AppendGitOutput($"{result.output}\n");
            AppendLog($"Git Push: {(result.success ? "成功" : "失败")}");

            if (!result.success)
            {
                MessageBox.Show(result.output, "Git Push 失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void GitAddCommitPush_Click(object sender, RoutedEventArgs e)
        {
            AppendGitOutput($"[{DateTime.Now:HH:mm:ss}] 执行一键操作 (Add+Commit+Push)...\n");
            var result = await _gitService!.AddCommitPushAsync();
            AppendGitOutput($"{result.output}\n");
            AppendLog($"一键操作: {(result.success ? "成功" : "失败")}");

            if (result.success)
            {
                MessageBox.Show("Git 操作完成！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(result.output, "操作失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AppendGitOutput(string text)
        {
            GitOutputTextBox.AppendText(text);
            GitOutputTextBox.ScrollToEnd();
        }

        private void AppendLog(string message)
        {
            var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n";
            LogTextBox.AppendText(logMessage);
            LogTextBox.ScrollToEnd();
            Log.Information(message);
        }

        private bool _isReallyClosing = false;

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // 如果是真正退出，则允许关闭
            if (_isReallyClosing)
            {
                return;
            }
            
            // 否则点击关闭按钮时最小化到托盘而不是退出
            e.Cancel = true;
            this.Hide();
            this.ShowInTaskbar = false;
            Log.Information("程序已最小化到系统托盘");
        }

        private void TrayIcon_DoubleClick(object sender, RoutedEventArgs e)
        {
            ShowWindow();
        }

        private void ShowWindow_Click(object sender, RoutedEventArgs e)
        {
            ShowWindow();
        }

        private void ShowWindow()
        {
            this.Show();
            this.ShowInTaskbar = true;
            this.WindowState = WindowState.Normal;
            this.Activate();
        }

        private void ExitApp_Click(object sender, RoutedEventArgs e)
        {
            // 先显示窗口，确保对话框可见
            ShowWindow();
            
            var result = MessageBox.Show(
                this,  // 指定父窗口
                "确定要退出程序吗？",
                "退出确认",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                Log.Information("程序退出");
                
                // 设置真正退出标志
                _isReallyClosing = true;
                
                // 释放托盘图标资源
                if (TrayIcon != null)
                {
                    TrayIcon.Dispose();
                }
                
                // 关闭窗口和退出应用程序
                this.Close();
                Application.Current.Shutdown();
            }
        }
    }
}
