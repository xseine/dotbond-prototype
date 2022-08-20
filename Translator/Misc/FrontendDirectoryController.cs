using System.Text.RegularExpressions;
using Translator.Workspace;

namespace Translator.Misc;

/// <summary>
/// Provides methods for file operations in a TypeScript environment
/// - Writing files in the correct root directory
/// - Generating and writing import statements in TypeScript
/// </summary>
public static class FrontendDirectoryController
{
    private static string _backendFilesRoot;

    /// <summary>
    /// Output directory for generated files. Provided as relative path from the project root.
    /// </summary>
    private static string _frontendFilesRoot;
    private static NameCase _fileNameCase = NameCase.KebabCase;
    private static NameCase _folderNameCase = NameCase.KebabCase;

    public static void Setup(string backendFilesRoot, string frontendFilesRoot)
    {
        _backendFilesRoot = backendFilesRoot;
        _frontendFilesRoot = frontendFilesRoot;
    }

    public static void Setup(string backendFilesRoot, string frontendFilesRoot, NameCase fileNameCase, NameCase folderNameCase)
    {
        _backendFilesRoot = backendFilesRoot;
        _frontendFilesRoot = frontendFilesRoot;
        _fileNameCase = fileNameCase;
        _folderNameCase = folderNameCase;
    }

    /*========================== Public API ==========================*/

    /// <summary>
    /// Writes translation to a file with mirrored path in Angular's directory.  
    /// </summary>
    public static void WriteToAMirroredPath(string absoluteSourcePath, string translatedContent)
    {
        var relativePath = Path.GetRelativePath(_backendFilesRoot, absoluteSourcePath)[..^2] + "ts";
        WriteToAngularDirectory(relativePath, translatedContent);
    }

    /// <summary>
    /// Writes to a provided path, ensuring the correct casing is used.
    /// </summary>
    public static void WriteToAngularDirectory(string relativeTargetPath, string content)
    {
        relativeTargetPath = CorrectSeparatorInPath(relativeTargetPath);
        var withCasingRulesApplied = ApplyCaseRules(relativeTargetPath);
        
        var absoluteTargetPath = Path.Combine(GetAngularRootPath(), withCasingRulesApplied);
        if (!File.Exists(absoluteTargetPath)) Directory.CreateDirectory(Directory.GetParent(absoluteTargetPath)!.FullName);
        File.WriteAllText(absoluteTargetPath, content);
    }
    
    /// <summary>
    /// Deletes translation file, and folder ancestors if empty.
    /// </summary>
    public static bool DeleteFileAndEmptyParents(string souceFilePath)
    {
        souceFilePath = GetAngularPathFromAbsolute(souceFilePath);

        if (!File.Exists(souceFilePath)) return false;
        File.Delete(souceFilePath);

        var parent = Directory.GetParent(souceFilePath);
        while (true)
        {
            if (parent!.GetFileSystemInfos().Length == 0)
            {
                Directory.Delete(parent.FullName);
                parent = parent.Parent;
                continue;
            }

            break;
        }

        return true;
    }


    /// <summary>
    /// Generates and attaches import statements at the beginning of the source file.
    /// </summary>
    public static void AddImportStatementsToFile(string sourceFilePath, List<(string symbolName, string symbolPath)> importedSymbols)
    {
        if (!importedSymbols.Any()) return;

        var targetFilePath = GetAngularPathFromAbsolute(sourceFilePath);
        var importText = GenerateImportStatementsForFile(targetFilePath, importedSymbols.Where(e => e.symbolPath != sourceFilePath).DistinctBy(e => e.symbolName));

        // Remove old imports
        var targetFileContent = File.ReadAllText(targetFilePath);
        var importsToRemove = new Regex(@"(.|\s)*?(?=export)").Match(targetFileContent).Value; // Regex.Replace was stalling for some reason
        if (!String.IsNullOrWhiteSpace(importsToRemove)) targetFileContent = targetFileContent.Replace(importsToRemove, "");

        // Add new
        File.WriteAllText(targetFilePath, importText + "\n\n" + targetFileContent);
    }

    public static string GenerateImportStatementsForFile(string targetFilePath, IEnumerable<(string symbolName, string symbolPath)> importedSymbols)
    {
        if (!importedSymbols.Any()) return null;

        var importStatements = importedSymbols.GroupBy(s => s.symbolPath).Select(group =>
            $"import {{{string.Join(", ", group.Select(e => e.symbolName))}}} from '{GetRelativePathUsingTsSyntax(Directory.GetParent(targetFilePath)!.FullName, GetAngularPathFromAbsolute(group.Key).EndsWith(".ts") ? GetAngularPathFromAbsolute(group.Key)[..^3] : GetAngularPathFromAbsolute(group.Key))}';");
        var importText = string.Join('\n', importStatements);

        return importText;
    }

    public static string DetermineAngularPath(string relativePath) => Path.Combine(GetAngularRootPath(), ApplyCaseRules(relativePath));
    
    public static bool DoesFileExist(string relativePath)
    {
        relativePath = CorrectSeparatorInPath(relativePath);
        var withCasingRulesApplied = ApplyCaseRules(relativePath);
        
        return File.Exists(Path.Combine(GetAngularRootPath(), withCasingRulesApplied)[..^2] + "ts");
    }

    public static string GetAngularRootPath() => Path.GetFullPath(Path.Combine(_backendFilesRoot, _frontendFilesRoot));
    
    /*========================== Private API ==========================*/

    private static string GetAngularPathFromAbsolute(string path)
    {
        var standardPath = Path.GetFullPath(path);

        return !standardPath.StartsWith(GetAngularRootPath()) ?
            Path.Combine(GetAngularRootPath(), ApplyCaseRules(Path.GetRelativePath(_backendFilesRoot, standardPath)[..^2] + "ts")) :
            standardPath;
    }

    private static string GetRelativePathUsingTsSyntax(string relativeTo, string path)
    {
        var relativePath = Path.GetRelativePath(relativeTo, path).Replace("\\", "/");
        return relativePath.StartsWith(".") ? relativePath : "./" + relativePath;
    }

    private static string ApplyCaseRules(string path) => Path.Combine(
        path.Split(Path.DirectorySeparatorChar)[..^1].Select(ApplyFolderCaseRule).Append(ApplyFileCaseRule(path.Split(Path.DirectorySeparatorChar).Last())).ToArray());

    private static string ApplyFileCaseRule(string source) => ApplyCaseRule(source, _fileNameCase);
    private static string ApplyFolderCaseRule(string source) => ApplyCaseRule(source, _folderNameCase);

    private static string ApplyCaseRule(string source, NameCase nameCase) => nameCase switch
    {
        NameCase.KebabCase => Regex.Replace(Regex.Replace(source, @"([a-z])([A-Z])", "$1-$2"), @"[\s_]+", "-").ToLower(),
        _ => throw new Exception("Unknown option for FolderNameCase. Use either: ")
    };


    

    private static string CorrectSeparatorInPath(string path) => path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
}