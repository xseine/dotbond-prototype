using System.Globalization;
using Translator.IntegratedQueryRuntime;

namespace Translator.Workspace.IQLogger;

/// <summary>
/// This one checks if the query file has changed since last translation.
/// </summary>
public static class IqLogger
{
    private static readonly string _logFilePath;

    static IqLogger()
    {
        _logFilePath = Path.Combine(Path.GetTempPath(), "BondLogs", "IqLog.txt");
        if (!File.Exists(_logFilePath))
        {
            Directory.CreateDirectory(Directory.GetParent(_logFilePath).FullName);
            File.WriteAllText(_logFilePath, DescriptionText);
        }
    }
    
    /// <summary>
    /// Writes current time as time when custom query file was processed.
    /// </summary>
    public static void LogTime()
    {
        File.WriteAllText(_logFilePath, DescriptionText);
        var time = DateTime.Now.ToUniversalTime().ToString(TranslationLogger.TranslationRecord.DateTimeFormat);
        File.AppendAllLines(_logFilePath, new[] { time });
    }

    /// <summary>
    /// Has custom query file been changed since the last time it was processed.
    /// </summary>
    public static bool IsOutOfDate()
    {
        if (!File.Exists(_logFilePath)) return true;
        
        var loggedValue = File.ReadAllLines(_logFilePath).FirstOrDefault(line => !line.StartsWith("//"));
        if (loggedValue == null) return true;

        var loggedTime = DateTime.ParseExact(loggedValue, TranslationLogger.TranslationRecord.DateTimeFormat, CultureInfo.InvariantCulture);
        return new FileInfo(EndpointGenInitializer.TsDefinitionsFile).LastWriteTimeUtc - loggedTime > TimeSpan.FromSeconds(1);
    }
    
    private static readonly string DescriptionText = @$"//------------------------------------------------------------------------------
// <description>
//     Logs the time of the last custom query file was processed.
// </description>
//------------------------------------------------------------------------------
";
}