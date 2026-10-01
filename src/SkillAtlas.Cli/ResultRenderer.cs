using SkillAtlas.Core;
using Spectre.Console;

namespace SkillAtlas.Cli;

public static class ResultRenderer
{
    public static void RenderSimilar(ScanResult result, Skill selected, IReadOnlyList<SimilarSkill> matches,
        bool pretty, IAnsiConsole? console = null)
    {
        console ??= AnsiConsole.Console;
        var heading = $"Similar to {selected.Name} in {result.Source}";
        if (pretty) console.MarkupLine($"[bold deepskyblue1]{Escape(heading)}[/]");
        else Console.WriteLine(SafeText(heading));
        var explanation = "Keyword overlap, not semantic similarity. Names weigh 3x descriptions; no AI.";
        if (pretty) console.WriteLine(explanation);
        else Console.WriteLine(explanation);
        if (matches.Count == 0)
        {
            if (pretty) console.WriteLine("No similar skills found.");
            else Console.WriteLine("No similar skills found.");
        }
        foreach (var match in matches)
        {
            var score = Math.Round(match.Score * 100).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var shared = string.Join(", ", match.SharedTerms.Take(8));
            if (pretty)
            {
                console.MarkupLine($"\n[bold]{Escape(match.Skill.Name)}[/]  [springgreen2]{score}% overlap[/]");
                console.MarkupLine($"[grey]{Escape(match.Skill.Description)}[/]");
                console.MarkupLine($"Shared: {Escape(shared)}");
                console.MarkupLine($"[deepskyblue1 link={Escape(match.Skill.Url)}]{Escape(match.Skill.Path)} ↗[/]");
            }
            else
            {
                Console.WriteLine($"\n{SafeText(match.Skill.Name)}  {score}% overlap");
                Console.WriteLine($"   {SafeText(match.Skill.Description)}");
                Console.WriteLine($"   Shared: {SafeText(shared)}");
                Console.WriteLine($"   {SafeText(match.Skill.Path)}");
                Console.WriteLine($"   {SafeText(match.Skill.Url)}");
            }
        }
    }

    public static void Render(ScanResult result, bool pretty, string? query, IAnsiConsole? console = null)
    {
        console ??= AnsiConsole.Console;
        if (!pretty) RenderPlain(result, Console.Out);
        else
        {
            console.WriteLine();
            console.MarkupLine("[bold deepskyblue1]skill-atlas[/]  [grey]One repository · every skill[/]");
            console.Write(new Rule().RuleStyle("grey23"));
            RenderPretty(result, console);
        }
        if (result.Skills.Count == 0)
            Console.WriteLine(query is null ? "No SKILL.md files found." : $"No skills match '{SafeText(query)}'.");
    }

    public static void RenderOrganization(OrganizationScanResult result, bool pretty, string? query, TextWriter output,
        IAnsiConsole? console = null)
    {
        console ??= AnsiConsole.Console;
        var withSkills = result.Repositories.Where(r => r.Result is { Skills.Count: > 0 }).ToArray();
        var skillCount = withSkills.Sum(r => r.Result!.Skills.Count);
        var summary = $"{Count(skillCount, "skill", "skills")} in {withSkills.Length} of " +
            Count(result.Repositories.Count, "repository", "repositories");
        var notes = new List<string>();
        var skipped = new List<string>();
        var forks = result.Skipped.Count(s => s.Reason == "fork");
        var archived = result.Skipped.Count(s => s.Reason == "archived");
        if (forks > 0) skipped.Add($"{Count(forks, "fork", "forks")} (--include-forks)");
        if (archived > 0) skipped.Add($"{Count(archived, "archived repository", "archived repositories")} (--include-archived)");
        if (skipped.Count > 0) notes.Add($"Skipped {string.Join(" and ", skipped)}.");
        var failed = result.Repositories.Count(r => r.Error is not null);
        if (failed > 0) notes.Add($"{Count(failed, "repository", "repositories")} failed; errors are listed on stderr.");

        if (!pretty)
        {
            output.WriteLine($"{SafeText(result.Owner)}  {summary}");
            foreach (var note in notes) output.WriteLine(note);
            foreach (var repository in withSkills)
            {
                output.WriteLine();
                RenderPlain(repository.Result!, output);
            }
        }
        else
        {
            console.WriteLine();
            console.MarkupLine("[bold deepskyblue1]skill-atlas[/]  [grey]Every repository · every skill[/]");
            console.Write(new Rule().RuleStyle("grey23"));
            console.MarkupLine($"[bold]{Escape(result.Owner)}[/]  [springgreen2]{Escape(summary)}[/]");
            foreach (var note in notes) console.MarkupLine($"[grey]{Escape(note)}[/]");
            foreach (var repository in withSkills)
            {
                console.WriteLine();
                console.Write(new Rule().RuleStyle("grey23"));
                RenderPretty(repository.Result!, console);
            }
        }
        if (skillCount == 0)
        {
            var message = query is null ? "No SKILL.md files found." : $"No skills match '{SafeText(query)}'.";
            if (pretty) console.WriteLine(message);
            else output.WriteLine(message);
        }
    }

    private static void RenderPlain(ScanResult result, TextWriter output)
    {
        output.WriteLine($"{SafeText(result.Source)}  {Count(result.Skills.Count, "skill", "skills")}");
        if (result.Revision is not null) output.WriteLine($"Revision: {SafeText(result.Revision)}");
        for (var index = 0; index < result.Skills.Count; index++)
        {
            var skill = result.Skills[index];
            output.WriteLine($"\n{index + 1}. {SafeText(skill.Name)}");
            output.WriteLine($"   {SafeText(skill.Description)}");
            output.WriteLine($"   {SafeText(skill.Path)}");
            output.WriteLine($"   {SafeText(skill.Url)}");
        }
    }

    private static void RenderPretty(ScanResult result, IAnsiConsole console)
    {
        console.MarkupLine($"[bold]{Escape(result.Source)}[/]  [springgreen2]{Count(result.Skills.Count, "skill", "skills")}[/]");
        if (result.Revision is not null)
            console.MarkupLine($"[grey]Commit {Escape(result.Revision[..Math.Min(12, result.Revision.Length)])}[/]");
        console.WriteLine();
        var table = new Table().Border(TableBorder.None).HideHeaders();
        table.AddColumn(new TableColumn("#").Width(4));
        table.AddColumn(new TableColumn("Skill"));
        table.AddColumn(new TableColumn("Source").NoWrap());
        for (var index = 0; index < result.Skills.Count; index++)
        {
            var skill = result.Skills[index];
            var description = SafeText(skill.Description);
            var textElements = System.Globalization.StringInfo.ParseCombiningCharacters(description);
            if (textElements.Length > 180) description = description[..textElements[177]] + "…";
            table.AddRow($"[grey]{index + 1} ›[/]",
                $"[bold deepskyblue1]{Escape(skill.Name)}[/]\n[grey]{Escape(description)}[/]\n[grey50]{Escape(skill.Path)}[/]\n",
                $"[deepskyblue1 link={Escape(skill.Url)}]SKILL.md ↗[/]");
        }
        console.Write(table);
    }

    private static string Count(int count, string singular, string plural) =>
        $"{count.ToString(System.Globalization.CultureInfo.InvariantCulture)} {(count == 1 ? singular : plural)}";

    // Repository content must never inject terminal control sequences or Spectre markup.
    public static string SafeText(string value) => string.Concat(value.Select(c =>
        char.IsControl(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format ? ' ' : c));

    private static string Escape(string value) => Markup.Escape(SafeText(value));
}
