using System.Text.RegularExpressions;
using DotBond.Generators;

namespace DotBond.Misc;

public static class TypescriptDecorators
{
    public static string DecoratorsPath = FrontendDirectoryController.DetermineAngularPath(Path.Combine(ApiGenerator.MainApiDirectory, "decorators"));
    
    /// <summary>
    /// Adds decorator function to Typescript
    /// </summary>
    /// <param name="decorators"></param>
    public static void AddDecorators(params string[] decorators)
    {
        var filePath = Path.Combine(DecoratorsPath, "other-decorators.ts");
        var fileContent = File.Exists(filePath) ? File.ReadAllText(filePath) : "";

        var validationDecorators = new[] { "required", "emailAddress", "regex", "range", "stringLength", "url" };
        
        foreach (var decorator in decorators.Except(validationDecorators))
        {
            if (fileContent.Contains($"export function {decorator}")) continue;

            var decoratorFunctionText = @$"
export function {decorator}(parameters?: any): (target: object | Function, propertyName: string) => any {{
    return function(target: object | Function, propertyName: string) {{
        addAttribute(target, propertyName, '{decorator}', parameters);
    }}
}}
";

            fileContent = fileContent.Contains("type attributes = never;") ?
                fileContent.Replace("type attributes = never;", $"type attributes = '{decorator}';") :
                new Regex(@"type attributes = (.*);").Replace(fileContent, $"type attributes = $1 | '{decorator}';");

            fileContent += decoratorFunctionText;
        }
        
        FrontendDirectoryController.WriteToAngularDirectory(Path.Combine(ApiGenerator.MainApiDirectory, "decorators", "other-decorators.ts"), fileContent);
    }
}