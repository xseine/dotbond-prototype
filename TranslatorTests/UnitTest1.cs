using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Jering.Javascript.NodeJS;
using Microsoft.AspNetCore.NodeServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Translator.IntegratedQueryRuntime;

namespace TranslatorTests;

public class Tests
{
    private static string[] _sources = Directory.GetFiles("TestSources").ToArray();
    private string _jsImports = File.ReadAllText("utilities.js") + "\n" + File.ReadAllText("date-format.js");
    
    private int _cnt;

    [SetUp]
    public void Setup()
    {
        _cnt = 1;
    }

    [Test]
    [TestCaseSource(nameof(_sources))]
    [Parallelizable(ParallelScope.None)]
    public async Task TestTranslation(string sourceFile)
    {
        var source = await File.ReadAllTextAsync(sourceFile);
        var tsSource = Translator.TranslateApi.TranslateDemo(source);

        var fileName = _cnt++ +".ts";
        var translationFilePath = Path.Combine(Path.GetTempPath(), "TranslatorTests", fileName);
        if (!Directory.GetParent(translationFilePath).Exists) Directory.CreateDirectory(Directory.GetParent(translationFilePath).FullName);
        
        await File.WriteAllTextAsync(translationFilePath, tsSource);

        var command = @$"""C:\Program Files (x86)\Microsoft SDKs\TypeScript\4.4\tsc.js"" ""{translationFilePath}""  --module es2015 --target es2017";
        var process = Process.Start("node", command);
        await process.WaitForExitAsync();
        
        var jsSource = await File.ReadAllTextAsync(translationFilePath[..^2] + "js");
        
        var classRx = new Regex(@$"(export )?class \w+ {TsRegexRepository.MatchBrackets(BracketType.CurlyBrackets)}");
        var matchedDefinitions = classRx.Matches(jsSource).Select(e => e.Value).ToList();
        foreach (var definition in matchedDefinitions)
            jsSource = jsSource.Replace(definition, "");
        
        var namespaceRx = new Regex(@$"\(function \(.*?\) {TsRegexRepository.MatchBrackets(BracketType.CurlyBrackets)}\)\([^)]+\)\)");
        matchedDefinitions.AddRange(namespaceRx.Matches(jsSource).Select(e => e.Value));
        foreach (var definition in matchedDefinitions)
            jsSource = jsSource.Replace(definition, "");

        jsSource = string.Join("\n", matchedDefinitions) + "\n" + jsSource;
        jsSource = Regex.Replace(jsSource, @"export (?=class|function|const|let|enum|var)", "");
        
        var result = await StaticNodeJSService.InvokeFromStringAsync<string>(@$"
module.exports = (callback, x, y) => {{
    // Your javascript logic
    {_jsImports}
    let __result = null;
    {jsSource}
    callback(null, __result);
}}
        ");

        File.Delete(translationFilePath);
        File.Delete(translationFilePath[..^2] + "js");
        Console.WriteLine(result);
    }
}