using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace SkillAtlas.Core;

public interface IGitRunner
{
    Task<string> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

public sealed class GitRunner(TimeSpan? operationTimeout = null, int maxOutputCharacters = 64 * 1024 * 1024) : IGitRunner
{
    private readonly TimeSpan _operationTimeout = operationTimeout ?? TimeSpan.FromMinutes(5);

    public async Task<string> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[] { "-c", "core.fsmonitor=false", "-c", "submodule.recurse=false" }.Concat(arguments))
            start.ArgumentList.Add(argument);
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GCM_INTERACTIVE"] = "Never";
        start.Environment["GIT_LFS_SKIP_SMUDGE"] = "1";
        // An enclosing shell's repository selection must not redirect our Git operations.
        foreach (var variable in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_COMMON_DIR", "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES" })
            start.Environment.Remove(variable);

        using var process = new Process { StartInfo = start };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_operationTimeout);
        try
        {
            if (!process.Start()) throw new ScanException("Could not start Git.");
        }
        catch (Win32Exception)
        {
            throw new ScanException("Git was not found. Install Git 2.25+ and make sure it is on PATH.");
        }

        process.StandardInput.Close();
        using var registration = timeout.Token.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });
        var stdout = ReadBoundedAsync(process.StandardOutput, maxOutputCharacters, timeout);
        var stderr = ReadBoundedAsync(process.StandardError, 1024 * 1024, timeout);
        try
        {
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(timeout.Token));
            if (process.ExitCode != 0)
                throw new GitCommandException(process.ExitCode, $"Git failed: {(await stderr).Trim()}\nCheck the repository URL, branch/tag and your Git credentials for private repositories.");
            return await stdout;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ScanException($"Git operation timed out after {_operationTimeout.TotalSeconds:0.###} seconds. Check your network connection and retry.");
        }
        finally
        {
            // Cancellation kills the process tree; wait for it to release files before clone cleanup.
            if (!process.HasExited) await process.WaitForExitAsync(CancellationToken.None);
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int limit, CancellationTokenSource cancellation)
    {
        var result = new StringBuilder();
        var buffer = new char[8192];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellation.Token)) != 0)
        {
            if (result.Length + read > limit)
            {
                await cancellation.CancelAsync();
                throw new ScanException("Git output exceeded the safety limit; the scan is incomplete.");
            }
            result.Append(buffer, 0, read);
        }
        return result.ToString();
    }
}
