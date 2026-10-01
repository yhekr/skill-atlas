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

    if (options.Organization is not null)
        return await OrganizationCommand.RunAsync(options,
            new OrganizationScanner(new GitHubRepositoryLister(token: OrganizationCommand.TokenFromEnvironment()), new GitHubScanner()),
            pretty, Console.Out, Console.Error, null, cancellation.Token);

    async Task<ScanResult> ScanSource(string source, string? reference, CancellationToken token)
    {
        if (Directory.Exists(source))
        {
            if (reference is not null) throw new ScanException("--ref is supported only for GitHub repositories.");
            return await new LocalScanner().ScanAsync(source, token);
        }
        if (Path.IsPathRooted(source) || source.StartsWith('.') || source.Contains('\\'))
            throw new ScanException($"Local directory does not exist: {source}");
        return await new GitHubScanner().ScanAsync(GitHubRepository.Parse(source), reference, token);
    }

    var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    if (options.Sources.Count > 1)
    {
        var targets = ScanBatch.Normalize(options.Sources, options.Reference, allowLocal: true);
        var batch = await ScanBatch.RunAsync(targets, async (target, token) =>
            (await ScanSource(target.Source, target.Reference, token)).Filter(options.Query), cancellation.Token);
        if (options.Json) Console.WriteLine(JsonSerializer.Serialize(new { repositories = batch }, jsonOptions));
        foreach (var item in batch)
        {
            if (item.Result is not null)
            {
                if (!options.Json) ResultRenderer.Render(item.Result, pretty, options.Query);
                foreach (var warning in item.Result.Warnings)
                    Console.Error.WriteLine($"Warning ({ResultRenderer.SafeText(item.Source)}): {ResultRenderer.SafeText(warning)}");
            }
            else Console.Error.WriteLine($"Error ({ResultRenderer.SafeText(item.Source)}): {ResultRenderer.SafeText(item.Error!)}");
        }
        return batch.Any(item => item.Error is not null) ? 1 : 0;
    }

    Task<ScanResult> Scan() => ScanSource(options.Source!, options.Reference, cancellation.Token);

    var result = pretty
        ? await AnsiConsole.Status().Spinner(Spinner.Known.Dots).StartAsync("Discovering skills…", _ => Scan())
        : await Scan();
    result = result.Filter(options.Query);
    var selectedIndex = options.SimilarTo is null ? -1 : SkillSimilarity.ResolveIndex(result.Skills, options.SimilarTo);
    var similar = selectedIndex < 0 ? null : SkillSimilarity.FindSimilar(result.Skills, selectedIndex);

    if (options.Json)
        Console.WriteLine(JsonSerializer.Serialize(selectedIndex < 0 ? (object)result : new
        {
            result.Source,
            result.Revision,
            selected = result.Skills[selectedIndex],
            matches = similar,
            result.Warnings
        }, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    else if (selectedIndex >= 0)
        ResultRenderer.RenderSimilar(result, result.Skills[selectedIndex], similar!, pretty);
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
