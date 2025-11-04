using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using LibGit2Sharp;
using Serilog;

namespace CloudUploader.Services
{
    public class GitService
    {
        private readonly string _repoPath;

        public GitService(string repoPath)
        {
            _repoPath = repoPath;
        }

        public async Task<(bool success, string output)> PullAsync()
        {
            return await Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    using var repo = new Repository(_repoPath);
                    var signature = GetSignature(repo);
                    
                    Commands.Pull(repo, signature, new PullOptions
                    {
                        FetchOptions = new FetchOptions()
                    });

                    sw.Stop();
                    var output = "Pull completed successfully";
                    LoggerService.LogGitCommand("git pull", output, sw.Elapsed);
                    return (true, output);
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    Log.Error(ex, "Git pull failed");
                    return (false, $"Pull failed: {ex.Message}");
                }
            });
        }

        public async Task<(bool success, string output)> AddAllAsync()
        {
            return await Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    using var repo = new Repository(_repoPath);
                    Commands.Stage(repo, "*");

                    sw.Stop();
                    var output = "All changes staged successfully";
                    LoggerService.LogGitCommand("git add -A", output, sw.Elapsed);
                    return (true, output);
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    Log.Error(ex, "Git add failed");
                    return (false, $"Add failed: {ex.Message}");
                }
            });
        }

        public async Task<(bool success, string output)> CommitAsync(string message = "")
        {
            return await Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    using var repo = new Repository(_repoPath);
                    var signature = GetSignature(repo);

                    // 检查是否有更改
                    var status = repo.RetrieveStatus();
                    if (!status.IsDirty)
                    {
                        sw.Stop();
                        return (true, "No changes to commit");
                    }

                    var commit = repo.Commit(message, signature, signature, new CommitOptions
                    {
                        AllowEmptyCommit = true
                    });

                    sw.Stop();
                    var output = $"Commit created: {commit.Sha.Substring(0, 7)}";
                    LoggerService.LogGitCommand("git commit", output, sw.Elapsed);
                    return (true, output);
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    Log.Error(ex, "Git commit failed");
                    return (false, $"Commit failed: {ex.Message}");
                }
            });
        }

        public async Task<(bool success, string output)> PushAsync()
        {
            return await Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    // 先检查冲突
                    var (hasConflict, conflictMsg) = CheckForConflicts();
                    if (hasConflict)
                    {
                        return (false, conflictMsg);
                    }

                    // 使用命令行方式执行 git push，更稳定
                    var result = ExecuteGitCommand("push");
                    
                    sw.Stop();
                    if (result.success)
                    {
                        var output = "Push completed successfully\n" + result.output;
                        LoggerService.LogGitCommand("git push", output, sw.Elapsed);
                        return (true, output);
                    }
                    else
                    {
                        Log.Error($"Git push failed: {result.output}");
                        return (false, $"Push failed: {result.output}");
                    }
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    Log.Error(ex, "Git push failed");
                    return (false, $"Push failed: {ex.Message}");
                }
            });
        }

        private (bool success, string output) ExecuteGitCommand(string arguments)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "git",
                        Arguments = arguments,
                        WorkingDirectory = _repoPath,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                var output = new StringBuilder();
                var error = new StringBuilder();

                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        output.AppendLine(e.Data);
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        error.AppendLine(e.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                var result = output.ToString();
                if (error.Length > 0)
                    result += "\n" + error.ToString();

                return (process.ExitCode == 0, result);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to execute git {arguments}");
                return (false, $"Failed to execute git command: {ex.Message}");
            }
        }

        public async Task<(bool success, string output)> AddCommitPushAsync()
        {
            var output = new StringBuilder();

            // Add
            var addResult = await AddAllAsync();
            output.AppendLine(addResult.output);
            if (!addResult.success)
            {
                return (false, output.ToString());
            }

            // Commit
            var commitResult = await CommitAsync();
            output.AppendLine(commitResult.output);
            if (!commitResult.success)
            {
                return (false, output.ToString());
            }

            // Push
            var pushResult = await PushAsync();
            output.AppendLine(pushResult.output);
            
            return (pushResult.success, output.ToString());
        }

        public (bool hasConflict, string message) CheckForConflicts()
        {
            try
            {
                using var repo = new Repository(_repoPath);
                var trackingBranch = repo.Head.TrackedBranch;
                
                if (trackingBranch == null)
                {
                    return (false, string.Empty);
                }

                // 检查本地分支是否落后于远程分支
                var historyDivergence = repo.ObjectDatabase.CalculateHistoryDivergence(
                    repo.Head.Tip, trackingBranch.Tip);

                if (historyDivergence.BehindBy > 0)
                {
                    return (true, "检测到 Git 冲突，请先执行 Pull 操作解决冲突");
                }

                return (false, string.Empty);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to check for conflicts");
                return (false, string.Empty);
            }
        }

        private Signature GetSignature(Repository repo)
        {
            try
            {
                var config = repo.Config;
                var name = config.Get<string>("user.name")?.Value ?? "Cloud Uploader";
                var email = config.Get<string>("user.email")?.Value ?? "uploader@local";
                return new Signature(name, email, DateTimeOffset.Now);
            }
            catch
            {
                return new Signature("Cloud Uploader", "uploader@local", DateTimeOffset.Now);
            }
        }
    }
}
