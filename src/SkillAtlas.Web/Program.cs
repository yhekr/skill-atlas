using SkillAtlas.Core;
using SkillAtlas.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IScanCatalog, ScanCatalog>();
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 4096);
var app = builder.Build();

app.Use(async (context, next) =>
{
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.CacheControl = "no-store";
        var origin = context.Request.Headers.Origin.ToString();
        var expectedOrigin = $"{context.Request.Scheme}://{context.Request.Host}";
        if ((origin.Length > 0 && origin != expectedOrigin) || context.Request.Headers["Sec-Fetch-Site"] == "cross-site")
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new { error = "Open Skill Atlas directly to use the scanner." });
            return;
        }
    }
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/api/scans", async (ScanRequest request, IScanCatalog catalog, HttpContext context, ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(request.Repository) || request.Repository.Length > 300 ||
        request.Reference?.Length > 200 || request.Reference?.Any(char.IsControl) == true ||
        request.Reference?.StartsWith('-') == true)
        return Results.BadRequest(new { error = "Enter a GitHub repository URL or owner/repository, and a valid branch or tag." });

    GitHubRepository repository;
    try { repository = GitHubRepository.Parse(request.Repository); }
    catch (ScanException)
    {
        return Results.BadRequest(new { error = "Use a GitHub repository URL, such as https://github.com/JetBrains/kotlin, or owner/repository." });
    }

    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
    timeout.CancelAfter(TimeSpan.FromMinutes(5));
    try
    {
        var scan = await catalog.ScanAsync(repository,
            string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim(), timeout.Token);
        return Results.Ok(new
        {
            scanId = scan.Id,
            scan.Snapshot.Catalog.Source,
            scan.Snapshot.Catalog.Revision,
            scan.Snapshot.Catalog.Skills,
            scan.Snapshot.Catalog.Warnings
        });
    }
    catch (ScanBusyException)
    {
        return Results.Json(new { error = "Two scans are already running. Please try again in a moment." }, statusCode: 429);
    }
    catch (OperationCanceledException)
    {
        return Results.Json(new { error = "The scan was cancelled or timed out. Please try again." }, statusCode: 408);
    }
    catch (Exception ex) when (ex is ScanException or IOException or UnauthorizedAccessException)
    {
        logger.LogWarning(ex, "Scan failed for {Repository}", repository.DisplayName);
        return Results.Json(new { error = "Could not read this repository. Check the repository address, branch or tag, and your Git access, then try again." }, statusCode: 422);
    }
});

app.MapGet("/api/scans/{id:guid}/skills/{index:int}", (Guid id, int index, IScanCatalog catalog) =>
{
    var scan = catalog.Find(id);
    if (scan is null) return Results.Json(new { error = "This scan has expired. Scan the repository again to read its skills." }, statusCode: 410);
    if (index < 0 || index >= scan.Snapshot.Catalog.Skills.Count) return Results.NotFound(new { error = "Skill not found." });
    var skill = scan.Snapshot.Catalog.Skills[index];
    var source = scan.Snapshot.Documents[skill.Path];
    return Results.Ok(new { skill.Name, skill.Path, skill.Url, source, html = SkillMarkdown.Render(source, skill.Url) });
});

app.MapGet("/api/scans/{id:guid}/skills/{index:int}/similar", (Guid id, int index, IScanCatalog catalog) =>
{
    var scan = catalog.Find(id);
    if (scan is null) return Results.Json(new { error = "This scan has expired. Scan the repository again to find similar skills." }, statusCode: 410);
    var skills = scan.Snapshot.Catalog.Skills;
    if (index < 0 || index >= skills.Count) return Results.NotFound(new { error = "Skill not found." });
    return Results.Ok(new { matches = SkillSimilarity.FindSimilar(skills, index) });
});

app.Run();

public sealed record ScanRequest(string? Repository, string? Reference);
public partial class Program;
