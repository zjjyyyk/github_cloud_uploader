using System;
using System.IO;
using Newtonsoft.Json;
using Serilog;

namespace CloudUploader.Services
{
    public class AppConfig
    {
        public string TargetFolder { get; set; } = @"C:\Users\zjj\Desktop\zjjyyyk.github.io\github_cloud";
        public string GitRepoFolder { get; set; } = @"C:\Users\zjj\Desktop\zjjyyyk.github.io";
        public bool AutoStart { get; set; } = true;
        public bool StartMinimized { get; set; } = true;
        public int LogRetentionDays { get; set; } = 30;
    }

    public class ConfigService
    {
        private static readonly string ConfigFilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "config.json");

        public static AppConfig Instance { get; private set; } = new AppConfig();

        public static void Load()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    string json = File.ReadAllText(ConfigFilePath);
                    Instance = JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();
                    Log.Information("Configuration loaded successfully");
                }
                else
                {
                    Log.Information("Configuration file not found, using default settings");
                    Save(); // 创建默认配置文件
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load configuration");
                Instance = new AppConfig();
            }
        }

        public static void Save()
        {
            try
            {
                string json = JsonConvert.SerializeObject(Instance, Formatting.Indented);
                File.WriteAllText(ConfigFilePath, json);
                Log.Information("Configuration saved successfully");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save configuration");
            }
        }

        public static void UpdateTargetFolder(string path)
        {
            Instance.TargetFolder = path;
            Save();
            Log.Information($"Target folder updated to: {path}");
        }

        public static void UpdateGitRepoFolder(string path)
        {
            Instance.GitRepoFolder = path;
            Save();
            Log.Information($"Git repo folder updated to: {path}");
        }

        public static void UpdateAutoStart(bool enabled)
        {
            Instance.AutoStart = enabled;
            Save();
            
            // 更新注册表启动项
            if (enabled)
            {
                RegistryService.EnableAutoStart();
            }
            else
            {
                RegistryService.DisableAutoStart();
            }
        }

        public static void UpdateStartMinimized(bool enabled)
        {
            Instance.StartMinimized = enabled;
            Save();
            Log.Information($"Start minimized: {enabled}");
        }
    }
}
