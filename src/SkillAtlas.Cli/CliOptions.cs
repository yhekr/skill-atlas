using System.Globalization;
using SkillAtlas.Core;

namespace SkillAtlas.Cli;

public sealed record CliOptions(string? Source, string? Reference, string? Query, bool Json, bool NoColor, bool Help, bool Version)
{
    public string? SimilarTo { get; init; }
    public IReadOnlyList<string> Sources { get; init; } = [];
    public string? Organization { get; init; }
    public bool IncludeForks { get; init; }
    public bool IncludeArchived { get; init; }
    public int Concurrency { get; init; } = OrganizationScanOptions.DefaultConcurrency;

    public static CliOptions Parse(string[] args)
    {
        if (args.Length == 0 || args is ["--help"] or ["-h"] || args is ["scan" or "org", "--help"] or ["scan" or "org", "-h"])
            return new(null, null, null, false, false, true, false);
        if (args is ["--version"])
            return new(null, null, null, false, false, false, true);
        if (args[0] == "org") return ParseOrganization(args);
        if (args[0] != "scan") throw new ArgumentException($"Unknown command '{args[0]}'.");

        string? source = null, reference = null, query = null, similarTo = null;
        var sources = new List<string>();
        var json = false;
        var noColor = false;
        var positional = false;
        for (var i = 1; i < args.Length; i++)
        {
            var argument = args[i];
            if (!positional && argument == "--") { positional = true; continue; }
            if (!positional && argument.StartsWith('-'))
            {
                switch (argument)
                {
                    case "--json": json = true; break;
                    case "--no-color": noColor = true; break;
                    case "--ref": reference = Value(args, ref i); break;
                    case "--query": case "-q": query = Value(args, ref i); break;
                    case "--similar": similarTo = Value(args, ref i); break;
                    case "--help": case "-h": return new(null, null, null, false, false, true, false);
                    default: throw new ArgumentException($"Unknown option '{argument}'.");
                }
            }
            else if (!string.IsNullOrWhiteSpace(argument)) { sources.Add(argument); source ??= argument; }
            else throw new ArgumentException("Repository entries cannot be blank.");
        }
        if (source is null) throw new ArgumentException("The scan command requires a repository or directory.");
        if (query is not null && similarTo is not null)
            throw new ArgumentException("Use --query or --similar, not both in the same scan.");
        if (sources.Count > SkillAtlas.Core.ScanBatch.MaximumSources)
            throw new ArgumentException("Specify at most 5 repositories or directories.");
        if (sources.Count > 1 && similarTo is not null)
            throw new ArgumentException("For --similar, select one repository or directory.");
        return new(source, reference, query, json, noColor, false, false) { SimilarTo = similarTo, Sources = sources };
    }

    private static CliOptions ParseOrganization(string[] args)
    {
        string? owner = null, query = null;
        bool json = false, noColor = false, includeForks = false, includeArchived = false, positional = false;
        var concurrency = OrganizationScanOptions.DefaultConcurrency;
        for (var i = 1; i < args.Length; i++)
        {
            var argument = args[i];
            if (!positional && argument == "--") { positional = true; continue; }
            if (!positional && argument.StartsWith('-'))
            {
                switch (argument)
                {
                    case "--json": json = true; break;
                    case "--no-color": noColor = true; break;
                    case "--query": case "-q": query = Value(args, ref i); break;
                    case "--include-forks": includeForks = true; break;
                    case "--include-archived": includeArchived = true; break;
                    case "--concurrency":
                        var text = Value(args, ref i);
                        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out concurrency) ||
                            concurrency is < 1 or > OrganizationScanOptions.MaximumConcurrency)
                            throw new ArgumentException($"--concurrency must be a whole number from 1 to {OrganizationScanOptions.MaximumConcurrency}.");
                        break;
                    case "--ref":
                        throw new ArgumentException("--ref is not supported by org; each repository's default branch is scanned.");
                    case "--similar":
                        throw new ArgumentException("--similar needs one repository: skill-atlas scan owner/repo --similar <name|path>.");
                    case "--help": case "-h": return new(null, null, null, false, false, true, false);
                    default: throw new ArgumentException($"Unknown option '{argument}'.");
                }
            }
            else if (owner is null && !string.IsNullOrWhiteSpace(argument)) owner = argument;
            else throw new ArgumentException("Specify exactly one GitHub organization or user.");
        }
        if (owner is null) throw new ArgumentException("The org command requires a GitHub organization or user, for example JetBrains.");
        try { owner = GitHubOwner.Parse(owner).Login; }
        catch (ScanException ex) { throw new ArgumentException(ex.Message); }
        return new(null, null, query, json, noColor, false, false)
        {
            Organization = owner,
            IncludeForks = includeForks,
            IncludeArchived = includeArchived,
            Concurrency = concurrency
        };
    }

    private static string Value(string[] args, ref int index)
    {
        var flag = args[index];
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]) || args[index].StartsWith('-'))
            throw new ArgumentException($"{flag} requires a value.");
        return args[index];
    }

    public const string HelpText = """
        skill-atlas — your repositories, every skill

        Usage:
          skill-atlas scan <github-url | owner/repo | local-directory> [options]
          skill-atlas scan <source1> <source2> ... [options]  (up to 5 sources)
          skill-atlas org <organization | https://github.com/owner> [org options]

        Options:
          --ref <branch-or-tag>  Scan a specific branch or tag (default: default branch)
          --query, -q <text>     Match every word in name, description or path (ignore case)
          --similar <name|path>  Find up to 5 related skills by keyword overlap (no AI)
          --json                Write structured JSON to stdout
          --no-color            Plain text output without ANSI formatting
          --help, -h            Show this help
          --version             Show the application version

        Org options (every repository of a GitHub organization or user, default branches):
          --include-forks       Also scan forks (skipped by default)
          --include-archived    Also scan archived repositories (skipped by default)
          --concurrency <1-16>  Repositories scanned in parallel (default: 4)
          --query, -q, --json and --no-color work as for scan; --ref and --similar are not supported

        Examples:
          skill-atlas scan https://github.com/JetBrains/kotlin
          skill-atlas scan owner/repo --ref main --query gradle
          skill-atlas scan . --json
          skill-atlas scan owner/repo --similar build-gradle
          skill-atlas scan JetBrains/kotlin JetBrains/MPS --query gradle --json
          skill-atlas org JetBrains --json > jetbrains-skills.json

        Multiple sources: --ref applies to all; failures preserve other results and return exit 1.
        JSON uses a repositories array with source, reference, result and error for each source.
        --similar requires a single source. Equivalent repository aliases are scanned once.

        org lists repositories through the GitHub REST API: anonymous access allows 60 requests
        per hour (100 repositories each). Set GITHUB_TOKEN (or GH_TOKEN) for a higher limit and
        private repositories; the token is sent only to api.github.com, never to Git.
        org JSON uses the same repositories array plus skipped forks/archived repositories.
        A failed repository returns exit 1 without discarding the others.

        Requires Git 2.25+ for remote scans; private repositories use your Git credentials.
        Skills are read as data. No instructions, hooks, or scripts from them are executed.
        Scans omit product/resources, test fixtures and build/dependency folders; symlinks and submodules are skipped.
        Identical mirrored .agents/.claude copies produce one entry. Empty repositories are supported.
        Warnings go to stderr. Exit codes: 0 success (including no matches), 1 scan failure,
        2 invalid arguments, 130 cancelled. Successful scans may contain warnings; inspect them.
        """;
}
