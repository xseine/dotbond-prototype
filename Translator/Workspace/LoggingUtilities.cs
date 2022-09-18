namespace Translator.Workspace;

public static class LoggingUtilities
{
    public static string CsprojPath;
    private static bool _doesDirectoryExist;

    public static string GetLogFolder()
    {
        var logDirectory = Path.Combine(Directory.GetParent(CsprojPath).FullName, ".bondlogs");
        
        if (!_doesDirectoryExist && !Directory.Exists(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
            _doesDirectoryExist = true;
        }
        
        return logDirectory;
    }
    
}