﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp.Utilities;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.Extensions.ContextQuery
{
    internal static class SyntaxTreeExtensions
    {
        public static bool IsAttributeNameContext(this SyntaxTree syntaxTree, int position, CancellationToken cancellationToken)
        {
            var token = syntaxTree.FindTokenOnLeftOfPosition(position, cancellationToken);
            token = token.GetPreviousTokenIfTouchingWord(position);

            // cases:
            //   [ |
            if (token.CSharpKind() == SyntaxKind.OpenBracketToken &&
                token.IsParentKind(SyntaxKind.AttributeList))
            {
                return true;
            }

            // cases:
            //   [Foo(1), |
            if (token.CSharpKind() == SyntaxKind.CommaToken &&
                token.IsParentKind(SyntaxKind.AttributeList))
            {
                return true;
            }

            // cases:
            //   [specifier: |
            if (token.CSharpKind() == SyntaxKind.ColonToken &&
                token.IsParentKind(SyntaxKind.AttributeTargetSpecifier))
            {
                return true;
            }

            // cases:
            //   [Namespace.|
            if (token.IsParentKind(SyntaxKind.QualifiedName) &&
                token.Parent.IsParentKind(SyntaxKind.Attribute))
            {
                return true;
            }

            // cases:
            //   [global::|
            if (token.IsParentKind(SyntaxKind.AliasQualifiedName) &&
                token.Parent.IsParentKind(SyntaxKind.Attribute))
            {
                return true;
            }

            return false;
        }

        public static bool IsGlobalMemberDeclarationContext(
            this SyntaxTree syntaxTree,
            int position,
            ISet<SyntaxKind> validModifiers,
            CancellationToken cancellationToken)
        {
            if (!syntaxTree.IsInteractiveOrScript())
            {
                return false;
            }

            var tokenOnLeftOfPosition = syntaxTree.FindTokenOnLeftOfPosition(position, cancellationToken);
            var token = tokenOnLeftOfPosition.GetPreviousTokenIfTouchingWord(position);

            var modifierTokens = syntaxTree.GetPrecedingModifiers(position, tokenOnLeftOfPosition, cancellationToken);
            if (modifierTokens.IsEmpty())
            {
                return false;
            }

            if (modifierTokens.IsSubsetOf(validModifiers))
            {
                // the parent is the member
                // the grandparent is the container of the member
                // in interactive, it's possible that there might be an intervening "incomplete" member for partially
                // typed declarations that parse ambiguously. For example, "internal e".
                if (token.IsParentKind(SyntaxKind.CompilationUnit) ||
                   (token.IsParentKind(SyntaxKind.IncompleteMember) && token.Parent.IsParentKind(SyntaxKind.CompilationUnit)))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsMemberDeclarationContext(
            this SyntaxTree syntaxTree, int position, SyntaxToken tokenOnLeftOfPosition, CancellationToken cancellationToken)
        {
            // cases:
            // class C {
            //   |

            // class C {
            //   void Foo() {
            //   }
            //   |

            // class C {
            //   int i;
            //   |

            // class C {
            //   public |

            // class C {
            //   [Foo]
            //   |

            var originalToken = tokenOnLeftOfPosition;
            var token = originalToken;

            // If we're touching the right of an identifier, move back to
            // previous token.
            token = token.GetPreviousTokenIfTouchingWord(position);

            if (token.CSharpKind() == SyntaxKind.OpenBraceToken)
            {
                if (token.Parent is BaseTypeDeclarationSyntax)
                {
                    return true;
                }
            }

            // class C {
            //   int i;
            //   |
            if (token.CSharpKind() == SyntaxKind.SemicolonToken)
            {
                if (token.Parent is MemberDeclarationSyntax &&
                    token.Parent.GetParent() is BaseTypeDeclarationSyntax)
                {
                    return true;
                }
            }

            // class A {
            //   class C {}
            //   |

            // class C {
            //    void Foo() {
            //    }
            //    |
            if (token.CSharpKind() == SyntaxKind.CloseBraceToken)
            {
                if (token.Parent is BaseTypeDeclarationSyntax &&
                    token.Parent.GetParent() is BaseTypeDeclarationSyntax)
                {
                    // after a nested type
                    return true;
                }
                else if (token.Parent is AccessorListSyntax)
                {
                    // after a property
                    return true;
                }
                else if (
                    token.IsParentKind(SyntaxKind.Block) &&
                    token.Parent.GetParent() is MemberDeclarationSyntax)
                {
                    // after a method/operator/etc.
                    return true;
                }
            }

            // namespace Foo {
            //   [Bar]
            //   |

            if (token.CSharpKind() == SyntaxKind.CloseBracketToken &&
                token.IsParentKind(SyntaxKind.AttributeList))
            {
                // attributes belong to a member which itself is in a
                // container.

                // the parent is the attribute
                // the grandparent is the owner of the attribute
                // the great-grandparent is the container that the owner is in
                var container = token.Parent.GetParent().GetParent();
                if (container is BaseTypeDeclarationSyntax)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsMemberDeclarationContext(
            this SyntaxTree syntaxTree,
            int position,
            CSharpSyntaxContext contextOpt,
            ISet<SyntaxKind> validModifiers,
            ISet<SyntaxKind> validTypeDeclarations,
            bool canBePartial,
            CancellationToken cancellationToken)
        {
            var typeDecl = contextOpt != null
                ? contextOpt.ContainingTypeOrEnumDeclaration
                : syntaxTree.GetContainingTypeOrEnumDeclaration(position, cancellationToken);

            if (typeDecl == null)
            {
                return false;
            }

            if (!validTypeDeclarations.Contains(typeDecl.CSharpKind()))
            {
                return false;
            }

            validTypeDeclarations = validTypeDeclarations ?? SpecializedCollections.EmptySet<SyntaxKind>();

            // Check many of the simple cases first.
            var leftToken = contextOpt != null
                ? contextOpt.LeftToken
                : syntaxTree.FindTokenOnLeftOfPosition(position, cancellationToken);

            if (syntaxTree.IsMemberDeclarationContext(position, leftToken, cancellationToken))
            {
                return true;
            }

            var token = contextOpt != null
                ? contextOpt.TargetToken
                : leftToken.GetPreviousTokenIfTouchingWord(position);

            // A member can also show up after certain types of modifiers
            if (canBePartial &&
                token.IsKindOrHasMatchingText(SyntaxKind.PartialKeyword))
            {
                return true;
            }

            var modifierTokens = contextOpt != null
                ? contextOpt.PrecedingModifiers
                : syntaxTree.GetPrecedingModifiers(position, leftToken, cancellationToken);

            if (modifierTokens.IsEmpty())
            {
                return false;
            }

            validModifiers = validModifiers ?? SpecializedCollections.EmptySet<SyntaxKind>();

            if (modifierTokens.IsSubsetOf(validModifiers))
            {
                var member = token.Parent;
                if (token.HasMatchingText(SyntaxKind.AsyncKeyword))
                {
                    // second appearance of "async", not followed by modifier: treat it as type
                    if (syntaxTree.GetPrecedingModifiers(token.SpanStart, token, cancellationToken).Any(x => x == SyntaxKind.AsyncKeyword))
                    {
                        return false;
                    }

                    // rule out async lambdas inside a method
                    if (token.GetAncestor<StatementSyntax>() == null)
                    {
                        member = token.GetAncestor<MemberDeclarationSyntax>();
                    }
                }

                // cases:
                // public |
                // async |
                // public async |
                return member != null &&
                    member.Parent is BaseTypeDeclarationSyntax;
            }

            return false;
        }

        public static bool IsTypeDeclarationContext(
            this SyntaxTree syntaxTree, int position, SyntaxToken tokenOnLeftOfPosition, CancellationToken cancellationToken)
        {
            // cases:
            // root: |

            // extern alias a;
            // |

            // using Foo;
            // |

            // using Foo = Bar;
            // |

            // namespace N {}
            // |

            // namespace N {
            // |

            // class C {}
            // |

            // class C {
            // |

            // class C {
            //   void Foo() {
            //   }
            //   |

            // class C {
            //   int i;
            //   |

            // class C {
            //   public |

            // class C {
            //   [Foo]
            //   |

            var originalToken = tokenOnLeftOfPosition;
            var token = originalToken;

            // If we're touching the right of an identifier, move back to
            // previous token.
            token = token.GetPreviousTokenIfTouchingWord(position);

            // a type decl can't come before usings/externs
            if (originalToken.GetNextToken(includeSkipped: true).IsUsingOrExternKeyword())
            {
                return false;
            }

            // root: |
            if (token.CSharpKind() == SyntaxKind.None)
            {
                // root namespace

                // a type decl can't come before usings/externs
                var compilationUnit = syntaxTree.GetRoot(cancellationToken) as CompilationUnitSyntax;
                if (compilationUnit != null &&
                    (compilationUnit.Externs.Count > 0 ||
                    compilationUnit.Usings.Count > 0))
                {
                    return false;
                }

                return true;
            }

            if (token.CSharpKind() == SyntaxKind.OpenBraceToken)
            {
                if (token.IsParentKind(SyntaxKind.ClassDeclaration) |
                    token.IsParentKind(SyntaxKind.StructDeclaration))
                {
                    return true;
                }
                else if (token.IsParentKind(SyntaxKind.NamespaceDeclaration))
                {
                    return true;
                }
            }

            // extern alias a;
            // |

            // using Foo;
            // |

            // class C {
            //   int i;
            //   |
            if (token.CSharpKind() == SyntaxKind.SemicolonToken)
            {
                if (token.IsParentKind(SyntaxKind.ExternAliasDirective) ||
                    token.IsParentKind(SyntaxKind.UsingDirective))
                {
                    return true;
                }
                else if (token.Parent is MemberDeclarationSyntax)
                {
                    return true;
                }
            }

            // class C {}
            // |

            // namespace N {}
            // |

            // class C {
            //    void Foo() {
            //    }
            //    |
            if (token.CSharpKind() == SyntaxKind.CloseBraceToken)
            {
                if (token.Parent is BaseTypeDeclarationSyntax)
                {
                    return true;
                }
                else if (token.IsParentKind(SyntaxKind.NamespaceDeclaration))
                {
                    return true;
                }
                else if (token.Parent is AccessorListSyntax)
                {
                    return true;
                }
                else if (
                    token.IsParentKind(SyntaxKind.Block) &&
                    token.Parent.GetParent() is MemberDeclarationSyntax)
                {
                    return true;
                }
            }

            // namespace Foo {
            //   [Bar]
            //   |

            if (token.CSharpKind() == SyntaxKind.CloseBracketToken &&
                token.IsParentKind(SyntaxKind.AttributeList))
            {
                // assembly attributes belong to the containing compilation unit
                if (token.Parent.IsParentKind(SyntaxKind.CompilationUnit))
                {
                    return true;
                }

                // other attributes belong to a member which itself is in a
                // container.

                // the parent is the attribute
                // the grandparent is the owner of the attribute
                // the great-grandparent is the container that the owner is in
                var container = token.Parent.GetParent().GetParent();
                if (container.IsKind(SyntaxKind.CompilationUnit) ||
                    container.IsKind(SyntaxKind.NamespaceDeclaration) ||
                    container.IsKind(SyntaxKind.ClassDeclaration) ||
                    container.IsKind(SyntaxKind.StructDeclaration))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsTypeDeclarationContext(
            this SyntaxTree syntaxTree,
            int position,
            CSharpSyntaxContext contextOpt,
            ISet<SyntaxKind> validModifiers,
            ISet<SyntaxKind> validTypeDeclarations,
            bool canBePartial,
            CancellationToken cancellationToken)
        {
            // We only allow nested types inside a class or struct, not inside a
            // an interface or enum.
            var typeDecl = contextOpt != null
                ? contextOpt.ContainingTypeDeclaration
                : syntaxTree.GetContainingTypeDeclaration(position, cancellationToken);

            validTypeDeclarations = validTypeDeclarations ?? SpecializedCollections.EmptySet<SyntaxKind>();

            if (typeDecl != null)
            {
                if (!validTypeDeclarations.Contains(typeDecl.CSharpKind()))
                {
                    return false;
                }
            }

            // Check many of the simple cases first.
            var leftToken = contextOpt != null
                ? contextOpt.LeftToken
                : syntaxTree.FindTokenOnLeftOfPosition(position, cancellationToken);

            if (syntaxTree.IsTypeDeclarationContext(position, leftToken, cancellationToken))
            {
                return true;
            }

            // If we're touching the right of an identifier, move back to
            // previous token.
            var token = contextOpt != null
                ? contextOpt.TargetToken
                : leftToken.GetPreviousTokenIfTouchingWord(position);

            // A type can also show up after certain types of modifiers
            if (canBePartial &&
                token.IsKindOrHasMatchingText(SyntaxKind.PartialKeyword))
            {
                return true;
            }

            var modifierTokens = contextOpt != null
                ? contextOpt.PrecedingModifiers
                : syntaxTree.GetPrecedingModifiers(position, leftToken, cancellationToken);

            if (modifierTokens.IsEmpty())
            {
                return false;
            }

            validModifiers = validModifiers ?? SpecializedCollections.EmptySet<SyntaxKind>();

            if (modifierTokens.IsProperSubsetOf(validModifiers))
            {
                // the parent is the member
                // the grandparent is the container of the member
                var container = token.Parent.GetParent();
                if (container.IsKind(SyntaxKind.CompilationUnit) ||
                    container.IsKind(SyntaxKind.NamespaceDeclaration) ||
                    container.IsKind(SyntaxKind.ClassDeclaration) ||
                    container.IsKind(SyntaxKind.StructDeclaration))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsNamespaceContext(
            this SyntaxTree syntaxTree,
            int position,
            CancellationToken cancellationToken,
            SemanticModel semanticModelOpt = null)
        {
            // first do quick exit check
            if (syntaxTree.IsInNonUserCode(position, cancellationToken) ||
                syntaxTree.IsRightOfDotOrArrow(position, cancellationToken))
            {
                return false;
            }

            var token = syntaxTree.FindTokenOnLeftOfPosition(position, cancellationToken)
                                  .GetPreviousTokenIfTouchingWord(position);

            // global::
            if (token.CSharpKind() == SyntaxKind.ColonColonToken &&
                token.GetPreviousToken(includeSkipped: true).CSharpKind() == SyntaxKind.GlobalKeyword)
            {
                return true;
            }

            // using |
            // but not:
            // using | = Bar

            // Note: we take care of the using alias case in the IsTypeContext
            // call below.

            if (token.CSharpKind() == SyntaxKind.UsingKeyword)
            {
                var usingDirective = token.GetAncestor<UsingDirectiveSyntax>();
                if (usingDirective != null)
                {
                    if (token.GetNextToken(includeSkipped: true).CSharpKind() != SyntaxKind.EqualsToken &&
                        usingDirective.Alias == null)
                    {
                        return true;
                    }
                }
            }

            // if it is not using directive location, most of places where 
            // type can appear, namespace can appear as well
            return syntaxTree.IsTypeContext(position, cancellationToken, semanticModelOpt);
        }

        public static bool IsDefinitelyNotTypeContext(this SyntaxTree syntaxTree, int position, CancellationToken cancellationToken)
        {
            return
                syntaxTree.IsInNonUserCode(position, cancellationToken) ||
                syntaxTree.IsRightOfDotOrArrow(position, cancellationToken);
        }

        public static bool IsTypeContext(
            this SyntaxTree syntaxTree, int position, CancellationToken cancellationToken, SemanticModel semanticModelOpt = null)
        {
            // first do quick exit check
            if (syntaxTree.IsDefinitelyNotTypeContext(position, cancellationToken))
            {
                return false;
            }

            // okay, now it is a case where we can't use parse tree (valid or error recovery) to
            // determine whether it is a right place to put type. use lex based one Cyrus created.

            var tokenOnLeftOfPosition = syntaxTree.FindTokenOnLeftOfPosition(position, cancellationToken);
            return
                syntaxTree.IsAfterKeyword(position, SyntaxKind.ConstKeyword, cancellationToken) ||
                syntaxTree.IsAfterKeyword(position, SyntaxKind.CaseKeyword, cancellationToken) ||
                syntaxTree.IsAfterKeyword(position, SyntaxKind.EventKeyword, cancellationToken) ||
                syntaxTree.IsAfterKeyword(position, SyntaxKind.StackAllocKeyword, cancellationToken) ||
                syntaxTree.IsAttributeNameContext(position, cancellationToken) ||
                syntaxTree.IsBaseClassOrInterfaceContext(position, cancellationToken) ||
                syntaxTree.IsCatchVariableDeclarationContext(position, cancellationToken) ||
                syntaxTree.IsDefiniteCastTypeContext(position, tokenOnLeftOfPosition, cancellationToken) ||
                syntaxTree.IsDelegateReturnTypeContext(position, tokenOnLeftOfPosition, cancellationToken) ||
                syntaxTree.IsExpressionContext(position, tokenOnLeftOfPosition, attributes: true, cancellationToken: cancellationToken, semanticModelOpt: semanticModelOpt) ||
                syntaxTree.IsPrimaryFunctionExpressionContext(position, tokenOnLeftOfPosition, cancellationToken) ||
                syntaxTree.IsGenericTypeArgumentContext(position, tokenOnLeftOfPosition, cancellationToken, semanticModelOpt) ||
                syntaxTree.IsFixedVariableDeclarationContext(position, tokenOnLeftOfPosition, cancellationToken) ||
                syntaxTree.IsImplicitOrExplicitOperatorTypeContext(position, tokenOnLeftOfPosition, cancellationToken) ||
                syntaxTree.IsIsOrAsTypeContext(position, tokenOnLeftOfPosition, cancellationToken) ||
                syntaxTree.IsLocalVariableDeclarationContext(position, tokenOnLeftOfPosition, cancellationToken) ||
                syntaxTree.IsObjectCreationTypeContext(position, tokenOnLeftOfPosition, cancellationToken) ||
                syntaxTree.IsParameterTypeContext(position, tokenOnLeftOfPosition, cancellationToken) ||
                syntaxTree.IsPossibleLambdaOrAnonymousMethodParameterTypeContext(position, tokenOnLeftOfPosition, cancellationToken) ||
                syntaxTree.IsStatementContext(position, tokenOnLeftOfPosition, cancellationToken) ||
                syntaxTree.IsTypeParameterConstraintContext(position, tokenOnLeftOfPosition, cancellationToken) ||
                syntaxTree.IsUsingAliasContext(position, cancellationToken) ||
                syntaxTree.IsGlobalMemberDeclarationContext(position, SyntaxKindSet.AllGlobalMemberModifiers, cancellationToken) ||
                syntaxTree.IsMemberDeclarationContext(
                    position,
                    contextOpt: null,
                    validModifiers: SyntaxKindSet.AllMemberModifiers,
                    validTypeDeclarations: SyntaxKindSet.ClassInterfaceStructTypeDeclarations,
                    canBePartial: false,
                    cancellationToken: cancellationToken);
        }

        public static bool IsBaseClassOrInterfaceContext(this SyntaxTree syntaxTree, int position, CancellationToken cancellationToken)
        {
            // class C : |
            // class C : Bar, |

            var token = syntaxTree.FindTokenOnLeftOfPosition(position, cancellationToken);
            token = token.GetPreviousTokenIfTouchingWord(position);

            if (token.CSharpKind() == SyntaxKind.ColonToken ||
                token.CSharpKind() == SyntaxKind.CommaToken)
            {
                if (token.IsParentKind(SyntaxKind.BaseList))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsUsingAliasContext(this SyntaxTree syntaxTree, int position, CancellationToken cancellationToken)
        {
            // using Foo = |

            var token = syntaxTree.FindTokenOnLeftOfPosition(position, cancellationToken);
            token = token.GetPreviousTokenIfTouchingWord(position);

            if (token.CSharpKind() == SyntaxKind.EqualsToken &&
                token.GetAncestor<UsingDirectiveSyntax>() != null)
            {
                return true;
            }

            return false;
        }

        public static bool IsTypeArgumentOfConstraintClause(
            this SyntaxTree syntaxTree, int position, CancellationToken cancellationToken)
        {
            // cases:
            //   where |
            //   class Foo<T> : Object where |

            var token = syntaxTree.FindTokenOnLeftOfPosition(position, cancellationToken);
            token = token.GetPreviousTokenIfTouchingWord(position);

            if (token.CSharpKind() == SyntaxKind.WhereKeyword &&
                token.IsParentKind(SyntaxKind.TypeParameterConstraintClause))
            {
                return true;
            }

            if (token.CSharpKind() == SyntaxKind.IdentifierToken &&
                token.HasMatchingText(SyntaxKind.WhereKeyword) &&
                token.IsParentKind(SyntaxKind.IdentifierName) &&
                token.Parent.IsParentKind(SyntaxKind.BaseList))
            {
                return true;
            }

            return false;
        }

        public static bool IsTypeParameterContraintStartContext(
            this SyntaxTree syntaxTree, int position, SyntaxToken tokenOnLeftOfPosition, CancellationToken cancellationToken)
        {
            // cases:
            //   where T : |

            var token = tokenOnLeftOfPosition;
            token = token.GetPreviousTokenIfTouchingWord(position);

            if (token.CSharpKind() == SyntaxKind.ColonToken &&
                token.GetPreviousToken(includeSkipped: true).CSharpKind() == SyntaxKind.IdentifierToken &&
                token.GetPreviousToken(includeSkipped: true).GetPreviousToken().CSharpKind() == SyntaxKind.WhereKeyword)
            {
                return true;
            }

            return false;
        }

        public static bool IsTypeParameterConstraintContext(this SyntaxTree syntaxTree, int position, SyntaxToken tokenOnLeftOfPosition, CancellationToken cancellationToken)
        {
            if (syntaxTree.IsTypeParameterContraintStartContext(position, tokenOnLeftOfPosition, cancellationToken))
            {
                return true;
            }

            var token = tokenOnLeftOfPosition;
            token = token.GetPreviousTokenIfTouchingWord(position);

            // Can't come after new()
            //
            //    where T : |
            //    where T : class, |
            //    where T : struct, |
            //    where T : Foo, |
            if (token.CSharpKind() == SyntaxKind.CommaToken &&
                token.IsParentKind(SyntaxKind.TypeParameterConstraintClause))
            {
                var constraintClause = token.Parent as TypeParameterConstraintClauseSyntax;

                // Check if there's a 'new()' constraint.  If there isn't, or we're before it, then
                // this is a type parameter constraint context. 
                var firstConstructorConstraint = constraintClause.Constraints.FirstOrDefault(t => t is ConstructorConstraintSyntax);
                if (firstConstructorConstraint == null || firstConstructorConstraint.SpanStart > token.Span.End)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsTypeOfExpressionContext(this SyntaxTree syntaxTree, int position, SyntaxToken tokenOnLeftOfPosition, CancellationToken cancellationToken)
        {
            var token = tokenOnLeftOfPosition.GetPreviousTokenIfTouchingWord(position);

            if (token.CSharpKind() == SyntaxKind.OpenParenToken && token.IsParentKind(SyntaxKind.TypeOfExpression))
            {
                return true;
            }

            return false;
        }

        public static bool IsDefaultExpressionContext(this SyntaxTree syntaxTree, int position, SyntaxToken tokenOnLeftOfPosition, CancellationToken cancellationToken)
        {
            var token = tokenOnLeftOfPosition.GetPreviousTokenIfTouchingWord(position);

            if (token.CSharpKind() == SyntaxKind.OpenParenToken && token.IsParentKind(SyntaxKind.DefaultExpression))
            {
                return true;
            }

            return false;
        }

        public static bool IsSizeOfExpressionContext(
            this SyntaxTree syntaxTree, int position, SyntaxToken tokenOnLeftOfPosition, CancellationToken cancellationToken)
        {
            var token = tokenOnLeftOfPosition.GetPreviousTokenIfTouchingWord(position);

            if (token.CSharpKind() == SyntaxKind.OpenParenToken && token.IsParentKind(SyntaxKind.SizeOfExpression))
            {
                return true;
            }

            return false;
        }

        public static bool IsGenericTypeArgumentContext(
            this SyntaxTree syntaxTree,
            int position,
            SyntaxToken tokenOnLeftOfPosition,
            CancellationToken cancellationToken,
            SemanticModel semanticModelOpt = null)
        {
            // cases: 
            //    Foo<|
            //    Foo<Bar,|
            //    Foo<Bar<Baz<int[],|
            var token = tokenOnLeftOfPosition;
            token = token.GetPreviousTokenIfTouchingWord(position);

            if (token.CSharpKind() != SyntaxKind.LessThanToken && token.CSharpKind() != SyntaxKind.CommaToken)
            {
                return false;
            }

            if (token.Parent is TypeArgumentListSyntax)
            {
                // Easy case, it was known to be a generic name, so this is a type argument context.
                return true;
            }

            SyntaxToken nameToken;
            if (!syntaxTree.IsInPartiallyWrittenGeneric(position, cancellationToken, out nameToken))
            {
                return false;
            }

            var name = nameToken.Parent as NameSyntax;
            if (name == null)
            {
                return false;
            }

            // Looks viable!  If they provided a binding, then check if it binds properly to
            // an actual generic entity.
            if (semanticModelOpt == null)
            {
                // No binding.  Just make the decision based on the syntax tree.
                return true;
            }

            // '?' is syntactically ambiguous in incomplete top-level statements:
            //
            // T ? foo<| 
            //
            // Might be an incomplete conditional expression or an incomplete declaration of a method returning a nullable type.
            // Bind T to see if it is a type. If it is we don't show signature help.
            if (name.IsParentKind(SyntaxKind.LessThanExpression) &&
                name.Parent.IsParentKind(SyntaxKind.ConditionalExpression) &&
                name.Parent.Parent.IsParentKind(SyntaxKind.ExpressionStatement) &&
                name.Parent.Parent.Parent.IsParentKind(SyntaxKind.GlobalStatement))
            {
                var conditionOrType = semanticModelOpt.GetSymbolInfo(
                    ((ConditionalExpressionSyntax)name.Parent.Parent).Condition, cancellationToken);
                if (conditionOrType.GetBestOrAllSymbols().FirstOrDefault() != null &&
                    conditionOrType.GetBestOrAllSymbols().FirstOrDefault().Kind == SymbolKind.NamedType)
                {
                    return false;
                }
            }

            var symbols = semanticModelOpt.LookupName(nameToken, namespacesAndTypesOnly: SyntaxFacts.IsInNamespaceOrTypeContext(name), cancellationToken: cancellationToken);
            return symbols.Any(s =>
                s.TypeSwitch(
                    (INamedTypeSymbol nt) => nt.Arity > 0,
                    (IMethodSymbol m) => m.Arity > 0));
        }

        public static bool IsParameterModifierContext(
            this SyntaxTree syntaxTree,
            int position,
            SyntaxToken tokenOnLeftOfPosition,
            CancellationToken cancellationToken,
            int? allowableIndex = null)
        {
            // cases:
            //   Foo(|
            //   Foo(int i, |
            //   Foo([Bar]|
            var token = tokenOnLeftOfPosition;
            token = token.GetPreviousTokenIfTouchingWord(position);

            if (token.CSharpKind() == SyntaxKind.OpenParenToken &&
                token.Parent.IsDelegateOrConstructorOrMethodParameterList())
            {
                if (allowableIndex.HasValue)
                {
                    if (allowableIndex.Value != 0)
                    {
                        return false;
                    }
                }

                return true;
            }

            if (token.CSharpKind() == SyntaxKind.CommaToken &&
                token.Parent.IsDelegateOrConstructorOrMethodParameterList())
            {
                if (allowableIndex.HasValue)
                {
                    var parameterList = token.GetAncestor<ParameterListSyntax>();
                    var commaIndex = parameterList.Parameters.GetWithSeparators().IndexOf(token);
                    var index = commaIndex / 2 + 1;
                    if (index != allowableIndex.Value)
                    {
                        return false;
                    }
                }

                return true;
            }

            if (token.CSharpKind() == SyntaxKind.CloseBracketToken &&
                token.IsParentKind(SyntaxKind.AttributeList) &&
                token.Parent.IsParentKind(SyntaxKind.Parameter) &&
                token.Parent.GetParent().GetParent().IsDelegateOrConstructorOrMethodParameterList())
            {
                if (allowableIndex.HasValue)
                {
                    var parameter = token.GetAncestor<ParameterSyntax>();
                    var parameterList = parameter.GetAncestorOrThis<ParameterListSyntax>();

                    int parameterIndex = parameterList.Parameters.IndexOf(parameter);
                    if (allowableIndex.Value != parameterIndex)
                    {
                        return false;
                    }
                }

                return true;
            }

            return false;
        }

        public static bool IsDelegateReturnTypeContext(
            this SyntaxTree syntaxTree, int position, SyntaxToken tokenOnLeftOfPosition, CancellationToken cancellationToken)
        {
            var token = tokenOnLeftOfPosition;
            token = token.GetPreviousTokenIfTouchingWord(position);

            if (token.CSharpKind() == SyntaxKind.DelegateKeyword &&
                token.IsParentKind(SyntaxKind.DelegateDeclaration))
            {
                return true;
            }

            return false;
        }

        public static bool IsImplicitOrExplicitOperatorTypeContext(
            this SyntaxTree syntaxTree, int position, SyntaxToken tokenOnLeftOfPosition, CancellationToken cancellationToken)
        {
            var token = tokenOnLeftOfPosition;
            token = token.GetPreviousTokenIfTouchingWord(position);

            if (token.CSharpKind() == SyntaxKind.OperatorKeyword)
            {
                if (token.GetPreviousToken(includeSkipped: true).CSharpKind() == SyntaxKind.ImplicitKeyword ||
                    token.GetPreviousToken(includeSkipped: true).CSharpKind() == SyntaxKind.ExplicitKeyword)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsParameterTypeContext(this SyntaxTree syntaxTree, int position, SyntaxToken tokenOnLeftOfPosition, CancellationToken cancellationToken)
        {
            var token = tokenOnLeftOfPosition.GetPreviousTokenIfTouchingWord(position);

            if (token.CSharpKind() == SyntaxKind.RefKeyword ||
                token.CSharpKind() == SyntaxKind.OutKeyword ||
                token.CSharpKind() == SyntaxKind.ParamsKeyword ||
                token.CSharpKind() == SyntaxKind.ThisKeyword)
            {
                position = token.SpanStart;
                tokenOnLeftOfPosition = syntaxTree.FindTokenOnLeftOfPosition(position, cancellationToken);
            }

            if (syntaxTree.IsParameterModifierContext(position, tokenOnLeftOfPosition, cancellationToken))
            {
                return true;
            }

            // int this[ |
            // int this[int i, |
            if (token.CSharpKind() == SyntaxKind.OpenParenToken ||
                token.CSharpKind() == SyntaxKind.OpenBracketToken ||
                token.CSharpKind() == SyntaxKind.CommaToken)
            {
                if (token.IsParentKind(SyntaxKind.ParameterList) || token.Parent.IsKind(SyntaxKind.BracketedParameterList))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsPossibleLambdaParameterModifierContext(
            this SyntaxTree syntaxTree, int position, SyntaxToken tokenOnLeftOfPosition, CancellationToken cancellationToken)
        {
            var token = tokenOnLeftOfPosition;
            token = token.GetPreviousTokenIfTouchingWord(position);

            if (token.CSharpKind() == SyntaxKind.OpenParenToken ||
                token.CSharpKind() == SyntaxKind.CommaToken)
            {
                if (token.IsParentKind(SyntaxKind.ParameterList) &&
                    token.Parent.IsParentKind(SyntaxKind.ParenthesizedLambdaExpression))
                {
                    return true;
                }

                // TODO(cyrusn): Tie into semantic analysis system to only 
                // consider this a lambda if this is a location where the
                // lambda's type would be inferred because of a delegate
                // or Expression<T> type.
                if (token.IsParentKind(SyntaxKind.ParenthesizedExpression))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsAnonymousMethodParameterModifierContext(
            this SyntaxTree syntaxTree, int position, SyntaxToken tokenOnLeftOfPosition, CancellationToken cancellationToken)
        {
            var token = tokenOnLeftOfPosition;
            token = token.GetPreviousTokenIfTouchingWord(position);

            if (token.CSharpKind() == SyntaxKind.OpenParenToken ||
                token.CSharpKind() == SyntaxKind.CommaToken)
            {
                if (token.IsParentKind(SyntaxKind.ParameterList) &&
                    token.Parent.IsParentKind(SyntaxKind.AnonymousMethodExpression))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsPossibleLambdaOrAnonymousMethodParameterTypeContext(
            this SyntaxTree syntaxTree, int position, SyntaxToken tokenOnLeftOfPosition, CancellationToken cancellationToken)
        {
            var token = tokenOnLeftOfPosition;
            token = token.GetPreviousTokenIfTouchingWord(position);

            if (token.CSharpKind() == SyntaxKind.RefKeyword ||
                token.CSharpKind() == SyntaxKind.OutKeyword)
            {
                position = token.SpanStart;
                tokenOnLeftOfPosition = syntaxTree.FindTokenOnLeftOfPosition(position, cancellationToken);
            }

            if (IsAnonymousMethodParameterModifierContext(syntaxTree, position, tokenOnLeftOfPosition, cancellationToken) ||
                IsPossibleLambdaParameterModifierContext(syntaxTree, position, tokenOnLeftOfPosition, cancellationToken))
            {
                return true;
            }

            return false;
        }

        public static bool IsValidContextForFromClause(
            this SyntaxTree syntaxTree,
            int position,
            SyntaxToken tokenOnLeftOfPosition,
            CancellationToken cancellationToken,
            SemanticModel semanticModelOpt = null)
        {
            if (syntaxTree.IsExpressionContext(position, tokenOnLeftOfPosition, attributes: false, cancellationToken: cancellationToken, semanticModelOpt: semanticModelOpt) &&
                !syntaxTree.IsConstantExpressionContext(position, tokenOnLeftOfPosition, cancellationToken))
            {
                return true;
            }

            // cases:
            //   var q = |
            //   var q = f|
            //
            //   var q = from x in y
            //           |
            //
            //   var q = from x in y
            //           f|
            //
            // this list is *not* exhaustive.
            // the first two are handled by 'IsExpressionContext'

            var token = tokenOnLeftOfPosition;
            token = token.GetPreviousTokenIfTouchingWord(position);

            // var q = from x in y
            //         |
            if (!token.IntersectsWith(position) &&
                token.IsLastTokenOfQueryClause())
            {
                return true;
            }

            return false;
        }

        public static bool IsValidContextForJoinClause(
            this SyntaxTree syntaxTree, int position, SyntaxToken tokenOnLeftOfPosition, CancellationToken cancellationToken)
        {
            var token = tokenOnLeftOfPosition;
            token = token.GetPreviousTokenIfTouchingWord(position);

            // var q = from x in y
            //         |
            if (!token.IntersectsWith(position) &&
                token.IsLastTokenOfQueryClause())
            {
                return true;
            }

            return false;
        }

        public static bool IsLocalVariableDeclarationContext(
            this SyntaxTree syntaxTree, int position, SyntaxToken tokenOnLeftOfPosition, CancellationToken cancellationToken)
        {
            // cases:
            //  const var
            //  for (var
            //  foreach (var
            //  using (var
            //  from var
            //  join var

            var token = tokenOnLeftOfPosition;
            token = token.GetPreviousTokenIfTouchingWord(position);

            if (token.CSharpKind() == SyntaxKind.ConstKeyword &&
                token.IsParentKind(SyntaxKind.LocalDeclarationStatement))
            {
                return true;
            }

            if (token.CSharpKind() == SyntaxKind.OpenParenToken)
            {
                var previous = token.GetPreviousToken(includeSkipped: true);
                if (previous.CSharpKind() == SyntaxKind.ForKeyword ||
                    previous.CSharpKind() == SyntaxKind.ForEachKeyword ||
                    previous.CSharpKind() == SyntaxKind.UsingKeyword)
                {
                    return true;
                }
            }

            var tokenOnLeftOfStart = syntaxTree.FindTokenOnLeftOfPosition(token.SpanStart, cancellationToken);
            if (token.IsKindOrHasMatchingText(SyntaxKind.FromKeyword) &&
                syntaxTree.IsValidContextForFromClause(token.SpanStart, tokenOnLeftOfStart, cancellationToken))
            {
                return true;
            }

            if (token.IsKind(SyntaxKind.JoinKeyword) &&
                syntaxTree.IsValidContextForJoinClause(token.SpanStart, tokenOnLeftOfStart, cancellationToken))
            {
                return true;
            }

            return false;
        }

        public static bool IsFixedVariableDeclarationContext(
            this SyntaxTree syntaxTree, int position, SyntaxToken tokenOnLeftOfPosition, CancellationToken cancellationToken)
        {
            // cases:
            //  fixed (var

            var token = tokenOnLeftOfPosition;
            token = token.GetPreviousTokenIfTouchingWord(position);

            if (token.CSharpKind() == SyntaxKind.OpenParenToken &&
                token.GetPreviousToken(includeSkipped: true).CSharpKind() == SyntaxKind.FixedKeyword)
            {
                return true;
            }

            return false;
        }

        public static bool IsCatchVariableDeclarationContext(this SyntaxTree syntaxTree, int position, CancellationToken cancellationToken)
        {
            // cases:
            //  catch (var

            var token = syntaxTree.FindTokenOnLeftOfPosition(position, cancellationToken);
            token = token.GetPreviousTokenIfTouchingWord(position);

            if (token.CSharpKind() == SyntaxKind.OpenParenToken &&
                token.GetPreviousToken(includeSkipped: true).CSharpKind() == SyntaxKind.CatchKeyword)
            {
                return true;
            }

            return false;
        }

        public static bool IsIsOrAsTypeContext(this SyntaxTree syntaxTree, int position, SyntaxToken tokenOnLeftOfPosition, CancellationToken cancellationToken)
        {
            var token = tokenOnLeftOfPosition;
            token = token.GetPreviousTokenIfTouchingWord(position);

            if (token.CSharpKind() == SyntaxKind.IsKeyword ||
                token.CSharpKind() == SyntaxKind.AsKeyword)
            {
                return true;
            }

            return false;
        }

        public static bool IsObjectCreationTypeContext(
            this SyntaxTree syntaxTree, int position, SyntaxToken tokenOnLeftOfPosition, CancellationToken cancellationToken)
        {
            var token = tokenOnLeftOfPosition.GetPreviousTokenIfTouchingWord(position);

            if (token.CSharpKind() == SyntaxKind.NewKeyword)
            {
                // we can follow a 'new' if it's the 'new' for an expression.
                var start = token.SpanStart;
                var tokenOnLeftOfStart = syntaxTree.FindTokenOnLeftOfPosition(start, cancellationToken);
                return
                    IsNonConstantExpressionContext(syntaxTree, token.SpanStart, tokenOnLeftOfStart, cancellationToken) ||
                    syntaxTree.IsStatementContext(token.SpanStart, tokenOnLeftOfStart, cancellationToken) ||
                    syntaxTree.IsGlobalStatementContext(token.SpanStart, cancellationToken);
            }

            return false;
        }

        private static bool IsNonConstantExpressionContext(SyntaxTree syntaxTree, int position, SyntaxToken tokenOnLeftOfPosition, CancellationToken cancellationToken)
        {
            return
                syntaxTree.IsExpressionContext(position, tokenOnLeftOfPosition, attributes: true, cancellationToken: cancellationToken) &&
                !syntaxTree.IsConstantExpressionContext(position, tokenOnLeftOfPosition, cancellationToken);
        }

        public static bool IsPreProcessorDirectiveContext(this SyntaxTree syntaxTree, int position, SyntaxToken preProcessorTokenOnLeftOfPosition, CancellationToken cancellationToken)
        {
            var token = preProcessorTokenOnLeftOfPosition;
            var directive = token.GetAncestor<DirectiveTriviaSyntax>();

            // Directives contain the EOL, so if the position is within the full span of the
            // directive, then it is on that line, the only exception is if the directive is on the
            // last line, the position at the end if technically not contained by the directive but
            // its also not on a new line, so it should be considered part of the preprocessor
            // context.
            if (directive == null)
            {
                return false;
            }

            return
                directive.FullSpan.Contains(position) ||
                directive.FullSpan.End == syntaxTree.GetRoot(cancellationToken).FullSpan.End;
        }

        public static bool IsPreProcessorDirectiveContext(this SyntaxTree syntaxTree, int position, CancellationToken cancellationToken)
        {
            var leftToken = syntaxTree.FindTokenOnLeftOfPosition(position, cancellationToken, includeDirectives: true);

            return syntaxTree.IsPreProcessorDirectiveContext(position, leftToken, cancellationToken);
        }

        public static bool IsPreProcessorKeywordContext(this SyntaxTree syntaxTree, int position, CancellationToken cancellationToken)
        {
            return IsPreProcessorKeywordContext(
                syntaxTree, position,
                syntaxTree.FindTokenOnLeftOfPosition(position, cancellationToken, includeDirectives: true),
                cancellationToken);
        }

        public static bool IsPreProcessorKeywordContext(this SyntaxTree syntaxTree, int position, SyntaxToken preProcessorTokenOnLeftOfPosition, CancellationToken cancellationToken)
        {
            // cases:
            //  #|
            //  #d|
            //  # |
            //  # d|

            // note: comments are not allowed between the # and item.
            var token = preProcessorTokenOnLeftOfPosition;
            token = token.GetPreviousTokenIfTouchingWord(position);

            if (token.CSharpKind() == SyntaxKind.HashToken)
            {
                return true;
            }

            return false;
        }

        public static bool IsStatementContext(this SyntaxTree syntaxTree, int position, SyntaxToken tokenOnLeftOfPosition, CancellationToken cancellationToken)
        {
#if false
            // we're in a statement if the thing that comes before allows for
            // statements to follow.  Or if we're on a just started identifier
            // in the first position where a statement can go.
            if (syntaxTree.IsInPreprocessorDirectiveContext(position, cancellationToken))
            {
                return false;
            }
#endif

            var token = tokenOnLeftOfPosition;
            token = token.GetPreviousTokenIfTouchingWord(position);

            return token.IsBeginningOfStatementContext();
        }

        public static bool IsGlobalStatementContext(this SyntaxTree syntaxTree, int position, CancellationToken cancellationToken)
        {
            if (!syntaxTree.IsInteractiveOrScript())
            {
                return false;
            }

#if false
            if (syntaxTree.IsInPreprocessorDirectiveContext(position, cancellationToken))
            {
                return false;
            }
#endif

            var token = syntaxTree.FindTokenOnLeftOfPosition(position, cancellationToken)
                                  .GetPreviousTokenIfTouchingWord(position);

            if (token.CSharpKind() == SyntaxKind.None)
            {
                // global statements can't come before usings/externs
                var compilationUnit = syntaxTree.GetRoot(cancellationToken) as CompilationUnitSyntax;
                if (compilationUnit != null &&
                    (compilationUnit.Externs.Count > 0 ||
                    compilationUnit.Usings.Count > 0))
                {
                    return false;
                }

                return true;
            }

            return token.IsBeginningOfGlobalStatementContext();
        }

        public static bool IsInstanceContext(this SyntaxTree syntaxTree, int position, SyntaxToken tokenOnLeftOfPosition, CancellationToken cancellationToken)
        {
#if false
            if (syntaxTree.IsInPreprocessorDirectiveContext(position, cancellationToken))
            {
                return false;
            }
#endif

            var token = tokenOnLeftOfPosition;

            // We're in an instance context if we're in the body of an instance member
            var containingMember = token.GetAncestor<MemberDeclarationSyntax>();
            if (containingMember == null)
            {
                return false;
            }

            var modifiers = containingMember.GetModifiers();
            if (modifiers.Any(SyntaxKind.StaticKeyword))
            {
                return false;
            }

            // Must be a property or something method-like.
            if (containingMember.HasMethodShape())
            {
                var body = containingMember.GetBody();
                return IsInBlock(body, position);
            }

            var accessor = token.GetAncestor<AccessorDeclarationSyntax>();
            if (accessor != null)
            {
                return IsInBlock(accessor.Body, position);
            }

            return false;
        }

        private static bool IsInBlock(BlockSyntax bodyOpt, int position)
        {
            if (bodyOpt == null)
            {
                return false;
            }

            return bodyOpt.OpenBraceToken.Span.End <= position &&
                (bodyOpt.CloseBraceToken.IsMissing || position <= bodyOpt.CloseBraceToken.SpanStart);
        }

        public static bool IsPossibleCastTypeContext(
            this SyntaxTree syntaxTree, int position, SyntaxToken tokenOnLeftOfPosition, CancellationToken cancellationToken)
        {
            var token = tokenOnLeftOfPosition.GetPreviousTokenIfTouchingWord(position);

            if (token.IsKind(SyntaxKind.OpenParenToken) &&
                syntaxTree.IsExpressionContext(token.SpanStart, syntaxTree.FindTokenOnLeftOfPosition(token.SpanStart, cancellationToken), false, cancellationToken))
            {
                return true;
            }

            return false;
        }

        public static bool IsDefiniteCastTypeContext(this SyntaxTree syntaxTree, int position, SyntaxToken tokenOnLeftOfPosition, CancellationToken cancellationToken)
        {
            var token = tokenOnLeftOfPosition;
            token = token.GetPreviousTokenIfTouchingWord(position);

            if (token.CSharpKind() == SyntaxKind.OpenParenToken &&
                token.IsParentKind(SyntaxKind.CastExpression))
            {
                return true;
            }

            return false;
        }

#if false
        public static bool IsNonConstantExpressionContext(
            this SyntaxTree syntaxTree,
            int position,
            bool attributes,
            CancellationToken cancellationToken,
            SemanticModel semanticModelOpt = null)
        {
            return
                syntaxTree.IsExpressionContext(position, attributes, cancellationToken, semanticModelOpt) &&
                !syntaxTree.IsConstantExpressionContext(position, cancellationToken);
        }
#endif

        public static bool IsConstantExpressionContext(this SyntaxTree syntaxTree, int position, SyntaxToken tokenOnLeftOfPosition, CancellationToken cancellationToken)
        {
            var token = tokenOnLeftOfPosition.GetPreviousTokenIfTouchingWord(position);

            // case |
            if (token.CSharpKind() == SyntaxKind.CaseKeyword &&
                token.IsParentKind(SyntaxKind.CaseSwitchLabel))
            {
                return true;
            }

            // goto case |
            if (token.CSharpKind() == SyntaxKind.CaseKeyword &&
                token.IsParentKind(SyntaxKind.GotoCaseStatement))
            {
                return true;
            }

            if (token.CSharpKind() == SyntaxKind.EqualsToken &&
                token.IsParentKind(SyntaxKind.EqualsValueClause))
            {
                var equalsValue = (EqualsValueClauseSyntax)token.Parent;

                if (equalsValue.IsParentKind(SyntaxKind.VariableDeclarator) &&
                    equalsValue.Parent.IsParentKind(SyntaxKind.VariableDeclaration))
                {
                    // class C { const int i = |
                    var fieldDeclaration = equalsValue.GetAncestor<FieldDeclarationSyntax>();
                    if (fieldDeclaration != null)
                    {
                        return fieldDeclaration.Modifiers.Any(SyntaxKind.ConstKeyword);
                    }

                    // void M() { const int i = |
                    var localDeclaration = equalsValue.GetAncestor<LocalDeclarationStatementSyntax>();
                    if (localDeclaration != null)
                    {
                        return localDeclaration.Modifiers.Any(SyntaxKind.ConstKeyword);
                    }
                }

                // enum E { A = |
                if (equalsValue.IsParentKind(SyntaxKind.EnumMemberDeclaration))
                {
                    return true;
                }

                // void M(int i = |
                if (equalsValue.IsParentKind(SyntaxKind.Parameter))
                {
                    return true;
                }
            }

            // [Foo( |
            // [Foo(x, |
            if (token.IsParentKind(SyntaxKind.AttributeArgumentList) &&
               (token.CSharpKind() == SyntaxKind.CommaToken ||
                token.CSharpKind() == SyntaxKind.OpenParenToken))
            {
                return true;
            }

            // [Foo(x: |
            if (token.CSharpKind() == SyntaxKind.ColonToken &&
                token.IsParentKind(SyntaxKind.NameColon) &&
                token.Parent.IsParentKind(SyntaxKind.AttributeArgument))
            {
                return true;
            }

            // [Foo(X = |
            if (token.CSharpKind() == SyntaxKind.EqualsToken &&
                token.IsParentKind(SyntaxKind.NameEquals) &&
                token.Parent.IsParentKind(SyntaxKind.AttributeArgument))
            {
                return true;
            }

            // TODO: Fixed-size buffer declarations

            return false;
        }

        public static bool IsLabelContext(this SyntaxTree syntaxTree, int position, CancellationToken cancellationToken)
        {
            var token = syntaxTree.FindTokenOnLeftOfPosition(position, cancellationToken);

            var gotoStatement = token.GetAncestor<GotoStatementSyntax>();
            if (gotoStatement != null)
            {
                if (gotoStatement.GotoKeyword == token)
                {
                    return true;
                }

                if (gotoStatement.Expression != null &&
                    !gotoStatement.Expression.IsMissing &&
                    gotoStatement.Expression is IdentifierNameSyntax &&
                    ((IdentifierNameSyntax)gotoStatement.Expression).Identifier == token &&
                    token.IntersectsWith(position))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsExpressionContext(
            this SyntaxTree syntaxTree,
            int position,
            SyntaxToken tokenOnLeftOfPosition,
            bool attributes,
            CancellationToken cancellationToken,
            SemanticModel semanticModelOpt = null)
        {
#if false
            if (syntaxTree.IsInPreprocessorDirectiveContext(position, cancellationToken))
            {
                return false;
            }
#endif

            // cases:
            //   var q = |
            //   var q = a|
            // this list is *not* exhaustive.

            var token = tokenOnLeftOfPosition.GetPreviousTokenIfTouchingWord(position);

            if (token.GetAncestor<ConditionalDirectiveTriviaSyntax>() != null)
            {
                return false;
            }

            if (!attributes)
            {
                if (token.GetAncestor<AttributeListSyntax>() != null)
                {
                    return false;
                }
            }

            if (syntaxTree.IsConstantExpressionContext(position, tokenOnLeftOfPosition, cancellationToken))
            {
                return true;
            }

            // no expressions after .   ::   ->
            if (token.CSharpKind() == SyntaxKind.DotToken ||
                token.CSharpKind() == SyntaxKind.ColonColonToken ||
                token.CSharpKind() == SyntaxKind.MinusGreaterThanToken)
            {
                return false;
            }

            // Normally you can have any sort of expression after an equals. However, this does not
            // apply to a "using Foo = ..." situation.
            if (token.CSharpKind() == SyntaxKind.EqualsToken)
            {
                if (token.IsParentKind(SyntaxKind.NameEquals) &&
                    token.Parent.IsParentKind(SyntaxKind.UsingDirective))
                {
                    return false;
                }
            }

            // q = |
            // q -= |
            // q *= |
            // q += |
            // q /= |
            // q ^= |
            // q %= |
            // q &= |
            // q |= |
            // q <<= |
            // q >>= |
            if (token.CSharpKind() == SyntaxKind.EqualsToken ||
                token.CSharpKind() == SyntaxKind.MinusEqualsToken ||
                token.CSharpKind() == SyntaxKind.AsteriskEqualsToken ||
                token.CSharpKind() == SyntaxKind.PlusEqualsToken ||
                token.CSharpKind() == SyntaxKind.SlashEqualsToken ||
                token.CSharpKind() == SyntaxKind.ExclamationEqualsToken ||
                token.CSharpKind() == SyntaxKind.CaretEqualsToken ||
                token.CSharpKind() == SyntaxKind.AmpersandEqualsToken ||
                token.CSharpKind() == SyntaxKind.BarEqualsToken ||
                token.CSharpKind() == SyntaxKind.PercentEqualsToken ||
                token.CSharpKind() == SyntaxKind.LessThanLessThanEqualsToken ||
                token.CSharpKind() == SyntaxKind.GreaterThanGreaterThanEqualsToken)
            {
                return true;
            }

            // ( |
            if (token.CSharpKind() == SyntaxKind.OpenParenToken &&
                token.IsParentKind(SyntaxKind.ParenthesizedExpression))
            {
                return true;
            }

            // - |
            // + |
            // ~ |
            // ! |
            if (token.Parent is PrefixUnaryExpressionSyntax)
            {
                var prefix = token.Parent as PrefixUnaryExpressionSyntax;
                return prefix.OperatorToken == token;
            }

            // not sure about these:
            //   ++ |
            //   -- |
#if false
                token.Kind == SyntaxKind.PlusPlusToken ||
                token.Kind == SyntaxKind.DashDashToken)
#endif
            // Check for binary operators.
            // Note:
            //   - We handle < specially as it can be ambiguous with generics.
            //   - We handle * specially because it can be ambiguous with pointer types.

            // a *
            // a /
            // a %
            // a +
            // a -
            // a <<
            // a >>
            // a <
            // a >
            // a &&
            // a ||
            // a &
            // a |
            // a ^
            if (token.Parent is BinaryExpressionSyntax)
            {
                // If the client provided a binding, then check if this is actually generic.  If so,
                // then this is not an expression context. i.e. if we have "Foo < |" then it could
                // be an expression context, or it could be a type context if Foo binds to a type or
                // method.
                if (semanticModelOpt != null && syntaxTree.IsGenericTypeArgumentContext(position, tokenOnLeftOfPosition, cancellationToken, semanticModelOpt))
                {
                    return false;
                }

                var binary = token.Parent as BinaryExpressionSyntax;
                if (binary.OperatorToken == token)
                {
                    // If this is a multiplication expression and a semantic model was passed in,
                    // check to see if the expression to the left is a type name. If it is, treat
                    // this as a pointer type.
                    if (token.CSharpKind() == SyntaxKind.AsteriskToken && semanticModelOpt != null)
                    {
                        var type = binary.Left as TypeSyntax;
                        if (type != null && type.IsPotentialTypeName(semanticModelOpt, cancellationToken))
                        {
                            return false;
                        }
                    }

                    return true;
                }
            }

            // Special case:
            //    Foo * bar
            //    Foo ? bar
            // This parses as a local decl called bar of type Foo* or Foo?
            if (tokenOnLeftOfPosition.IntersectsWith(position) &&
                tokenOnLeftOfPosition.CSharpKind() == SyntaxKind.IdentifierToken)
            {
                var previousToken = tokenOnLeftOfPosition.GetPreviousToken(includeSkipped: true);
                if (previousToken.CSharpKind() == SyntaxKind.AsteriskToken ||
                    previousToken.CSharpKind() == SyntaxKind.QuestionToken)
                {
                    if (previousToken.IsParentKind(SyntaxKind.PointerType) ||
                        previousToken.IsParentKind(SyntaxKind.NullableType))
                    {
                        var type = previousToken.Parent as TypeSyntax;
                        if (type.IsParentKind(SyntaxKind.VariableDeclaration) &&
                            type.Parent.IsParentKind(SyntaxKind.LocalDeclarationStatement))
                        {
                            var declStatement = type.Parent.Parent as LocalDeclarationStatementSyntax;

                            // note, this doesn't apply for cases where we know it 
                            // absolutely is not multiplcation or a conditional expression.
                            var underlyingType = type is PointerTypeSyntax
                                ? ((PointerTypeSyntax)type).ElementType
                                : ((NullableTypeSyntax)type).ElementType;

                            if (!underlyingType.IsPotentialTypeName(semanticModelOpt, cancellationToken))
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            // new int[|
            // new int[expr, |
            if (token.CSharpKind() == SyntaxKind.OpenBracketToken ||
                token.CSharpKind() == SyntaxKind.CommaToken)
            {
                if (token.IsParentKind(SyntaxKind.ArrayRankSpecifier))
                {
                    return true;
                }
            }

            // foo ? |
            if (token.CSharpKind() == SyntaxKind.QuestionToken &&
                token.IsParentKind(SyntaxKind.ConditionalExpression))
            {
                // If the condition is simply a TypeSyntax that binds to a type, treat this as a nullable type.
                var conditionalExpression = (ConditionalExpressionSyntax)token.Parent;
                var type = conditionalExpression.Condition as TypeSyntax;

                return type == null
                    || !type.IsPotentialTypeName(semanticModelOpt, cancellationToken);
            }

            // foo ? bar : |
            if (token.CSharpKind() == SyntaxKind.ColonToken &&
                token.IsParentKind(SyntaxKind.ConditionalExpression))
            {
                return true;
            }

            // typeof(|
            // default(|
            // sizeof(|
            if (token.CSharpKind() == SyntaxKind.OpenParenToken)
            {
                if (token.IsParentKind(SyntaxKind.TypeOfExpression) ||
                    token.IsParentKind(SyntaxKind.DefaultExpression) ||
                    token.IsParentKind(SyntaxKind.SizeOfExpression))
                {
                    return false;
                }
            }

            // Foo(|
            // Foo(expr, |
            // this[|
            if (token.CSharpKind() == SyntaxKind.OpenParenToken ||
                token.CSharpKind() == SyntaxKind.OpenBracketToken ||
                token.CSharpKind() == SyntaxKind.CommaToken)
            {
                if (token.IsParentKind(SyntaxKind.ArgumentList) || token.Parent.IsKind(SyntaxKind.BracketedArgumentList))
                {
                    return true;
                }
            }

            // [Foo(|
            // [Foo(expr, |
            if (attributes)
            {
                if (token.CSharpKind() == SyntaxKind.OpenParenToken ||
                    token.CSharpKind() == SyntaxKind.CommaToken)
                {
                    if (token.IsParentKind(SyntaxKind.AttributeArgumentList))
                    {
                        return true;
                    }
                }
            }

            // Foo(ref |
            // Foo(bar |
            if (token.CSharpKind() == SyntaxKind.RefKeyword ||
                token.CSharpKind() == SyntaxKind.OutKeyword)
            {
                if (token.IsParentKind(SyntaxKind.Argument))
                {
                    return true;
                }
            }

            // Foo(bar: |
            if (token.CSharpKind() == SyntaxKind.ColonToken &&
                token.IsParentKind(SyntaxKind.NameColon) &&
                token.Parent.IsParentKind(SyntaxKind.Argument))
            {
                return true;
            }

            // a => |
            if (token.CSharpKind() == SyntaxKind.EqualsGreaterThanToken)
            {
                return true;
            }

            // new List<int> { |
            // new List<int> { expr, |
            if (token.CSharpKind() == SyntaxKind.OpenBraceToken ||
                token.CSharpKind() == SyntaxKind.CommaToken)
            {
                if (token.Parent is InitializerExpressionSyntax)
                {
                    // The compiler treats the ambiguous case as an object initializer, so we'll say
                    // expressions are legal here
                    if (token.Parent.CSharpKind() == SyntaxKind.ObjectInitializerExpression && token.CSharpKind() == SyntaxKind.OpenBraceToken)
                    {
                        // In this position { a$$ =, the user is trying to type an object initializer.
                        if (!token.IntersectsWith(position) && token.GetNextToken().GetNextToken().CSharpKind() == SyntaxKind.EqualsToken)
                        {
                            return false;
                        }

                        return true;
                    }

                    // Perform a semantic check to determine whether or not the type being created
                    // can support a collection initializer. If not, this must be an object initializer
                    // and can't be an expression context.
                    if (semanticModelOpt != null &&
                        token.Parent.IsParentKind(SyntaxKind.ObjectCreationExpression))
                    {
                        var objectCreation = (ObjectCreationExpressionSyntax)token.Parent.Parent;
                        var type = semanticModelOpt.GetSymbolInfo(objectCreation.Type, cancellationToken).Symbol as ITypeSymbol;
                        if (type != null && !type.CanSupportCollectionInitializer())
                        {
                            return false;
                        }
                    }

                    return true;
                }
            }

            // for (; |
            // for (; ; |
            if (token.CSharpKind() == SyntaxKind.SemicolonToken &&
                token.IsParentKind(SyntaxKind.ForStatement))
            {
                var forStatement = (ForStatementSyntax)token.Parent;
                if (token == forStatement.FirstSemicolonToken ||
                    token == forStatement.SecondSemicolonToken)
                {
                    return true;
                }
            }

            // for ( |
            if (token.CSharpKind() == SyntaxKind.OpenParenToken &&
                token.IsParentKind(SyntaxKind.ForStatement))
            {
                var forStatement = (ForStatementSyntax)token.Parent;
                if (token == forStatement.OpenParenToken)
                {
                    return true;
                }
            }

            // for (; ; Foo(), | 
            // for ( Foo(), |
            if (token.CSharpKind() == SyntaxKind.CommaToken &&
                token.IsParentKind(SyntaxKind.ForStatement))
            {
                return true;
            }

            // foreach (var v in |
            // from a in |
            // join b in |
            if (token.CSharpKind() == SyntaxKind.InKeyword)
            {
                if (token.IsParentKind(SyntaxKind.ForEachStatement) ||
                    token.IsParentKind(SyntaxKind.FromClause) ||
                    token.IsParentKind(SyntaxKind.JoinClause))
                {
                    return true;
                }
            }

            // join x in y on |
            // join x in y on a equals |
            if (token.CSharpKind() == SyntaxKind.OnKeyword ||
                token.CSharpKind() == SyntaxKind.EqualsKeyword)
            {
                if (token.IsParentKind(SyntaxKind.JoinClause))
                {
                    return true;
                }
            }

            // where |
            if (token.CSharpKind() == SyntaxKind.WhereKeyword &&
                token.IsParentKind(SyntaxKind.WhereClause))
            {
                return true;
            }

            // orderby |
            // orderby a, |
            if (token.CSharpKind() == SyntaxKind.OrderByKeyword ||
                token.CSharpKind() == SyntaxKind.CommaToken)
            {
                if (token.IsParentKind(SyntaxKind.OrderByClause))
                {
                    return true;
                }
            }

            // select |
            if (token.CSharpKind() == SyntaxKind.SelectKeyword &&
                token.IsParentKind(SyntaxKind.SelectClause))
            {
                return true;
            }

            // group |
            // group expr by |
            if (token.CSharpKind() == SyntaxKind.GroupKeyword ||
                token.CSharpKind() == SyntaxKind.ByKeyword)
            {
                if (token.IsParentKind(SyntaxKind.GroupClause))
                {
                    return true;
                }
            }

            // return |
            // yield return |
            // but not: [return |
            if (token.CSharpKind() == SyntaxKind.ReturnKeyword)
            {
                if (token.GetPreviousToken(includeSkipped: true).CSharpKind() != SyntaxKind.OpenBracketToken)
                {
                    return true;
                }
            }

            // throw |
            if (token.CSharpKind() == SyntaxKind.ThrowKeyword)
            {
                return true;
            }

            // while ( |
            if (token.CSharpKind() == SyntaxKind.OpenParenToken &&
                token.GetPreviousToken(includeSkipped: true).CSharpKind() == SyntaxKind.WhileKeyword)
            {
                return true;
            }

            // todo: handle 'for' cases.

            // using ( |
            if (token.CSharpKind() == SyntaxKind.OpenParenToken &&
                token.GetPreviousToken(includeSkipped: true).CSharpKind() == SyntaxKind.UsingKeyword)
            {
                return true;
            }

            // lock ( |
            if (token.CSharpKind() == SyntaxKind.OpenParenToken &&
                token.GetPreviousToken(includeSkipped: true).CSharpKind() == SyntaxKind.LockKeyword)
            {
                return true;
            }

            // lock ( |
            if (token.CSharpKind() == SyntaxKind.OpenParenToken &&
                token.GetPreviousToken(includeSkipped: true).CSharpKind() == SyntaxKind.IfKeyword)
            {
                return true;
            }

            // switch ( |
            if (token.CSharpKind() == SyntaxKind.OpenParenToken &&
                token.GetPreviousToken(includeSkipped: true).CSharpKind() == SyntaxKind.SwitchKeyword)
            {
                return true;
            }

            // checked ( |
            if (token.CSharpKind() == SyntaxKind.OpenParenToken &&
                token.GetPreviousToken(includeSkipped: true).CSharpKind() == SyntaxKind.CheckedKeyword)
            {
                return true;
            }

            // unchecked ( |
            if (token.CSharpKind() == SyntaxKind.OpenParenToken &&
                token.GetPreviousToken(includeSkipped: true).CSharpKind() == SyntaxKind.UncheckedKeyword)
            {
                return true;
            }

            // (SometType) |
            if (token.IsAfterPossibleCast())
            {
                return true;
            }

            // In anonymous type initializer.
            //
            // new { | We allow new inside of anonymous object member declarators, so that the user
            // can dot into a member afterward. For example:
            //
            // var a = new { new C().Foo };
            if (token.CSharpKind() == SyntaxKind.OpenBraceToken || token.CSharpKind() == SyntaxKind.CommaToken)
            {
                if (token.IsParentKind(SyntaxKind.AnonymousObjectCreationExpression))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsIsOrAsContext(this SyntaxTree syntaxTree, int position, SyntaxToken tokenOnLeftOfPosition, CancellationToken cancellationToken)
        {
            // cases:
            //    expr |

            var token = tokenOnLeftOfPosition;
            token = token.GetPreviousTokenIfTouchingWord(position);

            if (token.GetAncestor<BlockSyntax>() == null)
            {
                return false;
            }

            // is/as are valid after expressions.
            if (token.IsLastTokenOfNode<ExpressionSyntax>())
            {
                // However, many names look like expressions.  For example:
                //    foreach (var |
                // ('var' is a TypeSyntax which is an expression syntax.

                var type = token.GetAncestors<TypeSyntax>().LastOrDefault();
                if (type == null)
                {
                    return true;
                }

                if (type.IsKind(SyntaxKind.GenericName) ||
                    type.IsKind(SyntaxKind.AliasQualifiedName) ||
                    type.IsKind(SyntaxKind.PredefinedType))
                {
                    return false;
                }

                ExpressionSyntax nameExpr = type;
                if (IsRightSideName(nameExpr))
                {
                    nameExpr = (ExpressionSyntax)nameExpr.Parent;
                }

                // If this name is the start of a local variable declaration context, we
                // shouldn't show is or as. For example: for(var |
                if (syntaxTree.IsLocalVariableDeclarationContext(token.SpanStart, syntaxTree.FindTokenOnLeftOfPosition(token.SpanStart, cancellationToken), cancellationToken))
                {
                    return false;
                }

                // Not on the left hand side of an object initializer
                if (token.MatchesKind(SyntaxKind.IdentifierToken) &&
                    token.IsParentKind(SyntaxKind.IdentifierName) &&
                    (token.Parent.IsParentKind(SyntaxKind.ObjectInitializerExpression) || token.Parent.IsParentKind(SyntaxKind.CollectionInitializerExpression)))
                {
                    return false;
                }

                // Now, make sure the name was actually in a location valid for
                // an expression.  If so, then we know we can follow it.
                if (syntaxTree.IsExpressionContext(nameExpr.SpanStart, syntaxTree.FindTokenOnLeftOfPosition(nameExpr.SpanStart, cancellationToken), attributes: false, cancellationToken: cancellationToken))
                {
                    return true;
                }

                return false;
            }

            return false;
        }

        private static bool IsRightSideName(ExpressionSyntax name)
        {
            if (name.Parent != null)
            {
                switch (name.Parent.CSharpKind())
                {
                    case SyntaxKind.QualifiedName:
                        return ((QualifiedNameSyntax)name.Parent).Right == name;
                    case SyntaxKind.AliasQualifiedName:
                        return ((AliasQualifiedNameSyntax)name.Parent).Name == name;
                    case SyntaxKind.SimpleMemberAccessExpression:
                        return ((MemberAccessExpressionSyntax)name.Parent).Name == name;
                }
            }

            return false;
        }

        public static bool IsCatchOrFinallyContext(
            this SyntaxTree syntaxTree, int position, SyntaxToken tokenOnLeftOfPosition, CancellationToken cancellationToken)
        {
            // cases:
            // try { 
            // } |

            // try {
            // } c|

            // try {
            // } catch {
            // } |

            // try {
            // } catch {
            // } c|

            var token = tokenOnLeftOfPosition;
            token = token.GetPreviousTokenIfTouchingWord(position);

            if (token.CSharpKind() == SyntaxKind.CloseBraceToken)
            {
                var block = token.GetAncestor<BlockSyntax>();

                if (block != null && token == block.GetLastToken(includeSkipped: true))
                {
                    if (block.IsParentKind(SyntaxKind.TryStatement) ||
                        block.IsParentKind(SyntaxKind.CatchClause))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static bool IsCatchFilterContext(this SyntaxTree syntaxTree, int position, SyntaxToken tokenOnLeftOfPosition)
        {
            // cases:
            //  catch |
            //  catch i|
            //  catch (declaration) |
            //  catch (declaration) i|

            var token = tokenOnLeftOfPosition;
            token = token.GetPreviousTokenIfTouchingWord(position);

            if (token.IsKind(SyntaxKind.CatchKeyword))
            {
                return true;
            }

            if (token.IsKind(SyntaxKind.CloseParenToken) &&
                token.Parent.IsKind(SyntaxKind.CatchDeclaration))
            {
                return true;
            }

            return false;
        }

        public static bool IsEnumBaseListContext(this SyntaxTree syntaxTree, int position, SyntaxToken tokenOnLeftOfPosition, CancellationToken cancellationToken)
        {
            var token = tokenOnLeftOfPosition;
            token = token.GetPreviousTokenIfTouchingWord(position);

            // Options:
            //  enum E : |
            //  enum E : i|

            return
                token.CSharpKind() == SyntaxKind.ColonToken &&
                token.IsParentKind(SyntaxKind.BaseList) &&
                token.Parent.IsParentKind(SyntaxKind.EnumDeclaration);
        }

        public static bool IsEnumTypeMemberAccessContext(this SyntaxTree syntaxTree, SemanticModel semanticModel, int position, CancellationToken cancellationToken)
        {
            var token = syntaxTree
                .FindTokenOnLeftOfPosition(position, cancellationToken)
                .GetPreviousTokenIfTouchingWord(position);

            if (!token.MatchesKind(SyntaxKind.DotToken) ||
                !token.IsParentKind(SyntaxKind.SimpleMemberAccessExpression))
            {
                return false;
            }

            var memberAccess = (MemberAccessExpressionSyntax)token.Parent;
            var leftHandBinding = semanticModel.GetSymbolInfo(memberAccess.Expression);
            var symbol = leftHandBinding.GetBestOrAllSymbols().FirstOrDefault();

            if (symbol == null)
            {
                return false;
            }

            switch (symbol.Kind)
            {
                case SymbolKind.NamedType:
                    return ((INamedTypeSymbol)symbol).TypeKind == TypeKind.Enum;
                case SymbolKind.Alias:
                    var target = ((IAliasSymbol)symbol).Target;
                    return target.IsType && ((ITypeSymbol)target).TypeKind == TypeKind.Enum;
            }

            return false;
        }
    }
}