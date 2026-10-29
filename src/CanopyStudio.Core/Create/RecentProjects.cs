namespace CanopyStudio.Core.Create;

// The mod folders opened in Create, most recent first, as
// StudioSettings.RecentProjects keeps them.
public static class RecentProjects
{
    public const int Limit = 8;

    // list with folder moved or added to the front, without repeats, at
    // most Limit long.
    public static IReadOnlyList<string> Add(IReadOnlyList<string>? list, string folder)
    {
        string full = Normal(folder);
        return [full, .. (list ?? []).Where(other => !Same(other, full)).Take(Limit - 1)];
    }

    public static IReadOnlyList<string> Remove(IReadOnlyList<string>? list, string folder) =>
        (list ?? []).Where(other => !Same(other, folder)).ToList();

    // Paths name one folder without a final separator, and on Windows
    // without case. Linux folders differing only in case are two folders.
    public static bool Same(string left, string right) =>
        string.Equals(Normal(left), Normal(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string Normal(string folder) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
}
