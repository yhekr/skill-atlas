using System.Diagnostics;
using System.Text;

namespace SkillAtlas.Tests;

internal sealed class TestWorkspace : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "skill-atlas-tests", Guid.NewGuid().ToString("N"));

    public TestWorkspace() => Directory.CreateDirectory(Root);

    public string Write(string relativePath, string content)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    public async Task<CommandResult> RunAsync(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var registration = timeout.Token.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        return new CommandResult(process.ExitCode, await output, await error);
    }

    public void Dispose()
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var file in Directory.EnumerateFiles(Root, "*", options)) File.SetAttributes(file, FileAttributes.Normal);
        for (var attempt = 0; ; attempt++)
        {
            try { Directory.Delete(Root, recursive: true); break; }
            catch (IOException) when (attempt < 10) { Thread.Sleep(100); }
        }
    }
}

internal sealed record CommandResult(int ExitCode, string Output, string Error);
