﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp
{
    /// <summary>
    /// This pass detects and reports diagnostics that do not affect lambda convertability.
    /// This part of the partial class focuses on features that cannot be used in expression trees.
    /// CAVEAT: Errors may be produced for ObsoleteAttribute, but such errors don't affect lambda convertability.
    /// </summary>
    internal sealed partial class DiagnosticsPass : BoundTreeWalker
    {
        private readonly DiagnosticBag diagnostics;
        private readonly CSharpCompilation compilation;
        private bool inExpressionLambda;
        private bool reportedUnsafe;
        private readonly MethodSymbol containingSymbol;

        public static void IssueDiagnostics(CSharpCompilation compilation, BoundNode node, DiagnosticBag diagnostics, MethodSymbol containingSymbol)
        {
            Debug.Assert(node != null);
            Debug.Assert((object)containingSymbol != null);

            var diagnosticPass = new DiagnosticsPass(compilation, diagnostics, containingSymbol);
            diagnosticPass.Visit(node);
        }

        private DiagnosticsPass(CSharpCompilation compilation, DiagnosticBag diagnostics, MethodSymbol containingSymbol)
        {
            Debug.Assert(diagnostics != null);
            Debug.Assert((object)containingSymbol != null);

            this.compilation = compilation;
            this.diagnostics = diagnostics;
            this.containingSymbol = containingSymbol;
        }

        private void Error(ErrorCode code, BoundNode node, params object[] args)
        {
            diagnostics.Add(code, node.Syntax.Location, args);
        }

        private void CheckUnsafeType(BoundExpression e)
        {
            if (e != null && (object)e.Type != null && e.Type.TypeKind == TypeKind.PointerType) NoteUnsafe(e);
        }

        private void NoteUnsafe(BoundNode node)
        {
            if (inExpressionLambda && !reportedUnsafe)
            {
                Error(ErrorCode.ERR_ExpressionTreeContainsPointerOp, node);
                reportedUnsafe = true;
            }
        }

        public override BoundNode VisitArrayCreation(BoundArrayCreation node)
        {
            var arrayType = (ArrayTypeSymbol)node.Type;
            if (inExpressionLambda && node.InitializerOpt != null && arrayType.Rank != 1)
            {
                Error(ErrorCode.ERR_ExpressionTreeContainsMultiDimensionalArrayInitializer, node);
            }

            return base.VisitArrayCreation(node);
        }

        public override BoundNode VisitSizeOfOperator(BoundSizeOfOperator node)
        {
            if (inExpressionLambda && node.ConstantValue == null)
            {
                Error(ErrorCode.ERR_ExpressionTreeContainsPointerOp, node);
            }

            return base.VisitSizeOfOperator(node);
        }

        public override BoundNode VisitBaseReference(BoundBaseReference node)
        {
            if (inExpressionLambda)
            {
                Error(ErrorCode.ERR_ExpressionTreeContainsBaseAccess, node);
            }

            return base.VisitBaseReference(node);
        }

        public override BoundNode VisitLockStatement(BoundLockStatement node)
        {
            this.Visit(node.Argument);
            this.Visit(node.Body);
            return null;
        }

        public override BoundNode VisitTryStatement(BoundTryStatement node)
        {
            this.Visit(node.TryBlock);
            this.VisitList(node.CatchBlocks);
            this.Visit(node.FinallyBlockOpt);
            return null;
        }

        private static Symbol GetLocalOrParameterSymbol(BoundExpression expr)
        {
            if (expr != null)
            {
                switch (expr.Kind)
                {
                    case BoundKind.Local:
                        return ((BoundLocal)expr).LocalSymbol;
                    case BoundKind.Parameter:
                        return ((BoundParameter)expr).ParameterSymbol;
                }
            }

            return null;
        }

        public override BoundNode VisitAssignmentOperator(BoundAssignmentOperator node)
        {
            if (!CheckForAssignmentToSelf(node) && inExpressionLambda && node.Left.Kind != BoundKind.ObjectInitializerMember && node.Left.Kind != BoundKind.DynamicObjectInitializerMember)
            {
                Error(ErrorCode.ERR_ExpressionTreeContainsAssignment, node);
            }

            return base.VisitAssignmentOperator(node);
        }

        public override BoundNode VisitDynamicObjectInitializerMember(BoundDynamicObjectInitializerMember node)
        {
            if (inExpressionLambda)
            {
                Error(ErrorCode.ERR_ExpressionTreeContainsDynamicOperation, node);
            }

            return base.VisitDynamicObjectInitializerMember(node);
        }

        public override BoundNode VisitEventAccess(BoundEventAccess node)
        {
            // Don't bother reporting an obsolete diagnostic if the access is already wrong for other reasons
            // (specifically, we can't use it as a field here).
            if (node.IsUsableAsField)
            {
                bool hasBaseReceiver = node.ReceiverOpt != null && node.ReceiverOpt.Kind == BoundKind.BaseReference;
                Binder.ReportDiagnosticsIfObsolete(diagnostics, node.EventSymbol.AssociatedField, node.Syntax, hasBaseReceiver, this.containingSymbol, this.containingSymbol.ContainingType, BinderFlags.None);
            }
            CheckReceiverIfField(node.ReceiverOpt);
            return base.VisitEventAccess(node);
        }

        public override BoundNode VisitEventAssignmentOperator(BoundEventAssignmentOperator node)
        {
            if (inExpressionLambda)
            {
                Error(ErrorCode.ERR_ExpressionTreeContainsAssignment, node);
            }

            bool hasBaseReceiver = node.ReceiverOpt != null && node.ReceiverOpt.Kind == BoundKind.BaseReference;
            Binder.ReportDiagnosticsIfObsolete(diagnostics, node.Event, ((BinaryExpressionSyntax)node.Syntax).Left, hasBaseReceiver, this.containingSymbol, this.containingSymbol.ContainingType, BinderFlags.None);
            CheckReceiverIfField(node.ReceiverOpt);
            return base.VisitEventAssignmentOperator(node);
        }

        public override BoundNode VisitCompoundAssignmentOperator(BoundCompoundAssignmentOperator node)
        {
            CheckLiftedCompoundAssignment(node);
            if (inExpressionLambda)
            {
                Error(ErrorCode.ERR_ExpressionTreeContainsAssignment, node);
            }

            return base.VisitCompoundAssignmentOperator(node);
        }

        private void VisitCall(
            MethodSymbol method,
            PropertySymbol propertyAccess,
            ImmutableArray<BoundExpression> arguments,
            ImmutableArray<RefKind> argumentRefKindsOpt,
            ImmutableArray<string> argumentNamesOpt,
            bool expanded,
            BoundNode node)
        {
            Debug.Assert((object)method != null);
            Debug.Assert(((object)propertyAccess == null) ||
                (method.AssociatedPropertyOrEvent == propertyAccess) ||
                propertyAccess.MustCallMethodsDirectly);

            CheckArguments(argumentRefKindsOpt, arguments, method);

            if (inExpressionLambda)
            {
                if (method.CallsAreOmitted(node.SyntaxTree))
                {
                    Error(ErrorCode.ERR_PartialMethodInExpressionTree, node);
                }
                else if ((object)propertyAccess != null && propertyAccess.IsIndexedProperty() && !propertyAccess.IsIndexer)
                {
                    Error(ErrorCode.ERR_ExpressionTreeContainsIndexedProperty, node);
                }
                else if (arguments.Length < (((object)propertyAccess != null) ? propertyAccess.ParameterCount : method.ParameterCount) + (expanded ? -1 : 0))
                {
                    Error(ErrorCode.ERR_ExpressionTreeContainsOptionalArgument, node);
                }
                else if (!argumentNamesOpt.IsDefault)
                {
                    Error(ErrorCode.ERR_ExpressionTreeContainsNamedArgument, node);
                }
                else if (IsComCallWithRefOmitted(method, arguments, argumentRefKindsOpt))
                {
                    Error(ErrorCode.ERR_ComRefCallInExpressionTree, node);
                }
            }
        }

        public override BoundNode VisitRefTypeOperator(BoundRefTypeOperator node)
        {
            if (inExpressionLambda)
            {
                Error(ErrorCode.ERR_FeatureNotValidInExpressionTree, node, "__reftype");
            }

            return base.VisitRefTypeOperator(node);
        }

        public override BoundNode VisitRefValueOperator(BoundRefValueOperator node)
        {
            if (inExpressionLambda)
            {
                Error(ErrorCode.ERR_FeatureNotValidInExpressionTree, node, "__refvalue");
            }

            return base.VisitRefValueOperator(node);
        }

        public override BoundNode VisitMakeRefOperator(BoundMakeRefOperator node)
        {
            if (inExpressionLambda)
            {
                Error(ErrorCode.ERR_FeatureNotValidInExpressionTree, node, "__makeref");
            }

            return base.VisitMakeRefOperator(node);
        }

        public override BoundNode VisitArgListOperator(BoundArgListOperator node)
        {
            if (inExpressionLambda)
            {
                Error(ErrorCode.ERR_VarArgsInExpressionTree, node);
            }

            return base.VisitArgListOperator(node);
        }

        public override BoundNode VisitCall(BoundCall node)
        {
            VisitCall(node.Method, null, node.Arguments, node.ArgumentRefKindsOpt, node.ArgumentNamesOpt, node.Expanded, node);
            CheckReceiverIfField(node.ReceiverOpt);
            return base.VisitCall(node);
        }

        public override BoundNode VisitCollectionElementInitializer(BoundCollectionElementInitializer node)
        {
            VisitCall(node.AddMethod, null, node.Arguments, default(ImmutableArray<RefKind>), default(ImmutableArray<string>), node.Expanded, node);
            return base.VisitCollectionElementInitializer(node);
        }

        public override BoundNode VisitObjectCreationExpression(BoundObjectCreationExpression node)
        {
            VisitCall(node.Constructor, null, node.Arguments, node.ArgumentRefKindsOpt, node.ArgumentNamesOpt, node.Expanded, node);
            return base.VisitObjectCreationExpression(node);
        }

        public override BoundNode VisitIndexerAccess(BoundIndexerAccess node)
        {
            var indexer = node.Indexer;
            var method = indexer.GetMethod ?? indexer.SetMethod;
            if ((object)method != null)
            {
                VisitCall(method, indexer, node.Arguments, node.ArgumentRefKindsOpt, node.ArgumentNamesOpt, node.Expanded, node);
            }
            CheckReceiverIfField(node.ReceiverOpt);
            return base.VisitIndexerAccess(node);
        }

        public override BoundNode VisitLambda(BoundLambda node)
        {
            if (inExpressionLambda)
            {
                switch (node.Syntax.Kind)
                {
                    case SyntaxKind.ParenthesizedLambdaExpression:
                        {
                            var lambdaSyntax = (ParenthesizedLambdaExpressionSyntax)node.Syntax;
                            if (lambdaSyntax.AsyncKeyword.CSharpKind() == SyntaxKind.AsyncKeyword)
                            {
                                Error(ErrorCode.ERR_BadAsyncExpressionTree, node);
                            }
                            else if (lambdaSyntax.Body.Kind == SyntaxKind.Block)
                            {
                                Error(ErrorCode.ERR_StatementLambdaToExpressionTree, node);
                            }

                            var lambda = node.ExpressionSymbol as MethodSymbol;
                            if ((object)lambda != null)
                            {
                                foreach (var p in lambda.Parameters)
                                {
                                    if (p.RefKind != RefKind.None && p.Locations.Length != 0)
                                    {
                                        diagnostics.Add(ErrorCode.ERR_ByRefParameterInExpressionTree, p.Locations[0]);
                                    }
                                }
                            }
                        }
                        break;

                    case SyntaxKind.SimpleLambdaExpression:
                        {
                            var lambdaSyntax = (SimpleLambdaExpressionSyntax)node.Syntax;
                            if (lambdaSyntax.AsyncKeyword.CSharpKind() == SyntaxKind.AsyncKeyword)
                            {
                                Error(ErrorCode.ERR_BadAsyncExpressionTree, node);
                            }
                            else if (lambdaSyntax.Body.Kind == SyntaxKind.Block)
                            {
                                Error(ErrorCode.ERR_StatementLambdaToExpressionTree, node);
                            }
                        }
                        break;

                    case SyntaxKind.AnonymousMethodExpression:
                        Error(ErrorCode.ERR_ExpressionTreeContainsAnonymousMethod, node);
                        break;

                    default:
                        // other syntax forms arise from query expressions, and always result from implied expression-lambda-like forms
                        break;
                }
            }

            return base.VisitLambda(node);
        }

        public override BoundNode VisitBinaryOperator(BoundBinaryOperator node)
        {
            // It is very common for bound trees to be left-heavy binary operators, eg,
            // a + b + c + d + ...
            // To avoid blowing the stack, do not recurse down the left hand side.

            // In order to avoid blowing the stack, we end up visiting right children
            // before left children; this should not be a problem in the diagnostics 
            // pass.

            BoundBinaryOperator current = node;
            while (true)
            {
                CheckBinaryOperator(current);

                Visit(current.Right);
                if (current.Left is BoundBinaryOperator)
                {
                    current = (BoundBinaryOperator)current.Left;
                }
                else
                {
                    Visit(current.Left);
                    break;
                }
            }

            return null;
        }

        private void CheckDynamic(BoundUnaryOperator node)
        {
            if (inExpressionLambda && node.OperatorKind.IsDynamic())
            {
                Error(ErrorCode.ERR_ExpressionTreeContainsDynamicOperation, node);
            }
        }

        private void CheckDynamic(BoundBinaryOperator node)
        {
            if (inExpressionLambda && node.OperatorKind.IsDynamic())
            {
                Error(ErrorCode.ERR_ExpressionTreeContainsDynamicOperation, node);
            }
        }

        public override BoundNode VisitUnaryOperator(BoundUnaryOperator node)
        {
            CheckUnsafeType(node);
            CheckLiftedUnaryOp(node);
            CheckDynamic(node);
            return base.VisitUnaryOperator(node);
        }

        public override BoundNode VisitAddressOfOperator(BoundAddressOfOperator node)
        {
            CheckUnsafeType(node);
            BoundExpression operand = node.Operand;
            if (operand.Kind == BoundKind.FieldAccess)
            {
                CheckFieldAddress((BoundFieldAccess)operand, consumerOpt: null);
            }
            return base.VisitAddressOfOperator(node);
        }

        public override BoundNode VisitIncrementOperator(BoundIncrementOperator node)
        {
            if (inExpressionLambda)
            {
                Error(ErrorCode.ERR_ExpressionTreeContainsAssignment, node);
            }

            return base.VisitIncrementOperator(node);
        }

        public override BoundNode VisitPointerElementAccess(BoundPointerElementAccess node)
        {
            NoteUnsafe(node);
            return base.VisitPointerElementAccess(node);
        }

        public override BoundNode VisitPointerIndirectionOperator(BoundPointerIndirectionOperator node)
        {
            NoteUnsafe(node);
            return base.VisitPointerIndirectionOperator(node);
        }

        public override BoundNode VisitConversion(BoundConversion node)
        {
            CheckUnsafeType(node.Operand);
            CheckUnsafeType(node);
            bool wasInExpressionLambda = inExpressionLambda;
            bool oldReportedUnsafe = reportedUnsafe;
            switch (node.ConversionKind)
            {
                case ConversionKind.MethodGroup:
                    VisitMethodGroup((BoundMethodGroup)node.Operand, parentIsConversion: true);
                    return node;

                case ConversionKind.AnonymousFunction:
                    if (!wasInExpressionLambda && node.Type.IsExpressionTree())
                    {
                        inExpressionLambda = true;
                        // we report "unsafe in expression tree" at most once for each expression tree
                        reportedUnsafe = false;
                    }
                    break;

                case ConversionKind.ImplicitDynamic:
                case ConversionKind.ExplicitDynamic:
                    if (inExpressionLambda)
                    {
                        Error(ErrorCode.ERR_ExpressionTreeContainsDynamicOperation, node);
                    }
                    break;

                default:
                    break;
            }

            var result = base.VisitConversion(node);
            inExpressionLambda = wasInExpressionLambda;
            reportedUnsafe = oldReportedUnsafe;
            return result;
        }

        public override BoundNode VisitDelegateCreationExpression(BoundDelegateCreationExpression node)
        {
            if (node.Argument.Kind != BoundKind.MethodGroup)
            {
                this.Visit(node.Argument);
            }

            return null;
        }

        public override BoundNode VisitMethodGroup(BoundMethodGroup node)
        {
            return VisitMethodGroup(node, parentIsConversion: false);
        }

        private BoundNode VisitMethodGroup(BoundMethodGroup node, bool parentIsConversion)
        {
            // Formerly reported ERR_MemGroupInExpressionTree when this occurred, but the expanded 
            // ERR_LambdaInIsAs makes this impossible (since the node will always be wrapped in
            // a failed conversion).
            Debug.Assert(!(!parentIsConversion && inExpressionLambda));

            CheckReceiverIfField(node.ReceiverOpt);
            return base.VisitMethodGroup(node);
        }

        public override BoundNode VisitNullCoalescingOperator(BoundNullCoalescingOperator node)
        {
            if (inExpressionLambda && node.LeftOperand.ConstantValue != null && node.LeftOperand.ConstantValue.IsNull)
            {
                Error(ErrorCode.ERR_ExpressionTreeContainsBadCoalesce, node.LeftOperand);
            }

            return base.VisitNullCoalescingOperator(node);
        }

        public override BoundNode VisitDynamicInvocation(BoundDynamicInvocation node)
        {
            if (inExpressionLambda)
            {
                Error(ErrorCode.ERR_ExpressionTreeContainsDynamicOperation, node);

                // avoid reporting errors for the method group:
                if (node.Expression.Kind == BoundKind.MethodGroup)
                {
                    return base.VisitMethodGroup((BoundMethodGroup)node.Expression);
                }
            }

            return base.VisitDynamicInvocation(node);
        }

        public override BoundNode VisitDynamicIndexerAccess(BoundDynamicIndexerAccess node)
        {
            if (inExpressionLambda)
            {
                Error(ErrorCode.ERR_ExpressionTreeContainsDynamicOperation, node);
            }

            CheckReceiverIfField(node.ReceiverOpt);
            return base.VisitDynamicIndexerAccess(node);
        }

        public override BoundNode VisitDynamicMemberAccess(BoundDynamicMemberAccess node)
        {
            if (inExpressionLambda)
            {
                Error(ErrorCode.ERR_ExpressionTreeContainsDynamicOperation, node);
            }

            return base.VisitDynamicMemberAccess(node);
        }

        public override BoundNode VisitDynamicCollectionElementInitializer(BoundDynamicCollectionElementInitializer node)
        {
            if (inExpressionLambda)
            {
                Error(ErrorCode.ERR_ExpressionTreeContainsDynamicOperation, node);
            }

            return base.VisitDynamicCollectionElementInitializer(node);
        }

        public override BoundNode VisitDynamicObjectCreationExpression(BoundDynamicObjectCreationExpression node)
        {
            if (inExpressionLambda)
            {
                Error(ErrorCode.ERR_ExpressionTreeContainsDynamicOperation, node);
            }

            return base.VisitDynamicObjectCreationExpression(node);
        }
    }
}