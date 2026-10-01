using System.Net;
using System.Text.Json;

namespace SkillAtlas.Core;

public sealed record GitHubRepositoryInfo(GitHubRepository Repository, bool IsFork, bool IsArchived);

/// <summary>
/// Lists an organization's or user's repositories through the GitHub REST API.
/// Only the listing uses the API; each repository is still scanned through Git.
/// </summary>
public sealed class GitHubRepositoryLister(HttpClient? http = null, string? token = null)
{
    public const int PageSize = 100;
    public const int MaximumRepositories = 10_000;
    private const long MaximumPageBytes = 32 * 1024 * 1024;
    private static readonly Uri ApiBase = new("https://api.github.com/");
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromMinutes(1) };
    private readonly HttpClient _http = http ?? SharedClient;
    private readonly string? _token = string.IsNullOrWhiteSpace(token) ? null : token.Trim();

    public async Task<IReadOnlyList<GitHubRepositoryInfo>> ListAsync(GitHubOwner owner, CancellationToken cancellationToken = default)
    {
        var repositories = await ListPagesAsync($"orgs/{owner.Login}/repos?type=all", cancellationToken)
            ?? await ListPagesAsync($"users/{owner.Login}/repos?type=owner", cancellationToken)
            ?? throw new ScanException($"GitHub organization or user '{owner.Login}' was not found." +
                (_token is null ? " Private organizations require GITHUB_TOKEN." : ""));
        return repositories.OrderBy(r => r.Repository.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    // Null means the account path does not exist, so an organization lookup can fall back to a user account.
    private async Task<List<GitHubRepositoryInfo>?> ListPagesAsync(string path, CancellationToken cancellationToken)
    {
        var repositories = new List<GitHubRepositoryInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var page = 1; ; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ApiBase, $"{path}&per_page={PageSize}&page={page}"));
            request.Headers.UserAgent.ParseAdd("skill-atlas");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            if (_token is not null) request.Headers.Authorization = new("Bearer", _token);

            using var response = await SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound && page == 1) return null;
            EnsureSuccess(response);
            if (response.Content.Headers.ContentLength > MaximumPageBytes)
                throw new ScanException("GitHub API returned an unexpectedly large repository page.");

            var entries = 0;
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                if (json.RootElement.ValueKind != JsonValueKind.Array)
                    throw new ScanException("GitHub API returned an unexpected repository list.");
                foreach (var item in json.RootElement.EnumerateArray())
                {
                    entries++;
                    // Disabled repositories cannot be cloned.
                    if (Flag(item, "disabled")) continue;
                    var repository = RepositoryName(item.GetProperty("full_name").GetString());
                    if (seen.Add(repository.DisplayName))
                        repositories.Add(new(repository, Flag(item, "fork"), Flag(item, "archived")));
                }
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                throw new ScanException("GitHub API returned an unexpected repository list.");
            }

            if (entries < PageSize) return repositories;
            if (page * PageSize >= MaximumRepositories)
                throw new ScanException($"The account has more than {MaximumRepositories} repositories; organization scans are limited to {MaximumRepositories}.");
        }
    }

    private static GitHubRepository RepositoryName(string? fullName)
    {
        // API data reaches Git command lines, so it is validated exactly like user input.
        try { return GitHubRepository.Parse(fullName ?? ""); }
        catch (ScanException) { throw new ScanException("GitHub API returned an invalid repository name."); }
    }

    private static bool Flag(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new ScanException($"Could not reach the GitHub API: {ex.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ScanException("The GitHub API did not respond in time. Check your network connection and retry.");
        }
    }

    private void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var rateLimited = response.StatusCode == HttpStatusCode.TooManyRequests ||
            (response.StatusCode == HttpStatusCode.Forbidden &&
             response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.FirstOrDefault() == "0");
        if (rateLimited)
            throw new ScanException("GitHub API rate limit reached while listing repositories. " +
                (_token is null ? "Set GITHUB_TOKEN for a higher limit, or retry later." : "Retry later."));
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new ScanException("GitHub rejected the token from GITHUB_TOKEN. Check or unset it and retry.");
        throw new ScanException($"GitHub API returned HTTP {(int)response.StatusCode} while listing repositories.");
    }
}
