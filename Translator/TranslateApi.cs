using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Translator.Misc;
using Translator.SyntaxRewriter.PartialImplementations;
using Translator.Workspace;

namespace Translator
{
    public static class TranslateApi
    {
        public static (string Translation, IEnumerable<ITypeSymbol> ImportedSymbols, HashSet<string> UsedAttributes) Translate(ITypeSymbol typeSymbol, Compilation compilation)
        {
            var syntaxTree = typeSymbol.DeclaringSyntaxReferences.First().SyntaxTree;
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var typeDeclarationNode = syntaxTree.GetRoot().DescendantNodes()
                .First(e => e is ClassDeclarationSyntax @class && @class.Identifier.Text == typeSymbol.Name
                            || e is RecordDeclarationSyntax record && record.Identifier.Text == typeSymbol.Name
                            || e is EnumDeclarationSyntax @enum && @enum.Identifier.Text == typeSymbol.Name);

            var walker = new Rewriter(semanticModel);
            return RewriteNode(walker, typeDeclarationNode);
        }

        private static Compilation _demoCompilation;
        private static SyntaxTree _previousSyntaxTree;

        public static string TranslateDemo(string source)
        {
            var usings = @"
            using System;
            using System.IO;
            using System.Net;
            using System.Linq;
            using System.Text;
            using System.Text.RegularExpressions;
            using System.Collections.Generic;
";
            
            var syntaxTree = CSharpSyntaxTree.ParseText(usings + source);

            _demoCompilation = _demoCompilation?.RemoveSyntaxTrees(_previousSyntaxTree) ??
                               RoslynUtilities.CreateDemoCompilation();

            if (!syntaxTree.GetRoot().ChildNodes().Any(e => e is not UsingDirectiveSyntax and not ClassDeclarationSyntax))
            {
                var requiredStatement = SyntaxFactory.GlobalStatement(SyntaxFactory.ParseStatement("Console.WriteLine(\"required\");"));
                syntaxTree = syntaxTree.GetRoot().InsertNodesBefore(syntaxTree.GetRoot().ChildNodes().First(e => e is ClassDeclarationSyntax), new[] {requiredStatement}).SyntaxTree;
            }
            
            _demoCompilation = _demoCompilation.AddSyntaxTrees(syntaxTree);

            var a = _demoCompilation.GetDiagnostics().Where(e => e.Severity == DiagnosticSeverity.Error).ToList();

            _previousSyntaxTree = syntaxTree;

            var semanticModel = _demoCompilation.GetSemanticModel(syntaxTree);
            var walker = new Rewriter(semanticModel);

            return RewriteNode(walker, syntaxTree.GetRoot()).Translation.Replace("console.log(\"required\");", "");
        }

        /// <summary>
        /// Rewrite using Visit() method, and hoist up the class declarations to the top of the file.
        /// </summary>
        /// <param name="rewriter"></param>
        /// <param name="node"></param>
        /// <returns>String of the translation, symbols imported in the node, along with the list of attributes used in the node.</returns>
        private static (string Translation, IEnumerable<ITypeSymbol> ImportedSymbols, HashSet<string> UsedAttributes) RewriteNode(Rewriter rewriter, SyntaxNode node)
        {
            var parsedTree = rewriter.Visit(node);

            // Same as below
            if (parsedTree.ChildNodes().Any(e => e is NamespaceDeclarationSyntax or FileScopedNamespaceDeclarationSyntax) == false)
            {
                var classDeclarations = parsedTree.ChildNodes().OfType<ClassDeclarationSyntax>().ToList();

                if (classDeclarations.Any())
                {
                    parsedTree = parsedTree.RemoveNodes(classDeclarations, SyntaxRemoveOptions.KeepExteriorTrivia);
                    classDeclarations[^1] = classDeclarations[^1].WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

                    var lastUsingStatement = parsedTree.ChildNodes().OfType<UsingDirectiveSyntax>().FirstOrDefault();

                    parsedTree = lastUsingStatement != null
                        ? parsedTree.InsertNodesAfter(lastUsingStatement, classDeclarations)
                        : parsedTree.InsertNodesBefore(parsedTree.ChildNodes().First(), classDeclarations);
                }
            }

            return (parsedTree.ToFullString(), rewriter.ImportedSymbols, rewriter.Attributes);
        }


    }
}