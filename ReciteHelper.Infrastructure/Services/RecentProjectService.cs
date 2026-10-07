using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.ValueObjects;
using System.Text.Json;

namespace ReciteHelper.Infrastructure.Services;

public sealed class RecentProjectService : IRecentProjectService
{
    private const int MaxRecentProjects = 10;
    private readonly string _recentProjectsPath;
    private readonly string _legacyRecentProjectsPath;

    public RecentProjectService()
    {
        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ReciteHelper");
        _recentProjectsPath = Path.Combine(appDataDirectory, "recent_projects.json");
        _legacyRecentProjectsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "recent_projects.json");
    }

    private string EnsureRecentProjectsPath()
    {
        // Migrate the list from the install directory (not writable under Program
        // Files) into %APPDATA% on the first run after upgrading.
        if (!File.Exists(_recentProjectsPath) && File.Exists(_legacyRecentProjectsPath))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_recentProjectsPath)!);
                File.Copy(_legacyRecentProjectsPath, _recentProjectsPath);
            }
            catch (IOException)
            {
                return File.Exists(_recentProjectsPath) ? _recentProjectsPath : _legacyRecentProjectsPath;
            }
        }

        return _recentProjectsPath;
    }

    public async Task<IReadOnlyList<RecentProject>> LoadAsync()
    {
        var path = EnsureRecentProjectsPath();
        if (!File.Exists(path))
            return [];

        await using var stream = File.OpenRead(path);
        var projects = await JsonSerializer.DeserializeAsync<List<RecentProject>>(stream);
        return Sort(projects ?? []);
    }

    public async Task<IReadOnlyList<RecentProject>> AddOrUpdateAsync(string projectPath, string? projectName = null)
    {
        var projects = (await LoadAsync()).ToList();
        var existingIndex = projects.FindIndex(
            project => string.Equals(project.ProjectPath, projectPath, StringComparison.OrdinalIgnoreCase));

        var displayName = projectName ?? Path.GetFileName(projectPath);
        var updated = RecentProject.Create(displayName, projectPath, DateTime.Now);

        if (existingIndex >= 0)
            projects[existingIndex] = updated;
        else
            projects.Add(updated);

        projects = Sort(projects).Take(MaxRecentProjects).ToList();
        await SaveAsync(projects);

        return projects;
    }

    public async Task<IReadOnlyList<RecentProject>> RemoveMissingAsync()
    {
        var projects = (await LoadAsync())
            .Where(project => File.Exists(project.ProjectPath))
            .ToList();

        await SaveAsync(projects);
        return Sort(projects);
    }

    private async Task SaveAsync(IReadOnlyList<RecentProject> projects)
    {
        var path = EnsureRecentProjectsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(projects, new JsonSerializerOptions { WriteIndented = true });

        var tempPath = $"{path}.tmp";
        await File.WriteAllTextAsync(tempPath, json);
        File.Move(tempPath, path, overwrite: true);
    }

    private static List<RecentProject> Sort(IEnumerable<RecentProject> projects)
    {
        return projects
            .OrderByDescending(project => project.LastAccessed)
            .ToList();
    }
}
