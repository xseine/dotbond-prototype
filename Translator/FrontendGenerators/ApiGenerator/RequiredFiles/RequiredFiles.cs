using Translator.Misc;

namespace Translator.Generators;

public static class RequiredFiles
{
    public static void CreateRequiredFiles(string outputFolder)
    {
        // Compose output directories' path
        var libraryOutput = Path.Combine(outputFolder, "actions", "library");
        var decoratorsOutput = Path.Combine(outputFolder, "actions", "decorators");
        
        var otherDecoratorsPath = Path.Combine(decoratorsOutput, "other-decorators.ts");
        var otherDecoratorsExistingContent = File.Exists(otherDecoratorsPath) ? File.ReadAllText(otherDecoratorsPath) : null;
        
        // Delete directories if they already exist
        if (Directory.Exists(libraryOutput)) Directory.Delete(libraryOutput, true);
        if (Directory.Exists(decoratorsOutput)) Directory.Delete(decoratorsOutput, true);

        // Copy files from publish path
        CopyDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ts-files", "library"), libraryOutput, true);
        CopyDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ts-files", "decorators"), decoratorsOutput, true);

        if (!string.IsNullOrWhiteSpace(otherDecoratorsExistingContent))
            File.WriteAllText(otherDecoratorsPath, otherDecoratorsExistingContent);
    }

    static void CopyDirectory(string sourceDir, string destinationDir, bool recursive)
    {
        // Get information about the source directory
        var dir = new DirectoryInfo(sourceDir);

        // Check if the source directory exists
        if (!dir.Exists)
            throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

        // Cache directories before we start copying
        DirectoryInfo[] dirs = dir.GetDirectories();

        // Create the destination directory
        Directory.CreateDirectory(destinationDir);

        // Get the files in the source directory and copy to the destination directory
        foreach (FileInfo file in dir.GetFiles())
        {
            string targetFilePath = Path.Combine(destinationDir, file.Name);
            file.CopyTo(targetFilePath);
        }

        // If recursive and copying subdirectories, recursively call this method
        if (recursive)
        {
            foreach (DirectoryInfo subDir in dirs)
            {
                string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                CopyDirectory(subDir.FullName, newDestinationDir, true);
            }
        }
    }
}