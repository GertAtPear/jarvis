using Mediahost.Shared.Services;
using Octokit;

namespace Rex.Agent.Services;

public class GitHubService(IScopedVaultService vault, ILogger<GitHubService> logger)
{
    private GitHubClient? _client;

    private async Task<GitHubClient> GetClientAsync(CancellationToken ct = default)
    {
        if (_client is not null) return _client;

        var token    = await vault.GetSecretAsync("/rex/github", "token", ct);
        var username = await vault.GetSecretAsync("/rex/github", "username", ct);

        _client = new GitHubClient(new ProductHeaderValue("Rex-Agent"))
        {
            Credentials = new Credentials(token)
        };

        logger.LogInformation("GitHub client initialised for {User}", username);
        return _client;
    }

    public async Task<IReadOnlyList<Repository>> ListReposAsync(CancellationToken ct = default)
    {
        var client = await GetClientAsync(ct);
        return await client.Repository.GetAllForCurrent();
    }

    public async Task<Repository> GetRepoAsync(string owner, string repo, CancellationToken ct = default)
    {
        var client = await GetClientAsync(ct);
        return await client.Repository.Get(owner, repo);
    }

    public async Task<Repository> CreateRepoAsync(string name, string description, bool isPrivate, CancellationToken ct = default)
    {
        var client = await GetClientAsync(ct);
        return await client.Repository.Create(new NewRepository(name)
        {
            Description = description,
            Private     = isPrivate,
            AutoInit    = true
        });
    }

    public async Task<IReadOnlyList<Branch>> ListBranchesAsync(string owner, string repo, CancellationToken ct = default)
    {
        var client = await GetClientAsync(ct);
        return await client.Repository.Branch.GetAll(owner, repo);
    }

    public async Task<PullRequest> CreatePrAsync(string owner, string repo, string title, string body, string head, string @base, CancellationToken ct = default)
    {
        var client = await GetClientAsync(ct);
        return await client.PullRequest.Create(owner, repo, new NewPullRequest(title, head, @base) { Body = body });
    }

    public async Task<IReadOnlyList<Issue>> ListIssuesAsync(string owner, string repo, CancellationToken ct = default)
    {
        var client = await GetClientAsync(ct);
        return await client.Issue.GetAllForRepository(owner, repo);
    }

    public async Task<RepositoryContent> GetFileAsync(string owner, string repo, string path, string? branch = null, CancellationToken ct = default)
    {
        var client = await GetClientAsync(ct);
        IReadOnlyList<RepositoryContent> contents;
        if (branch is not null)
            contents = await client.Repository.Content.GetAllContentsByRef(owner, repo, path, branch);
        else
            contents = await client.Repository.Content.GetAllContents(owner, repo, path);
        return contents[0];
    }

    public async Task CreateOrUpdateFileAsync(string owner, string repo, string path, string content, string message, string? branch = null, CancellationToken ct = default)
    {
        var client = await GetClientAsync(ct);

        // Check if file exists to get its SHA (required for updates)
        string? sha = null;
        try
        {
            var existing = await GetFileAsync(owner, repo, path, branch, ct);
            sha = existing.Sha;
        }
        catch (NotFoundException) { /* file doesn't exist yet — create */ }

        var contentBytes = System.Text.Encoding.UTF8.GetBytes(content);
        var base64Content = Convert.ToBase64String(contentBytes);

        if (sha is not null)
        {
            var update = new UpdateFileRequest(message, base64Content, sha, branch ?? "main") { Committer = null };
            await client.Repository.Content.UpdateFile(owner, repo, path, update);
        }
        else
        {
            var create = new CreateFileRequest(message, base64Content, branch ?? "main");
            await client.Repository.Content.CreateFile(owner, repo, path, create);
        }
    }
}
