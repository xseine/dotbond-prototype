using System.Globalization;
using Translator.Misc;

namespace Translator.Workspace;

/// <summary>
/// Provides methods for recording a translation, and getting previously recorded translations.
/// Translation recording is used to:
/// - let the translator know which translations are out of date when it starts running
/// - delete translation files when their source is deleted
/// </summary>
public static class DependencyLogger
{
    
    private const string DependencySeparator = " | ";
    
    private static readonly string _logFilePath;

    static DependencyLogger()
    {
        _logFilePath = Path.Combine(Path.GetTempPath(), "BondLogs", "DependencyLog.txt");
        if (!File.Exists(_logFilePath))
        {
            Directory.CreateDirectory(Directory.GetParent(_logFilePath).FullName);
            File.WriteAllText(_logFilePath, DescriptionText);
        }
    }
    
    public static void LogDependencies(IEnumerable<UsedTypeSymbolLocation> allDependencies)
    {
        var content = allDependencies.Select(e => $"{ConvertLocationToString(e.Location)}{DependencySeparator}{(e.UsingTypeLocation != null ? ConvertLocationToString((TypeSymbolLocation)e.UsingTypeLocation) : null)}");
        File.WriteAllText(_logFilePath, DescriptionText);
        File.AppendAllLines(_logFilePath, content);
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
    
    private static List<string> GetRecords() => File.ReadAllLines(_logFilePath).ToList();
    

    private static readonly string DescriptionText = @$"//------------------------------------------------------------------------------
// <description>
//     Contains a list dependencies between types.
//     Type on the left is used by type on the right of the ""{DependencySeparator}"".
//     This file is changed everytime dependencies are updated,
//     and translation runtime uses it observe for changes in these types
//     without the full processing of up-to-date translations.
// </description>
//------------------------------------------------------------------------------
";
}