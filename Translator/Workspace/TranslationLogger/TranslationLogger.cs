using System.Globalization;

namespace Translator.Workspace;

/// <summary>
/// Provides methods for recording a translation, and getting previously recorded translations.
/// Translation recording is used to:
/// - let the translator know which translations are out of date when it starts running
/// - delete translation files when their source is deleted
/// </summary>
public static class TranslationLogger
{
    public record TranslationRecord(string TranslationId, IEnumerable<string> TranslatedTypes, DateTime TranslationDateTime)
    {
        public override string ToString()
        {
            return $"{TranslationId}, {String.Join(TypesSeparator, TranslatedTypes)}, {TranslationDateTime.ToUniversalTime().ToString(DateTimeFormat)}";
        }

        public static readonly string DateTimeFormat = "dd-MM-yy HH:mm:ss";
        public static DateTime ParseDate(string dateTime) => DateTime.ParseExact(dateTime, DateTimeFormat, CultureInfo.InvariantCulture);
    }

    private const char TypesSeparator = '|';
    
    private static readonly string _logFilePath;

    static TranslationLogger()
    {
        _logFilePath = Path.Combine(Path.GetTempPath(), "BondLogs", "TranslationLog.txt");
        if (!File.Exists(_logFilePath))
        {
            Directory.CreateDirectory(Directory.GetParent(_logFilePath).FullName);
            File.WriteAllText(_logFilePath, DescriptionText);
        }
    }

    /// <summary>
    /// Adds, or updates, an entry to TranslationLog.txt
    /// </summary>
    /// <param name="projectRelativeFilepath"></param>
    /// <param name="translatedTypes">List of translated types' names from the file.</param>
    public static void LogTranslation(string projectRelativeFilepath, IEnumerable<string> translatedTypes)
    {
        var allLines = GetRecords();
        var existingEntryIdx = FindLindeIdx(allLines, projectRelativeFilepath);

        var newEntry = new TranslationRecord(projectRelativeFilepath, translatedTypes, DateTime.Now);

        if (existingEntryIdx != -1)
        {
            allLines[existingEntryIdx] = newEntry.ToString();
            File.WriteAllLines(_logFilePath, allLines);
        }
        else
            File.AppendAllLines(_logFilePath, new []{ newEntry.ToString() });
        
    }

    /// <summary>
    /// Retrieves records of previous translations.
    /// </summary>
    public static List<TranslationRecord> GetTranslationLogs()
    {
        var allLines = GetRecords();
        var records = allLines
            .Where(line => !line.StartsWith("//") && !string.IsNullOrWhiteSpace(line))
            .Select(e => e.Split(", "))
            .Select(e => new TranslationRecord(e[0], e[1].Split(TypesSeparator).ToList(), TranslationRecord.ParseDate(e[2])))
            .ToList();

        return records;
    }

    public static void DeleteTranslationRecord(string translationId)
    {
        var allLines = GetRecords();
        var entryIdx = FindLindeIdx(allLines, translationId);

        allLines = allLines.Where((_, idx) => idx != entryIdx).ToList();
        
        File.WriteAllLines(_logFilePath, allLines);
    }
    
    private static List<string> GetRecords() => File.ReadAllLines(_logFilePath).ToList();
    
    private static int FindLindeIdx(List<string> lines, string translationId) => 
        lines.FindIndex(line => !line.StartsWith("//") && !string.IsNullOrWhiteSpace(line) && line.Split(", ")[0] == translationId);

    private static readonly string DescriptionText = @"//------------------------------------------------------------------------------
// <description>
//     Contains a list of translation finished by DotBond.
//     Each entry contains data about the source that was translated, its destination, and the time of the translation.
//     This enables the translation runtime to know what translations are stale,
//     when source file is deleted, from where to delete the translated content, etc. 
// </description>
//------------------------------------------------------------------------------
";
}