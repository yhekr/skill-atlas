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
        var count = $"{result.Skills.Count} skill{(result.Skills.Count == 1 ? "" : "s")}";
        if (!pretty)
        {
            Console.WriteLine($"{SafeText(result.Source)}  {count}");
            if (result.Revision is not null) Console.WriteLine($"Revision: {SafeText(result.Revision)}");
            for (var index = 0; index < result.Skills.Count; index++)
            {
                var skill = result.Skills[index];
                Console.WriteLine($"\n{index + 1}. {SafeText(skill.Name)}");
                Console.WriteLine($"   {SafeText(skill.Description)}");
                Console.WriteLine($"   {SafeText(skill.Path)}");
                Console.WriteLine($"   {SafeText(skill.Url)}");
            }
        }
        else
        {
            console.WriteLine();
            console.MarkupLine("[bold deepskyblue1]skill-atlas[/]  [grey]One repository · every skill[/]");
            console.Write(new Rule().RuleStyle("grey23"));
            console.MarkupLine($"[bold]{Escape(result.Source)}[/]  [springgreen2]{count}[/]");
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
        if (result.Skills.Count == 0)
            Console.WriteLine(query is null ? "No SKILL.md files found." : $"No skills match '{SafeText(query)}'.");
    }

    // Repository content must never inject terminal control sequences or Spectre markup.
    public static string SafeText(string value) => string.Concat(value.Select(c =>
        char.IsControl(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format ? ' ' : c));

    private static string Escape(string value) => Markup.Escape(SafeText(value));
}
