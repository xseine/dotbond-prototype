using System.Text.Json;

namespace DotBond.Workspace;

public class BondConfigSchema
{
    public string FileNameCase { get; set; }
    public string FolderNameCase { get; set; }
    public string OutputFolder { get; set; }
    
    public static BondConfigSchema DeriveFromProjectFiles(string root)
    {
        var slnRoot = new DirectoryInfo(root) is var dir && dir.GetFiles("*.sln").Any() ? dir : dir.Parent;
        var angularRoot = slnRoot.GetFiles("angular.json", SearchOption.AllDirectories).FirstOrDefault()?.Directory.FullName;
        if (angularRoot == null) throw new Exception("Could not find angular root. Tried finding angular.json from the: " + slnRoot.FullName);

        var appFolder = Path.Combine(angularRoot, "src", "app");
        if (Directory.Exists(appFolder) == false)  throw new Exception("src/app is missing from Angular root: " + angularRoot);

        return new BondConfigSchema()
        {
            FileNameCase = "kebab-case",    // only one supported currently
            FolderNameCase = "kebab-case",  // only one supported currently
            OutputFolder = Path.Combine(appFolder, "api")
        };
    }
}

public enum NameCase
{
    KebabCase
}