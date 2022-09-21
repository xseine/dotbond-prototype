using System.Globalization;
using DotBond.Misc;

namespace DotBond.Workspace;

/// <summary>
/// Provides methods for recording a translation, and getting previously recorded translations.
/// Translation recording is used to:
/// - let the translator know which translations are out of date when it starts running
/// - delete translation files when their source is deleted
/// </summary>
public static class DependencyLogger
{
    
    private const string DependencySeparator = " | ";

    private static string _logFilePath;
    private static string LogFilePath => _logFilePath ??= Path.Combine(LoggingUtilities.GetLogFolder(), "DependencyLog.txt");

    public static void LogDependencies(IEnumerable<UsedTypeSymbolLocation> allDependencies)
    {
        var content = allDependencies.Select(e => $"{ConvertLocationToString(e.Location)}{DependencySeparator}{(e.UsingTypeLocation != null ? ConvertLocationToString((TypeSymbolLocation)e.UsingTypeLocation) : null)}");
        File.AppendAllLines(LogFilePath, content);
    }

    /// <summary>
    /// 
    /// </summary>
    public static List<UsedTypeSymbolLocation> GetDependencyLogs()
    {
        var allLines = GetRecords();
        var records = allLines
            .Where(line => !line.StartsWith("//") && !string.IsNullOrWhiteSpace(line))
            .Select(e => e.Split(DependencySeparator))
            .Select(e => new UsedTypeSymbolLocation(ConvertStringToLocation(e[0]), e[1] != "" ? ConvertStringToLocation(e[1]) : null))
            .ToList();

        return records;
    }

    private static string ConvertLocationToString(TypeSymbolLocation location) => $"{location.FilePath},{location.FullName}";
    private static TypeSymbolLocation ConvertStringToLocation(string location) => new (location.Split(",")[0], location.Split(",")[1]);
    
    private static List<string> GetRecords() => File.Exists(LogFilePath) ? File.ReadAllLines(LogFilePath).ToList() : new List<string>();
    
}