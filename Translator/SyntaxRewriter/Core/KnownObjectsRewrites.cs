using ConsoleApp1.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using Translator.SyntaxRewriter.PartialImplementations;
using ExpressionSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax;
using GenericNameSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.GenericNameSyntax;
using IdentifierNameSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax;
using ObjectCreationExpressionSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.ObjectCreationExpressionSyntax;

namespace Translator.TranslatorFiles.Translator;

public static class KnownObjectsRewrites
{
    /// <summary>
    /// Rewrites object creation using predefined translations.
    /// </summary>
    /// <param name="node"></param>
    /// <param name="rewriter"></param>
    /// <returns>Null if no predefined translation is found</returns>
    public static ExpressionSyntax RewriteObjectCreationExpression(ObjectCreationExpressionSyntax node, Rewriter rewriter)
    {
        var typeName = node.Type switch
        {
            IdentifierNameSyntax identifierName => identifierName.Identifier.Text,
            GenericNameSyntax genericName => genericName.Identifier.Text,
            _ => null
        };

        if (typeName == nameof(DateTime))
        {
            var arguments = node.ArgumentList!.Arguments.Select(e => e.ToString().PadLeft(2, '0')).ToList();
            for (var i = arguments.Count; i < 7; i++) arguments.Add("00");
            var a =
                $"new Date('{arguments[0]:0000}-{arguments[1]:00}-{arguments[2]:00}T{arguments[3]:00}:{arguments[4]:00}:{arguments[5]:00}.{arguments[6]:00}')";
            return SyntaxFactory.ParseExpression(a);
        }

        if (typeName == "List")
        {
            var tsType = TypeTranslation.ParseType(node.Type, rewriter.SemanticModel);
            var newExpression = $"[] as {tsType}";

            // var needsBraces = !node.Parent.IsKind(SyntaxKind.EqualsValueClause);
            // if (needsBraces)
            //     newExpression = $"{newExpression}";

            return SyntaxFactory.ParseExpression(newExpression);
        }

        if (typeName == "Dictionary")
        {
            var tsType = TypeTranslation.ParseType(node.Type, rewriter.SemanticModel);
            var newExpression = $"{{}} as {tsType}";

            return SyntaxFactory.ParseExpression(newExpression);
        }

        return null;
    }
}