﻿' Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports Microsoft.CodeAnalysis.Test.Utilities
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax
Imports Roslyn.Test.Utilities

Namespace Microsoft.CodeAnalysis.VisualBasic.UnitTests.Semantics
    Public Class SyncLockTests
        Inherits FlowTestBase

#Region "ControlFlowPass and DataflowAnalysis"

        <Fact()>
        Sub SyncLockInSelect()
            Dim analysis = CompileAndAnalyzeControlAndDataFlow(
<compilation name="SyncLockInSelect">
    <file name="a.vb">
Option Infer On
Imports System
Class Program
    Shared Sub Main()
        Select ""
            Case "a"
                [|
                SyncLock New Object()
                    GoTo lab1
                End SyncLock
                |]
        End Select
lab1:
    End Sub
End Class
    </file>
</compilation>)
            Dim analsisControlflow = analysis.Item1
            Dim analsisDataflow = analysis.Item2
            Assert.Equal(Nothing, GetSymbolNamesSortedAndJoined(analsisDataflow.VariablesDeclared))
            Assert.Equal(Nothing, GetSymbolNamesSortedAndJoined(analsisDataflow.AlwaysAssigned))
            Assert.Equal(Nothing, GetSymbolNamesSortedAndJoined(analsisDataflow.DataFlowsIn))
            Assert.Equal(Nothing, GetSymbolNamesSortedAndJoined(analsisDataflow.DataFlowsOut))
            Assert.Equal(Nothing, GetSymbolNamesSortedAndJoined(analsisDataflow.ReadInside))
            Assert.Equal(Nothing, GetSymbolNamesSortedAndJoined(analsisDataflow.ReadOutside))
            Assert.Equal(Nothing, GetSymbolNamesSortedAndJoined(analsisDataflow.WrittenInside))
            Assert.Equal(Nothing, GetSymbolNamesSortedAndJoined(analsisDataflow.WrittenOutside))

            Assert.Equal(0, analsisControlflow.EntryPoints.Count())
            Assert.Equal(1, analsisControlflow.ExitPoints.Count())
        End Sub

        <Fact()>
        Sub UnreachableCode()
            Dim analysis = CompileAndAnalyzeControlAndDataFlow(
<compilation name="UnreachableCode">
    <file name="a.vb">
Option Infer On
Imports System
Class Program
    Shared Sub Main()
        [|Dim x1 As Object
        SyncLock x1
            Return
        End SyncLock|]
        System.Threading.Monitor.Exit(x1)
    End Sub
End Class
    </file>
</compilation>)

            Dim analsisControlflow = analysis.Item1
            Dim analsisDataflow = analysis.Item2
            Assert.Equal("x1", GetSymbolNamesSortedAndJoined(analsisDataflow.VariablesDeclared))
            Assert.Equal(Nothing, GetSymbolNamesSortedAndJoined(analsisDataflow.AlwaysAssigned))
            Assert.Equal(Nothing, GetSymbolNamesSortedAndJoined(analsisDataflow.DataFlowsIn))
            Assert.Equal(Nothing, GetSymbolNamesSortedAndJoined(analsisDataflow.DataFlowsOut))
            Assert.Equal("x1", GetSymbolNamesSortedAndJoined(analsisDataflow.ReadInside))
            Assert.Equal("x1", GetSymbolNamesSortedAndJoined(analsisDataflow.ReadOutside))
            Assert.Equal(Nothing, GetSymbolNamesSortedAndJoined(analsisDataflow.WrittenInside))
            Assert.Equal(Nothing, GetSymbolNamesSortedAndJoined(analsisDataflow.WrittenOutside))

            Assert.Equal(0, analsisControlflow.EntryPoints.Count())
            Assert.Equal(1, analsisControlflow.ExitPoints.Count())
        End Sub

        <Fact()>
        Sub AssignmentInSyncLock()
            Dim analysis = CompileAndAnalyzeControlAndDataFlow(
<compilation name="AssignmentInSyncLock">
    <file name="a.vb">
Option Infer On
Imports System
Class Program
    Shared Sub Main()
        Dim myLock As Object
        [|SyncLock Nothing
            myLock = New Object()
        End SyncLock|]
        System.Console.WriteLine(myLock)
    End Sub
End Class
    </file>
</compilation>)
            Dim analsisControlflow = analysis.Item1
            Dim analsisDataflow = analysis.Item2
            Assert.Equal(Nothing, GetSymbolNamesSortedAndJoined(analsisDataflow.VariablesDeclared))
            Assert.Equal("myLock", GetSymbolNamesSortedAndJoined(analsisDataflow.AlwaysAssigned))
            Assert.Equal(Nothing, GetSymbolNamesSortedAndJoined(analsisDataflow.DataFlowsIn))
            Assert.Equal("myLock", GetSymbolNamesSortedAndJoined(analsisDataflow.DataFlowsOut))
            Assert.Equal(Nothing, GetSymbolNamesSortedAndJoined(analsisDataflow.ReadInside))
            Assert.Equal("myLock", GetSymbolNamesSortedAndJoined(analsisDataflow.ReadOutside))
            Assert.Equal("myLock", GetSymbolNamesSortedAndJoined(analsisDataflow.WrittenInside))
            Assert.Equal(Nothing, GetSymbolNamesSortedAndJoined(analsisDataflow.WrittenOutside))

            Assert.Equal(0, analsisControlflow.EntryPoints.Count())
            Assert.Equal(0, analsisControlflow.ExitPoints.Count())
        End Sub

        <Fact()>
        Sub SyncLock_AssignmentInInLambda()
            Dim analysis = CompileAndAnalyzeControlAndDataFlow(
<compilation name="SyncLock_AssignmentInInLambda">
    <file name="a.vb">
Option Infer On
Imports System
Class Program
    Shared Sub Main()
        Dim myLock As Object
[|
        SyncLock Sub()
                     myLock = New Object()
                 End Sub
        End SyncLock|]
        System.Console.WriteLine(myLock)
    End Sub
End Class
    </file>
</compilation>)
            Dim analsisControlflow = analysis.Item1
            Dim analsisDataflow = analysis.Item2
            Assert.Equal(Nothing, GetSymbolNamesSortedAndJoined(analsisDataflow.VariablesDeclared))
            Assert.Equal(Nothing, GetSymbolNamesSortedAndJoined(analsisDataflow.AlwaysAssigned))
            Assert.Equal(Nothing, GetSymbolNamesSortedAndJoined(analsisDataflow.DataFlowsIn))
            Assert.Equal("myLock", GetSymbolNamesSortedAndJoined(analsisDataflow.DataFlowsOut))
            Assert.Equal(Nothing, GetSymbolNamesSortedAndJoined(analsisDataflow.ReadInside))
            Assert.Equal("myLock", GetSymbolNamesSortedAndJoined(analsisDataflow.ReadOutside))
            Assert.Equal("myLock", GetSymbolNamesSortedAndJoined(analsisDataflow.WrittenInside))
            Assert.Equal(Nothing, GetSymbolNamesSortedAndJoined(analsisDataflow.WrittenOutside))

            Assert.Equal(0, analsisControlflow.EntryPoints.Count())
            Assert.Equal(0, analsisControlflow.ExitPoints.Count())
        End Sub

        <Fact()>
        Sub NestedSyncLock()
            Dim analsisDataflow = CompileAndAnalyzeDataFlow(
<compilation name="NestedSyncLock">
    <file name="a.vb">
Public Class Program
    Public Sub foo()
        Dim syncroot As Object = New Object
        SyncLock syncroot
            [|SyncLock syncroot.ToString()
                GoTo lab1
                syncroot = Nothing
            End SyncLock|]
lab1:
        End SyncLock
        System.Threading.Monitor.Enter(syncroot)
    End Sub
End Class
    </file>
</compilation>)
            Assert.Equal(Nothing, GetSymbolNamesSortedAndJoined(analsisDataflow.VariablesDeclared))
            Assert.Equal(Nothing, GetSymbolNamesSortedAndJoined(analsisDataflow.AlwaysAssigned))
            Assert.Equal("syncroot", GetSymbolNamesSortedAndJoined(analsisDataflow.DataFlowsIn))
            Assert.Equal(Nothing, GetSymbolNamesSortedAndJoined(analsisDataflow.DataFlowsOut))
            Assert.Equal("syncroot", GetSymbolNamesSortedAndJoined(analsisDataflow.ReadInside))
            Assert.Equal("syncroot", GetSymbolNamesSortedAndJoined(analsisDataflow.ReadOutside))
            Assert.Equal("syncroot", GetSymbolNamesSortedAndJoined(analsisDataflow.WrittenInside))
            Assert.Equal("Me, syncroot", GetSymbolNamesSortedAndJoined(analsisDataflow.WrittenOutside))

        End Sub

        <Fact()>
        Sub DataflowOfInnerStatement()
            Dim analsisDataflow = CompileAndAnalyzeDataFlow(
<compilation name="DataflowOfInnerStatement">
    <file name="a.vb">
Public Class Program
    Public Sub foo()
        Dim syncroot As Object = New Object
        SyncLock syncroot.ToString()
            [|Dim x As Integer
            Return|]
        End SyncLock
        System.Threading.Monitor.Enter(syncroot)
    End Sub
End Class
    </file>
</compilation>)
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(analsisDataflow.VariablesDeclared))
            Assert.Equal(Nothing, GetSymbolNamesSortedAndJoined(analsisDataflow.AlwaysAssigned))
            Assert.Equal(Nothing, GetSymbolNamesSortedAndJoined(analsisDataflow.DataFlowsIn))
            Assert.Equal(Nothing, GetSymbolNamesSortedAndJoined(analsisDataflow.DataFlowsOut))
            Assert.Equal(Nothing, GetSymbolNamesSortedAndJoined(analsisDataflow.ReadInside))
            Assert.Equal("syncroot", GetSymbolNamesSortedAndJoined(analsisDataflow.ReadOutside))
            Assert.Equal(Nothing, GetSymbolNamesSortedAndJoined(analsisDataflow.WrittenInside))
            Assert.Equal("Me, syncroot", GetSymbolNamesSortedAndJoined(analsisDataflow.WrittenOutside))

        End Sub

