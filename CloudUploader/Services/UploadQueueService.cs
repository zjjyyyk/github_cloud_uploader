using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace CloudUploader.Services
{
    public enum UploadStatus
    {
        Waiting,
        Copying,
        GitOperating,
        Completed,
        Failed
    }

    public class UploadTask
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string SourcePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public UploadStatus Status { get; set; } = UploadStatus.Waiting;
        public int Progress { get; set; } = 0;
        public string Message { get; set; } = string.Empty;
        public bool AutoGit { get; set; } = true;
    }

    public class UploadQueueService
    {
        private readonly ConcurrentQueue<UploadTask> _queue = new();
        private readonly List<UploadTask> _allTasks = new();
        private readonly object _lockObject = new();
        private bool _isProcessing = false;
        private readonly string _targetFolder;
        private readonly GitService _gitService;

        public event EventHandler<UploadTask>? TaskAdded;
        public event EventHandler<UploadTask>? TaskUpdated;
        public event EventHandler<UploadTask>? TaskCompleted;

        public UploadQueueService(string targetFolder, string gitRepoFolder)
        {
            _targetFolder = targetFolder;
            _gitService = new GitService(gitRepoFolder);

            // 确保目标文件夹存在
            Directory.CreateDirectory(_targetFolder);
        }

        public void EnqueueFile(string filePath, bool autoGit = true)
        {
            if (!File.Exists(filePath))
            {
                Log.Warning($"File not found: {filePath}");
                return;
            }

            var fileInfo = new FileInfo(filePath);
            var task = new UploadTask
            {
                SourcePath = filePath,
                FileName = fileInfo.Name,
                FileSize = fileInfo.Length,
                AutoGit = autoGit
            };

            lock (_lockObject)
            {
                _queue.Enqueue(task);
                _allTasks.Add(task);
            }

            TaskAdded?.Invoke(this, task);
            Log.Information($"File enqueued: {filePath}");

            StartProcessing();
        }

        public void EnqueueDirectory(string directoryPath, bool autoGit = true)
        {
            if (!Directory.Exists(directoryPath))
            {
                Log.Warning($"Directory not found: {directoryPath}");
                return;
            }

            var dirInfo = new DirectoryInfo(directoryPath);
            var task = new UploadTask
            {
                SourcePath = directoryPath,
                FileName = dirInfo.Name,
                FileSize = GetDirectorySize(directoryPath),
                AutoGit = autoGit
            };

            lock (_lockObject)
            {
                _queue.Enqueue(task);
                _allTasks.Add(task);
            }

            TaskAdded?.Invoke(this, task);
            Log.Information($"Directory enqueued: {directoryPath}");

            StartProcessing();
        }

        public void EnqueueMultiple(string[] paths, bool autoGit = true)
        {
            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    EnqueueFile(path, autoGit);
                }
                else if (Directory.Exists(path))
                {
                    EnqueueDirectory(path, autoGit);
                }
            }
        }

        private void StartProcessing()
        {
            if (_isProcessing) return;

            _isProcessing = true;
            Task.Run(ProcessQueueAsync);
        }

        private async Task ProcessQueueAsync()
        {
            while (_queue.TryDequeue(out var task))
            {
                try
                {
                    await ProcessTaskAsync(task);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"Failed to process task: {task.SourcePath}");
                    task.Status = UploadStatus.Failed;
                    task.Message = $"Error: {ex.Message}";
                    TaskUpdated?.Invoke(this, task);
                }
            }

            _isProcessing = false;
        }

        private async Task ProcessTaskAsync(UploadTask task)
        {
            // 复制文件/文件夹
            task.Status = UploadStatus.Copying;
            task.Message = "正在复制文件...";
            TaskUpdated?.Invoke(this, task);

            var targetPath = Path.Combine(_targetFolder, task.FileName);
            
            if (File.Exists(task.SourcePath))
            {
                await CopyFileAsync(task.SourcePath, targetPath, task);
            }
            else if (Directory.Exists(task.SourcePath))
            {
                await CopyDirectoryAsync(task.SourcePath, targetPath, task);
            }

            LoggerService.LogFileOperation("Upload", task.SourcePath, targetPath, task.FileSize);

            // 执行 Git 操作
            if (task.AutoGit)
            {
                task.Status = UploadStatus.GitOperating;
                task.Message = "正在执行 Git 操作...";
                TaskUpdated?.Invoke(this, task);

                var gitResult = await _gitService.AddCommitPushAsync();
                
                if (gitResult.success)
                {
                    task.Status = UploadStatus.Completed;
                    task.Progress = 100;
                    task.Message = "上传完成";
                }
                else
                {
                    task.Status = UploadStatus.Failed;
                    task.Message = $"Git 操作失败: {gitResult.output}";
                }
            }
            else
            {
                task.Status = UploadStatus.Completed;
                task.Progress = 100;
                task.Message = "复制完成（未执行 Git 操作）";
            }

            TaskUpdated?.Invoke(this, task);
            TaskCompleted?.Invoke(this, task);
        }

        private async Task CopyFileAsync(string sourcePath, string targetPath, UploadTask task)
        {
            await Task.Run(() =>
            {
                using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read);
                using var targetStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write);

                byte[] buffer = new byte[81920]; // 80KB buffer
                long totalBytesRead = 0;
                int bytesRead;

                while ((bytesRead = sourceStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    targetStream.Write(buffer, 0, bytesRead);
                    totalBytesRead += bytesRead;

                    // 更新进度
                    task.Progress = (int)((double)totalBytesRead / task.FileSize * 90); // 90% 用于复制
                    TaskUpdated?.Invoke(this, task);
                }
            });
        }

        private async Task CopyDirectoryAsync(string sourcePath, string targetPath, UploadTask task)
        {
            await Task.Run(() =>
            {
                CopyDirectory(sourcePath, targetPath);
                task.Progress = 90; // 目录复制完成后设为 90%
                TaskUpdated?.Invoke(this, task);
            });
        }

        private void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string targetFile = Path.Combine(targetDir, Path.GetFileName(file));
                File.Copy(file, targetFile, true);
            }

            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string targetSubDir = Path.Combine(targetDir, Path.GetFileName(subDir));
                CopyDirectory(subDir, targetSubDir);
            }
        }

        private long GetDirectorySize(string path)
        {
            try
            {
                return new DirectoryInfo(path)
                    .GetFiles("*", SearchOption.AllDirectories)
                    .Sum(file => file.Length);
            }
            catch
            {
                return 0;
            }
        }

        public List<UploadTask> GetAllTasks()
        {
            lock (_lockObject)
            {
                return new List<UploadTask>(_allTasks);
            }
        }

        public void ClearCompletedTasks()
        {
            lock (_lockObject)
            {
                _allTasks.RemoveAll(t => t.Status == UploadStatus.Completed);
            }
        }
    }
}
