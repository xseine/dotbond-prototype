﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Translator.SyntaxRewriter.Core;
using IPropertySymbol = Microsoft.CodeAnalysis.IPropertySymbol;
using ITypeSymbol = Microsoft.CodeAnalysis.ITypeSymbol;
using SymbolKind = Microsoft.CodeAnalysis.SymbolKind;
using SyntaxNode = Microsoft.CodeAnalysis.SyntaxNode;

namespace Translator.SyntaxRewriter.PartialImplementations;

public partial class Rewriter : AbstractRewriterWithSemantics
{
    protected ISymbol GetSymbol(SyntaxNode node)
    {
        return SemanticModel.SyntaxTree.GetRoot().Contains(node) ? SemanticModel.GetSymbolInfo(node).Symbol : null;
    }
    
    public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        return VisitAccessExpression(node);
    }

    public override SyntaxNode VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
    {
        return VisitAccessExpression(node);
    }

    // Predfined translations
    // + server-side method evaluation
    // + registering types for ensuing translation
    private SyntaxNode VisitAccessExpression(SyntaxNode node)
    {
        var isConditionalAccess = node.IsKind(SyntaxKind.ConditionalAccessExpression);
        var overrideVisit = !isConditionalAccess ?
            base.VisitMemberAccessExpression((MemberAccessExpressionSyntax)node) :
            base.VisitConditionalAccessExpression((ConditionalAccessExpressionSyntax)node);
        if (overrideVisit == null) return null; // TODO: is this needed?

        overrideVisit = overrideVisit.WithTrailingTrivia(node.GetTrailingTrivia());
        
        var symbol = SemanticModel.SyntaxTree.GetRoot().Contains(node) ?
            !isConditionalAccess ? SemanticModel.GetSymbolInfo(node).Symbol : SemanticModel.GetSymbolInfo(((ConditionalAccessExpressionSyntax)node).WhenNotNull).Symbol :
            null;
        if (symbol == null) return overrideVisit;

        var isLocalVariableToImportTypeOf =
            SemanticModel.GetSymbolInfo(node is MemberAccessExpressionSyntax member ? member.Expression : ((ConditionalAccessExpressionSyntax)node).Expression).Symbol?.Kind ==
            SymbolKind.Local;

        // If property, record the type and return
        if (symbol is IPropertySymbol propertySymbol)
        {
            if (isLocalVariableToImportTypeOf)
                ImportedSymbols.Add(propertySymbol.ContainingSymbol as ITypeSymbol);
            else if (propertySymbol.IsStatic)
                return TranslateStaticProperty(propertySymbol) ?? throw new Exception($"No translation provided for the {propertySymbol}. This part of the source must be removed.");

            // return overrideVisit;
        }


        var containingNamespace = symbol.ContainingType.ContainingNamespace.ToString();
        var methodName = node switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.ToString(),
            ConditionalAccessExpressionSyntax
            {
                WhenNotNull: InvocationExpressionSyntax { Expression: MemberBindingExpressionSyntax memberBinding }
            } => memberBinding.Name.ToString(),
            _ => throw new ArgumentOutOfRangeException(nameof(node), node, null)
        };

        // ? ((MemberAccessExpressionSyntax)node).Name.ToString()
        // : ((InvocationExpressionSyntax)((ConditionalAccessExpressionSyntax)node).WhenNotNull).Expression

        // New name for the method to, e.g. "toString"
        string newMethodName = null;
        
        switch (containingNamespace)
        {
            // List methods
            case "System.Collections.Generic":
            case "System.Linq":
            {
                switch (methodName)
                {
                    case "ForEach":
                    {
                        newMethodName = "forEach";
                        break;
                    }

                    case nameof(Enumerable.Select):
                    {
                        newMethodName = "map";
                        break;
                    }

                    case nameof(Enumerable.SelectMany):
                    {
                        newMethodName = "flatMap";
                        break;
                    }

                    case nameof(Enumerable.Where):
                    case nameof(List<object>.FindAll):
                    {
                        newMethodName = "filter";
                        break;
                    }

                    case nameof(Enumerable.Count):
                    {
                        newMethodName = "length";
                        if (node.Parent is InvocationExpressionSyntax)
                            RegisterAncestorRewrite(syntaxNode => ((InvocationExpressionSyntax)syntaxNode)
                                .WithArgumentList(_emptyArgumentsList.WithTrailingTrivia(((InvocationExpressionSyntax)syntaxNode).GetTrailingTrivia())), SyntaxKind.InvocationExpression);
                        break;
                    }

                    case nameof(Enumerable.Contains):
                    {
                        newMethodName = "includes";
                        break;
                    }

                    case nameof(Enumerable.First):
                    case nameof(Enumerable.FirstOrDefault):
                    case nameof(List<object>.Find):
                    {
                        newMethodName = "find";
                        break;
                    }

                    case nameof(Enumerable.Last):
                    case nameof(Enumerable.LastOrDefault):
                    case nameof(List<object>.FindLast):
                    {
                        newMethodName = "slice().reverse().find";
                        break;
                    }

                    case nameof(List<object>.FindIndex):
                    {
                        newMethodName = "findIndex";
                        break;
                    }

                    case nameof(List<object>.FindLastIndex):
                    {
                        newMethodName = "slice().reverse().findIndex";
                        break;
                    }

                    case nameof(Enumerable.Aggregate):
                    {
                        newMethodName = "reduce"; // Note: Parent InvocationExpression has already reversed the arguments

                        if (node is MemberAccessExpressionSyntax)
                            RegisterAncestorRewrite(syntaxNode =>
                            {
                                var reversedArguments = SyntaxFactory.SeparatedList(((InvocationExpressionSyntax)syntaxNode).ArgumentList.Arguments.Reverse());

                                return ((InvocationExpressionSyntax)syntaxNode)?.WithArgumentList(SyntaxFactory.ArgumentList(reversedArguments));
                            }, SyntaxKind.InvocationExpression);
                        else if (node is ConditionalAccessExpressionSyntax { WhenNotNull: InvocationExpressionSyntax invocation } conditional)
                        {
                            var reversedArguments = SyntaxFactory.SeparatedList(invocation.ArgumentList.Arguments.Reverse());
                            overrideVisit = conditional.WithWhenNotNull(invocation.WithArgumentList(SyntaxFactory.ArgumentList(reversedArguments)));
                        }


                        break;
                    }

                    case nameof(Enumerable.Sum):
                    {
                        newMethodName = "reduce((acc, curr) => acc + curr)"; // Note: Parent InvocationExpression will add "map" transformation, and provide the callback function
                        RegisterAncestorRewrite(syntaxNode =>
                        {
                            var invocationExpression = (InvocationExpressionSyntax)syntaxNode;
                            var memberAccessExpression = (MemberAccessExpressionSyntax)invocationExpression.Expression;
                            memberAccessExpression = memberAccessExpression.WithExpression(
                                SyntaxFactory.InvocationExpression(
                                    SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, memberAccessExpression.Expression, SyntaxFactory.IdentifierName("map")),
                                    invocationExpression.ArgumentList));

                            invocationExpression = invocationExpression.WithExpression(memberAccessExpression)
                                .WithArgumentList(SyntaxFactory.ArgumentList().WithOpenParenToken(CreateToken(SyntaxKind.OpenParenToken)).WithCloseParenToken(CreateToken(SyntaxKind.CloseParenToken)));

                            return invocationExpression;
                        }, SyntaxKind.InvocationExpression);

                        break;
                    }

                    case nameof(Enumerable.Take):
                    {
                        var argOfSkipBeforeTake = node switch
                        {
                            ConditionalAccessExpressionSyntax { Expression: InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax x1 } x2 }
                                when x1.Name.ToString() == nameof(Enumerable.Skip) => int.Parse(x2.ArgumentList.Arguments.First().Expression.ToString()),
                            MemberAccessExpressionSyntax { Expression: InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax x1 } x2 }
                                when x1.Name.ToString() == nameof(Enumerable.Skip) => int.Parse(x2.ArgumentList.Arguments.First().Expression.ToString()),
                            _ => null as int?
                        };

                        var takeArg = node switch
                        {
                            ConditionalAccessExpressionSyntax { Parent: InvocationExpressionSyntax x1 } => int.Parse(x1.ArgumentList.Arguments.First().Expression.ToString()),
                            MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax x1 } => int.Parse(x1.ArgumentList.Arguments.First().Expression.ToString())
                        };
                        
                        newMethodName = argOfSkipBeforeTake != null ? $"slice({argOfSkipBeforeTake}, {argOfSkipBeforeTake + takeArg})" : $"slice(0, {takeArg})";

                        if (argOfSkipBeforeTake != null)
                        {
                            var emptyName = SyntaxFactory.IdentifierName("");
                            var emptyToken = SyntaxFactory.MissingToken(SyntaxKind.DotToken);
                            
                            overrideVisit = overrideVisit switch
                            {
                                ConditionalAccessExpressionSyntax { Expression: InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax x1 } x2 } x3
                                    => x3.WithExpression(x2.WithArgumentList(_emptyArgumentsList).WithExpression(x1.WithName(emptyName).WithOperatorToken(emptyToken))),
                                MemberAccessExpressionSyntax { Expression: InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax x1 } x2 } x3
                                    => x3.WithExpression(x2.WithArgumentList(_emptyArgumentsList).WithExpression(x1.WithName(emptyName).WithOperatorToken(emptyToken)))
                            };
                        }

                        var isStringSource = ((IMethodSymbol)symbol).TypeArguments.First() is { Name: "Char" };
                        if (isStringSource && argOfSkipBeforeTake == null)
                        {
                            var leadingArrayTrivia = SyntaxFactory.Whitespace("[...");
                            var trailingArrayTrivia = SyntaxFactory.Whitespace("]");
                            
                            overrideVisit = overrideVisit switch
                            {
                                ConditionalAccessExpressionSyntax { Expression: { } x1 } x2
                                    => x2.WithExpression(x1.WithLeadingTrivia(x1.GetLeadingTrivia().Append(leadingArrayTrivia))
                                        .WithTrailingTrivia(x1.GetTrailingTrivia().Prepend(trailingArrayTrivia))),
                                MemberAccessExpressionSyntax { Expression: { } x1 } x2
                                    => x2.WithExpression(x1.WithLeadingTrivia(x1.GetLeadingTrivia().Append(leadingArrayTrivia))
                                        .WithTrailingTrivia(x1.GetTrailingTrivia().Prepend(trailingArrayTrivia))),
                            };
                        }

                        RegisterAncestorRewrite(syntaxNode => 
                            ((InvocationExpressionSyntax)syntaxNode).WithArgumentList(_emptyArgumentsList), SyntaxKind.InvocationExpression);
                        
                        break;
                    }

                    case nameof(Enumerable.Skip):
                    {
                        var skipArg = node switch
                        {
                            ConditionalAccessExpressionSyntax { Parent: InvocationExpressionSyntax x1 } => x1.ArgumentList.Arguments.First().Expression.ToString(),
                            MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax x1 } => x1.ArgumentList.Arguments.First().Expression.ToString(),
                            _ => null
                        };

                        newMethodName = $"slice({skipArg})";

                        var isStringSource = ((IMethodSymbol)symbol).TypeArguments.First() is { Name: "Char" };
                        if (isStringSource)
                        {
                            var leadingArrayTrivia = SyntaxFactory.Whitespace("[...");
                            var trailingArrayTrivia = SyntaxFactory.Whitespace("]");

                            overrideVisit = overrideVisit switch
                            {
                                ConditionalAccessExpressionSyntax { Expression: { } x1 } x2
                                    => x2.WithExpression(x1.WithLeadingTrivia(x1.GetLeadingTrivia().Append(leadingArrayTrivia))
                                        .WithTrailingTrivia(x1.GetTrailingTrivia().Prepend(trailingArrayTrivia))),
                                MemberAccessExpressionSyntax { Expression: { } x1 } x2
                                    => x2.WithExpression(x1.WithLeadingTrivia(x1.GetLeadingTrivia().Append(leadingArrayTrivia))
                                        .WithTrailingTrivia(x1.GetTrailingTrivia().Prepend(trailingArrayTrivia)))
                            };
                        }
                        
                        var emptyArgumentsList = SyntaxFactory.ArgumentList(
                            SyntaxFactory.MissingToken(SyntaxKind.OpenParenToken),
                            default,
                            SyntaxFactory.MissingToken(SyntaxKind.CloseParenToken));

                        RegisterAncestorRewrite(syntaxNode =>
                            ((InvocationExpressionSyntax)syntaxNode).WithArgumentList(emptyArgumentsList), SyntaxKind.InvocationExpression);
                        
                        break;
                    }

                    case nameof(Enumerable.OrderBy):
                    case nameof(Enumerable.OrderByDescending):
                    {
                        var orderKey = GetOrderKey(node);
                        orderKey = orderKey[0].ToString().ToLower() + orderKey[1..];
                        newMethodName = methodName == nameof(Enumerable.OrderBy) ? $"sort((a, b) => a.{orderKey} - b.{orderKey})" : $"sort((a, b) => b.{orderKey} - a.{orderKey})";
                        RegisterAncestorRewrite(syntaxNode =>
                            ((InvocationExpressionSyntax)syntaxNode).WithArgumentList(_emptyArgumentsList), SyntaxKind.InvocationExpression);
                        break;
                    }
                    
                }


                goto default;
            }

            case "System":
            {
                switch (methodName)
                {
                    case "Write":
                    case "WriteLine":
                    {
                        if (((MemberAccessExpressionSyntax)node).Expression.ToString() == "Console")
                        {
                            newMethodName = "log";
                            overrideVisit = ((MemberAccessExpressionSyntax)overrideVisit).WithExpression(SyntaxFactory.IdentifierName("console")).WithLeadingTrivia(node.GetLeadingTrivia());
                        }

                        break;
                    }

                    case "ToString":
                    {
                        newMethodName = symbol.ContainingType.Name switch
                        {
                            nameof(DateTime) => "format",
                            _ => "toString"
                        };
                        break;
                    }

                    case "Join":
                    {
                        var isStringJoin = symbol.ContainingType.Name == "String";
                        if (isStringJoin)
                            RegisterAncestorRewrite(
                                syntaxNode =>
                                {
                                    var arguments = ((InvocationExpressionSyntax)syntaxNode).ArgumentList.Arguments;
                                    var array = arguments.Count > 2 ? $"[{string.Join(", ", arguments.Skip(1).Select(arg => arg.Expression.ToFullString()))}]" : arguments[1].Expression.ToFullString();
                                    return CreateNode($"{array}.join({arguments[0].Expression})");
                                },
                                SyntaxKind.InvocationExpression);
                        break;
                    }

                    case "Parse":
                    {
                        var targetType = symbol.ContainingType.Name;

                        if (new[] { nameof(Byte), nameof(Int16), nameof(Int32), nameof(Int64), nameof(Byte), nameof(UInt16), nameof(UInt32), nameof(UInt64), nameof(Double), nameof(Decimal) }
                            .Contains(targetType))
                        {
                            RegisterAncestorRewrite(
                                syntaxNode =>
                                {
                                    var arguments = ((InvocationExpressionSyntax)syntaxNode).ArgumentList.Arguments;
                                    return arguments[0].Expression.WithLeadingTrivia(SyntaxFactory.SyntaxTrivia(SyntaxKind.WhitespaceTrivia, " (+"))
                                        .WithTrailingTrivia(SyntaxFactory.SyntaxTrivia(SyntaxKind.WhitespaceTrivia, ")"));
                                },
                                SyntaxKind.InvocationExpression);
                        }
                        else if (nameof(Boolean) == targetType)
                        {
                            RegisterAncestorRewrite(syntaxNode =>
                                {
                                    var value = (((InvocationExpressionSyntax)syntaxNode).ArgumentList.Arguments[0].Expression as LiteralExpressionSyntax)!.Token.Text;
                                    return SyntaxFactory.LiteralExpression(value == "false" ? SyntaxKind.FalseLiteralExpression : SyntaxKind.TrueLiteralExpression);
                                },
                                SyntaxKind.InvocationExpression);
                        }
                        else if (nameof(DateTime) == targetType)
                        {
                            newMethodName = "";
                            overrideVisit = ((MemberAccessExpressionSyntax)overrideVisit).WithExpression(SyntaxFactory.IdentifierName("new Date"))
                                .WithOperatorToken(SyntaxFactory.Token(default, SyntaxKind.DotToken, "", "", default)).WithLeadingTrivia(node.GetLeadingTrivia());
                        }

                        break;
                    }

                    case "IsNullOrEmpty":
                    {
                        if (symbol.ContainingType.Name == "String")
                        {
                            RegisterAncestorRewrite(syntaxNode =>
                                {
                                    var argument = (syntaxNode as InvocationExpressionSyntax).ArgumentList.Arguments.First().Expression.ToString();
                                    return SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, CreateToken(SyntaxKind.StringLiteralToken, $"(!({argument}))"));
                                },
                                SyntaxKind.InvocationExpression);
                        }

                        break;
                    }

                    case "IsNullOrWhiteSpace":
                    {
                        if (symbol.ContainingType.Name == "String")
                        {
                            RegisterAncestorRewrite(syntaxNode =>
                                {
                                    var argument = (syntaxNode as InvocationExpressionSyntax).ArgumentList.Arguments.First().Expression.ToString();
                                    return SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, CreateToken(SyntaxKind.StringLiteralToken, $"(!({argument}).trim())"));
                                },
                                SyntaxKind.InvocationExpression);
                        }

                        break;
                    }

                    case nameof(string.ToLower):
                    case nameof(string.ToLowerInvariant):
                    {
                        newMethodName = "toLowerCase";
                        break;
                    }

                    case nameof(string.ToUpper):
                    case nameof(string.ToUpperInvariant):
                    {
                        newMethodName = "toLowerCase";
                        break;
                    }

                    case nameof(DateTime.Year):
                    {
                        newMethodName = "getFullYear()";
                        break;
                    }
                    case nameof(DateTime.Month):
                    {
                        newMethodName = "getMonth()";
                        break;
                    }
                    case nameof(DateTime.Day):
                    {
                        newMethodName = "getDay()";
                        break;
                    }
                    case nameof(DateTime.Hour):
                    {
                        newMethodName = "getHours()";
                        break;
                    }
                    case nameof(DateTime.Minute):
                    {
                        newMethodName = "getMinutes()";
                        break;
                    }
                    case nameof(DateTime.Second):
                    {
                        newMethodName = "getSeconds()";
                        break;
                    }
                    case nameof(DateTime.Millisecond):
                    {
                        newMethodName = "getMilliseconds()";
                        break;
                    }
                    case nameof(DateTime.AddYears):
                    {
                        newMethodName = "addYears";
                        break;
                    }
                    case nameof(DateTime.AddMonths):
                    {
                        newMethodName = "addMonths";
                        break;
                    }
                    case nameof(DateTime.AddDays):
                    {
                        newMethodName = "addDays";
                        break;
                    }
                    case nameof(DateTime.AddHours):
                    {
                        newMethodName = "addHours";
                        break;
                    }
                    case nameof(DateTime.AddMinutes):
                    {
                        newMethodName = "addMinutes";
                        break;
                    }
                    case nameof(DateTime.AddSeconds):
                    {
                        newMethodName = "addSeconds";
                        break;
                    }
                    case nameof(DateTime.AddMilliseconds):
                    {
                        newMethodName = "addMilliseconds";
                        break;
                    }
                    
                }

                goto default;
            }
            default:
            {
                if (newMethodName != null) break;

                switch (methodName)
                {
                    case nameof(Enumerable.ToList):
                        RegisterAncestorRewrite(syntaxNode => ((MemberAccessExpressionSyntax)((InvocationExpressionSyntax)syntaxNode).Expression).Expression, SyntaxKind.InvocationExpression);
                        break;
                    case nameof(Enumerable.Aggregate):
                        RegisterAncestorRewrite(syntaxNode =>
                        {
                            var reversedArguments = SyntaxFactory.SeparatedList(((InvocationExpressionSyntax)syntaxNode).ArgumentList.Arguments.Reverse());

                            return ((InvocationExpressionSyntax)syntaxNode)?.WithArgumentList(SyntaxFactory.ArgumentList(reversedArguments));
                        }, SyntaxKind.InvocationExpression);
                        break;
                    default:
                        if (isLocalVariableToImportTypeOf) break;

                        // TODO: provjeriti da li je metod included
                        if (((symbol.ContainingSymbol as ITypeSymbol)!).IsValueType == false && symbol.ContainingSymbol.Name != "String")
                            ImportedSymbols.Add(symbol.ContainingSymbol as ITypeSymbol);
                        break;
                }

                break;
            }
        }

        if (newMethodName != null)
        {
            var newIdentifier = SyntaxFactory.IdentifierName(newMethodName);
            overrideVisit = overrideVisit switch
            {
                MemberAccessExpressionSyntax memberAccess => memberAccess.WithName(newIdentifier),
                ConditionalAccessExpressionSyntax
                {
                    WhenNotNull: InvocationExpressionSyntax { Expression: MemberBindingExpressionSyntax memberBinding } invocation
                } conditional => conditional.WithWhenNotNull(invocation.WithExpression(memberBinding.WithName(newIdentifier))),
                _ => throw new ArgumentOutOfRangeException(nameof(node), node, null)
            };
        }

        return overrideVisit;
    }

    private static string GetOrderKey(SyntaxNode node)
    {
        var orderKey =
            ((IdentifierNameSyntax)(((MemberAccessExpressionSyntax)((LambdaExpressionSyntax)(((InvocationExpressionSyntax)node.Parent)!).ArgumentList.Arguments.First().Expression)
                .ExpressionBody)!).Name).Identifier.Text;
        return orderKey;
    }

    private SyntaxNode TranslateStaticProperty(IPropertySymbol propertySymbol)
    {
        if (propertySymbol.ContainingType.Name == nameof(DateTime))
        {
            if (propertySymbol.Name == "Now")
                return SyntaxFactory.ParseExpression("new Date()");
        }

        return null;
    }
    
    private ArgumentListSyntax _emptyArgumentsList = SyntaxFactory.ArgumentList(
    SyntaxFactory.MissingToken(SyntaxKind.OpenParenToken),
    default,
    SyntaxFactory.MissingToken(SyntaxKind.CloseParenToken));

}