#End Region

#Region "Semantic API"

        <WorkItem(545364)>
        <Fact()>
        Public Sub SyncLockLambda()
            Dim compilation = CreateCompilationWithMscorlibAndVBRuntime(
<compilation name="SyncLockLambda">
    <file name="a.vb">
Option Infer On
Imports System
Class Program
    Shared Sub Main()
        Dim myLock As Object
        SyncLock Sub()
                     myLock = New Object()
                 End Sub
        End SyncLock
    End Sub
End Class
    </file>
</compilation>)
            Dim expression = GetExpressionFromSyncLock(compilation)
            Dim semanticSummary = GetSemanticInfoSummary(compilation, expression)
            Assert.Null(semanticSummary.Type)
            Assert.Equal(TypeKind.Delegate, semanticSummary.ConvertedType.TypeKind)
            Assert.Equal("Sub <generated method>()", semanticSummary.ConvertedType.ToDisplayString())
            Assert.Equal(ConversionKind.Widening Or ConversionKind.Lambda, semanticSummary.ImplicitConversion.Kind)

            Assert.Equal("Sub ()", semanticSummary.Symbol.ToTestDisplayString())
            Assert.Equal(SymbolKind.Method, semanticSummary.Symbol.Kind)
            Assert.Equal(True, semanticSummary.Symbol.IsLambdaMethod)

            Assert.Equal(0, semanticSummary.CandidateSymbols.Length)
            Assert.Null(semanticSummary.Alias)
            Assert.Equal(0, semanticSummary.MemberGroup.Length)
            Assert.False(semanticSummary.ConstantValue.HasValue)
        End Sub

        <Fact()>
        Sub SyncLockQuery()

            Dim compilation = CreateCompilationWithMscorlibAndVBRuntimeAndReferences(
<compilation name="SyncLockQuery">
    <file name="a.vb">
Option Strict On
Imports System.Linq
Class Program
    Shared Sub Main()
        SyncLock From w In From x In New Integer() {1, 2, 3}
                           From y In New Char() {"a"c, "b"c}
                           Let bOdd = (x And 1) = 1
                           Where
                               bOdd Where y > "a"c Let z = x.ToString() &amp; y.ToString()
        End SyncLock
    End Sub
End Class
    </file>
</compilation>, {SystemCoreRef})

            Dim expression = GetExpressionFromSyncLock(compilation)
            Dim semanticSummary = GetSemanticInfoSummary(compilation, expression)

            Assert.NotNull(semanticSummary.Type)
            Assert.NotNull(semanticSummary.ConvertedType)
            Assert.True(semanticSummary.Type.IsReferenceType)
            Assert.Equal("System.Collections.Generic.IEnumerable(Of <anonymous type: Key x As Integer, Key y As Char, Key bOdd As Boolean, Key z As String>)", semanticSummary.ConvertedType.ToDisplayString())
            Assert.Equal(ConversionKind.Identity, semanticSummary.ImplicitConversion.Kind)

            Assert.Null(semanticSummary.Symbol)
            Assert.Equal(0, semanticSummary.CandidateSymbols.Length)

            Assert.Null(semanticSummary.Alias)
            Assert.Equal(0, semanticSummary.MemberGroup.Length)
            Assert.False(semanticSummary.ConstantValue.HasValue)
        End Sub

        <Fact()>
        Sub SyncLockGenericType()
            Dim compilation = CreateCompilationWithMscorlibAndVBRuntimeAndReferences(
<compilation name="SyncLockGenericType">
    <file name="a.vb">
Option Infer ON
Class Program
    Private Shared Sub Foo(Of T As D)(x As T)
        SyncLock x
        End SyncLock
    End Sub
End Class
Class D
End Class
    </file>
</compilation>)

            Dim expression = GetExpressionFromSyncLock(compilation)
            Dim semanticSummary = GetSemanticInfoSummary(compilation, expression)

            Assert.Equal("T", semanticSummary.Type.ToDisplayString())
            Assert.True(semanticSummary.ConvertedType.IsReferenceType)
            Assert.Equal("T", semanticSummary.ConvertedType.ToDisplayString())
            Assert.Equal(ConversionKind.Identity, semanticSummary.ImplicitConversion.Kind)

            Assert.Equal("x As T", semanticSummary.Symbol.ToDisplayString())
            Assert.Equal(0, semanticSummary.CandidateSymbols.Length)

            Assert.Null(semanticSummary.Alias)
            Assert.Equal(0, semanticSummary.MemberGroup.Length)
            Assert.False(semanticSummary.ConstantValue.HasValue)

        End Sub

        <Fact()>
        Sub SyncLockAnonymous()

            Dim compilation = CreateCompilationWithMscorlibAndVBRuntimeAndReferences(
<compilation name="SyncLockAnonymous">
    <file name="a.vb">
Module M1
    Sub Main()
        SyncLock New With {Key .p1 = 10.0}
        End SyncLock
    End Sub
End Module
    </file>
</compilation>)
            Dim expression = GetExpressionFromSyncLock(compilation)
            Dim semanticSummary = GetSemanticInfoSummary(compilation, expression)

            Assert.True(semanticSummary.Type.IsReferenceType)
            Assert.Equal("<anonymous type: Key p1 As Double>", semanticSummary.Type.ToDisplayString())
            Assert.Equal(TypeKind.Class, semanticSummary.Type.TypeKind)
            Assert.True(semanticSummary.ConvertedType.IsReferenceType)
            Assert.Equal("<anonymous type: Key p1 As Double>", semanticSummary.ConvertedType.ToDisplayString())
            Assert.Equal(TypeKind.Class, semanticSummary.Type.TypeKind)
            Assert.Equal(ConversionKind.Identity, semanticSummary.ImplicitConversion.Kind)

            Assert.Equal("Public Sub New(p1 As Double)", semanticSummary.Symbol.ToDisplayString())

        End Sub

        <Fact()>
        Sub SyncLockCreateObject()

            Dim compilation = CreateCompilationWithMscorlibAndVBRuntimeAndReferences(
<compilation name="SyncLockCreateObject">
    <file name="a.vb">
Module M1
    Sub Main()
        SyncLock New object()
        End SyncLock
    End Sub
End Module
    </file>
</compilation>)
            Dim expression = GetExpressionFromSyncLock(compilation)
            Dim semanticSummary = GetSemanticInfoSummary(compilation, expression)

            Assert.True(semanticSummary.Type.IsReferenceType)
            Assert.Equal("Object", semanticSummary.Type.ToDisplayString())
            Assert.Equal(TypeKind.Class, semanticSummary.Type.TypeKind)
            Dim symbol = compilation.GetTypeByMetadataName("System.Object")
            Assert.Equal(symbol, semanticSummary.Type)
            Assert.True(semanticSummary.ConvertedType.IsReferenceType)
            Assert.Equal("Object", semanticSummary.ConvertedType.ToDisplayString())
            Assert.Equal(TypeKind.Class, semanticSummary.Type.TypeKind)
            Assert.Equal(ConversionKind.Identity, semanticSummary.ImplicitConversion.Kind)

            Assert.Equal("Public Overloads Sub New()", semanticSummary.Symbol.ToDisplayString())

        End Sub

        <Fact()>
        Sub SimpleSyncLockNothing()
            Dim compilation = CreateCompilationWithMscorlibAndVBRuntimeAndReferences(
<compilation name="SimpleSyncLockNothing">
    <file name="a.vb">
Option Strict ON
Imports System
Class Program
    Shared Sub Main()
        SyncLock Nothing
            Exit Sub
        End SyncLock
    End Sub
End Class
    </file>
</compilation>)
            Dim expression = GetExpressionFromSyncLock(compilation)
            Dim semanticSummary = GetSemanticInfoSummary(compilation, expression)

            Assert.Null(semanticSummary.Type)
            Assert.Equal("Object", semanticSummary.ConvertedType.ToDisplayString())
            Assert.Equal(TypeKind.Class, semanticSummary.ConvertedType.TypeKind)
            Dim symbol = compilation.GetTypeByMetadataName("System.Object")
            Assert.Equal(symbol, semanticSummary.ConvertedType)
            Assert.Equal(ConversionKind.WideningNothingLiteral, semanticSummary.ImplicitConversion.Kind)

            Assert.Null(semanticSummary.Symbol)
        End Sub

        <Fact()>
        Sub SimpleSyncLockDelegate()
            Dim compilation = CreateCompilationWithMscorlibAndVBRuntimeAndReferences(
<compilation name="SimpleSyncLockDelegate">
    <file name="a.vb">
Delegate Sub D(p1 As Integer)
Class Program
    Public Shared Sub Main(args As String())
        SyncLock New D(AddressOf PM)
        End SyncLock
    End Sub
    Private Shared Sub PM(p1 As Integer)
    End Sub
End Class
    </file>
</compilation>)

            Dim expression = GetExpressionFromSyncLock(compilation)
            Dim semanticSummary = GetSemanticInfoSummary(compilation, expression)

            Assert.True(semanticSummary.Type.IsReferenceType)
            Assert.Equal("D", semanticSummary.Type.ToDisplayString())
            Assert.Equal(TypeKind.Delegate, semanticSummary.Type.TypeKind)
            Dim symbol = compilation.GetTypeByMetadataName("D")
            Assert.Equal(symbol, semanticSummary.Type)
            Assert.True(semanticSummary.ConvertedType.IsReferenceType)
            Assert.Equal("D", semanticSummary.ConvertedType.ToDisplayString())
            Assert.Equal(TypeKind.Delegate, semanticSummary.Type.TypeKind)
            Assert.Equal(ConversionKind.Identity, semanticSummary.ImplicitConversion.Kind)

            Assert.Null(semanticSummary.Symbol)
        End Sub

        <Fact()>
        Sub SyncLockMe()
            Dim compilation = CreateCompilationWithMscorlibAndVBRuntimeAndReferences(
<compilation name="SyncLockMe">
    <file name="a.vb">
Class Program
    Sub foo()
        SyncLock Me
        End SyncLock
    End Sub
End Class
    </file>
</compilation>)

            Dim expression = GetExpressionFromSyncLock(compilation)
            Dim semanticSummary = GetSemanticInfoSummary(compilation, expression)

            Assert.True(semanticSummary.Type.IsReferenceType)
            Assert.Equal("Program", semanticSummary.Type.ToDisplayString())
            Assert.Equal(TypeKind.Class, semanticSummary.Type.TypeKind)
            Dim symbol = compilation.GetTypeByMetadataName("Program")
            Assert.Equal(symbol, semanticSummary.Type)
            Assert.True(semanticSummary.ConvertedType.IsReferenceType)
            Assert.Equal("Program", semanticSummary.ConvertedType.ToDisplayString())
            Assert.Equal(TypeKind.Class, semanticSummary.Type.TypeKind)
            Assert.Equal(ConversionKind.Identity, semanticSummary.ImplicitConversion.Kind)

            Assert.Equal("[Me] As Program", semanticSummary.Symbol.ToTestDisplayString())
            Assert.Equal(SymbolKind.Parameter, semanticSummary.Symbol.Kind)

        End Sub
#End Region

#Region "Help Method"

        Private Function GetExpressionFromSyncLock(Compilation As VisualBasicCompilation, Optional which As Integer = 1) As ExpressionSyntax
            Dim tree = Compilation.SyntaxTrees.[Single]()
            Dim model = Compilation.GetSemanticModel(tree)
            Dim SyncLockBlock = tree.GetCompilationUnitRoot().DescendantNodes().OfType(Of SyncLockStatementSyntax)().ToList()
            Return SyncLockBlock(which - 1).Expression
        End Function

#End Region
    End Class
End Namespace
