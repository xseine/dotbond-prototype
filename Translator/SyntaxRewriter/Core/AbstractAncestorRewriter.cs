using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Translator.SyntaxRewriter.Core
{
    /// <summary>
    /// Provides methods for nodes to be able to add rewrite logic for their ancestors.
    /// Ancestor to rewrite is specified by providing <see cref="SyntaxKind"/> in the method.
    ///
    /// Example usages:
    /// - Adding getters and setters from property declaration to class declaration
    /// - Reimplement object initialization logic from C# to Typescript by adding the statements after the simple initialization
    /// </summary>
    public abstract class AbstractAncestorRewriter : CSharpSyntaxRewriter
    {
        public List<(SyntaxKind[] ancestorKind, Func<SyntaxNode, SyntaxNode> rewriteFunc)> AncestorRewrites { get; } = new();
        
        /// <summary>
        /// Buffers rewrites for a particular node, so they would get executed only after base visit completes, and not by some of node's children
        /// </summary>
        public Dictionary<SyntaxNode, List<(SyntaxKind[] ancestorKind, Func<SyntaxNode, SyntaxNode> rewriteFunc)>> AncestorRewritesBuffer { get; } = new();

        public Stack<SyntaxNode> ActiveNodeStack = new();

        protected AbstractAncestorRewriter(bool visitIntoStructuredTrivia = false) : base(visitIntoStructuredTrivia)
        {
            
        }
        
        public void RegisterAncestorRewrite(Func<SyntaxNode, SyntaxNode> rewriteFunc, params SyntaxKind[] ancestorKind)
        {
            if (ActiveNodeStack.Any())
            {
                var node = ActiveNodeStack.Peek();
                AncestorRewritesBuffer[node].Add((ancestorKind, rewriteFunc));
            } else 
                AncestorRewrites.Add((ancestorKind, rewriteFunc));
        }

        public override SyntaxNode VisitExpressionStatement(ExpressionStatementSyntax node) => VisitAndRewrite(node);
        public override SyntaxNode VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node) => VisitAndRewrite(node);
        public override SyntaxNode VisitReturnStatement(ReturnStatementSyntax node) => VisitAndRewrite(node);
        public override SyntaxNode VisitArrowExpressionClause(ArrowExpressionClauseSyntax node) => VisitAndRewrite(node);
        public override SyntaxNode VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) => VisitAndRewrite(node);
        public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node) => VisitAndRewrite(node);
        public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node) => VisitAndRewrite(node);
        public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node) => VisitAndRewrite(node);
        public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node) => VisitAndRewrite(node);
        public override SyntaxNode VisitIfStatement(IfStatementSyntax node) => VisitAndRewrite(node);
        public override SyntaxNode VisitIsPatternExpression(IsPatternExpressionSyntax node) => VisitAndRewrite(node);
        public override SyntaxNode VisitSubpattern(SubpatternSyntax node) => VisitAndRewrite(node);
        public override SyntaxNode VisitElementAccessExpression(ElementAccessExpressionSyntax node) => VisitAndRewrite(node);


        protected SyntaxNode VisitAndRewrite(SyntaxNode node)
        {
            var syntaxKind = node.Kind();
            
            ActiveNodeStack.Push(node);
            AncestorRewritesBuffer[node] = new List<(SyntaxKind[] ancestorKind, Func<SyntaxNode, SyntaxNode> rewriteFunc)>();
            
            var baseVisit = node switch
            {
                ExpressionStatementSyntax syntax => base.VisitExpressionStatement(syntax),
                LocalDeclarationStatementSyntax syntax => base.VisitLocalDeclarationStatement(syntax),
                ReturnStatementSyntax syntax => base.VisitReturnStatement(syntax),
                ArrowExpressionClauseSyntax syntax => base.VisitArrowExpressionClause(syntax),
                SimpleLambdaExpressionSyntax syntax => base.VisitSimpleLambdaExpression(syntax),
                InvocationExpressionSyntax syntax => base.VisitInvocationExpression(syntax),
                ClassDeclarationSyntax syntax => base.VisitClassDeclaration(syntax),
                MemberAccessExpressionSyntax syntax => base.VisitMemberAccessExpression(syntax),
                PropertyDeclarationSyntax syntax => base.VisitPropertyDeclaration(syntax),
                IfStatementSyntax syntax => base.VisitIfStatement(syntax),
                IsPatternExpressionSyntax syntax => base.VisitIsPatternExpression(syntax),
                SubpatternSyntax syntax => base.VisitSubpattern(syntax),
                ElementAccessExpressionSyntax syntax => base.VisitElementAccessExpression(syntax),
                _ => throw new ArgumentOutOfRangeException(nameof(node), node, null)
            };

            ActiveNodeStack.Pop();
            AncestorRewrites.AddRange(AncestorRewritesBuffer[node]);
            AncestorRewritesBuffer.Remove(node);
            
            return ExecuteRewrites(baseVisit!, syntaxKind);
        }
        
        
        /// <summary>
        /// Executes rewrites registered for the provided SyntaxKind.
        /// </summary>
        /// <param name="visitedSyntaxNode">Already visited syntax node (CSharpSyntaxRewriter)</param>
        /// <param name="kind">Used to find applicable rewrites from <see cref="AncestorRewrites"/>.</param>
        /// <returns></returns>
        private SyntaxNode ExecuteRewrites(SyntaxNode visitedSyntaxNode, SyntaxKind kind)
        {
            var registeredRewrites = AncestorRewrites.FindAll(rewrite => rewrite.ancestorKind.Contains(kind)).Select(rewrite => rewrite.rewriteFunc).ToList();

            if (registeredRewrites.Any() == false) return visitedSyntaxNode;

            AncestorRewrites.RemoveAll(rewrite => rewrite.ancestorKind.Contains(kind));

            return registeredRewrites.Aggregate(visitedSyntaxNode, (acc, curr) => acc == null ? null : curr(acc));
        }
    }
}