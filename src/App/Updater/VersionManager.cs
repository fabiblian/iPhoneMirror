using System.Reflection;

namespace IPhoneMirror.App.Updater;

internal static class VersionManager
{
    internal static SemanticVersion Current
    {
        get
        {
            var assembly = typeof(VersionManager).Assembly;
            var informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            if (SemanticVersion.TryParse(informational, out var semantic))
                return semantic;
            var version = assembly.GetName().Version ?? new Version(0, 0, 0);
            return new SemanticVersion(version.Major, version.Minor,
                Math.Max(0, version.Build));
        }
    }

    internal static string DisplayVersion => $"v{Current}";

    /// <summary>Git branch the build came from, empty when unknown.</summary>
    internal static string BuildBranch => Metadata("GitBranch");

    /// <summary>Short git commit the build came from, empty when unknown.</summary>
    internal static string BuildCommit => Metadata("GitCommit");

    /// <summary>"v1.8.5-pre2 · redesign · 94902cb", leaving out unknown parts.</summary>
    internal static string BuildLabel => string.Join(" · ",
        new[] { DisplayVersion, BuildBranch, BuildCommit }.Where(part => part.Length > 0));

    private static string Metadata(string key) =>
        typeof(VersionManager).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == key)?.Value?.Trim() ?? "";
}
