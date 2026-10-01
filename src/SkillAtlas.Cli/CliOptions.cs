namespace SkillAtlas.Cli;

public sealed record CliOptions(string? Source, string? Reference, string? Query, bool Json, bool NoColor, bool Help, bool Version)
{
    public string? SimilarTo { get; init; }
    public IReadOnlyList<string> Sources { get; init; } = [];

    public static CliOptions Parse(string[] args)
    {
        if (args.Length == 0 || args is ["--help"] or ["-h"] || args is ["scan", "--help"] or ["scan", "-h"])
            return new(null, null, null, false, false, true, false);
        if (args is ["--version"])
            return new(null, null, null, false, false, false, true);
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

        Options:
          --ref <branch-or-tag>  Scan a specific branch or tag (default: default branch)
          --query, -q <text>     Match every word in name, description or path (ignore case)
          --similar <name|path>  Find up to 5 related skills by keyword overlap (no AI)
          --json                Write structured JSON to stdout
          --no-color            Plain text output without ANSI formatting
          --help, -h            Show this help
          --version             Show the application version

        Examples:
          skill-atlas scan https://github.com/JetBrains/kotlin
          skill-atlas scan owner/repo --ref main --query gradle
          skill-atlas scan . --json
          skill-atlas scan owner/repo --similar build-gradle
          skill-atlas scan JetBrains/kotlin JetBrains/MPS --query gradle --json

        Multiple sources: --ref applies to all; failures preserve other results and return exit 1.
        JSON uses a repositories array with source, reference, result and error for each source.
        --similar requires a single source. Equivalent repository aliases are scanned once.

        Requires Git 2.25+ for remote scans; private repositories use your Git credentials.
        Skills are read as data. No instructions, hooks, or scripts from them are executed.
        Scans omit product/resources, test fixtures and build/dependency folders; symlinks and submodules are skipped.
        Identical mirrored .agents/.claude copies produce one entry. Empty repositories are supported.
        Warnings go to stderr. Exit codes: 0 success (including no matches), 1 scan failure,
        2 invalid arguments, 130 cancelled. Successful scans may contain warnings; inspect them.
        """;
}
