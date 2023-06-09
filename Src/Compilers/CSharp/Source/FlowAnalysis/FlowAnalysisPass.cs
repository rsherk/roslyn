﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.CodeAnalysis.CSharp
{
    internal class FlowAnalysisPass
    {
        /// <summary>
        /// The flow analysis pass.  This pass reports required diagnostics for unreachable
        /// statements and uninitialized variables (through the call to FlowAnalysisWalker.Analyze),
        /// and inserts a final return statement if the end of a void-returning method is reachable.
        /// </summary>
        /// <param name="method">the method to be analyzed</param>
        /// <param name="block">the method's body</param>
        /// <param name="diagnostics">the receiver of the reported diagnostics</param>
        /// <returns>the rewritten block for the method (with a return statement possibly inserted)</returns>
        public static BoundBlock Rewrite(MethodSymbol method, BoundBlock block, DiagnosticBag diagnostics)
        {
            var compilation = method.DeclaringCompilation;

            SourceMethodSymbol sourceMethod = method as SourceMethodSymbol;

            if (method.ReturnsVoid || (object)method.IteratorElementType != null
                || (method.IsAsync && compilation.GetWellKnownType(WellKnownType.System_Threading_Tasks_Task) == method.ReturnType))
            {
                if (method.IsImplicitlyDeclared || Analyze(compilation, method, block, diagnostics))
                {
                    // we don't analyze synthesized void methods.
                    block = AppendImplicitReturn(block, method, (object)sourceMethod != null ? sourceMethod.BlockSyntax : null);
                }
            }
            else if (Analyze(compilation, method, block, diagnostics))
            {
                // If the method is a lambda expression being converted to a non-void delegate type
                // and the end point is reachable then suppress the error here; a special error
                // will be reported by the lambda binder.
                Debug.Assert(method.MethodKind != MethodKind.AnonymousFunction);

                // If there's more than one location, then the method is partial and we
                // have already reported a non-void partial method error.
                if (method.Locations.Length == 1)
                {
                    diagnostics.Add(ErrorCode.ERR_ReturnExpected, method.Locations[0], method);
                }
            }

            return block;
        }

        // insert the implicit "return" statement at the end of the method body
        // Normally, we wouldn't bother attaching syntax trees to compiler-generated nodes, but these
        // ones are going to have sequence points.
        internal static BoundBlock AppendImplicitReturn(BoundStatement node, MethodSymbol method = null, CSharpSyntaxNode syntax = null)
        {
            if (syntax == null)
            {
                syntax = node.Syntax;
            }

            BoundStatement ret =
                (object)method != null && (object)method.IteratorElementType != null
                ? BoundYieldBreakStatement.Synthesized(syntax) as BoundStatement
                : BoundReturnStatement.Synthesized(syntax, null);

            if (syntax.Kind == SyntaxKind.Block)
            {
                var blockSyntax = (BlockSyntax)syntax;

                ret = new BoundSequencePointWithSpan(
                    blockSyntax,
                    ret,
                    blockSyntax.CloseBraceToken.Span)
                { WasCompilerGenerated = true };
            }

            switch (node.Kind)
            {
                case BoundKind.Block:
                    {
                        var block = (BoundBlock)node;
                        return block.Update(block.LocalsOpt, block.Statements.Add(ret));
                    }
                default:
                    return new BoundBlock(syntax, ImmutableArray<LocalSymbol>.Empty, ImmutableArray.Create<BoundStatement>(ret, node));
            }
        }

        private static bool Analyze(CSharpCompilation compilation, MethodSymbol method, BoundBlock block, DiagnosticBag diagnostics)
        {
            var result = ControlFlowPass.Analyze(compilation, method, block, diagnostics);
            DataFlowPass.Analyze(compilation, method, block, diagnostics);
            DisposeCheckerPass.Analyze(compilation, method, block, diagnostics);
            return result;
        }
    }
}