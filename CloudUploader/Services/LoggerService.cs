using System;
using System.IO;
using Serilog;
using Serilog.Events;

namespace CloudUploader.Services
{
    public static class LoggerService
    {
        private static readonly string LogDirectory = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "logs");

        public static void Initialize()
        {
            // 确保日志目录存在
            Directory.CreateDirectory(LogDirectory);

            // 配置 Serilog
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    Path.Combine(LogDirectory, "app_.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            Log.Information("Logger initialized");
            CleanOldLogs();
        }

        private static void CleanOldLogs()
        {
            try
            {
                var retentionDate = DateTime.Now.AddDays(-ConfigService.Instance.LogRetentionDays);
                var logFiles = Directory.GetFiles(LogDirectory, "app_*.log");

                foreach (var logFile in logFiles)
                {
                    var fileInfo = new FileInfo(logFile);
                    if (fileInfo.CreationTime < retentionDate)
                    {
                        File.Delete(logFile);
                        Log.Information($"Deleted old log file: {logFile}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to clean old logs");
            }
        }

        public static void LogOperation(string operation, string details, bool success)
        {
            if (success)
            {
                Log.Information($"[{operation}] {details}");
            }
            else
            {
                Log.Error($"[{operation}] Failed - {details}");
            }
        }

        public static void LogFileOperation(string operation, string sourcePath, string targetPath, long fileSize)
        {
            Log.Information($"[{operation}] Source: {sourcePath}, Target: {targetPath}, Size: {fileSize} bytes");
        }

        public static void LogGitCommand(string command, string output, TimeSpan duration)
        {
            Log.Information($"[Git] Command: {command}, Duration: {duration.TotalSeconds:F2}s");
            if (!string.IsNullOrWhiteSpace(output))
            {
                Log.Debug($"[Git Output] {output}");
            }
        }
    }
}
