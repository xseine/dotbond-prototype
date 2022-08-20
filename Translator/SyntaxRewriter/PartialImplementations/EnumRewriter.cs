using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Translator.SyntaxRewriter.PartialImplementations;

public partial class Rewriter
{
    public override SyntaxNode VisitEnumDeclaration(EnumDeclarationSyntax node)
    {
        var newAccessToken = CreateToken(SyntaxKind.PublicKeyword, node.GetLeadingTrivia() + "export ");
        return node.WithModifiers(SyntaxFactory.TokenList(newAccessToken));
    }
}