using System.Globalization;
using DotBond.IntegratedQueryRuntime;

namespace DotBond.Workspace.IQLogger;

/// <summary>
/// This one checks if the query file has changed since last translation.
/// </summary>
public static class IqLogger
{
    private static string _logFilePath;
    private static string LogFilePath => _logFilePath ??= Path.Combine(LoggingUtilities.GetLogFolder(), "IqLog.txt");

    /// <summary>
    /// Writes current time as time when custom query file was processed.
    /// </summary>
    public static void LogTime()
    {
        var time = DateTime.Now.ToUniversalTime().ToString(TranslationLogger.TranslationRecord.DateTimeFormat);
        File.AppendAllLines(LogFilePath, new[] { time });
    }

    /// <summary>
    /// Has custom query file been changed since the last time it was processed.
    /// </summary>
    public static bool IsOutOfDate()
    {
        if (!File.Exists(LogFilePath)) return true;
        
        var loggedValue = (File.Exists(LogFilePath) ? File.ReadAllLines(LogFilePath) : Array.Empty<string>()).FirstOrDefault(line => !line.StartsWith("//"));
        if (loggedValue == null) return true;

        var loggedTime = DateTime.ParseExact(loggedValue, TranslationLogger.TranslationRecord.DateTimeFormat, CultureInfo.InvariantCulture);
        return new FileInfo(EndpointGenInitializer.TsDefinitionsFile).LastWriteTimeUtc - loggedTime > TimeSpan.FromSeconds(1);
    }
    
}