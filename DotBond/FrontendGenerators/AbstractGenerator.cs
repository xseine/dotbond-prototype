using System.Collections;
using System.Text.RegularExpressions;
using DotBond.Misc;
using DotBond.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBond.Generators;

/// <summary>
/// Note: Program.cs does all of the explaining to unerstand the big picture of the generators.
/// Class providing abstract methods for Roslyn analysis of files.
/// Two methods are used for analysis:
/// 
/// - "GetControllerCallback" is the callback that is applied to all files from the FileObserver
/// - "GetDefinitionFileCallback" is the callback that is applied only to files containing definitions of symbols returned from "GetControllerCallback"
///
/// These callbacks return symbols.
/// Those returned by first one are used to find definition files to observe.
/// And, those returned by the second one are translated automatically.
/// Any of these methods can have side effects and generate code on their own.
/// </summary>
public abstract class AbstractGenerator
{
    protected readonly string AssemblyName;
    
    protected AbstractGenerator(string assemblyName)
    {
        AssemblyName = assemblyName;
    }
    
    /*========================== Public API ==========================*/

    /// <summary>
    /// Analyses controller file for specific elements.
    /// </summary>
    public abstract HashSet<TypeSymbolLocation> GetControllerCallback(FileAnalysisCallbackInput input);

    /// <summary>
    /// Regenerate when a file is deleted.
    /// </summary>
    /// <param name="filePath">Path of the deleted file.</param>
    /// <returns>List of types it currently uses. If deletion has no impact, it returns null.</returns>
    public abstract HashSet<TypeSymbolLocation> DeleteSource(string filePath);
    
    /*========================== Private API ==========================*/
    
    protected bool IsReferenceTypeForTranslation(ITypeSymbol symbol) => symbol is not IErrorTypeSymbol && !symbol.IsValueType && symbol.ContainingSymbol.Name != "String" && symbol.ContainingAssembly.Name == AssemblyName;

    protected static string RemoveNamespace(string type) => new Regex(@"(?:\w+\.)+(\w+)").Replace(type, "$1");
}