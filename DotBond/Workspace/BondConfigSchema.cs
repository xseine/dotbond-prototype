using System.Text.Json;

namespace DotBond.Workspace;

public class BondConfigSchema
{
    public string FileNameCase { get; set; }
    public string FolderNameCase { get; set; }
    public string OutputFolder { get; set; }
    
    public static BondConfigSchema LoadFromFile(string path)
    {
        var config = new BondConfigSchema();

        var jsonConfig = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(path));

        if (!jsonConfig.ContainsKey("outputFolder")
            || !jsonConfig.ContainsKey("fileNameCase")
            || !jsonConfig.ContainsKey("folderNameCase")) throw new Exception("bond.json config is invalid file.");

        // OutputFolder
        config.OutputFolder = jsonConfig["outputFolder"].GetString();
        
        // FileNameCase
        config.FileNameCase = jsonConfig["fileNameCase"].GetString();

        // FolderNameCase
        config.FolderNameCase = jsonConfig["folderNameCase"].GetString();

        return config;
    }
}

public enum NameCase
{
    KebabCase
}