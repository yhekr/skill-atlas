using System.Text;
using System.Text.Json;
using SkillAtlas.Cli;
using SkillAtlas.Core;
using Spectre.Console;

Console.OutputEncoding = Encoding.UTF8;
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };

try
{
    var options = CliOptions.Parse(args);
    if (options.Help) { Console.WriteLine(CliOptions.HelpText); return 0; }
    if (options.Version) { Console.WriteLine($"skill-atlas {typeof(CliOptions).Assembly.GetName().Version?.ToString(3)}"); return 0; }

    var pretty = !options.Json && !options.NoColor && !Console.IsOutputRedirected &&
        Environment.GetEnvironmentVariable("NO_COLOR") is null;

    async Task<ScanResult> Scan()
    {
        if (Directory.Exists(options.Source))
        {
            if (options.Reference is not null) throw new ScanException("--ref is supported only for GitHub repositories.");
            return await new LocalScanner().ScanAsync(options.Source!, cancellation.Token);
        }
        if (Path.IsPathRooted(options.Source!) || options.Source!.StartsWith('.') || options.Source.Contains('\\'))
            throw new ScanException($"Local directory does not exist: {options.Source}");
        return await new GitHubScanner().ScanAsync(GitHubRepository.Parse(options.Source), options.Reference, cancellation.Token);
    }

    var result = pretty
        ? await AnsiConsole.Status().Spinner(Spinner.Known.Dots).StartAsync("Discovering skills…", _ => Scan())
        : await Scan();
    result = result.Filter(options.Query);

    if (options.Json)
        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    else
        ResultRenderer.Render(result, pretty, options.Query);

    foreach (var warning in result.Warnings)
        Console.Error.WriteLine($"Warning: {ResultRenderer.SafeText(warning)}");
    return 0;
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"Error: {ResultRenderer.SafeText(ex.Message)}\nRun 'skill-atlas --help' for usage.");
    return 2;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Scan cancelled.");
    return 130;
}
catch (Exception ex) when (ex is ScanException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"Error: {ResultRenderer.SafeText(ex.Message)}");
    return 1;
}
