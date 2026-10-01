using System.Text.Json;
using SkillAtlas.Core;
using Spectre.Console;

namespace SkillAtlas.Cli;

public static class OrganizationCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>Returns 0 when every repository was scanned and 1 when any failed; results are written either way.</summary>
    public static async Task<int> RunAsync(CliOptions options, OrganizationScanner scanner, bool pretty,
        TextWriter output, TextWriter error, IAnsiConsole? console, CancellationToken cancellationToken)
    {
        var owner = new GitHubOwner(options.Organization ?? throw new ArgumentException("An organization is required."));
        var scanOptions = new OrganizationScanOptions
        {
            IncludeForks = options.IncludeForks,
            IncludeArchived = options.IncludeArchived,
            Concurrency = options.Concurrency
        };

        OrganizationScanResult result;
        if (pretty)
        {
            console ??= AnsiConsole.Console;
            var gate = new Lock();
            result = await console.Status().Spinner(Spinner.Known.Dots).StartAsync($"Listing {Markup.Escape(owner.Login)} repositories…",
                status => scanner.ScanAsync(owner, scanOptions, (done, total) =>
                {
                    lock (gate) status.Status($"Scanning {Markup.Escape(owner.Login)}: {done}/{total} repositories…");
                }, cancellationToken));
        }
        else result = await scanner.ScanAsync(owner, scanOptions, null, cancellationToken);

        result = result.Filter(options.Query);
        if (options.Json) output.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        else ResultRenderer.RenderOrganization(result, pretty, options.Query, output, console);

        foreach (var repository in result.Repositories)
        {
            var source = ResultRenderer.SafeText(repository.Source);
            if (repository.Error is not null)
                error.WriteLine($"Error ({source}): {ResultRenderer.SafeText(repository.Error)}");
            foreach (var warning in repository.Result?.Warnings ?? [])
                error.WriteLine($"Warning ({source}): {ResultRenderer.SafeText(warning)}");
        }
        return result.Repositories.Any(r => r.Error is not null) ? 1 : 0;
    }

    public static string? TokenFromEnvironment() =>
        new[] { "GITHUB_TOKEN", "GH_TOKEN" }.Select(Environment.GetEnvironmentVariable)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
