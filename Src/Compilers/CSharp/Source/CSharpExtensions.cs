﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis
{
    public static class CSharpExtensions
    {
        public static bool IsKind(this SyntaxToken token, SyntaxKind kind)
        {
            return token.CSharpKind() == kind;
        }

        public static bool IsContextualKind(this SyntaxToken token, SyntaxKind kind)
        {
            return token.CSharpContextualKind() == kind;
        }

        public static bool IsKind(this SyntaxTrivia trivia, SyntaxKind kind)
        {
            return trivia.CSharpKind() == kind;
        }

        public static bool IsKind(this SyntaxNode node, SyntaxKind kind)
        {
            return node != null
                && node.CSharpKind() == kind;
        }

        public static bool IsKind(this SyntaxNodeOrToken nodeOrToken, SyntaxKind kind)
        {
            return nodeOrToken.CSharpKind() == kind;
        }

        public static SyntaxKind CSharpKind(this SyntaxToken token)
        {
            return (object)token.Language == (object)LanguageNames.CSharp ? (SyntaxKind)token.RawKind : SyntaxKind.None;
        }

        public static SyntaxKind CSharpContextualKind(this SyntaxToken token)
        {
            return (object)token.Language == (object)LanguageNames.CSharp ? (SyntaxKind)token.RawContextualKind : SyntaxKind.None;
        }

        public static SyntaxKind CSharpKind(this SyntaxTrivia trivia)
        {
            return (object)trivia.Language == (object)LanguageNames.CSharp ? (SyntaxKind)trivia.RawKind : SyntaxKind.None;
        }

        public static SyntaxKind CSharpKind(this SyntaxNode node)
        {
            return (object)node.Language == (object)LanguageNames.CSharp ? (SyntaxKind)node.RawKind : SyntaxKind.None;
        }

        public static SyntaxKind CSharpKind(this SyntaxNodeOrToken nodeOrToken)
        {
            return (object)nodeOrToken.Language == (object)LanguageNames.CSharp ? (SyntaxKind)nodeOrToken.RawKind : SyntaxKind.None;
        }

        /// <summary>
        /// True if the list has at least one node of the specified kind.
        /// </summary>
        public static bool Any<TNode>(this SyntaxList<TNode> list, SyntaxKind kind) where TNode : SyntaxNode
        {
            foreach (var element in list)
            {
                if (element.IsKind(kind))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool Any<TNode>(this SeparatedSyntaxList<TNode> list, SyntaxKind kind) where TNode : SyntaxNode
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].IsKind(kind))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool Any(this SyntaxTriviaList list, SyntaxKind kind)
        {
            foreach (var trivia in list)
            {
                if (trivia.IsKind(kind))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Tests whether a list contains tokens of a particular kind.
        /// </summary>
        /// <param name="list"></param>
        /// <param name="kind">The <see cref="CSharp.SyntaxKind"/> to test for.</param>
        /// <returns>Returns true if the list contains a token which matches <paramref name="kind"/></returns>
        public static bool Any(this SyntaxTokenList list, SyntaxKind kind)
        {
            if (list.Count > 0)
            {
                foreach (var token in list)
                {
                    if (token.IsKind(kind))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        internal static SyntaxToken FirstOrDefault(this SyntaxTokenList list, SyntaxKind kind)
        {
            if (list.Count > 0)
            {
                foreach (var token in list)
                {
                    if (token.IsKind(kind))
                    {
                        return token;
                    }
                }
            }

            return default(SyntaxToken);
        }
    }
}

namespace Microsoft.CodeAnalysis.CSharp
{
    public static class CSharpExtensions
    {
        public static bool IsKeyword(this SyntaxToken token)
        {
            return SyntaxFacts.IsKeywordKind(token.CSharpKind());
        }

        public static bool IsContextualKeyword(this SyntaxToken token)
        {
            return SyntaxFacts.IsContextualKeyword(token.CSharpKind());
        }

        public static bool IsReservedKeyword(this SyntaxToken token)
        {
            return SyntaxFacts.IsReservedKeyword(token.CSharpKind());
        }

        public static bool IsVerbatimStringLiteral(this SyntaxToken token)
        {
            return token.IsKind(SyntaxKind.StringLiteralToken) && token.Text.Length > 0 && token.Text[0] == '@';
        }

        public static bool IsVerbatimIdentifier(this SyntaxToken token)
        {
            return token.IsKind(SyntaxKind.IdentifierToken) && token.Text.Length > 0 && token.Text[0] == '@';
        }

        public static VarianceKind VarianceKindFromToken(this SyntaxToken node)
        {
            switch (node.CSharpKind())
            {
                case SyntaxKind.OutKeyword: return VarianceKind.Out;
                case SyntaxKind.InKeyword: return VarianceKind.In;
                default: return VarianceKind.None;
            }
        }

        /// <summary>
        /// Insert one or more tokens in the list at the specified index.
        /// </summary>
        /// <returns>A new list with the tokens inserted.</returns>
        public static SyntaxTokenList Insert(this SyntaxTokenList list, int index, params SyntaxToken[] items)
        {
            if (index < 0 || index > list.Count)
            {
                throw new ArgumentOutOfRangeException("index");
            }

            if (items == null)
            {
                throw new ArgumentNullException("items");
            }

            if (list.Count == 0)
            {
                return SyntaxFactory.TokenList(items);
            }
            else
            {
                var builder = new Syntax.SyntaxTokenListBuilder(list.Count + items.Length);
                if (index > 0)
                {
                    builder.Add(list, 0, index);
                }

                builder.Add(items);

                if (index < list.Count)
                {
                    builder.Add(list, index, list.Count - index);
                }

                return builder.ToList();
            }
        }

        /// <summary>
        /// Creates a new token with the specified old trivia replaced with computed new trivia.
        /// </summary>
        /// <param name="token"></param>
        /// <param name="trivia">The trivia to be replaced; descendants of the root token.</param>
        /// <param name="computeReplacementTrivia">A function that computes a replacement trivia for
        /// the argument trivia. The first argument is the original trivia. The second argument is
        /// the same trivia rewritten with replaced structure.</param>
        public static SyntaxToken ReplaceTrivia(this SyntaxToken token, IEnumerable<SyntaxTrivia> trivia, Func<SyntaxTrivia, SyntaxTrivia, SyntaxTrivia> computeReplacementTrivia)
        {
            return Syntax.SyntaxReplacer.Replace(token, trivia: trivia, computeReplacementTrivia: computeReplacementTrivia);
        }

        /// <summary>
        /// Creates a new token with the specified old trivia replaced with a new trivia. The old trivia may appear in
        /// the token's leading or trailing trivia.
        /// </summary>
        /// <param name="token"></param>
        /// <param name="oldTrivia">The trivia to be replaced.</param>
        /// <param name="newTrivia">The new trivia to use in the new tree in place of the old
        /// trivia.</param>
        public static SyntaxToken ReplaceTrivia(this SyntaxToken token, SyntaxTrivia oldTrivia, SyntaxTrivia newTrivia)
        {
            return Syntax.SyntaxReplacer.Replace(token, trivia: new[] { oldTrivia }, computeReplacementTrivia: (o, r) => newTrivia);
        }

        internal static Syntax.InternalSyntax.DirectiveStack ApplyDirectives(this SyntaxNode node, Syntax.InternalSyntax.DirectiveStack stack)
        {
            return ((Syntax.InternalSyntax.CSharpSyntaxNode)node.Green).ApplyDirectives(stack);
        }

        internal static Syntax.InternalSyntax.DirectiveStack ApplyDirectives(this SyntaxToken token, Syntax.InternalSyntax.DirectiveStack stack)
        {
            return ((Syntax.InternalSyntax.CSharpSyntaxNode)token.Node).ApplyDirectives(stack);
        }

        internal static Syntax.InternalSyntax.DirectiveStack ApplyDirectives(this SyntaxNodeOrToken nodeOrToken, Syntax.InternalSyntax.DirectiveStack stack)
        {
            if (nodeOrToken.IsToken)
            {
                return nodeOrToken.AsToken().ApplyDirectives(stack);
            }

            if (nodeOrToken.IsNode)
            {
                return nodeOrToken.AsNode().ApplyDirectives(stack);
            }

            return stack;
        }

        /// <summary>
        /// Returns this list as a <see cref="Microsoft.CodeAnalysis.SeparatedSyntaxList&lt;TNode&gt;"/>.
        /// </summary>
        /// <typeparam name="TOther">The type of the list elements in the separated list.</typeparam>
        /// <returns></returns>
        internal static SeparatedSyntaxList<TOther> AsSeparatedList<TOther>(this SyntaxNodeOrTokenList list) where TOther : SyntaxNode
        {
            var builder = SeparatedSyntaxListBuilder<TOther>.Create();
            foreach (var i in list)
            {
                var node = i.AsNode();
                if (node != null)
                {
                    builder.Add((TOther)node);
                }
                else
                {
                    builder.AddSeparator(i.AsToken());
                }
            }

            return builder.ToList();
        }

        #region SyntaxNode 
        internal static IList<DirectiveTriviaSyntax> GetDirectives(this SyntaxNode node, Func<DirectiveTriviaSyntax, bool> filter = null)
        {
            return ((CSharpSyntaxNode)node).GetDirectives(filter);
        }

        /// <summary>
        /// Gets the first directive of the tree rooted by this node.
        /// </summary>
        public static DirectiveTriviaSyntax GetFirstDirective(this SyntaxNode node, Func<DirectiveTriviaSyntax, bool> predicate = null)
        {
            return ((CSharpSyntaxNode)node).GetFirstDirective(predicate);
        }

        /// <summary>
        /// Gets the last directive of the tree rooted by this node.
        /// </summary>
        public static DirectiveTriviaSyntax GetLastDirective(this SyntaxNode node, Func<DirectiveTriviaSyntax, bool> predicate = null)
        {
            return ((CSharpSyntaxNode)node).GetLastDirective(predicate);
        }
        #endregion  

        #region SyntaxTree 
        public static CompilationUnitSyntax GetCompilationUnitRoot(this SyntaxTree tree, CancellationToken cancellationToken = default(CancellationToken))
        {
            return (CompilationUnitSyntax)tree.GetRoot(cancellationToken);
        }

        internal static bool HasReferenceDirectives(this SyntaxTree tree)
        {
            var csharpTree = tree as CSharpSyntaxTree;
            return csharpTree != null && csharpTree.HasReferenceDirectives;
        }

        internal static bool IsAnyPreprocessorSymbolDefined(this SyntaxTree tree, IEnumerable<string> conditionalSymbols)
        {
            var csharpTree = tree as CSharpSyntaxTree;
            return csharpTree != null && csharpTree.IsAnyPreprocessorSymbolDefined(conditionalSymbols);
        }

        internal static bool IsPreprocessorSymbolDefined(this SyntaxTree tree, string symbolName, int position)
        {
            var csharpTree = tree as CSharpSyntaxTree;
            return csharpTree != null && csharpTree.IsPreprocessorSymbolDefined(symbolName, position);
        }

        // Given the error code and the source location, get the warning state based on pragma warning directives.
        internal static ReportDiagnostic GetPragmaDirectiveWarningState(this SyntaxTree tree, string id, int position)
        {
            return ((CSharpSyntaxTree)tree).GetPragmaDirectiveWarningState(id, position);
        }
        #endregion

        #region Compilation 
        // NOTE(cyrusn): There is a bit of a discoverability problem with this method and the same
        // named method in SyntaxTreeSemanticModel.  Technically, i believe these are the appropriate
        // locations for these methods.  This method has no dependencies on anything but the
        // compilation, while the other method needs a bindings object to determine what bound node
        // an expression syntax binds to.  Perhaps when we document these methods we should explain
        // where a user can find the other.
        public static Conversion ClassifyConversion(this Compilation compilation, ITypeSymbol source, ITypeSymbol destination)
        {
            var cscomp = compilation as CSharpCompilation;
            if (cscomp != null)
            {
                return cscomp.ClassifyConversion(source, destination);
            }
            else
            {
                return Conversion.NoConversion;
            }
        }
        #endregion

        #region SemanticModel 
        /// <summary>
        /// Gets the semantic information for an ordering clause in an orderby query clause.
        /// </summary>
        public static SymbolInfo GetSymbolInfo(this SemanticModel semanticModel, OrderingSyntax node, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetSymbolInfo(node, cancellationToken);
            }
            else
            {
                return SymbolInfo.None;
            }
        }

        /// <summary>
        /// Gets the semantic information associated with a select or group clause.
        /// </summary>
        public static SymbolInfo GetSymbolInfo(this SemanticModel semanticModel, SelectOrGroupClauseSyntax node, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetSymbolInfo(node, cancellationToken);
            }
            else
            {
                return SymbolInfo.None;
            }
        }

        /// <summary>
        /// Returns what symbol(s), if any, the given expression syntax bound to in the program.
        /// 
        /// An AliasSymbol will never be returned by this method. What the alias refers to will be
        /// returned instead. To get information about aliases, call GetAliasInfo.
        /// 
        /// If binding the type name C in the expression "new C(...)" the actual constructor bound to
        /// will be returned (or all constructor if overload resolution failed). This occurs as long as C
        /// unambiguously binds to a single type that has a constructor. If C ambiguously binds to multiple
        /// types, or C binds to a static class, then type(s) are returned.
        /// </summary>
        public static SymbolInfo GetSymbolInfo(this SemanticModel semanticModel, ExpressionSyntax expression, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetSymbolInfo(expression, cancellationToken);
            }
            else
            {
                return SymbolInfo.None;
            }
        }

        /// <summary>
        /// Returns what 'Add' method symbol(s), if any, corresponds to the given expression syntax 
        /// within <see cref="ObjectCreationExpressionSyntax.Initializer"/>.
        /// </summary>
        public static SymbolInfo GetCollectionInitializerSymbolInfo(this SemanticModel semanticModel, ExpressionSyntax expression, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetCollectionInitializerSymbolInfo(expression, cancellationToken);
            }
            else
            {
                return SymbolInfo.None;
            }
        }

        /// <summary>
        /// Returns what symbol(s), if any, the given constructor initializer syntax bound to in the program.
        /// </summary>
        public static SymbolInfo GetSymbolInfo(this SemanticModel semanticModel, ConstructorInitializerSyntax constructorInitializer, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetSymbolInfo(constructorInitializer, cancellationToken);
            }
            else
            {
                return SymbolInfo.None;
            }
        }

        /// <summary>
        /// Returns what symbol(s), if any, the given attribute syntax bound to in the program.
        /// </summary>
        public static SymbolInfo GetSymbolInfo(this SemanticModel semanticModel, AttributeSyntax attributeSyntax, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetSymbolInfo(attributeSyntax, cancellationToken);
            }
            else
            {
                return SymbolInfo.None;
            }
        }

        /// <summary>
        /// Gets the semantic information associated with a documentation comment cref.
        /// </summary>
        public static SymbolInfo GetSymbolInfo(this SemanticModel semanticModel, CrefSyntax crefSyntax, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetSymbolInfo(crefSyntax, cancellationToken);
            }
            else
            {
                return SymbolInfo.None;
            }
        }

        /// <summary>
        /// Binds the expression in the context of the specified location and gets symbol information.
        /// This method is used to get symbol information about an expression that did not actually
        /// appear in the source code.
        /// </summary>
        public static SymbolInfo GetSpeculativeSymbolInfo(this SemanticModel semanticModel, int position, ExpressionSyntax expression, SpeculativeBindingOption bindingOption)
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetSpeculativeSymbolInfo(position, expression, bindingOption);
            }
            else
            {
                return SymbolInfo.None;
            }
        }

        /// <summary>
        /// Bind the attribute in the context of the specified location and get semantic information
        /// such as type, symbols and diagnostics. This method is used to get semantic information about an attribute
        /// that did not actually appear in the source code.
        /// </summary>
        public static SymbolInfo GetSpeculativeSymbolInfo(this SemanticModel semanticModel, int position, AttributeSyntax attribute)
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetSpeculativeSymbolInfo(position, attribute);
            }
            else
            {
                return SymbolInfo.None;
            }
        }

        /// <summary>
        /// Bind the constructor initializer in the context of the specified location and get semantic information
        /// such as type, symbols and diagnostics. This method is used to get semantic information about a constructor
        /// initializer that did not actually appear in the source code.
        /// 
        /// NOTE: This will only work in locations where there is already a constructor initializer.
        /// </summary>
        public static SymbolInfo GetSpeculativeSymbolInfo(this SemanticModel semanticModel, int position, ConstructorInitializerSyntax constructorInitializer)
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetSpeculativeSymbolInfo(position, constructorInitializer);
            }
            else
            {
                return SymbolInfo.None;
            }
        }

        /// <summary>
        /// Gets type information about a constructor initializer.
        /// </summary>
        public static TypeInfo GetTypeInfo(this SemanticModel semanticModel, ConstructorInitializerSyntax constructorInitializer, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetTypeInfo(constructorInitializer, cancellationToken);
            }
            else
            {
                return CSharpTypeInfo.None;
            }
        }

        public static TypeInfo GetTypeInfo(this SemanticModel semanticModel, SelectOrGroupClauseSyntax node, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetTypeInfo(node, cancellationToken);
            }
            else
            {
                return CSharpTypeInfo.None;
            }
        }

        /// <summary>
        /// Gets type information about an expression.
        /// </summary>
        public static TypeInfo GetTypeInfo(this SemanticModel semanticModel, ExpressionSyntax expression, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetTypeInfo(expression, cancellationToken);
            }
            else
            {
                return CSharpTypeInfo.None;
            }
        }

        /// <summary>
        /// Gets type information about an attribute.
        /// </summary>
        public static TypeInfo GetTypeInfo(this SemanticModel semanticModel, AttributeSyntax attributeSyntax, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetTypeInfo(attributeSyntax, cancellationToken);
            }
            else
            {
                return CSharpTypeInfo.None;
            }
        }

        /// <summary>
        /// Binds the expression in the context of the specified location and gets type information.
        /// This method is used to get type information about an expression that did not actually
        /// appear in the source code.
        /// </summary>
        public static TypeInfo GetSpeculativeTypeInfo(this SemanticModel semanticModel, int position, ExpressionSyntax expression, SpeculativeBindingOption bindingOption)
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetSpeculativeTypeInfo(position, expression, bindingOption);
            }
            else
            {
                return CSharpTypeInfo.None;
            }
        }

        public static Conversion GetConversion(this SemanticModel semanticModel, SyntaxNode expression, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetConversion(expression, cancellationToken);
            }
            else
            {
                return Conversion.NoConversion;
            }
        }

        public static Conversion GetSpeculativeConversion(this SemanticModel semanticModel, int position, ExpressionSyntax expression, SpeculativeBindingOption bindingOption)
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetSpeculativeConversion(position, expression, bindingOption);
            }
            else
            {
                return Conversion.NoConversion;
            }
        }

        public static ForEachStatementInfo GetForEachStatementInfo(this SemanticModel semanticModel, ForEachStatementSyntax forEachStatement)
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetForEachStatementInfo(forEachStatement);
            }
            else
            {
                return default(ForEachStatementInfo);
            }
        }

        public static AwaitExpressionInfo GetAwaitExpressionInfo(this SemanticModel semanticModel, PrefixUnaryExpressionSyntax awaitExpression)
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetAwaitExpressionInfo(awaitExpression);
            }
            else
            {
                return default(AwaitExpressionInfo);
            }
        }

        public static ImmutableArray<ISymbol> GetMemberGroup(this SemanticModel semanticModel, ExpressionSyntax expression, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetMemberGroup(expression, cancellationToken);
            }
            else
            {
                return ImmutableArray.Create<ISymbol>();
            }
        }

        public static ImmutableArray<ISymbol> GetMemberGroup(this SemanticModel semanticModel, AttributeSyntax attribute, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetMemberGroup(attribute, cancellationToken);
            }
            else
            {
                return ImmutableArray.Create<ISymbol>();
            }
        }

        public static ImmutableArray<ISymbol> GetMemberGroup(this SemanticModel semanticModel, ConstructorInitializerSyntax initializer, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetMemberGroup(initializer, cancellationToken);
            }
            else
            {
                return ImmutableArray.Create<ISymbol>();
            }
        }

        /// <summary>
        /// Returns the list of accessible, non-hidden indexers that could be invoked with the given expression as receiver.
        /// </summary>
        public static ImmutableArray<IPropertySymbol> GetIndexerGroup(this SemanticModel semanticModel, ExpressionSyntax expression, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetIndexerGroup(expression, cancellationToken);
            }
            else
            {
                return ImmutableArray.Create<IPropertySymbol>();
            }
        }

        public static Optional<object> GetConstantValue(this SemanticModel semanticModel, ExpressionSyntax expression, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetConstantValue(expression, cancellationToken);
            }
            else
            {
                return default(Optional<object>);
            }
        }

        /// <summary>
        /// Gets the semantic information associated with a query clause.
        /// </summary>
        public static QueryClauseInfo GetQueryClauseInfo(this SemanticModel semanticModel, QueryClauseSyntax node, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetQueryClauseInfo(node, cancellationToken);
            }
            else
            {
                return default(QueryClauseInfo);
            }
        }

        /// <summary>
        /// If <paramref name="nameSyntax"/> resolves to an alias name, return the AliasSymbol corresponding
        /// to A. Otherwise return null.
        /// </summary>
        public static IAliasSymbol GetAliasInfo(this SemanticModel semanticModel, IdentifierNameSyntax nameSyntax, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetAliasInfo(nameSyntax, cancellationToken);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Binds the name in the context of the specified location and sees if it resolves to an
        /// alias name. If it does, return the AliasSymbol corresponding to it. Otherwise, return null.
        /// </summary>
        public static IAliasSymbol GetSpeculativeAliasInfo(this SemanticModel semanticModel, int position, IdentifierNameSyntax nameSyntax, SpeculativeBindingOption bindingOption)
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetSpeculativeAliasInfo(position, nameSyntax, bindingOption);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Analyze control-flow within a part of a method body. 
        /// </summary>
        public static ControlFlowAnalysis AnalyzeControlFlow(this SemanticModel semanticModel, StatementSyntax firstStatement, StatementSyntax lastStatement)
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.AnalyzeControlFlow(firstStatement, lastStatement);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Analyze control-flow within a part of a method body. 
        /// </summary>
        public static ControlFlowAnalysis AnalyzeControlFlow(this SemanticModel semanticModel, StatementSyntax statement)
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.AnalyzeControlFlow(statement);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Analyze data-flow within an expression. 
        /// </summary>
        public static DataFlowAnalysis AnalyzeDataFlow(this SemanticModel semanticModel, ExpressionSyntax expression)
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.AnalyzeDataFlow(expression);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Analyze data-flow within a part of a method body. 
        /// </summary>
        public static DataFlowAnalysis AnalyzeDataFlow(this SemanticModel semanticModel, StatementSyntax firstStatement, StatementSyntax lastStatement)
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.AnalyzeDataFlow(firstStatement, lastStatement);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Analyze data-flow within a part of a method body. 
        /// </summary>
        public static DataFlowAnalysis AnalyzeDataFlow(this SemanticModel semanticModel, StatementSyntax statement)
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.AnalyzeDataFlow(statement);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Get a SemanticModel object that is associated with a method body that did not appear in this source code.
        /// Given <paramref name="position"/> must lie within an existing method body of the Root syntax node for this SemanticModel.
        /// Locals and labels declared within this existing method body are not considered to be in scope of the speculated method body.
        /// </summary>
        public static bool TryGetSpeculativeSemanticModelForMethodBody(this SemanticModel semanticModel, int position, BaseMethodDeclarationSyntax method, out SemanticModel speculativeModel)
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.TryGetSpeculativeSemanticModelForMethodBody(position, method, out speculativeModel);
            }
            else
            {
                speculativeModel = null;
                return false;
            }
        }

        /// <summary>
        /// Get a SemanticModel object that is associated with a method body that did not appear in this source code.
        /// Given <paramref name="position"/> must lie within an existing method body of the Root syntax node for this SemanticModel.
        /// Locals and labels declared within this existing method body are not considered to be in scope of the speculated method body.
        /// </summary>
        public static bool TryGetSpeculativeSemanticModelForMethodBody(this SemanticModel semanticModel, int position, AccessorDeclarationSyntax accessor, out SemanticModel speculativeModel)
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.TryGetSpeculativeSemanticModelForMethodBody(position, accessor, out speculativeModel);
            }
            else
            {
                speculativeModel = null;
                return false;
            }
        }

        /// <summary>
        /// Get a SemanticModel object that is associated with a type syntax node that did not appear in
        /// this source code. This can be used to get detailed semantic information about sub-parts
        /// of a type syntax that did not appear in source code. 
        /// </summary>
        public static bool TryGetSpeculativeSemanticModel(this SemanticModel semanticModel, int position, TypeSyntax type, out SemanticModel speculativeModel, SpeculativeBindingOption bindingOption = SpeculativeBindingOption.BindAsExpression)
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.TryGetSpeculativeSemanticModel(position, type, out speculativeModel, bindingOption);
            }
            else
            {
                speculativeModel = null;
                return false;
            }
        }

        /// <summary>
        /// Get a SemanticModel object that is associated with a cref syntax node that did not appear in
        /// this source code. This can be used to get detailed semantic information about sub-parts
        /// of a cref syntax that did not appear in source code. 
        /// </summary>
        public static bool TryGetSpeculativeSemanticModel(this SemanticModel semanticModel, int position, CrefSyntax crefSyntax, out SemanticModel speculativeModel)
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.TryGetSpeculativeSemanticModel(position, crefSyntax, out speculativeModel);
            }
            else
            {
                speculativeModel = null;
                return false;
            }
        }

        /// <summary>
        /// Get a SemanticModel object that is associated with a statement that did not appear in
        /// this source code. This can be used to get detailed semantic information about sub-parts
        /// of a statement that did not appear in source code. 
        /// </summary>
        public static bool TryGetSpeculativeSemanticModel(this SemanticModel semanticModel, int position, StatementSyntax statement, out SemanticModel speculativeModel)
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.TryGetSpeculativeSemanticModel(position, statement, out speculativeModel);
            }
            else
            {
                speculativeModel = null;
                return false;
            }
        }

        /// <summary>
        /// Get a SemanticModel object that is associated with an initializer that did not appear in
        /// this source code. This can be used to get detailed semantic information about sub-parts
        /// of a field initializer or default parameter value that did not appear in source code.
        /// </summary>
        public static bool TryGetSpeculativeSemanticModel(this SemanticModel semanticModel, int position, EqualsValueClauseSyntax initializer, out SemanticModel speculativeModel)
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.TryGetSpeculativeSemanticModel(position, initializer, out speculativeModel);
            }
            else
            {
                speculativeModel = null;
                return false;
            }
        }

        /// <summary>
        /// Get a SemanticModel object that is associated with a constructor initializer that did not appear in
        /// this source code. This can be used to get detailed semantic information about sub-parts
        /// of a constructor initializer that did not appear in source code. 
        /// 
        /// NOTE: This will only work in locations where there is already a constructor initializer.
        /// </summary>
        public static bool TryGetSpeculativeSemanticModel(this SemanticModel semanticModel, int position, ConstructorInitializerSyntax constructorInitializer, out SemanticModel speculativeModel)
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.TryGetSpeculativeSemanticModel(position, constructorInitializer, out speculativeModel);
            }
            else
            {
                speculativeModel = null;
                return false;
            }
        }

        /// <summary>
        /// Get a SemanticModel object that is associated with an attribute that did not appear in
        /// this source code. This can be used to get detailed semantic information about sub-parts
        /// of an attribute that did not appear in source code. 
        /// </summary>
        public static bool TryGetSpeculativeSemanticModel(this SemanticModel semanticModel, int position, AttributeSyntax attribute, out SemanticModel speculativeModel)
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.TryGetSpeculativeSemanticModel(position, attribute, out speculativeModel);
            }
            else
            {
                speculativeModel = null;
                return false;
            }
        }

        /// <summary>
        /// Determines what type of conversion, if any, would be used if a given expression was
        /// converted to a given type.  If isExplicitInSource is true, the conversion produced is
        /// that which would be used if the conversion were done for a cast expression.
        /// </summary>
        public static Conversion ClassifyConversion(this SemanticModel semanticModel, ExpressionSyntax expression, ITypeSymbol destination, bool isExplicitInSource = false)
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.ClassifyConversion(expression, (TypeSymbol)destination, isExplicitInSource);
            }
            else
            {
                return Conversion.NoConversion;
            }
        }

        /// <summary>
        /// Determines what type of conversion, if any, would be used if a given expression was
        /// converted to a given type.  If isExplicitInSource is true, the conversion produced is
        /// that which would be used if the conversion were done for a cast expression.
        /// </summary>
        public static Conversion ClassifyConversion(this SemanticModel semanticModel, int position, ExpressionSyntax expression, ITypeSymbol destination, bool isExplicitInSource = false)
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.ClassifyConversion(position, expression, (TypeSymbol)destination, isExplicitInSource);
            }
            else
            {
                return Conversion.NoConversion;
            }
        }

        /// <summary>
        /// Given a member declaration syntax, get the corresponding symbol.
        /// </summary>
        public static ISymbol GetDeclaredSymbol(this SemanticModel semanticModel, MemberDeclarationSyntax declarationSyntax, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetDeclaredSymbol(declarationSyntax, cancellationToken);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Given a namespace declaration syntax node, get the corresponding namespace symbol for
        /// the declaration assembly.
        /// </summary>
        public static INamespaceSymbol GetDeclaredSymbol(this SemanticModel semanticModel, NamespaceDeclarationSyntax declarationSyntax, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetDeclaredSymbol(declarationSyntax, cancellationToken);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Given a type declaration, get the corresponding type symbol.
        /// </summary>
        public static INamedTypeSymbol GetDeclaredSymbol(this SemanticModel semanticModel, BaseTypeDeclarationSyntax declarationSyntax, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetDeclaredSymbol(declarationSyntax, cancellationToken);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Given a delegate declaration, get the corresponding type symbol.
        /// </summary>
        public static INamedTypeSymbol GetDeclaredSymbol(this SemanticModel semanticModel, DelegateDeclarationSyntax declarationSyntax, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetDeclaredSymbol(declarationSyntax, cancellationToken);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Given a enum member declaration, get the corresponding field symbol.
        /// </summary>
        public static IFieldSymbol GetDeclaredSymbol(this SemanticModel semanticModel, EnumMemberDeclarationSyntax declarationSyntax, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetDeclaredSymbol(declarationSyntax, cancellationToken);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Given a base method declaration syntax, get the corresponding method symbol.
        /// </summary>
        public static IMethodSymbol GetDeclaredSymbol(this SemanticModel semanticModel, BaseMethodDeclarationSyntax declarationSyntax, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetDeclaredSymbol(declarationSyntax, cancellationToken);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Given a syntax node that declares a property, indexer or an event, get the corresponding declared symbol.
        /// </summary>
        public static ISymbol GetDeclaredSymbol(this SemanticModel semanticModel, BasePropertyDeclarationSyntax declarationSyntax, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetDeclaredSymbol(declarationSyntax, cancellationToken);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Given a syntax node that declares a property, get the corresponding declared symbol.
        /// </summary>
        public static IPropertySymbol GetDeclaredSymbol(this SemanticModel semanticModel, PropertyDeclarationSyntax declarationSyntax, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetDeclaredSymbol(declarationSyntax, cancellationToken);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Given a syntax node that declares an indexer, get the corresponding declared symbol.
        /// </summary>
        public static IPropertySymbol GetDeclaredSymbol(this SemanticModel semanticModel, IndexerDeclarationSyntax declarationSyntax, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetDeclaredSymbol(declarationSyntax, cancellationToken);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Given a syntax node that declares a (custom) event, get the corresponding event symbol.
        /// </summary>
        public static IEventSymbol GetDeclaredSymbol(this SemanticModel semanticModel, EventDeclarationSyntax declarationSyntax, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetDeclaredSymbol(declarationSyntax, cancellationToken);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Given a syntax node of anonymous object creation initializer, get the anonymous object property symbol.
        /// </summary>
        public static IPropertySymbol GetDeclaredSymbol(this SemanticModel semanticModel, AnonymousObjectMemberDeclaratorSyntax declaratorSyntax, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetDeclaredSymbol(declaratorSyntax, cancellationToken);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Given a syntax node of anonymous object creation expression, get the anonymous object type symbol.
        /// </summary>
        public static INamedTypeSymbol GetDeclaredSymbol(this SemanticModel semanticModel, AnonymousObjectCreationExpressionSyntax declaratorSyntax, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetDeclaredSymbol(declaratorSyntax, cancellationToken);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Given a syntax node that declares a property or member accessor, get the corresponding symbol.
        /// </summary>
        public static IMethodSymbol GetDeclaredSymbol(this SemanticModel semanticModel, AccessorDeclarationSyntax declarationSyntax, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetDeclaredSymbol(declarationSyntax, cancellationToken);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Given a variable declarator syntax, get the corresponding symbol.
        /// </summary>
        public static ISymbol GetDeclaredSymbol(this SemanticModel semanticModel, VariableDeclaratorSyntax declarationSyntax, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetDeclaredSymbol(declarationSyntax, cancellationToken);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Given a labeled statement syntax, get the corresponding label symbol.
        /// </summary>
        public static ILabelSymbol GetDeclaredSymbol(this SemanticModel semanticModel, LabeledStatementSyntax declarationSyntax, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetDeclaredSymbol(declarationSyntax, cancellationToken);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Given a switch label syntax, get the corresponding label symbol.
        /// </summary>
        public static ILabelSymbol GetDeclaredSymbol(this SemanticModel semanticModel, SwitchLabelSyntax declarationSyntax, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetDeclaredSymbol(declarationSyntax, cancellationToken);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Given a using declaration get the corresponding symbol for the using alias that was introduced.
        /// </summary>
        public static IAliasSymbol GetDeclaredSymbol(this SemanticModel semanticModel, UsingDirectiveSyntax declarationSyntax, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetDeclaredSymbol(declarationSyntax, cancellationToken);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Given an extern alias declaration get the corresponding symbol for the alias that was introduced.
        /// </summary>
        public static IAliasSymbol GetDeclaredSymbol(this SemanticModel semanticModel, ExternAliasDirectiveSyntax declarationSyntax, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetDeclaredSymbol(declarationSyntax, cancellationToken);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Given a parameter declaration syntax node, get the corresponding symbol.
        /// </summary>
        public static IParameterSymbol GetDeclaredSymbol(this SemanticModel semanticModel, ParameterSyntax declarationSyntax, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetDeclaredSymbol(declarationSyntax, cancellationToken);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Given a type parameter declaration (field or method), get the corresponding symbol
        /// </summary>
        public static ITypeParameterSymbol GetDeclaredSymbol(this SemanticModel semanticModel, TypeParameterSyntax typeParameter, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetDeclaredSymbol(typeParameter, cancellationToken);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Given a foreach statement, get the symbol for the iteration variable
        /// </summary>
        public static ILocalSymbol GetDeclaredSymbol(this SemanticModel semanticModel, ForEachStatementSyntax forEachStatement, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetDeclaredSymbol(forEachStatement, cancellationToken);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Given a catch declaration, get the symbol for the exception variable
        /// </summary>
        public static ILocalSymbol GetDeclaredSymbol(this SemanticModel semanticModel, CatchDeclarationSyntax catchDeclaration, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetDeclaredSymbol(catchDeclaration, cancellationToken);
            }
            else
            {
                return null;
            }
        }

        public static IRangeVariableSymbol GetDeclaredSymbol(this SemanticModel semanticModel, QueryClauseSyntax queryClause, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetDeclaredSymbol(queryClause, cancellationToken);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Get the query range variable declared in a join into clause.
        /// </summary>
        public static IRangeVariableSymbol GetDeclaredSymbol(this SemanticModel semanticModel, JoinIntoClauseSyntax node, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetDeclaredSymbol(node, cancellationToken);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Get the query range variable declared in a query continuation clause.
        /// </summary>
        public static IRangeVariableSymbol GetDeclaredSymbol(this SemanticModel semanticModel, QueryContinuationSyntax node, CancellationToken cancellationToken = default(CancellationToken))
        {
            var csmodel = semanticModel as CSharpSemanticModel;
            if (csmodel != null)
            {
                return csmodel.GetDeclaredSymbol(node, cancellationToken);
            }
            else
            {
                return null;
            }
        }
        #endregion
    }
}