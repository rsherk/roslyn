﻿' Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports System.Collections.Immutable
Imports System.IO
Imports System.Reflection.Metadata
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.CodeGen
Imports Microsoft.CodeAnalysis.Emit
Imports Microsoft.CodeAnalysis.Test.Utilities
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax
Imports Roslyn.Test.Utilities

Namespace Microsoft.CodeAnalysis.VisualBasic.UnitTests

    Public Class EditAndContinueTests
        Inherits BasicTestBase

        <Fact>
        Public Sub NamespacesAndOverloads()
            Dim compilation0 = CreateCompilationWithMscorlibAndVBRuntime(options:=UnoptimizedDll, sources:=
<compilation>
    <file name="a.vb"><![CDATA[
Class C
End Class
Namespace N
    Class C
    End Class
End Namespace
Namespace M
    Class C
        Sub M1(o As N.C)
        End Sub
        Sub M1(o As M.C)
        End Sub
        Sub M2(a As N.C, b As M.C, c As Global.C)
            M1(a)
        End Sub
    End Class
End Namespace
]]></file>
</compilation>)

            Dim bytes = compilation0.EmitToArray(debug:=True)
            Dim generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes), EmptyLocalsProvider)
            Dim compilation1 = CreateCompilationWithMscorlibAndVBRuntime(options:=UnoptimizedDll, sources:=
<compilation>
    <file name="a.vb"><![CDATA[
Class C
End Class
Namespace N
    Class C
    End Class
End Namespace
Namespace M
    Class C
        Sub M1(o As N.C)
        End Sub
        Sub M1(o As M.C)
        End Sub
        Sub M1(o As Global.C)
        End Sub
        Sub M2(a As N.C, b As M.C, c As Global.C)
            M1(a)
            M1(b)
        End Sub
    End Class
End Namespace
]]></file>
</compilation>)

            Dim diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(New SemanticEdit(SemanticEditKind.Insert, Nothing, compilation1.GetMembers("M.C.M1")(2)),
                                      New SemanticEdit(SemanticEditKind.Update, compilation0.GetMembers("M.C.M2")(0), compilation1.GetMembers("M.C.M2")(0))))

            diff1.VerifyIL(<![CDATA[
{
  // Code size       18 (0x12)
  .maxstack  8
  IL_0000:  nop
  IL_0001:  ldarg.0
  IL_0002:  ldarg.1
  IL_0003:  call       0x06000004
  IL_0008:  nop
  IL_0009:  ldarg.0
  IL_000a:  ldarg.2
  IL_000b:  call       0x06000005
  IL_0010:  nop
  IL_0011:  ret
}
{
  // Code size        2 (0x2)
  .maxstack  8
  IL_0000:  nop
  IL_0001:  ret
}
]]>.Value)

            Dim compilation2 = CreateCompilationWithMscorlibAndVBRuntime(options:=UnoptimizedDll, sources:=
<compilation>
    <file name="a.vb"><![CDATA[
Class C
End Class
Namespace N
    Class C
    End Class
End Namespace
Namespace M
    Class C
        Sub M1(o As N.C)
        End Sub
        Sub M1(o As M.C)
        End Sub
        Sub M1(o As Global.C)
        End Sub
        Sub M2(a As N.C, b As M.C, c As Global.C)
            M1(a)
            M1(b)
            M1(c)
        End Sub
    End Class
End Namespace
]]></file>
</compilation>)

            Dim diff2 = compilation2.EmitDifference(
                diff1.NextGeneration,
                ImmutableArray.Create(New SemanticEdit(SemanticEditKind.Update, compilation1.GetMembers("M.C.M2")(0), compilation2.GetMembers("M.C.M2")(0))))

            diff2.VerifyIL(<![CDATA[
{
  // Code size       26 (0x1a)
  .maxstack  8
  IL_0000:  nop
  IL_0001:  ldarg.0
  IL_0002:  ldarg.1
  IL_0003:  call       0x06000004
  IL_0008:  nop
  IL_0009:  ldarg.0
  IL_000a:  ldarg.2
  IL_000b:  call       0x06000005
  IL_0010:  nop
  IL_0011:  ldarg.0
  IL_0012:  ldarg.3
  IL_0013:  call       0x06000007
  IL_0018:  nop
  IL_0019:  ret
}
]]>.Value)
        End Sub

        <WorkItem(829353)>
        <Fact()>
        Public Sub PrivateImplementationDetails_ArrayInitializer_FromMetadata()
            Dim sources0 = <compilation>
                               <file name="a.vb"><![CDATA[
Class C
    Shared Sub M()
        Dim a As Integer() = {1, 2, 3}
        System.Console.Write(a(0))
    End Sub
End Class
]]></file>
                           </compilation>
            Dim sources1 = <compilation>
                               <file name="a.vb"><![CDATA[
Class C
    Shared Sub M()
        Dim a As Integer() = {1, 2, 3}
        System.Console.Write(a(1))
    End Sub
End Class
]]></file>
                           </compilation>
            Dim compilation0 = CreateCompilationWithMscorlibAndVBRuntime(sources0, UnoptimizedDll)
            Dim compilation1 = CreateCompilationWithMscorlibAndVBRuntime(sources1, UnoptimizedDll)

            Dim testData0 = New CompilationTestData()
            Dim bytes0 = compilation0.EmitToArray(debug:=True, testData:=testData0, mvid:=Guid.Parse("a2f225f6-b5b9-40f6-bb78-4479a0c55a9c"))
            Dim methodData0 = testData0.GetMethodData("C.M")
            methodData0.VerifyIL(<![CDATA[
{
  // Code size       29 (0x1d)
  .maxstack  3
  .locals init (Integer() V_0) //a
  IL_0000:  nop
  IL_0001:  ldc.i4.3
  IL_0002:  newarr     "Integer"
  IL_0007:  dup
  IL_0008:  ldtoken    "<PrivateImplementationDetails>{a2f225f6-b5b9-40f6-bb78-4479a0c55a9c}.__StaticArrayInitTypeSize=12 <PrivateImplementationDetails>{a2f225f6-b5b9-40f6-bb78-4479a0c55a9c}.$$method0x6000001-0"
  IL_000d:  call       "Sub System.Runtime.CompilerServices.RuntimeHelpers.InitializeArray(System.Array, System.RuntimeFieldHandle)"
  IL_0012:  stloc.0
  IL_0013:  ldloc.0
  IL_0014:  ldc.i4.0
  IL_0015:  ldelem.i4
  IL_0016:  call       "Sub System.Console.Write(Integer)"
  IL_001b:  nop
  IL_001c:  ret
}
]]>.Value)
            Dim method0 = compilation0.GetMember(Of MethodSymbol)("C.M")
            Dim generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), Function(m) GetLocalNames(methodData0))
            Dim testData1 = New CompilationTestData()
            Dim method1 = compilation1.GetMember(Of MethodSymbol)("C.M")
            Dim edit = New SemanticEdit(SemanticEditKind.Update, method0, method1, GetLocalMap(method1, method0), preserveLocalVariables:=True)
            Dim diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(edit))
            diff1.VerifyIL("C.M", <![CDATA[
{
  // Code size       30 (0x1e)
  .maxstack  4
  .locals init (Integer() V_0) //a
  IL_0000:  nop
  IL_0001:  ldc.i4.3
  IL_0002:  newarr     "Integer"
  IL_0007:  dup
  IL_0008:  ldc.i4.0
  IL_0009:  ldc.i4.1
  IL_000a:  stelem.i4
  IL_000b:  dup
  IL_000c:  ldc.i4.1
  IL_000d:  ldc.i4.2
  IL_000e:  stelem.i4
  IL_000f:  dup
  IL_0010:  ldc.i4.2
  IL_0011:  ldc.i4.3
  IL_0012:  stelem.i4
  IL_0013:  stloc.0
  IL_0014:  ldloc.0
  IL_0015:  ldc.i4.1
  IL_0016:  ldelem.i4
  IL_0017:  call       "Sub System.Console.Write(Integer)"
  IL_001c:  nop
  IL_001d:  ret
}
]]>.Value)
        End Sub

        ''' <summary>
        ''' Should not generate method for string switch since
        ''' the CLR only allows adding private members.
        ''' </summary>
        <WorkItem(834086)>
        <Fact()>
        Public Sub PrivateImplementationDetails_ComputeStringHash()
            Dim sources = <compilation>
                              <file name="a.vb"><![CDATA[
Class C
    Shared Function F(s As String)
        Select Case s
            Case "1"
                Return 1
            Case "2"
                Return 2
            Case "3"
                Return 3
            Case "4"
                Return 4
            Case "5"
                Return 5
            Case "6"
                Return 6
            Case "7"
                Return 7
            Case Else
                Return 0
        End Select
    End Function
End Class
]]></file>
                          </compilation>
            Const ComputeStringHashName As String = "$$method0x6000001-ComputeStringHash"
            Dim compilation0 = CreateCompilationWithMscorlibAndVBRuntime(sources, UnoptimizedDll)
            Dim compilation1 = CreateCompilationWithMscorlibAndVBRuntime(sources, UnoptimizedDll)

            Dim testData0 = New CompilationTestData()
            Dim bytes0 = compilation0.EmitToArray(debug:=True, testData:=testData0)
            Dim methodData0 = testData0.GetMethodData("C.F")
            Dim method0 = compilation0.GetMember(Of MethodSymbol)("C.F")
            Dim generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), Function(m) GetLocalNames(methodData0))

            ' Should have generated call to ComputeStringHash and
            ' added the method to <PrivateImplementationDetails>.
            Dim actualIL0 = methodData0.GetMethodIL()
            Assert.True(actualIL0.Contains(ComputeStringHashName))

            Using md0 = ModuleMetadata.CreateFromImage(bytes0)
                Dim reader0 = md0.MetadataReader
                CheckNames(reader0, reader0.GetMethodDefNames(), ".ctor", "F", ComputeStringHashName)

                Dim testData1 = New CompilationTestData()
                Dim method1 = compilation1.GetMember(Of MethodSymbol)("C.F")
                Dim edit = New SemanticEdit(SemanticEditKind.Update, method0, method1, GetLocalMap(method1, method0), preserveLocalVariables:=True)
                Dim diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(edit))

                ' Should not have generated call to ComputeStringHash nor
                ' added the method to <PrivateImplementationDetails>.
                Dim actualIL1 = diff1.TestData.GetMethodData("C.F").GetMethodIL()
                Assert.False(actualIL1.Contains(ComputeStringHashName))

                Using md1 = diff1.GetMetadata()
                    Dim reader1 = md1.Reader
                    Dim readers = {reader0, reader1}
                    CheckNames(readers, reader1.GetMethodDefNames(), "F")
                End Using
            End Using
        End Sub

        ''' <summary>
        ''' Avoid adding references from method bodies
        ''' other than the changed methods.
        ''' </summary>
        <Fact>
        Public Sub ReferencesInIL()
            Dim sources0 = <compilation>
                               <file name="a.vb"><![CDATA[
Module M
    Sub F()
        System.Console.WriteLine(1)
    End Sub
    Sub G()
        System.Console.WriteLine(2)
    End Sub
End Module
]]></file>
                           </compilation>
            Dim sources1 = <compilation>
                               <file name="a.vb"><![CDATA[
Module M
    Sub F()
        System.Console.WriteLine(1)
    End Sub
    Sub G()
        System.Console.Write(2)
    End Sub
End Module
]]></file>
                           </compilation>
            Dim compilation0 = CreateCompilationWithMscorlibAndVBRuntime(sources0, UnoptimizedDll)
            Dim compilation1 = CreateCompilationWithMscorlibAndVBRuntime(sources1, UnoptimizedDll)

            ' Verify full metadata contains expected rows.
            Dim bytes0 = compilation0.EmitToArray(debug:=True)
            Using md0 = ModuleMetadata.CreateFromImage(bytes0)
                Dim reader0 = md0.MetadataReader
                CheckNames(reader0, reader0.GetTypeDefNames(), "<Module>", "M")
                CheckNames(reader0, reader0.GetMethodDefNames(), "F", "G")
                CheckNames(reader0, reader0.GetMemberRefNames(), ".ctor", ".ctor", ".ctor", "WriteLine")

                Dim generation0 = EmitBaseline.CreateInitialBaseline(md0, EmptyLocalsProvider)

                Dim diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(New SemanticEdit(SemanticEditKind.Insert, Nothing, compilation1.GetMember("M.G"))))

                ' "Write" should be included in string table, but "WriteLine" should not.
                Assert.True(diff1.MetadataBlob.IsIncluded("Write"))
                Assert.False(diff1.MetadataBlob.IsIncluded("WriteLine"))
            End Using
        End Sub

        <Fact>
        Public Sub SymbolMatcher_TypeArguments()
            Dim source =
                <compilation>
                    <file name="c.vb"><![CDATA[
Class A(Of T)
    Class B(Of U)
        Shared Function M(Of V)(x As A(Of U).B(Of T), y As A(Of Object).S) As A(Of V)
            Return Nothing
        End Function
        Shared Function M(Of V)(x As A(Of U).B(Of T), y As A(Of V).S) As A(Of V)
            Return Nothing
        End Function
    End Class
    Structure S
    End Structure
End Class
]]>
                    </file>
                </compilation>
            Dim compilation0 = CreateCompilationWithMscorlibAndVBRuntime(source, UnoptimizedDll)
            Dim compilation1 = CreateCompilationWithMscorlibAndVBRuntime(source, UnoptimizedDll)

            Dim matcher = New SymbolMatcher(Nothing, compilation1.SourceAssembly, Nothing, compilation0.SourceAssembly, Nothing)
            Dim members = compilation1.GetMember(Of NamedTypeSymbol)("A.B").GetMembers("M")
            Assert.Equal(members.Length, 2)
            For Each member In members
                Dim other = DirectCast(matcher.MapDefinition(DirectCast(member, Cci.IMethodDefinition)), MethodSymbol)
                Assert.NotNull(other)
            Next
        End Sub

        <Fact>
        Public Sub SymbolMatcher_Constraints()
            Dim source =
                <compilation>
                    <file name="c.vb"><![CDATA[
Interface I(Of T As I(Of T))
End Interface
Class C
    Shared Sub M(Of T As I(Of T))(o As I(Of T))
    End Sub
End Class
]]>
                    </file>
                </compilation>
            Dim compilation0 = CreateCompilationWithMscorlibAndVBRuntime(source, UnoptimizedDll)
            Dim compilation1 = CreateCompilationWithMscorlibAndVBRuntime(source, UnoptimizedDll)

            Dim matcher = New SymbolMatcher(Nothing, compilation1.SourceAssembly, Nothing, compilation0.SourceAssembly, Nothing)
            Dim member = compilation1.GetMember(Of MethodSymbol)("C.M")
            Dim other = DirectCast(matcher.MapDefinition(DirectCast(member, Cci.IMethodDefinition)), MethodSymbol)
            Assert.NotNull(other)
        End Sub

        <Fact>
        Public Sub SymbolMatcher_CustomModifiers()
            Dim ilSource = <![CDATA[
.class public abstract A
{
  .method public hidebysig specialname rtspecialname instance void .ctor() { ret }
  .method public abstract virtual instance object modopt(A) [] F() { }
}
]]>.Value
            Dim source =
                <compilation>
                    <file name="c.vb"><![CDATA[
Class B
    Inherits A
    Public Overrides Function F() As Object()
        Return Nothing
    End Function
End Class
]]>
                    </file>
                </compilation>
            Dim metadata = CompileIL(ilSource)
            Dim compilation0 = CreateCompilationWithMscorlibAndVBRuntime(source, {metadata}, UnoptimizedDll)
            Dim compilation1 = CreateCompilationWithMscorlibAndVBRuntime(source, {metadata}, UnoptimizedDll)

            Dim member1 = compilation1.GetMember(Of MethodSymbol)("B.F")
            Const nModifiers As Integer = 1
            Assert.Equal(nModifiers, DirectCast(member1.ReturnType, ArrayTypeSymbol).CustomModifiers.Length)

            Dim matcher = New SymbolMatcher(Nothing, compilation1.SourceAssembly, Nothing, compilation0.SourceAssembly, Nothing)
            Dim other = DirectCast(matcher.MapDefinition(DirectCast(member1, Cci.IMethodDefinition)), MethodSymbol)
            Assert.NotNull(other)
            Assert.Equal(nModifiers, DirectCast(other.ReturnType, ArrayTypeSymbol).CustomModifiers.Length)
        End Sub

        <WorkItem(844472)>
        <Fact()>
        Public Sub MethodSignatureWithNoPIAType()
            Dim sourcesPIA = <compilation>
                                 <file name="a.vb"><![CDATA[
Imports System
Imports System.Runtime.InteropServices
<Assembly: ImportedFromTypeLib("_.dll")>
<Assembly: Guid("35DB1A6B-D635-4320-A062-28D42920F2A3")>
<ComImport()>
<Guid("35DB1A6B-D635-4320-A062-28D42920F2A4")>
Public Interface I
End Interface
]]></file>
                             </compilation>
            Dim sources0 = <compilation>
                               <file name="a.vb"><![CDATA[
Class C
    Shared Sub M(x As I)
        Dim y As I = Nothing
        M(Nothing)
    End Sub
End Class
]]></file>
                           </compilation>
            Dim sources1 = <compilation>
                               <file name="a.vb"><![CDATA[
Class C
    Shared Sub M(x As I)
        Dim y As I = Nothing
        M(x)
    End Sub
End Class
]]></file>
                           </compilation>
            Dim compilationPIA = CreateCompilationWithMscorlibAndVBRuntime(sourcesPIA)
            compilationPIA.AssertTheseDiagnostics()
            Dim referencePIA As MetadataReference = New MetadataImageReference(compilationPIA.EmitToArray(), embedInteropTypes:=True)
            Dim compilation0 = CreateCompilationWithMscorlibAndVBRuntimeAndReferences(sources0, options:=UnoptimizedDll, additionalRefs:={referencePIA})
            Dim compilation1 = CreateCompilationWithMscorlibAndVBRuntimeAndReferences(sources1, options:=UnoptimizedDll, additionalRefs:={referencePIA})

            Dim testData0 = New CompilationTestData()
            Dim bytes0 = compilation0.EmitToArray(debug:=True, testData:=testData0)
            Dim methodData0 = testData0.GetMethodData("C.M")
            Using md0 = ModuleMetadata.CreateFromImage(bytes0)
                Dim generation0 = EmitBaseline.CreateInitialBaseline(md0, EmptyLocalsProvider)
                Dim method0 = compilation0.GetMember(Of MethodSymbol)("C.M")
                Dim method1 = compilation1.GetMember(Of MethodSymbol)("C.M")
                Dim diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(New SemanticEdit(SemanticEditKind.Update, method0, method1, GetLocalMap(method1, method0), preserveLocalVariables:=True)))
                diff1.VerifyIL("C.M", <![CDATA[
{
  // Code size       11 (0xb)
  .maxstack  1
  .locals init (I V_0,
  I V_1) //y
  IL_0000:  nop
  IL_0001:  ldnull
  IL_0002:  stloc.1
  IL_0003:  ldarg.0
  IL_0004:  call       "Sub C.M(I)"
  IL_0009:  nop
  IL_000a:  ret
}
]]>.Value)
            End Using
        End Sub

        ''' <summary>
        ''' Disallow edits that require NoPIA references.
        ''' </summary>
        <Fact()>
        Public Sub NoPIAReferences()
            Dim sourcesPIA = <compilation>
                                 <file name="a.vb"><![CDATA[
Imports System
Imports System.Runtime.InteropServices
<Assembly: ImportedFromTypeLib("_.dll")>
<Assembly: Guid("35DB1A6B-D635-4320-A062-28D42920F2B3")>
<ComImport()>
<Guid("35DB1A6B-D635-4320-A062-28D42920F2B4")>
Public Interface IA
    Sub M()
    ReadOnly Property P As Integer
    Event E As Action
End Interface
<ComImport()>
<Guid("35DB1A6B-D635-4320-A062-28D42920F2B5")>
Public Interface IB
End Interface
<ComImport()>
<Guid("35DB1A6B-D635-4320-A062-28D42920F2B6")>
Public Interface IC
End Interface
Public Structure S
    Public F As Object
End Structure
]]></file>
                             </compilation>
            Dim sources0 = <compilation>
                               <file name="a.vb"><![CDATA[
Class C(Of T)
    Shared Private F As Object = GetType(IC)
    Shared Sub M1()
        Dim o As IA = Nothing
        o.M()
        M2(o.P)
        AddHandler o.E, AddressOf M1
        M2(C(Of IA).F)
        M2(New S())
    End Sub
    Shared Sub M2(o As Object)
    End Sub
End Class
]]></file>
                           </compilation>
            Dim sources1A = sources0
            Dim sources1B = <compilation>
                                <file name="a.vb"><![CDATA[
Class C(Of T)
    Shared Private F As Object = GetType(IC)
    Shared Sub M1()
        M2(Nothing)
    End Sub
    Shared Sub M2(o As Object)
    End Sub
End Class
]]></file>
                            </compilation>
            Dim compilationPIA = CreateCompilationWithMscorlibAndVBRuntime(sourcesPIA)
            compilationPIA.AssertTheseDiagnostics()
            Dim referencePIA As MetadataReference = New MetadataImageReference(compilationPIA.EmitToArray(), embedInteropTypes:=True)
            Dim compilation0 = CreateCompilationWithMscorlibAndVBRuntimeAndReferences(sources0, options:=UnoptimizedDll, additionalRefs:={referencePIA})
            Dim compilation1A = CreateCompilationWithMscorlibAndVBRuntimeAndReferences(sources1A, options:=UnoptimizedDll, additionalRefs:={referencePIA})
            Dim compilation1B = CreateCompilationWithMscorlibAndVBRuntimeAndReferences(sources1B, options:=UnoptimizedDll, additionalRefs:={referencePIA})

            Dim testData0 = New CompilationTestData()
            Dim bytes0 = compilation0.EmitToArray(debug:=True, testData:=testData0)
            Dim methodData0 = testData0.GetMethodData("C(Of T).M1")
            Using md0 = ModuleMetadata.CreateFromImage(bytes0)
                Dim reader0 = md0.MetadataReader
                CheckNames(reader0, reader0.GetTypeDefNames(), "<Module>", "C`1", "IA", "IC", "S")
                Dim generation0 = EmitBaseline.CreateInitialBaseline(md0, Function(m) GetLocalNames(methodData0))
                Dim method0 = compilation0.GetMember(Of MethodSymbol)("C.M1")

                ' Disallow edits that require NoPIA references.
                Dim method1A = compilation1A.GetMember(Of MethodSymbol)("C.M1")
                Dim diff1A = compilation1A.EmitDifference(
                    generation0,
                    ImmutableArray.Create(New SemanticEdit(SemanticEditKind.Update, method0, method1A, GetLocalMap(method1A, method0), preserveLocalVariables:=True)))
                diff1A.Result.Diagnostics.AssertTheseDiagnostics(<errors><![CDATA[
BC37230: Cannot continue since the edit includes a reference to an embedded type: 'IA'.
BC37230: Cannot continue since the edit includes a reference to an embedded type: 'S'.
     ]]></errors>)

                ' Allow edits that do not require NoPIA references,
                Dim method1B = compilation1B.GetMember(Of MethodSymbol)("C.M1")
                Dim diff1B = compilation1B.EmitDifference(
                    generation0,
                    ImmutableArray.Create(New SemanticEdit(SemanticEditKind.Update, method0, method1B, GetLocalMap(method1B, method0), preserveLocalVariables:=True)))
                diff1B.VerifyIL("C(Of T).M1", <![CDATA[
{
  // Code size        9 (0x9)
  .maxstack  1
  .locals init (IA V_0,
  S V_1)
  IL_0000:  nop
  IL_0001:  ldnull
  IL_0002:  call       "Public Shared Sub M2(o As Object)"
  IL_0007:  nop
  IL_0008:  ret
}
]]>.Value)
                Using md1 = diff1B.GetMetadata()
                    Dim reader1 = md1.Reader
                    CheckNames({reader0, reader1}, reader1.GetTypeDefNames())
                End Using
            End Using
        End Sub

        <WorkItem(844536)>
        <Fact()>
        Public Sub NoPIATypeInNamespace()
            Dim sourcesPIA = <compilation>
                                 <file name="a.vb"><![CDATA[
Imports System
Imports System.Runtime.InteropServices
<Assembly: ImportedFromTypeLib("_.dll")>
<Assembly: Guid("35DB1A6B-D635-4320-A062-28D42920F2A5")>
Namespace N
    <ComImport()>
    <Guid("35DB1A6B-D635-4320-A062-28D42920F2A6")>
    Public Interface IA
    End Interface
End Namespace
<ComImport()>
<Guid("35DB1A6B-D635-4320-A062-28D42920F2A6")>
Public Interface IB
End Interface
]]></file>
                             </compilation>
            Dim sources = <compilation>
                              <file name="a.vb"><![CDATA[
Class C(Of T)
    Shared Sub M(o As Object)
        M(C(Of N.IA).E.X)
        M(C(Of IB).E.X)
    End Sub
    Enum E
        X
    End Enum
End Class
]]></file>
                          </compilation>
            Dim compilationPIA = CreateCompilationWithMscorlibAndVBRuntime(sourcesPIA)
            compilationPIA.AssertTheseDiagnostics()
            Dim referencePIA As MetadataReference = New MetadataImageReference(compilationPIA.EmitToArray(), embedInteropTypes:=True)
            Dim compilation0 = CreateCompilationWithMscorlibAndVBRuntimeAndReferences(sources, options:=UnoptimizedDll, additionalRefs:={referencePIA})
            Dim compilation1 = CreateCompilationWithMscorlibAndVBRuntimeAndReferences(sources, options:=UnoptimizedDll, additionalRefs:={referencePIA})

            Dim bytes0 = compilation0.EmitToArray(debug:=True)
            Using md0 = ModuleMetadata.CreateFromImage(bytes0)
                Dim generation0 = EmitBaseline.CreateInitialBaseline(md0, EmptyLocalsProvider)
                Dim method0 = compilation0.GetMember(Of MethodSymbol)("C.M")
                Dim method1 = compilation1.GetMember(Of MethodSymbol)("C.M")
                Dim diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(New SemanticEdit(SemanticEditKind.Update, method0, method1, GetLocalMap(method1, method0), preserveLocalVariables:=True)))
                diff1.Result.Diagnostics.AssertTheseDiagnostics(<errors><![CDATA[
BC37230: Cannot continue since the edit includes a reference to an embedded type: 'IB'.
BC37230: Cannot continue since the edit includes a reference to an embedded type: 'N.IA'.
     ]]></errors>)
                diff1.VerifyIL("C(Of T).M", <![CDATA[
{
  // Code size       26 (0x1a)
  .maxstack  1
  IL_0000:  nop
  IL_0001:  ldc.i4.0
  IL_0002:  box        "C(Of N.IA).E"
  IL_0007:  call       "Public Shared Sub M(o As Object)"
  IL_000c:  nop
  IL_000d:  ldc.i4.0
  IL_000e:  box        "C(Of IB).E"
  IL_0013:  call       "Public Shared Sub M(o As Object)"
  IL_0018:  nop
  IL_0019:  ret
}
]]>.Value)
            End Using
        End Sub

        <Fact, WorkItem(837315)>
        Public Sub AddingSetAccessor()
            Dim source0 =
<compilation>
    <file name="a.vb">
Module Module1

    Sub Main()
        System.Console.WriteLine("hello")
    End Sub

    Friend name As String
    Readonly Property GetName
        Get
            Return name
        End Get
    End Property
End Module
</file>
</compilation>

            Dim source1 =
<compilation>
    <file name="a.vb">
Module Module1

    Sub Main()
        System.Console.WriteLine("hello")
    End Sub

    Friend name As String
    Property GetName
        Get
            Return name
        End Get
        Private Set(value)

        End Set
    End Property
End Module</file>
</compilation>

            Dim compilation0 = CreateCompilationWithMscorlibAndVBRuntime(source0, UnoptimizedDll)
            Dim compilation1 = CreateCompilationWithMscorlibAndVBRuntime(source1, UnoptimizedDll)

            Dim testData0 = New CompilationTestData()
            Dim bytes0 = compilation0.EmitToArray(debug:=True, testData:=testData0)

            Using md0 = ModuleMetadata.CreateFromImage(bytes0)
                Dim reader0 = md0.MetadataReader

                Dim prop0 = compilation0.GetMember(Of PropertySymbol)("Module1.GetName")
                Dim prop1 = compilation1.GetMember(Of PropertySymbol)("Module1.GetName")

                Dim method1 = prop1.SetMethod

                Dim getLocalNamesFunc As LocalVariableNameProvider = Function(m) ImmutableArray(Of String).Empty
                Dim generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), getLocalNamesFunc)

                Dim diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(New SemanticEdit(SemanticEditKind.Insert, Nothing, method1, preserveLocalVariables:=True)))

                Using md1 = diff1.GetMetadata()
                    Dim reader1 = md1.Reader
                    CheckNames({reader0, reader1}, reader1.GetMethodDefNames(), "set_GetName")
                End Using

                diff1.VerifyIL("Module1.set_GetName", <![CDATA[
{
  // Code size        2 (0x2)
  .maxstack  0
  IL_0000:  nop
  IL_0001:  ret
}
]]>.Value)
            End Using

        End Sub

#Region "Local Slots"
        <Fact, WorkItem(828389)>
        Public Sub CatchClause()
            Dim source0 =
<compilation>
    <file name="a.vb">
Class C
    Shared Sub M()
        Try
            System.Console.WriteLine(1)
        Catch ex As System.Exception
        End Try
    End Sub
End Class
</file>
</compilation>

            Dim source1 =
<compilation>
    <file name="a.vb">
Class C
    Shared Sub M()
        Try
            System.Console.WriteLine(2)
        Catch ex As System.Exception
        End Try
    End Sub
End Class
</file>
</compilation>

            Dim compilation0 = CreateCompilationWithMscorlibAndVBRuntime(source0, UnoptimizedDll)
            Dim compilation1 = CreateCompilationWithMscorlibAndVBRuntime(source1, UnoptimizedDll)

            Dim testData0 = New CompilationTestData()
            Dim bytes0 = compilation0.EmitToArray(debug:=True, testData:=testData0)

            Dim method0 = compilation0.GetMember(Of MethodSymbol)("C.M")
            Dim method1 = compilation1.GetMember(Of MethodSymbol)("C.M")

            Dim getLocalNamesFunc As LocalVariableNameProvider = Function(m) GetLocalNames(testData0.GetMethodData("C.M"))
            Dim generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), getLocalNamesFunc)

            Dim diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(New SemanticEdit(SemanticEditKind.Update, method0, method1, preserveLocalVariables:=True)))

            diff1.VerifyIL("C.M", <![CDATA[
{
  // Code size       28 (0x1c)
  .maxstack  2
  .locals init (System.Exception V_0,
  System.Exception V_1) //ex
  IL_0000:  nop
  .try
{
  IL_0001:  nop
  IL_0002:  ldc.i4.2
  IL_0003:  call       "Sub System.Console.WriteLine(Integer)"
  IL_0008:  nop
  IL_0009:  leave.s    IL_001a
}
  catch System.Exception
{
  IL_000b:  dup
  IL_000c:  call       "Sub Microsoft.VisualBasic.CompilerServices.ProjectData.SetProjectError(System.Exception)"
  IL_0011:  stloc.1
  IL_0012:  nop
  IL_0013:  call       "Sub Microsoft.VisualBasic.CompilerServices.ProjectData.ClearProjectError()"
  IL_0018:  leave.s    IL_001a
}
  IL_001a:  nop
  IL_001b:  ret
}
]]>.Value)
        End Sub

        ''' <summary>
        ''' Local slots must be preserved based on signature.
        ''' </summary>
        <Fact>
        Public Sub PreserveLocalSlots()
            Dim sources0 = <compilation>
                               <file name="a.vb"><![CDATA[
Class A(Of T)
End Class
Class B
    Inherits A(Of B)
    Shared Function F() As B
        Return Nothing
    End Function
    Shared Sub M(o As Object)
        Dim x As Object = F()
        Dim y As A(Of B) = F()
        Dim z As Object = F()
        M(x)
        M(y)
        M(z)
    End Sub
    Shared Sub N()
        Dim a As Object = F()
        Dim b As Object = F()
        M(a)
        M(b)
    End Sub
End Class
]]></file>
                           </compilation>
            Dim sources1 = <compilation>
                               <file name="a.vb"><![CDATA[
Class A(Of T)
End Class
Class B
    Inherits A(Of B)
    Shared Function F() As B
        Return Nothing
    End Function
    Shared Sub M(o As Object)
        Dim z As B = F()
        Dim y As A(Of B) = F()
        Dim w As Object = F()
        M(w)
        M(y)
    End Sub
    Shared Sub N()
        Dim a As Object = F()
        Dim b As Object = F()
        M(a)
        M(b)
    End Sub
End Class
]]></file>
                           </compilation>

            Dim sources2 = <compilation>
                               <file name="a.vb"><![CDATA[
Class A(Of T)
End Class
Class B
    Inherits A(Of B)
    Shared Function F() As B
        Return Nothing
    End Function
    Shared Sub M(o As Object)
        Dim x As Object = F()
        Dim z As B = F()
        M(x)
        M(z)
    End Sub
    Shared Sub N()
        Dim a As Object = F()
        Dim b As Object = F()
        M(a)
        M(b)
    End Sub
End Class
]]></file>
                           </compilation>

            Dim sources3 = <compilation>
                               <file name="a.vb"><![CDATA[
Class A(Of T)
End Class
Class B
    Inherits A(Of B)
    Shared Function F() As B
        Return Nothing
    End Function
    Shared Sub M(o As Object)
        Dim x As Object = F()
        Dim z As B = F()
        M(x)
        M(z)
    End Sub
    Shared Sub N()
        Dim c As Object = F()
        Dim b As Object = F()
        M(c)
        M(b)
    End Sub
End Class
]]></file>
                           </compilation>

            Dim compilation0 = CreateCompilationWithMscorlib(sources0, UnoptimizedDll)
            Dim compilation1 = CreateCompilationWithMscorlib(sources1, UnoptimizedDll)
            Dim compilation2 = CreateCompilationWithMscorlib(sources2, UnoptimizedDll)
            Dim compilation3 = CreateCompilationWithMscorlib(sources3, UnoptimizedDll)

            ' Verify full metadata contains expected rows.
            Dim bytes0 = compilation0.EmitToArray(debug:=True)
            Dim method0 = compilation0.GetMember(Of MethodSymbol)("B.M")
            Dim method1 = compilation1.GetMember(Of MethodSymbol)("B.M")
            Dim methodN = compilation0.GetMember(Of MethodSymbol)("B.N")
            Dim getLocalNamesFunc As LocalVariableNameProvider = Function(m)
                                                                     Select Case (m)
                                                                         Case 4
                                                                             Return GetLocalNames(method0)
                                                                         Case 5
                                                                             Return GetLocalNames(methodN)
                                                                         Case Else
                                                                             Return Nothing
                                                                     End Select
                                                                 End Function

            Dim generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), getLocalNamesFunc)

            Dim diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(New SemanticEdit(SemanticEditKind.Update, method0, method1, GetLocalMap(method1, method0), preserveLocalVariables:=True)))

            diff1.VerifyIL(<![CDATA[
{
  // Code size       41 (0x29)
  .maxstack  1
  IL_0000:  nop
  IL_0001:  call       0x06000003
  IL_0006:  stloc.3
  IL_0007:  call       0x06000003
  IL_000c:  stloc.1
  IL_000d:  call       0x06000003
  IL_0012:  stloc.s    V_4
  IL_0014:  ldloc.s    V_4
  IL_0016:  call       0x0A000006
  IL_001b:  call       0x06000004
  IL_0020:  nop
  IL_0021:  ldloc.1
  IL_0022:  call       0x06000004
  IL_0027:  nop
  IL_0028:  ret
}
]]>.Value)

            diff1.VerifyPdb({&H06000001UI, &H06000002UI, &H06000003UI, &H06000004UI, &H06000005UI},
<?xml version="1.0" encoding="utf-16"?>
<symbols>
    <files>
        <file id="1" name="a.vb" language="3a12d0b8-c26c-11d0-b442-00a0244a1dd2" languageVendor="994b45c4-e6e9-11d2-903f-00c04fa302a1" documentType="5a869d0b-6611-11d3-bd2a-0000f80849bd"/>
    </files>
    <methods>
        <method token="0x6000004">
            <sequencepoints total="7">
                <entry il_offset="0x0" start_row="8" start_column="5" end_row="8" end_column="30" file_ref="1"/>
                <entry il_offset="0x1" start_row="9" start_column="13" end_row="9" end_column="25" file_ref="1"/>
                <entry il_offset="0x7" start_row="10" start_column="13" end_row="10" end_column="31" file_ref="1"/>
                <entry il_offset="0xd" start_row="11" start_column="13" end_row="11" end_column="30" file_ref="1"/>
                <entry il_offset="0x14" start_row="12" start_column="9" end_row="12" end_column="13" file_ref="1"/>
                <entry il_offset="0x21" start_row="13" start_column="9" end_row="13" end_column="13" file_ref="1"/>
                <entry il_offset="0x28" start_row="14" start_column="5" end_row="14" end_column="12" file_ref="1"/>
            </sequencepoints>
            <locals>
                <local name="z" il_index="3" il_start="0x0" il_end="0x29" attributes="0"/>
                <local name="y" il_index="1" il_start="0x0" il_end="0x29" attributes="0"/>
                <local name="w" il_index="4" il_start="0x0" il_end="0x29" attributes="0"/>
            </locals>
            <scope startOffset="0x0" endOffset="0x29">
                <currentnamespace name=""/>
                <local name="z" il_index="3" il_start="0x0" il_end="0x29" attributes="0"/>
                <local name="y" il_index="1" il_start="0x0" il_end="0x29" attributes="0"/>
                <local name="w" il_index="4" il_start="0x0" il_end="0x29" attributes="0"/>
            </scope>
        </method>
    </methods>
</symbols>.ToString)

            Dim method2 = compilation2.GlobalNamespace.GetMember(Of NamedTypeSymbol)("B").GetMember(Of MethodSymbol)("M")

            Dim diff2 = compilation2.EmitDifference(
                diff1.NextGeneration,
                ImmutableArray.Create(New SemanticEdit(SemanticEditKind.Update, method1, method2, GetLocalMap(method2, method1), preserveLocalVariables:=True)))

            diff2.VerifyIL(<![CDATA[
{
  // Code size       35 (0x23)
  .maxstack  1
  IL_0000:  nop
  IL_0001:  call       0x06000003
  IL_0006:  stloc.s    V_5
  IL_0008:  call       0x06000003
  IL_000d:  stloc.3
  IL_000e:  ldloc.s    V_5
  IL_0010:  call       0x0A000007
  IL_0015:  call       0x06000004
  IL_001a:  nop
  IL_001b:  ldloc.3
  IL_001c:  call       0x06000004
  IL_0021:  nop
  IL_0022:  ret
}
]]>.Value)

            diff2.VerifyPdb({&H06000001UI, &H06000002UI, &H06000003UI, &H06000004UI, &H06000005UI},
<?xml version="1.0" encoding="utf-16"?>
<symbols>
    <files>
        <file id="1" name="a.vb" language="3a12d0b8-c26c-11d0-b442-00a0244a1dd2" languageVendor="994b45c4-e6e9-11d2-903f-00c04fa302a1" documentType="5a869d0b-6611-11d3-bd2a-0000f80849bd"/>
    </files>
    <methods>
        <method token="0x6000004">
            <sequencepoints total="6">
                <entry il_offset="0x0" start_row="8" start_column="5" end_row="8" end_column="30" file_ref="1"/>
                <entry il_offset="0x1" start_row="9" start_column="13" end_row="9" end_column="30" file_ref="1"/>
                <entry il_offset="0x8" start_row="10" start_column="13" end_row="10" end_column="25" file_ref="1"/>
                <entry il_offset="0xe" start_row="11" start_column="9" end_row="11" end_column="13" file_ref="1"/>
                <entry il_offset="0x1b" start_row="12" start_column="9" end_row="12" end_column="13" file_ref="1"/>
                <entry il_offset="0x22" start_row="13" start_column="5" end_row="13" end_column="12" file_ref="1"/>
            </sequencepoints>
            <locals>
                <local name="x" il_index="5" il_start="0x0" il_end="0x23" attributes="0"/>
                <local name="z" il_index="3" il_start="0x0" il_end="0x23" attributes="0"/>
            </locals>
            <scope startOffset="0x0" endOffset="0x23">
                <currentnamespace name=""/>
                <local name="x" il_index="5" il_start="0x0" il_end="0x23" attributes="0"/>
                <local name="z" il_index="3" il_start="0x0" il_end="0x23" attributes="0"/>
            </scope>
        </method>
    </methods>
</symbols>.ToString)

            ' Modify different method. (Previous generations
            ' have not referenced method.)

            method2 = compilation2.GlobalNamespace.GetMember(Of NamedTypeSymbol)("B").GetMember(Of MethodSymbol)("N")
            Dim method3 = compilation3.GlobalNamespace.GetMember(Of NamedTypeSymbol)("B").GetMember(Of MethodSymbol)("N")
            Dim metadata3 As ImmutableArray(Of Byte) = Nothing
            Dim il3 As ImmutableArray(Of Byte) = Nothing
            Dim pdb3 As Stream = Nothing

            Dim diff3 = compilation3.EmitDifference(
                diff2.NextGeneration,
                ImmutableArray.Create(New SemanticEdit(SemanticEditKind.Update, method2, method3, GetLocalMap(method3, method2), preserveLocalVariables:=True)))

            diff3.VerifyIL(<![CDATA[
{
  // Code size       38 (0x26)
  .maxstack  1
  IL_0000:  nop
  IL_0001:  call       0x06000003
  IL_0006:  stloc.2
  IL_0007:  call       0x06000003
  IL_000c:  stloc.1
  IL_000d:  ldloc.2
  IL_000e:  call       0x0A000008
  IL_0013:  call       0x06000004
  IL_0018:  nop
  IL_0019:  ldloc.1
  IL_001a:  call       0x0A000008
  IL_001f:  call       0x06000004
  IL_0024:  nop
  IL_0025:  ret
}
]]>.Value)

            diff3.VerifyPdb({&H06000001UI, &H06000002UI, &H06000003UI, &H06000004UI, &H06000005UI},
<?xml version="1.0" encoding="utf-16"?>
<symbols>
    <files>
        <file id="1" name="a.vb" language="3a12d0b8-c26c-11d0-b442-00a0244a1dd2" languageVendor="994b45c4-e6e9-11d2-903f-00c04fa302a1" documentType="5a869d0b-6611-11d3-bd2a-0000f80849bd"/>
    </files>
    <methods>
        <method token="0x6000005">
            <sequencepoints total="6">
                <entry il_offset="0x0" start_row="14" start_column="5" end_row="14" end_column="19" file_ref="1"/>
                <entry il_offset="0x1" start_row="15" start_column="13" end_row="15" end_column="30" file_ref="1"/>
                <entry il_offset="0x7" start_row="16" start_column="13" end_row="16" end_column="30" file_ref="1"/>
                <entry il_offset="0xd" start_row="17" start_column="9" end_row="17" end_column="13" file_ref="1"/>
                <entry il_offset="0x19" start_row="18" start_column="9" end_row="18" end_column="13" file_ref="1"/>
                <entry il_offset="0x25" start_row="19" start_column="5" end_row="19" end_column="12" file_ref="1"/>
            </sequencepoints>
            <locals>
                <local name="c" il_index="2" il_start="0x0" il_end="0x26" attributes="0"/>
                <local name="b" il_index="1" il_start="0x0" il_end="0x26" attributes="0"/>
            </locals>
            <scope startOffset="0x0" endOffset="0x26">
                <currentnamespace name=""/>
                <local name="c" il_index="2" il_start="0x0" il_end="0x26" attributes="0"/>
                <local name="b" il_index="1" il_start="0x0" il_end="0x26" attributes="0"/>
            </scope>
        </method>
    </methods>
</symbols>.ToString)
        End Sub

        ''' <summary>
        ''' Preserve locals for method added after initial compilation.
        ''' </summary>
        <Fact()>
        Public Sub PreserveLocalSlots_NewMethod()
            Dim sources0 = <compilation>
                               <file name="a.vb"><![CDATA[
Class C
End Class
]]></file>
                           </compilation>
            Dim sources1 = <compilation>
                               <file name="a.vb"><![CDATA[
Class C
    Shared Sub M()
        Dim a = New Object()
        Dim b = String.Empty
    End Sub
End Class
]]></file>
                           </compilation>

            Dim sources2 = <compilation>
                               <file name="a.vb"><![CDATA[
Class C
    Shared Sub M()
        Dim a = 1
        Dim b = String.Empty
    End Sub
End Class
]]></file>
                           </compilation>

            Dim compilation0 = CreateCompilationWithMscorlib(sources0, UnoptimizedDll)
            Dim compilation1 = CreateCompilationWithMscorlib(sources1, UnoptimizedDll)
            Dim compilation2 = CreateCompilationWithMscorlib(sources2, UnoptimizedDll)

            Dim bytes0 = compilation0.EmitToArray(debug:=True)
            Dim generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), EmptyLocalsProvider)

            Dim method1 = compilation1.GetMember(Of MethodSymbol)("C.M")
            Dim diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(New SemanticEdit(SemanticEditKind.Insert, Nothing, method1, Nothing, preserveLocalVariables:=True)))

            Dim method2 = compilation2.GetMember(Of MethodSymbol)("C.M")
            Dim diff2 = compilation2.EmitDifference(
                diff1.NextGeneration,
                ImmutableArray.Create(New SemanticEdit(SemanticEditKind.Update, method1, method2, GetLocalMap(method2, method1), preserveLocalVariables:=True)))
            diff2.VerifyIL("C.M", <![CDATA[
{
  // Code size       10 (0xa)
  .maxstack  1
  .locals init (Object V_0,
  String V_1, //b
  Integer V_2) //a
  IL_0000:  nop
  IL_0001:  ldc.i4.1
  IL_0002:  stloc.2
  IL_0003:  ldsfld     "String.Empty As String"
  IL_0008:  stloc.1
  IL_0009:  ret
}
]]>.Value)
            diff2.VerifyPdb({&H06000002UI},
<?xml version="1.0" encoding="utf-16"?>
<symbols>
    <files>
        <file id="1" name="a.vb" language="3a12d0b8-c26c-11d0-b442-00a0244a1dd2" languageVendor="994b45c4-e6e9-11d2-903f-00c04fa302a1" documentType="5a869d0b-6611-11d3-bd2a-0000f80849bd"/>
    </files>
    <methods>
        <method token="0x6000002">
            <sequencepoints total="4">
                <entry il_offset="0x0" start_row="2" start_column="5" end_row="2" end_column="19" file_ref="1"/>
                <entry il_offset="0x1" start_row="3" start_column="13" end_row="3" end_column="18" file_ref="1"/>
                <entry il_offset="0x3" start_row="4" start_column="13" end_row="4" end_column="29" file_ref="1"/>
                <entry il_offset="0x9" start_row="5" start_column="5" end_row="5" end_column="12" file_ref="1"/>
            </sequencepoints>
            <locals>
                <local name="a" il_index="2" il_start="0x0" il_end="0xa" attributes="0"/>
                <local name="b" il_index="1" il_start="0x0" il_end="0xa" attributes="0"/>
            </locals>
            <scope startOffset="0x0" endOffset="0xa">
                <currentnamespace name=""/>
                <local name="a" il_index="2" il_start="0x0" il_end="0xa" attributes="0"/>
                <local name="b" il_index="1" il_start="0x0" il_end="0xa" attributes="0"/>
            </scope>
        </method>
    </methods>
</symbols>.ToString)
        End Sub

        ''' <summary>
        ''' Local types should be retained, even if the local is no longer
        ''' used by the method body, since there may be existing
        ''' references to that slot, in a Watch window for instance.
        ''' </summary>
        <WorkItem(843320)>
        <Fact>
        Public Sub PreserveLocalTypes()
            Dim sources0 = <compilation>
                               <file name="a.vb"><![CDATA[
Class C
    Shared Sub Main()
        Dim x = True
        Dim y = x
        System.Console.WriteLine(y)
    End Sub
End Class
]]></file>
                           </compilation>
            Dim sources1 = <compilation>
                               <file name="a.vb"><![CDATA[
Class C
    Shared Sub Main()
        Dim x = "A"
        Dim y = x
        System.Console.WriteLine(y)
    End Sub
End Class
]]></file>
                           </compilation>

            Dim compilation0 = CreateCompilationWithMscorlib(sources0, UnoptimizedDll)
            Dim compilation1 = CreateCompilationWithMscorlib(sources1, UnoptimizedDll)
            Dim bytes0 = compilation0.EmitToArray(debug:=True)
            Dim method0 = compilation0.GetMember(Of MethodSymbol)("C.Main")
            Dim method1 = compilation1.GetMember(Of MethodSymbol)("C.Main")
            Dim generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), Function(m) GetLocalNames(method0))
            Dim diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(New SemanticEdit(SemanticEditKind.Update, method0, method1, GetLocalMap(method1, method0), preserveLocalVariables:=True)))
            diff1.VerifyIL("C.Main", <![CDATA[
{
  // Code size       17 (0x11)
  .maxstack  1
  .locals init (Boolean V_0,
  Boolean V_1,
  String V_2, //x
  String V_3) //y
  IL_0000:  nop
  IL_0001:  ldstr      "A"
  IL_0006:  stloc.2
  IL_0007:  ldloc.2
  IL_0008:  stloc.3
  IL_0009:  ldloc.3
  IL_000a:  call       "Sub System.Console.WriteLine(String)"
  IL_000f:  nop
  IL_0010:  ret
}
]]>.Value)
        End Sub

        ''' <summary>
        ''' Local slots must be preserved based on signature.
        ''' </summary>
        <Fact>
        Public Sub PreserveLocalSlotsReferences()
            Dim sources0 = <compilation>
                               <file name="a.vb"><![CDATA[

Class A(Of T)
End Class
Class C
    Inherits A(Of C)
    Shared Function F() As C
        Return Nothing
    End Function
    Shared Sub M(o As Object)
        dim x = new system.collections.generic.stack(of Integer)
        x.Push(1)
    End Sub
End Class
]]></file>
                           </compilation>

            Dim compilation0 = CreateCompilationWithMscorlibAndVBRuntimeAndReferences(sources0, XmlReferences, UnoptimizedDll)
            Dim compilation1 = CreateCompilationWithMscorlibAndVBRuntimeAndReferences(sources0, XmlReferences, UnoptimizedDll)

            Dim testData0 = New CompilationTestData()
            Dim bytes0 = compilation0.EmitToArray(debug:=True, testData:=testData0)

            Dim actualIL0 = testData0.GetMethodData("C.M").GetMethodIL()
            Dim expectedIL0 =
            <![CDATA[
{
  // Code size       16 (0x10)
  .maxstack  2
  .locals init (System.Collections.Generic.Stack(Of Integer) V_0) //x
  IL_0000:  nop
  IL_0001:  newobj     "Sub System.Collections.Generic.Stack(Of Integer)..ctor()"
  IL_0006:  stloc.0
  IL_0007:  ldloc.0
  IL_0008:  ldc.i4.1
  IL_0009:  callvirt   "Sub System.Collections.Generic.Stack(Of Integer).Push(Integer)"
  IL_000e:  nop
  IL_000f:  ret
}
]]>.Value

            AssertEx.AssertEqualToleratingWhitespaceDifferences(expectedIL0, actualIL0)

            Dim method0 = compilation0.GetMember(Of MethodSymbol)("C.M")
            Dim method1 = compilation1.GetMember(Of MethodSymbol)("C.M")

            Dim getLocalNamesFunc As LocalVariableNameProvider = Function(m) GetLocalNames(testData0.GetMethodData("C.M"))
            Dim modMeta = ModuleMetadata.CreateFromImage(bytes0)
            Dim generation0 = EmitBaseline.CreateInitialBaseline(modMeta, getLocalNamesFunc)

            Dim diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(New SemanticEdit(SemanticEditKind.Update, method0, method1, preserveLocalVariables:=True)))

            diff1.VerifyIL("C.M", <![CDATA[
{
  // Code size       16 (0x10)
  .maxstack  2
  .locals init (System.Collections.Generic.Stack(Of Integer) V_0) //x
  IL_0000:  nop
  IL_0001:  newobj     "Sub System.Collections.Generic.Stack(Of Integer)..ctor()"
  IL_0006:  stloc.0
  IL_0007:  ldloc.0
  IL_0008:  ldc.i4.1
  IL_0009:  callvirt   "Sub System.Collections.Generic.Stack(Of Integer).Push(Integer)"
  IL_000e:  nop
  IL_000f:  ret
}
]]>.Value)
        End Sub

        ''' <summary>
        ''' Local slots must be preserved based on signature.
        ''' </summary>
        <Fact>
        Public Sub PreserveLocalSlotsUsing()
            Dim sources0 = <compilation>
                               <file name="a.vb"><![CDATA[

Class A(Of T)
End Class
Class C
    Inherits A(Of C)
    Shared Function F() As C
        Return Nothing
    End Function
    Shared Sub M(o As Object)
        dim x as System.IDisposable = nothing
        Using x
        end using
    End Sub
End Class
]]></file>
                           </compilation>

            Dim compilation0 = CreateCompilationWithMscorlib(sources0, UnoptimizedDll)
            Dim compilation1 = CreateCompilationWithMscorlib(sources0, UnoptimizedDll)

            Dim testData0 = New CompilationTestData()
            Dim bytes0 = compilation0.EmitToArray(debug:=True, testData:=testData0)

            Dim actualIL0 = testData0.GetMethodData("C.M").GetMethodIL()
            Dim expectedIL0 =
            <![CDATA[
{
  // Code size       26 (0x1a)
  .maxstack  2
  .locals init (System.IDisposable V_0, //x
  System.IDisposable V_1, //VB$Using
  Boolean V_2)
  IL_0000:  nop
  IL_0001:  ldnull
  IL_0002:  stloc.0
  IL_0003:  nop
  IL_0004:  ldloc.0
  IL_0005:  stloc.1
  .try
{
  IL_0006:  leave.s    IL_0019
}
  finally
{
  IL_0008:  nop
  IL_0009:  ldloc.1
  IL_000a:  ldnull
  IL_000b:  ceq
  IL_000d:  stloc.2
  IL_000e:  ldloc.2
  IL_000f:  brtrue.s   IL_0018
  IL_0011:  ldloc.1
  IL_0012:  callvirt   "Sub System.IDisposable.Dispose()"
  IL_0017:  nop
  IL_0018:  endfinally
}
  IL_0019:  ret
}
]]>.Value

            AssertEx.AssertEqualToleratingWhitespaceDifferences(expectedIL0, actualIL0)

            Dim method0 = compilation0.GetMember(Of MethodSymbol)("C.M")
            Dim method1 = compilation1.GetMember(Of MethodSymbol)("C.M")

            Dim getLocalNamesFunc As LocalVariableNameProvider = Function(m) GetLocalNames(testData0.GetMethodData("C.M"))
            Dim generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), getLocalNamesFunc)

            Dim diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(New SemanticEdit(SemanticEditKind.Update, method0, method1, preserveLocalVariables:=True)))

            diff1.VerifyIL("C.M", <![CDATA[
{
  // Code size       26 (0x1a)
  .maxstack  2
  .locals init (System.IDisposable V_0, //x
  System.IDisposable V_1, //VB$Using
  Boolean V_2,
  Boolean V_3)
  IL_0000:  nop
  IL_0001:  ldnull
  IL_0002:  stloc.0
  IL_0003:  nop
  IL_0004:  ldloc.0
  IL_0005:  stloc.1
  .try
{
  IL_0006:  leave.s    IL_0019
}
  finally
{
  IL_0008:  nop
  IL_0009:  ldloc.1
  IL_000a:  ldnull
  IL_000b:  ceq
  IL_000d:  stloc.3
  IL_000e:  ldloc.3
  IL_000f:  brtrue.s   IL_0018
  IL_0011:  ldloc.1
  IL_0012:  callvirt   "Sub System.IDisposable.Dispose()"
  IL_0017:  nop
  IL_0018:  endfinally
}
  IL_0019:  ret
}
]]>.Value)
        End Sub

        ''' <summary>
        ''' Local slots must be preserved based on signature.
        ''' </summary>
        <Fact>
        Public Sub PreserveLocalSlotsWithByRef()
            Dim sources0 = <compilation>
                               <file name="a.vb"><![CDATA[

Class A(Of T)
End Class
Class C
    Inherits A(Of C)
    Shared Function F() As C
        Return Nothing
    End Function
    Shared Sub M(o As Object)
        dim x as System.Guid() = nothing
        With x(3)
            .ToString()
        end With
    End Sub
End Class
]]></file>
                           </compilation>

            Dim compilation0 = CreateCompilationWithMscorlib(sources0, UnoptimizedDll)
            Dim compilation1 = CreateCompilationWithMscorlib(sources0, UnoptimizedDll)

            Dim testData0 = New CompilationTestData()
            Dim bytes0 = compilation0.EmitToArray(debug:=True, testData:=testData0)

            Dim actualIL0 = testData0.GetMethodData("C.M").GetMethodIL()
            Dim expectedIL0 =
            <![CDATA[
{
  // Code size       27 (0x1b)
  .maxstack  2
  .locals init (System.Guid() V_0, //x
  System.Guid& V_1) //VB$With
  IL_0000:  nop
  IL_0001:  ldnull
  IL_0002:  stloc.0
  IL_0003:  nop
  IL_0004:  ldloc.0
  IL_0005:  ldc.i4.3
  IL_0006:  ldelema    "System.Guid"
  IL_000b:  stloc.1
  IL_000c:  ldloc.1
  IL_000d:  constrained. "System.Guid"
  IL_0013:  callvirt   "Function Object.ToString() As String"
  IL_0018:  pop
  IL_0019:  nop
  IL_001a:  ret
}
]]>.Value

            AssertEx.AssertEqualToleratingWhitespaceDifferences(expectedIL0, actualIL0)

            Dim method0 = compilation0.GetMember(Of MethodSymbol)("C.M")
            Dim method1 = compilation1.GetMember(Of MethodSymbol)("C.M")

            Dim getLocalNamesFunc As LocalVariableNameProvider = Function(m) GetLocalNames(testData0.GetMethodData("C.M"))
            Dim generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), getLocalNamesFunc)

            Dim diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(New SemanticEdit(SemanticEditKind.Update, method0, method1, preserveLocalVariables:=True)))

            diff1.VerifyIL("C.M", <![CDATA[
{
  // Code size       27 (0x1b)
  .maxstack  2
  .locals init (System.Guid() V_0, //x
  System.Guid& V_1) //VB$With
  IL_0000:  nop
  IL_0001:  ldnull
  IL_0002:  stloc.0
  IL_0003:  nop
  IL_0004:  ldloc.0
  IL_0005:  ldc.i4.3
  IL_0006:  ldelema    "System.Guid"
  IL_000b:  stloc.1
  IL_000c:  ldloc.1
  IL_000d:  constrained. "System.Guid"
  IL_0013:  callvirt   "Function Object.ToString() As String"
  IL_0018:  pop
  IL_0019:  nop
  IL_001a:  ret
}
]]>.Value)
        End Sub

        ''' <summary>
        ''' Local slots must be preserved based on signature.
        ''' </summary>
        <Fact>
        Public Sub PreserveLocalSlotsWithByVal()
            Dim sources0 = <compilation>
                               <file name="a.vb"><![CDATA[

Class A(Of T)
End Class
Class C
    Inherits A(Of C)
    Shared Function F() As C
        Return Nothing
    End Function
    Shared Sub M(o As Object)
        dim x as System.Guid() = nothing
        With x
            .ToString()
        end With
    End Sub
End Class
]]></file>
                           </compilation>

            Dim compilation0 = CreateCompilationWithMscorlib(sources0, UnoptimizedDll)
            Dim compilation1 = CreateCompilationWithMscorlib(sources0, UnoptimizedDll)

            Dim testData0 = New CompilationTestData()
            Dim bytes0 = compilation0.EmitToArray(debug:=True, testData:=testData0)

            Dim actualIL0 = testData0.GetMethodData("C.M").GetMethodIL()
            Dim expectedIL0 =
            <![CDATA[
{
  // Code size       17 (0x11)
  .maxstack  1
  .locals init (System.Guid() V_0, //x
  System.Guid() V_1) //VB$With
  IL_0000:  nop
  IL_0001:  ldnull
  IL_0002:  stloc.0
  IL_0003:  nop
  IL_0004:  ldloc.0
  IL_0005:  stloc.1
  IL_0006:  ldloc.1
  IL_0007:  callvirt   "Function Object.ToString() As String"
  IL_000c:  pop
  IL_000d:  nop
  IL_000e:  ldnull
  IL_000f:  stloc.1
  IL_0010:  ret
}
]]>.Value

            AssertEx.AssertEqualToleratingWhitespaceDifferences(expectedIL0, actualIL0)

            Dim method0 = compilation0.GetMember(Of MethodSymbol)("C.M")
            Dim method1 = compilation1.GetMember(Of MethodSymbol)("C.M")

            Dim getLocalNamesFunc As LocalVariableNameProvider = Function(m) GetLocalNames(testData0.GetMethodData("C.M"))
            Dim generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), getLocalNamesFunc)

            Dim diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(New SemanticEdit(SemanticEditKind.Update, method0, method1, preserveLocalVariables:=True)))

            diff1.VerifyIL("C.M", <![CDATA[
{
  // Code size       17 (0x11)
  .maxstack  1
  .locals init (System.Guid() V_0, //x
  System.Guid() V_1) //VB$With
  IL_0000:  nop
  IL_0001:  ldnull
  IL_0002:  stloc.0
  IL_0003:  nop
  IL_0004:  ldloc.0
  IL_0005:  stloc.1
  IL_0006:  ldloc.1
  IL_0007:  callvirt   "Function Object.ToString() As String"
  IL_000c:  pop
  IL_000d:  nop
  IL_000e:  ldnull
  IL_000f:  stloc.1
  IL_0010:  ret
}
]]>.Value)
        End Sub

        ''' <summary>
        ''' Local slots must be preserved based on signature.
        ''' </summary>
        <Fact>
        Public Sub PreserveLocalSlotsSyncLock()
            Dim sources0 = <compilation>
                               <file name="a.vb"><![CDATA[
Class A(Of T)
End Class
Class C
    Inherits A(Of C)
    Shared Function F() As C
        Return Nothing
    End Function
    Shared Sub M(o As Object)
        dim x as System.Guid() = nothing
        SyncLock x
            dim y as System.Guid() = nothing
            SyncLock y
                x.ToString()
            end SyncLock
        end SyncLock
    End Sub
End Class
]]></file>
                           </compilation>

            Dim compilation0 = CreateCompilationWithMscorlib(sources0, UnoptimizedDll)
            Dim compilation1 = CreateCompilationWithMscorlib(sources0, UnoptimizedDll)

            Dim testData0 = New CompilationTestData()
            Dim bytes0 = compilation0.EmitToArray(debug:=True, testData:=testData0)

            Dim actualIL0 = testData0.GetMethodData("C.M").GetMethodIL()
            Dim expectedIL0 =
            <![CDATA[
{
  // Code size       90 (0x5a)
  .maxstack  2
  .locals init (System.Guid() V_0, //x
  Object V_1, //VB$Lock
  Boolean V_2, //VB$LockTaken
  System.Guid() V_3, //y
  Object V_4, //VB$Lock
  Boolean V_5, //VB$LockTaken
  Boolean V_6)
  IL_0000:  nop
  IL_0001:  ldnull
  IL_0002:  stloc.0
  IL_0003:  nop
  IL_0004:  ldloc.0
  IL_0005:  stloc.1
  IL_0006:  ldc.i4.0
  IL_0007:  stloc.2
  .try
{
  IL_0008:  ldloc.1
  IL_0009:  ldloca.s   V_2
  IL_000b:  call       "Sub System.Threading.Monitor.Enter(Object, ByRef Boolean)"
  IL_0010:  nop
  IL_0011:  ldnull
  IL_0012:  stloc.3
  IL_0013:  nop
  IL_0014:  ldloc.3
  IL_0015:  stloc.s    V_4
  IL_0017:  ldc.i4.0
  IL_0018:  stloc.s    V_5
  .try
{
  IL_001a:  ldloc.s    V_4
  IL_001c:  ldloca.s   V_5
  IL_001e:  call       "Sub System.Threading.Monitor.Enter(Object, ByRef Boolean)"
  IL_0023:  nop
  IL_0024:  ldloc.0
  IL_0025:  callvirt   "Function Object.ToString() As String"
  IL_002a:  pop
  IL_002b:  leave.s    IL_0042
}
  finally
{
  IL_002d:  ldloc.s    V_5
  IL_002f:  ldc.i4.0
  IL_0030:  ceq
  IL_0032:  stloc.s    V_6
  IL_0034:  ldloc.s    V_6
  IL_0036:  brtrue.s   IL_0040
  IL_0038:  ldloc.s    V_4
  IL_003a:  call       "Sub System.Threading.Monitor.Exit(Object)"
  IL_003f:  nop
  IL_0040:  nop
  IL_0041:  endfinally
}
  IL_0042:  nop
  IL_0043:  leave.s    IL_0058
}
  finally
{
  IL_0045:  ldloc.2
  IL_0046:  ldc.i4.0
  IL_0047:  ceq
  IL_0049:  stloc.s    V_6
  IL_004b:  ldloc.s    V_6
  IL_004d:  brtrue.s   IL_0056
  IL_004f:  ldloc.1
  IL_0050:  call       "Sub System.Threading.Monitor.Exit(Object)"
  IL_0055:  nop
  IL_0056:  nop
  IL_0057:  endfinally
}
  IL_0058:  nop
  IL_0059:  ret
}
]]>.Value

            AssertEx.AssertEqualToleratingWhitespaceDifferences(expectedIL0, actualIL0)

            Dim method0 = compilation0.GetMember(Of MethodSymbol)("C.M")
            Dim method1 = compilation1.GetMember(Of MethodSymbol)("C.M")

            Dim getLocalNamesFunc As LocalVariableNameProvider = Function(m) GetLocalNames(testData0.GetMethodData("C.M"))
            Dim generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), getLocalNamesFunc)

            Dim diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(New SemanticEdit(SemanticEditKind.Update, method0, method1, preserveLocalVariables:=True)))

            diff1.VerifyIL("C.M", <![CDATA[
{
  // Code size       90 (0x5a)
  .maxstack  2
  .locals init (System.Guid() V_0, //x
  Object V_1, //VB$Lock
  Boolean V_2, //VB$LockTaken
  System.Guid() V_3, //y
  Object V_4, //VB$Lock
  Boolean V_5, //VB$LockTaken
  Boolean V_6,
  Boolean V_7)
  IL_0000:  nop
  IL_0001:  ldnull
  IL_0002:  stloc.0
  IL_0003:  nop
  IL_0004:  ldloc.0
  IL_0005:  stloc.1
  IL_0006:  ldc.i4.0
  IL_0007:  stloc.2
  .try
{
  IL_0008:  ldloc.1
  IL_0009:  ldloca.s   V_2
  IL_000b:  call       "Sub System.Threading.Monitor.Enter(Object, ByRef Boolean)"
  IL_0010:  nop
  IL_0011:  ldnull
  IL_0012:  stloc.3
  IL_0013:  nop
  IL_0014:  ldloc.3
  IL_0015:  stloc.s    V_4
  IL_0017:  ldc.i4.0
  IL_0018:  stloc.s    V_5
  .try
{
  IL_001a:  ldloc.s    V_4
  IL_001c:  ldloca.s   V_5
  IL_001e:  call       "Sub System.Threading.Monitor.Enter(Object, ByRef Boolean)"
  IL_0023:  nop
  IL_0024:  ldloc.0
  IL_0025:  callvirt   "Function Object.ToString() As String"
  IL_002a:  pop
  IL_002b:  leave.s    IL_0042
}
  finally
{
  IL_002d:  ldloc.s    V_5
  IL_002f:  ldc.i4.0
  IL_0030:  ceq
  IL_0032:  stloc.s    V_7
  IL_0034:  ldloc.s    V_7
  IL_0036:  brtrue.s   IL_0040
  IL_0038:  ldloc.s    V_4
  IL_003a:  call       "Sub System.Threading.Monitor.Exit(Object)"
  IL_003f:  nop
  IL_0040:  nop
  IL_0041:  endfinally
}
  IL_0042:  nop
  IL_0043:  leave.s    IL_0058
}
  finally
{
  IL_0045:  ldloc.2
  IL_0046:  ldc.i4.0
  IL_0047:  ceq
  IL_0049:  stloc.s    V_7
  IL_004b:  ldloc.s    V_7
  IL_004d:  brtrue.s   IL_0056
  IL_004f:  ldloc.1
  IL_0050:  call       "Sub System.Threading.Monitor.Exit(Object)"
  IL_0055:  nop
  IL_0056:  nop
  IL_0057:  endfinally
}
  IL_0058:  nop
  IL_0059:  ret
}
]]>.Value)
        End Sub

        ''' <summary>
        ''' Local slots must be preserved based on signature.
        ''' </summary>
        <Fact>
        Public Sub PreserveLocalSlotsForEach()
            Dim sources0 = <compilation>
                               <file name="a.vb"><![CDATA[

Class A(Of T)
End Class
Class C
    Inherits A(Of C)
    Shared Function F() As C
        Return Nothing
    End Function
    Shared Sub M(o As Object)
        dim x as System.Collections.Generic.List(of integer) = nothing
        for each [i] in [x]
        Next
        for each i as integer in x
        Next
    End Sub
End Class
]]></file>
                           </compilation>

            Dim compilation0 = CreateCompilationWithMscorlib(sources0, UnoptimizedDll)
            Dim compilation1 = CreateCompilationWithMscorlib(sources0, UnoptimizedDll)

            Dim testData0 = New CompilationTestData()
            Dim bytes0 = compilation0.EmitToArray(debug:=True, testData:=testData0)

            Dim actualIL0 = testData0.GetMethodData("C.M").GetMethodIL()
            Dim expectedIL0 =
            <![CDATA[
{
  // Code size       99 (0x63)
  .maxstack  1
  .locals init (System.Collections.Generic.List(Of Integer) V_0, //x
  System.Collections.Generic.List(Of Integer).Enumerator V_1, //VB$ForEachEnumerator
  Integer V_2, //i
  Boolean V_3,
  System.Collections.Generic.List(Of Integer).Enumerator V_4, //VB$ForEachEnumerator
  Integer V_5) //i
  IL_0000:  nop
  IL_0001:  ldnull
  IL_0002:  stloc.0
  .try
{
  IL_0003:  ldloc.0
  IL_0004:  callvirt   "Function System.Collections.Generic.List(Of Integer).GetEnumerator() As System.Collections.Generic.List(Of Integer).Enumerator"
  IL_0009:  stloc.1
  IL_000a:  br.s       IL_0015
  IL_000c:  ldloca.s   V_1
  IL_000e:  call       "Function System.Collections.Generic.List(Of Integer).Enumerator.get_Current() As Integer"
  IL_0013:  stloc.2
  IL_0014:  nop
  IL_0015:  ldloca.s   V_1
  IL_0017:  call       "Function System.Collections.Generic.List(Of Integer).Enumerator.MoveNext() As Boolean"
  IL_001c:  stloc.3
  IL_001d:  ldloc.3
  IL_001e:  brtrue.s   IL_000c
  IL_0020:  leave.s    IL_0031
}
  finally
{
  IL_0022:  ldloca.s   V_1
  IL_0024:  constrained. "System.Collections.Generic.List(Of Integer).Enumerator"
  IL_002a:  callvirt   "Sub System.IDisposable.Dispose()"
  IL_002f:  nop
  IL_0030:  endfinally
}
  IL_0031:  nop
  .try
{
  IL_0032:  ldloc.0
  IL_0033:  callvirt   "Function System.Collections.Generic.List(Of Integer).GetEnumerator() As System.Collections.Generic.List(Of Integer).Enumerator"
  IL_0038:  stloc.s    V_4
  IL_003a:  br.s       IL_0046
  IL_003c:  ldloca.s   V_4
  IL_003e:  call       "Function System.Collections.Generic.List(Of Integer).Enumerator.get_Current() As Integer"
  IL_0043:  stloc.s    V_5
  IL_0045:  nop
  IL_0046:  ldloca.s   V_4
  IL_0048:  call       "Function System.Collections.Generic.List(Of Integer).Enumerator.MoveNext() As Boolean"
  IL_004d:  stloc.3
  IL_004e:  ldloc.3
  IL_004f:  brtrue.s   IL_003c
  IL_0051:  leave.s    IL_0062
}
  finally
{
  IL_0053:  ldloca.s   V_4
  IL_0055:  constrained. "System.Collections.Generic.List(Of Integer).Enumerator"
  IL_005b:  callvirt   "Sub System.IDisposable.Dispose()"
  IL_0060:  nop
  IL_0061:  endfinally
}
  IL_0062:  ret
}
]]>.Value

            AssertEx.AssertEqualToleratingWhitespaceDifferences(expectedIL0, actualIL0)

            Dim method0 = compilation0.GetMember(Of MethodSymbol)("C.M")
            Dim method1 = compilation1.GetMember(Of MethodSymbol)("C.M")

            Dim getLocalNamesFunc As LocalVariableNameProvider = Function(m) GetLocalNames(testData0.GetMethodData("C.M"))
            Dim generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), getLocalNamesFunc)

            Dim diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(New SemanticEdit(SemanticEditKind.Update, method0, method1, preserveLocalVariables:=True)))

            diff1.VerifyIL("C.M", <![CDATA[
{
  // Code size      103 (0x67)
  .maxstack  1
  .locals init (System.Collections.Generic.List(Of Integer) V_0, //x
  System.Collections.Generic.List(Of Integer).Enumerator V_1, //VB$ForEachEnumerator
  Integer V_2, //i
  Boolean V_3,
  System.Collections.Generic.List(Of Integer).Enumerator V_4, //VB$ForEachEnumerator
  Integer V_5, //i
  Boolean V_6)
  IL_0000:  nop
  IL_0001:  ldnull
  IL_0002:  stloc.0
  .try
{
  IL_0003:  ldloc.0
  IL_0004:  callvirt   "Function System.Collections.Generic.List(Of Integer).GetEnumerator() As System.Collections.Generic.List(Of Integer).Enumerator"
  IL_0009:  stloc.1
  IL_000a:  br.s       IL_0015
  IL_000c:  ldloca.s   V_1
  IL_000e:  call       "Function System.Collections.Generic.List(Of Integer).Enumerator.get_Current() As Integer"
  IL_0013:  stloc.2
  IL_0014:  nop
  IL_0015:  ldloca.s   V_1
  IL_0017:  call       "Function System.Collections.Generic.List(Of Integer).Enumerator.MoveNext() As Boolean"
  IL_001c:  stloc.s    V_6
  IL_001e:  ldloc.s    V_6
  IL_0020:  brtrue.s   IL_000c
  IL_0022:  leave.s    IL_0033
}
  finally
{
  IL_0024:  ldloca.s   V_1
  IL_0026:  constrained. "System.Collections.Generic.List(Of Integer).Enumerator"
  IL_002c:  callvirt   "Sub System.IDisposable.Dispose()"
  IL_0031:  nop
  IL_0032:  endfinally
}
  IL_0033:  nop
  .try
{
  IL_0034:  ldloc.0
  IL_0035:  callvirt   "Function System.Collections.Generic.List(Of Integer).GetEnumerator() As System.Collections.Generic.List(Of Integer).Enumerator"
  IL_003a:  stloc.s    V_4
  IL_003c:  br.s       IL_0048
  IL_003e:  ldloca.s   V_4
  IL_0040:  call       "Function System.Collections.Generic.List(Of Integer).Enumerator.get_Current() As Integer"
  IL_0045:  stloc.s    V_5
  IL_0047:  nop
  IL_0048:  ldloca.s   V_4
  IL_004a:  call       "Function System.Collections.Generic.List(Of Integer).Enumerator.MoveNext() As Boolean"
  IL_004f:  stloc.s    V_6
  IL_0051:  ldloc.s    V_6
  IL_0053:  brtrue.s   IL_003e
  IL_0055:  leave.s    IL_0066
}
  finally
{
  IL_0057:  ldloca.s   V_4
  IL_0059:  constrained. "System.Collections.Generic.List(Of Integer).Enumerator"
  IL_005f:  callvirt   "Sub System.IDisposable.Dispose()"
  IL_0064:  nop
  IL_0065:  endfinally
}
  IL_0066:  ret
}
]]>.Value)
        End Sub

        ''' <summary>
        ''' Local slots must be preserved based on signature.
        ''' </summary>
        <Fact>
        Public Sub PreserveLocalSlotsForEach001()
            Dim sources0 = <compilation>
                               <file name="a.vb"><![CDATA[
Class A(Of T)
End Class
Class C
    Inherits A(Of C)
    Shared Function F() As C
        Return Nothing
    End Function
    Shared Sub M(o As Object)
        dim x as System.Collections.Generic.List(of integer) = nothing
        Dim i as integer
        for each i in x
        Next
        for each i in x
        Next
    End Sub
End Class
]]></file>
                           </compilation>

            Dim compilation0 = CreateCompilationWithMscorlib(sources0, UnoptimizedDll)
            Dim compilation1 = CreateCompilationWithMscorlib(sources0, UnoptimizedDll)

            Dim testData0 = New CompilationTestData()
            Dim bytes0 = compilation0.EmitToArray(debug:=True, testData:=testData0)

            Dim actualIL0 = testData0.GetMethodData("C.M").GetMethodIL()
            Dim expectedIL0 =
            <![CDATA[
{
  // Code size       98 (0x62)
  .maxstack  1
  .locals init (System.Collections.Generic.List(Of Integer) V_0, //x
  Integer V_1, //i
  System.Collections.Generic.List(Of Integer).Enumerator V_2, //VB$ForEachEnumerator
  Boolean V_3,
  System.Collections.Generic.List(Of Integer).Enumerator V_4) //VB$ForEachEnumerator
  IL_0000:  nop
  IL_0001:  ldnull
  IL_0002:  stloc.0
  .try
{
  IL_0003:  ldloc.0
  IL_0004:  callvirt   "Function System.Collections.Generic.List(Of Integer).GetEnumerator() As System.Collections.Generic.List(Of Integer).Enumerator"
  IL_0009:  stloc.2
  IL_000a:  br.s       IL_0015
  IL_000c:  ldloca.s   V_2
  IL_000e:  call       "Function System.Collections.Generic.List(Of Integer).Enumerator.get_Current() As Integer"
  IL_0013:  stloc.1
  IL_0014:  nop
  IL_0015:  ldloca.s   V_2
  IL_0017:  call       "Function System.Collections.Generic.List(Of Integer).Enumerator.MoveNext() As Boolean"
  IL_001c:  stloc.3
  IL_001d:  ldloc.3
  IL_001e:  brtrue.s   IL_000c
  IL_0020:  leave.s    IL_0031
}
  finally
{
  IL_0022:  ldloca.s   V_2
  IL_0024:  constrained. "System.Collections.Generic.List(Of Integer).Enumerator"
  IL_002a:  callvirt   "Sub System.IDisposable.Dispose()"
  IL_002f:  nop
  IL_0030:  endfinally
}
  IL_0031:  nop
  .try
{
  IL_0032:  ldloc.0
  IL_0033:  callvirt   "Function System.Collections.Generic.List(Of Integer).GetEnumerator() As System.Collections.Generic.List(Of Integer).Enumerator"
  IL_0038:  stloc.s    V_4
  IL_003a:  br.s       IL_0045
  IL_003c:  ldloca.s   V_4
  IL_003e:  call       "Function System.Collections.Generic.List(Of Integer).Enumerator.get_Current() As Integer"
  IL_0043:  stloc.1
  IL_0044:  nop
  IL_0045:  ldloca.s   V_4
  IL_0047:  call       "Function System.Collections.Generic.List(Of Integer).Enumerator.MoveNext() As Boolean"
  IL_004c:  stloc.3
  IL_004d:  ldloc.3
  IL_004e:  brtrue.s   IL_003c
  IL_0050:  leave.s    IL_0061
}
  finally
{
  IL_0052:  ldloca.s   V_4
  IL_0054:  constrained. "System.Collections.Generic.List(Of Integer).Enumerator"
  IL_005a:  callvirt   "Sub System.IDisposable.Dispose()"
  IL_005f:  nop
  IL_0060:  endfinally
}
  IL_0061:  ret
}
]]>.Value

            AssertEx.AssertEqualToleratingWhitespaceDifferences(expectedIL0, actualIL0)

            Dim method0 = compilation0.GetMember(Of MethodSymbol)("C.M")
            Dim method1 = compilation1.GetMember(Of MethodSymbol)("C.M")

            Dim getLocalNamesFunc As LocalVariableNameProvider = Function(m) GetLocalNames(testData0.GetMethodData("C.M"))
            Dim generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), getLocalNamesFunc)

            Dim diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(New SemanticEdit(SemanticEditKind.Update, method0, method1, preserveLocalVariables:=True)))

            diff1.VerifyIL("C.M", <![CDATA[
{
  // Code size      102 (0x66)
  .maxstack  1
  .locals init (System.Collections.Generic.List(Of Integer) V_0, //x
  Integer V_1, //i
  System.Collections.Generic.List(Of Integer).Enumerator V_2, //VB$ForEachEnumerator
  Boolean V_3,
  System.Collections.Generic.List(Of Integer).Enumerator V_4, //VB$ForEachEnumerator
  Boolean V_5)
  IL_0000:  nop
  IL_0001:  ldnull
  IL_0002:  stloc.0
  .try
{
  IL_0003:  ldloc.0
  IL_0004:  callvirt   "Function System.Collections.Generic.List(Of Integer).GetEnumerator() As System.Collections.Generic.List(Of Integer).Enumerator"
  IL_0009:  stloc.2
  IL_000a:  br.s       IL_0015
  IL_000c:  ldloca.s   V_2
  IL_000e:  call       "Function System.Collections.Generic.List(Of Integer).Enumerator.get_Current() As Integer"
  IL_0013:  stloc.1
  IL_0014:  nop
  IL_0015:  ldloca.s   V_2
  IL_0017:  call       "Function System.Collections.Generic.List(Of Integer).Enumerator.MoveNext() As Boolean"
  IL_001c:  stloc.s    V_5
  IL_001e:  ldloc.s    V_5
  IL_0020:  brtrue.s   IL_000c
  IL_0022:  leave.s    IL_0033
}
  finally
{
  IL_0024:  ldloca.s   V_2
  IL_0026:  constrained. "System.Collections.Generic.List(Of Integer).Enumerator"
  IL_002c:  callvirt   "Sub System.IDisposable.Dispose()"
  IL_0031:  nop
  IL_0032:  endfinally
}
  IL_0033:  nop
  .try
{
  IL_0034:  ldloc.0
  IL_0035:  callvirt   "Function System.Collections.Generic.List(Of Integer).GetEnumerator() As System.Collections.Generic.List(Of Integer).Enumerator"
  IL_003a:  stloc.s    V_4
  IL_003c:  br.s       IL_0047
  IL_003e:  ldloca.s   V_4
  IL_0040:  call       "Function System.Collections.Generic.List(Of Integer).Enumerator.get_Current() As Integer"
  IL_0045:  stloc.1
  IL_0046:  nop
  IL_0047:  ldloca.s   V_4
  IL_0049:  call       "Function System.Collections.Generic.List(Of Integer).Enumerator.MoveNext() As Boolean"
  IL_004e:  stloc.s    V_5
  IL_0050:  ldloc.s    V_5
  IL_0052:  brtrue.s   IL_003e
  IL_0054:  leave.s    IL_0065
}
  finally
{
  IL_0056:  ldloca.s   V_4
  IL_0058:  constrained. "System.Collections.Generic.List(Of Integer).Enumerator"
  IL_005e:  callvirt   "Sub System.IDisposable.Dispose()"
  IL_0063:  nop
  IL_0064:  endfinally
}
  IL_0065:  ret
}
]]>.Value)
        End Sub

        ''' <summary>
        ''' Local slots must be preserved based on signature.
        ''' </summary>
        <Fact>
        Public Sub PreserveLocalSlotsFor001()
            Dim sources0 = <compilation>
                               <file name="a.vb"><![CDATA[
Class A(Of T)
End Class
Class C
    Inherits A(Of C)
    Shared Function F() As C
        Return Nothing
    End Function
    Shared Sub M(o As object)
        for i as double = foo() to foo() step foo()
            for j as double = foo() to foo() step foo()
            next
        next
    End Sub

    shared function foo() as double
        return 1
    end function
End Class
]]></file>
                           </compilation>

            Dim compilation0 = CreateCompilationWithMscorlib(sources0, UnoptimizedDll)
            Dim compilation1 = CreateCompilationWithMscorlib(sources0, UnoptimizedDll)

            Dim testData0 = New CompilationTestData()
            Dim bytes0 = compilation0.EmitToArray(debug:=True, testData:=testData0)

            Dim actualIL0 = testData0.GetMethodData("C.M").GetMethodIL()
            Dim expectedIL0 =
            <![CDATA[
{
  // Code size      158 (0x9e)
  .maxstack  2
  .locals init (Double V_0, //VB$LoopObject
  Double V_1, //VB$ForLimit
  Double V_2, //VB$ForStep
  Boolean V_3, //VB$LoopDirection
  Double V_4, //i
  Double V_5,
  Double V_6, //VB$LoopObject
  Double V_7, //VB$ForLimit
  Double V_8, //VB$ForStep
  Boolean V_9, //VB$LoopDirection
  Double V_10, //j
  Boolean V_11)
  IL_0000:  nop
  IL_0001:  call       "Function C.foo() As Double"
  IL_0006:  stloc.s    V_5
  IL_0008:  call       "Function C.foo() As Double"
  IL_000d:  stloc.1
  IL_000e:  call       "Function C.foo() As Double"
  IL_0013:  stloc.2
  IL_0014:  ldloc.2
  IL_0015:  ldc.r8     0
  IL_001e:  clt.un
  IL_0020:  ldc.i4.0
  IL_0021:  ceq
  IL_0023:  stloc.3
  IL_0024:  ldloc.s    V_5
  IL_0026:  stloc.s    V_4
  IL_0028:  br.s       IL_0082
  IL_002a:  call       "Function C.foo() As Double"
  IL_002f:  stloc.s    V_5
  IL_0031:  call       "Function C.foo() As Double"
  IL_0036:  stloc.s    V_7
  IL_0038:  call       "Function C.foo() As Double"
  IL_003d:  stloc.s    V_8
  IL_003f:  ldloc.s    V_8
  IL_0041:  ldc.r8     0
  IL_004a:  clt.un
  IL_004c:  ldc.i4.0
  IL_004d:  ceq
  IL_004f:  stloc.s    V_9
  IL_0051:  ldloc.s    V_5
  IL_0053:  stloc.s    V_10
  IL_0055:  br.s       IL_005e
  IL_0057:  ldloc.s    V_10
  IL_0059:  ldloc.s    V_8
  IL_005b:  add
  IL_005c:  stloc.s    V_10
  IL_005e:  ldloc.s    V_9
  IL_0060:  brtrue.s   IL_006d
  IL_0062:  ldloc.s    V_10
  IL_0064:  ldloc.s    V_7
  IL_0066:  clt.un
  IL_0068:  ldc.i4.0
  IL_0069:  ceq
  IL_006b:  br.s       IL_0076
  IL_006d:  ldloc.s    V_10
  IL_006f:  ldloc.s    V_7
  IL_0071:  cgt.un
  IL_0073:  ldc.i4.0
  IL_0074:  ceq
  IL_0076:  stloc.s    V_11
  IL_0078:  ldloc.s    V_11
  IL_007a:  brtrue.s   IL_0057
  IL_007c:  ldloc.s    V_4
  IL_007e:  ldloc.2
  IL_007f:  add
  IL_0080:  stloc.s    V_4
  IL_0082:  ldloc.3
  IL_0083:  brtrue.s   IL_008f
  IL_0085:  ldloc.s    V_4
  IL_0087:  ldloc.1
  IL_0088:  clt.un
  IL_008a:  ldc.i4.0
  IL_008b:  ceq
  IL_008d:  br.s       IL_0097
  IL_008f:  ldloc.s    V_4
  IL_0091:  ldloc.1
  IL_0092:  cgt.un
  IL_0094:  ldc.i4.0
  IL_0095:  ceq
  IL_0097:  stloc.s    V_11
  IL_0099:  ldloc.s    V_11
  IL_009b:  brtrue.s   IL_002a
  IL_009d:  ret
}
]]>.Value

            AssertEx.AssertEqualToleratingWhitespaceDifferences(expectedIL0, actualIL0)

            Dim method0 = compilation0.GetMember(Of MethodSymbol)("C.M")
            Dim method1 = compilation1.GetMember(Of MethodSymbol)("C.M")

            Dim getLocalNamesFunc As LocalVariableNameProvider = Function(m) GetLocalNames(testData0.GetMethodData("C.M"))
            Dim generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), getLocalNamesFunc)

            Dim diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(New SemanticEdit(SemanticEditKind.Update, method0, method1, preserveLocalVariables:=True)))

            diff1.VerifyIL("C.M", <![CDATA[
{
  // Code size      158 (0x9e)
  .maxstack  2
  .locals init (Double V_0, //VB$LoopObject
  Double V_1, //VB$ForLimit
  Double V_2, //VB$ForStep
  Boolean V_3, //VB$LoopDirection
  Double V_4, //i
  Double V_5,
  Double V_6, //VB$LoopObject
  Double V_7, //VB$ForLimit
  Double V_8, //VB$ForStep
  Boolean V_9, //VB$LoopDirection
  Double V_10, //j
  Boolean V_11,
  Double V_12,
  Boolean V_13)
  IL_0000:  nop
  IL_0001:  call       "Function C.foo() As Double"
  IL_0006:  stloc.s    V_12
  IL_0008:  call       "Function C.foo() As Double"
  IL_000d:  stloc.1
  IL_000e:  call       "Function C.foo() As Double"
  IL_0013:  stloc.2
  IL_0014:  ldloc.2
  IL_0015:  ldc.r8     0
  IL_001e:  clt.un
  IL_0020:  ldc.i4.0
  IL_0021:  ceq
  IL_0023:  stloc.3
  IL_0024:  ldloc.s    V_12
  IL_0026:  stloc.s    V_4
  IL_0028:  br.s       IL_0082
  IL_002a:  call       "Function C.foo() As Double"
  IL_002f:  stloc.s    V_12
  IL_0031:  call       "Function C.foo() As Double"
  IL_0036:  stloc.s    V_7
  IL_0038:  call       "Function C.foo() As Double"
  IL_003d:  stloc.s    V_8
  IL_003f:  ldloc.s    V_8
  IL_0041:  ldc.r8     0
  IL_004a:  clt.un
  IL_004c:  ldc.i4.0
  IL_004d:  ceq
  IL_004f:  stloc.s    V_9
  IL_0051:  ldloc.s    V_12
  IL_0053:  stloc.s    V_10
  IL_0055:  br.s       IL_005e
  IL_0057:  ldloc.s    V_10
  IL_0059:  ldloc.s    V_8
  IL_005b:  add
  IL_005c:  stloc.s    V_10
  IL_005e:  ldloc.s    V_9
  IL_0060:  brtrue.s   IL_006d
  IL_0062:  ldloc.s    V_10
  IL_0064:  ldloc.s    V_7
  IL_0066:  clt.un
  IL_0068:  ldc.i4.0
  IL_0069:  ceq
  IL_006b:  br.s       IL_0076
  IL_006d:  ldloc.s    V_10
  IL_006f:  ldloc.s    V_7
  IL_0071:  cgt.un
  IL_0073:  ldc.i4.0
  IL_0074:  ceq
  IL_0076:  stloc.s    V_13
  IL_0078:  ldloc.s    V_13
  IL_007a:  brtrue.s   IL_0057
  IL_007c:  ldloc.s    V_4
  IL_007e:  ldloc.2
  IL_007f:  add
  IL_0080:  stloc.s    V_4
  IL_0082:  ldloc.3
  IL_0083:  brtrue.s   IL_008f
  IL_0085:  ldloc.s    V_4
  IL_0087:  ldloc.1
  IL_0088:  clt.un
  IL_008a:  ldc.i4.0
  IL_008b:  ceq
  IL_008d:  br.s       IL_0097
  IL_008f:  ldloc.s    V_4
  IL_0091:  ldloc.1
  IL_0092:  cgt.un
  IL_0094:  ldc.i4.0
  IL_0095:  ceq
  IL_0097:  stloc.s    V_13
  IL_0099:  ldloc.s    V_13
  IL_009b:  brtrue.s   IL_002a
  IL_009d:  ret
}
]]>.Value)
        End Sub

        ''' <summary>
        ''' Local slots must be preserved based on signature.
        ''' </summary>
        <Fact>
        Public Sub PreserveLocalSlotsImplicit()
            Dim sources0 = <compilation>
                               <file name="a.vb"><![CDATA[

option explicit off

Class A(Of T)
End Class
Class C
    Inherits A(Of C)
    Shared Function F() As C
        Return Nothing
    End Function
    Shared Sub M(o As Object)
        dim x as System.Guid() = nothing
        With x
            Dim z = .ToString
            y = z
        end With
    End Sub
End Class
]]></file>
                           </compilation>

            Dim compilation0 = CreateCompilationWithMscorlib(sources0, UnoptimizedDll)
            Dim compilation1 = CreateCompilationWithMscorlib(sources0, UnoptimizedDll)

            Dim testData0 = New CompilationTestData()
            Dim bytes0 = compilation0.EmitToArray(debug:=True, testData:=testData0)

            Dim actualIL0 = testData0.GetMethodData("C.M").GetMethodIL()
            Dim expectedIL0 =
            <![CDATA[
{
  // Code size       19 (0x13)
  .maxstack  1
  .locals init (Object V_0, //y
  System.Guid() V_1, //x
  System.Guid() V_2, //VB$With
  String V_3) //z
  IL_0000:  nop
  IL_0001:  ldnull
  IL_0002:  stloc.1
  IL_0003:  nop
  IL_0004:  ldloc.1
  IL_0005:  stloc.2
  IL_0006:  ldloc.2
  IL_0007:  callvirt   "Function Object.ToString() As String"
  IL_000c:  stloc.3
  IL_000d:  ldloc.3
  IL_000e:  stloc.0
  IL_000f:  nop
  IL_0010:  ldnull
  IL_0011:  stloc.2
  IL_0012:  ret
}
]]>.Value

            AssertEx.AssertEqualToleratingWhitespaceDifferences(expectedIL0, actualIL0)

            Dim method0 = compilation0.GetMember(Of MethodSymbol)("C.M")
            Dim method1 = compilation1.GetMember(Of MethodSymbol)("C.M")

            Dim getLocalNamesFunc As LocalVariableNameProvider = Function(m) GetLocalNames(testData0.GetMethodData("C.M"))
            Dim generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), getLocalNamesFunc)

            Dim edit = New SemanticEdit(SemanticEditKind.Update, method0, method1, preserveLocalVariables:=True)
            Dim diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(edit))

            diff1.VerifyIL("C.M", <![CDATA[
{
  // Code size       19 (0x13)
  .maxstack  1
  .locals init (Object V_0, //y
  System.Guid() V_1, //x
  System.Guid() V_2, //VB$With
  String V_3) //z
  IL_0000:  nop
  IL_0001:  ldnull
  IL_0002:  stloc.1
  IL_0003:  nop
  IL_0004:  ldloc.1
  IL_0005:  stloc.2
  IL_0006:  ldloc.2
  IL_0007:  callvirt   "Function Object.ToString() As String"
  IL_000c:  stloc.3
  IL_000d:  ldloc.3
  IL_000e:  stloc.0
  IL_000f:  nop
  IL_0010:  ldnull
  IL_0011:  stloc.2
  IL_0012:  ret
}
]]>.Value)
        End Sub

        ''' <summary>
        ''' Local slots must be preserved based on signature.
        ''' </summary>
        <Fact>
        Public Sub PreserveLocalSlotsImplicitQualified()
            Dim sources0 = <compilation>
                               <file name="a.vb"><![CDATA[

option explicit off

Class A(Of T)
End Class

Class C
    Inherits A(Of C)
    Shared Function F() As C
        Return Nothing
    End Function
    Shared Sub M(o As Object)
        dim x as System.Guid() = nothing

        goto Length         ' this does not declare Length
        Length:             ' this does not declare Length

        dim y = x.Length    ' this does not declare Length
        Length = 5          ' this does 

    End Sub
End Class
]]></file>
                           </compilation>

            Dim compilation0 = CreateCompilationWithMscorlib(sources0, UnoptimizedDll)
            Dim compilation1 = CreateCompilationWithMscorlib(sources0, UnoptimizedDll)

            Dim testData0 = New CompilationTestData()
            Dim bytes0 = compilation0.EmitToArray(debug:=True, testData:=testData0)

            Dim actualIL0 = testData0.GetMethodData("C.M").GetMethodIL()
            Dim expectedIL0 =
            <![CDATA[
{
  // Code size       18 (0x12)
  .maxstack  1
  .locals init (Object V_0, //Length
  System.Guid() V_1, //x
  Integer V_2) //y
  IL_0000:  nop
  IL_0001:  ldnull
  IL_0002:  stloc.1
  IL_0003:  br.s       IL_0005
  IL_0005:  nop
  IL_0006:  ldloc.1
  IL_0007:  ldlen
  IL_0008:  conv.i4
  IL_0009:  stloc.2
  IL_000a:  ldc.i4.5
  IL_000b:  box        "Integer"
  IL_0010:  stloc.0
  IL_0011:  ret
}
]]>.Value

            AssertEx.AssertEqualToleratingWhitespaceDifferences(expectedIL0, actualIL0)

            Dim method0 = compilation0.GetMember(Of MethodSymbol)("C.M")
            Dim method1 = compilation1.GetMember(Of MethodSymbol)("C.M")

            Dim getLocalNamesFunc As LocalVariableNameProvider = Function(m) GetLocalNames(testData0.GetMethodData("C.M"))
            Dim generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), getLocalNamesFunc)

            Dim edit = New SemanticEdit(SemanticEditKind.Update, method0, method1, preserveLocalVariables:=True)
            Dim diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(edit))

            diff1.VerifyIL("C.M", <![CDATA[
{
  // Code size       18 (0x12)
  .maxstack  1
  .locals init (Object V_0, //Length
  System.Guid() V_1, //x
  Integer V_2) //y
  IL_0000:  nop
  IL_0001:  ldnull
  IL_0002:  stloc.1
  IL_0003:  br.s       IL_0005
  IL_0005:  nop
  IL_0006:  ldloc.1
  IL_0007:  ldlen
  IL_0008:  conv.i4
  IL_0009:  stloc.2
  IL_000a:  ldc.i4.5
  IL_000b:  box        "Integer"
  IL_0010:  stloc.0
  IL_0011:  ret
}
]]>.Value)
        End Sub

        ''' <summary>
        ''' Local slots must be preserved based on signature.
        ''' </summary>
        <Fact>
        Public Sub PreserveLocalSlotsImplicitXmlNs()
            Dim sources0 = <compilation>
                               <file name="a.vb"><![CDATA[

option explicit off

Imports <xmlns:Length="http://roslyn/F">

Class A(Of T)
End Class

Class C
    Inherits A(Of C)
    Shared Function F() As C
        Return Nothing
    End Function
    Shared Sub M(o As Object)
        dim x as System.Guid() = nothing

        GetXmlNamespace(Length).ToString()    ' this does not declare Length
        dim z as object = GetXmlNamespace(Length)    ' this does not declare Length
        Length = 5          ' this does 

        Dim aa = Length
    End Sub
End Class
]]></file>
                           </compilation>



            Dim compilation0 = CreateCompilationWithMscorlibAndVBRuntimeAndReferences(sources0, XmlReferences, UnoptimizedDll)
            Dim compilation1 = CreateCompilationWithMscorlibAndVBRuntimeAndReferences(sources0, XmlReferences, UnoptimizedDll)

            Dim testData0 = New CompilationTestData()
            Dim bytes0 = compilation0.EmitToArray(debug:=True, testData:=testData0)

            Dim actualIL0 = testData0.GetMethodData("C.M").GetMethodIL()
            Dim expectedIL0 =
            <![CDATA[
{
  // Code size       45 (0x2d)
  .maxstack  1
  .locals init (Object V_0, //Length
  System.Guid() V_1, //x
  Object V_2, //z
  Object V_3) //aa
  IL_0000:  nop
  IL_0001:  ldnull
  IL_0002:  stloc.1
  IL_0003:  ldstr      "http://roslyn/F"
  IL_0008:  call       "Function System.Xml.Linq.XNamespace.Get(String) As System.Xml.Linq.XNamespace"
  IL_000d:  callvirt   "Function System.Xml.Linq.XNamespace.ToString() As String"
  IL_0012:  pop
  IL_0013:  ldstr      "http://roslyn/F"
  IL_0018:  call       "Function System.Xml.Linq.XNamespace.Get(String) As System.Xml.Linq.XNamespace"
  IL_001d:  stloc.2
  IL_001e:  ldc.i4.5
  IL_001f:  box        "Integer"
  IL_0024:  stloc.0
  IL_0025:  ldloc.0
  IL_0026:  call       "Function System.Runtime.CompilerServices.RuntimeHelpers.GetObjectValue(Object) As Object"
  IL_002b:  stloc.3
  IL_002c:  ret
}
]]>.Value

            AssertEx.AssertEqualToleratingWhitespaceDifferences(expectedIL0, actualIL0)

            Dim method0 = compilation0.GetMember(Of MethodSymbol)("C.M")
            Dim method1 = compilation1.GetMember(Of MethodSymbol)("C.M")

            Dim getLocalNamesFunc As LocalVariableNameProvider = Function(m) GetLocalNames(testData0.GetMethodData("C.M"))
            Dim generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), getLocalNamesFunc)

            Dim edit = New SemanticEdit(SemanticEditKind.Update, method0, method1, preserveLocalVariables:=True)
            Dim diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(edit))

            diff1.VerifyIL("C.M", <![CDATA[
{
  // Code size       45 (0x2d)
  .maxstack  1
  .locals init (Object V_0, //Length
  System.Guid() V_1, //x
  Object V_2, //z
  Object V_3) //aa
  IL_0000:  nop
  IL_0001:  ldnull
  IL_0002:  stloc.1
  IL_0003:  ldstr      "http://roslyn/F"
  IL_0008:  call       "Function System.Xml.Linq.XNamespace.Get(String) As System.Xml.Linq.XNamespace"
  IL_000d:  callvirt   "Function System.Xml.Linq.XNamespace.ToString() As String"
  IL_0012:  pop
  IL_0013:  ldstr      "http://roslyn/F"
  IL_0018:  call       "Function System.Xml.Linq.XNamespace.Get(String) As System.Xml.Linq.XNamespace"
  IL_001d:  stloc.2
  IL_001e:  ldc.i4.5
  IL_001f:  box        "Integer"
  IL_0024:  stloc.0
  IL_0025:  ldloc.0
  IL_0026:  call       "Function System.Runtime.CompilerServices.RuntimeHelpers.GetObjectValue(Object) As Object"
  IL_002b:  stloc.3
  IL_002c:  ret
}
]]>.Value)
        End Sub

        ''' <summary>
        ''' Local slots must be preserved based on signature.
        ''' </summary>
        <Fact>
        Public Sub PreserveLocalSlotsImplicitNamedArgXml()
            Dim sources0 = <compilation>
                               <file name="a.vb"><![CDATA[
Option Explicit Off

Class A(Of T)
End Class

Class C
    Inherits A(Of C)
    Shared Function F() As C
        Return Nothing
    End Function
    Shared Sub F(qq As Object)
    End Sub
    Shared Sub M(o As Object)
        F(qq:=<qq a="qq"></>)        'does not declare qq

        qq = 5
        Dim aa = qq
    End Sub
End Class
]]></file>
                           </compilation>

            Dim compilation0 = CreateCompilationWithMscorlibAndVBRuntimeAndReferences(sources0, XmlReferences, UnoptimizedDll)
            Dim compilation1 = CreateCompilationWithMscorlibAndVBRuntimeAndReferences(sources0, XmlReferences, UnoptimizedDll)

            Dim testData0 = New CompilationTestData()
            Dim bytes0 = compilation0.EmitToArray(debug:=True, testData:=testData0)

            Dim actualIL0 = testData0.GetMethodData("C.M").GetMethodIL()
            Dim expectedIL0 =
            <![CDATA[
{
  // Code size       88 (0x58)
  .maxstack  3
  .locals init (Object V_0, //qq
  Object V_1, //aa
  System.Xml.Linq.XElement V_2)
  IL_0000:  nop
  IL_0001:  ldstr      "qq"
  IL_0006:  ldstr      ""
  IL_000b:  call       "Function System.Xml.Linq.XName.Get(String, String) As System.Xml.Linq.XName"
  IL_0010:  newobj     "Sub System.Xml.Linq.XElement..ctor(System.Xml.Linq.XName)"
  IL_0015:  stloc.2
  IL_0016:  ldloc.2
  IL_0017:  ldstr      "a"
  IL_001c:  ldstr      ""
  IL_0021:  call       "Function System.Xml.Linq.XName.Get(String, String) As System.Xml.Linq.XName"
  IL_0026:  ldstr      "qq"
  IL_002b:  newobj     "Sub System.Xml.Linq.XAttribute..ctor(System.Xml.Linq.XName, Object)"
  IL_0030:  callvirt   "Sub System.Xml.Linq.XContainer.Add(Object)"
  IL_0035:  nop
  IL_0036:  ldloc.2
  IL_0037:  ldstr      ""
  IL_003c:  callvirt   "Sub System.Xml.Linq.XContainer.Add(Object)"
  IL_0041:  nop
  IL_0042:  ldloc.2
  IL_0043:  call       "Sub C.F(Object)"
  IL_0048:  nop
  IL_0049:  ldc.i4.5
  IL_004a:  box        "Integer"
  IL_004f:  stloc.0
  IL_0050:  ldloc.0
  IL_0051:  call       "Function System.Runtime.CompilerServices.RuntimeHelpers.GetObjectValue(Object) As Object"
  IL_0056:  stloc.1
  IL_0057:  ret
}
]]>.Value

            AssertEx.AssertEqualToleratingWhitespaceDifferences(expectedIL0, actualIL0)

            Dim method0 = compilation0.GetMember(Of MethodSymbol)("C.M")
            Dim method1 = compilation1.GetMember(Of MethodSymbol)("C.M")

            Dim getLocalNamesFunc As LocalVariableNameProvider = Function(m) GetLocalNames(testData0.GetMethodData("C.M"))
            Dim generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), getLocalNamesFunc)

            Dim edit = New SemanticEdit(SemanticEditKind.Update, method0, method1, preserveLocalVariables:=True)
            Dim diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(edit))

            diff1.VerifyIL("C.M", <![CDATA[
{
  // Code size       88 (0x58)
  .maxstack  3
  .locals init (Object V_0, //qq
  Object V_1, //aa
  System.Xml.Linq.XElement V_2,
  System.Xml.Linq.XElement V_3)
  IL_0000:  nop
  IL_0001:  ldstr      "qq"
  IL_0006:  ldstr      ""
  IL_000b:  call       "Function System.Xml.Linq.XName.Get(String, String) As System.Xml.Linq.XName"
  IL_0010:  newobj     "Sub System.Xml.Linq.XElement..ctor(System.Xml.Linq.XName)"
  IL_0015:  stloc.3
  IL_0016:  ldloc.3
  IL_0017:  ldstr      "a"
  IL_001c:  ldstr      ""
  IL_0021:  call       "Function System.Xml.Linq.XName.Get(String, String) As System.Xml.Linq.XName"
  IL_0026:  ldstr      "qq"
  IL_002b:  newobj     "Sub System.Xml.Linq.XAttribute..ctor(System.Xml.Linq.XName, Object)"
  IL_0030:  callvirt   "Sub System.Xml.Linq.XContainer.Add(Object)"
  IL_0035:  nop
  IL_0036:  ldloc.3
  IL_0037:  ldstr      ""
  IL_003c:  callvirt   "Sub System.Xml.Linq.XContainer.Add(Object)"
  IL_0041:  nop
  IL_0042:  ldloc.3
  IL_0043:  call       "Sub C.F(Object)"
  IL_0048:  nop
  IL_0049:  ldc.i4.5
  IL_004a:  box        "Integer"
  IL_004f:  stloc.0
  IL_0050:  ldloc.0
  IL_0051:  call       "Function System.Runtime.CompilerServices.RuntimeHelpers.GetObjectValue(Object) As Object"
  IL_0056:  stloc.1
  IL_0057:  ret
}
]]>.Value)
        End Sub

        <Fact()>
        Public Sub AnonymousTypes()
            Dim sources0 = <compilation>
                               <file name="a.vb"><![CDATA[
Namespace N
    Class A
        Shared F As Object = New With {.A = 1, .B = 2}
    End Class
End Namespace
Namespace M
    Class B
        Shared Sub M()
            Dim x As New With {.B = 3, .A = 4}
            Dim y = x.A
            Dim z As New With {.C = 5}
        End Sub
    End Class
End Namespace
]]></file>
                           </compilation>
            Dim sources1 = <compilation>
                               <file name="a.vb"><![CDATA[
Namespace N
    Class A
        Shared F As Object = New With {.A = 1, .B = 2}
    End Class
End Namespace
Namespace M
    Class B
        Shared Sub M()
            Dim x As New With {.B = 3, .A = 4}
            Dim y As New With {.A = x.A}
            Dim z As New With {.C = 5}
        End Sub
    End Class
End Namespace
]]></file>
                           </compilation>

            Dim compilation0 = CreateCompilationWithMscorlib(sources0, UnoptimizedDll)
            Dim compilation1 = CreateCompilationWithMscorlib(sources1, UnoptimizedDll)

            Dim bytes0 = compilation0.EmitToArray(debug:=True)
            Using md0 = ModuleMetadata.CreateFromImage(bytes0)
                Dim generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), Function(m) ImmutableArray.Create("x", "y", "z"))
                Dim method0 = compilation0.GetMember(Of MethodSymbol)("M.B.M")
                Dim reader0 = md0.MetadataReader
                CheckNames(reader0, reader0.GetTypeDefNames(), "<Module>", "VB$AnonymousType_1`2", "VB$AnonymousType_0`2", "VB$AnonymousType_2`1", "A", "B")
                Dim method1 = compilation1.GetMember(Of MethodSymbol)("M.B.M")
                Dim diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(New SemanticEdit(SemanticEditKind.Update, method0, method1, GetLocalMap(method1, method0), preserveLocalVariables:=True)))
                Using md1 = diff1.GetMetadata()
                    Dim reader1 = md1.Reader
                    CheckNames({reader0, reader1}, reader1.GetTypeDefNames(), "VB$AnonymousType_3`1")
                    diff1.VerifyIL("M.B.M", <![CDATA[
{
  // Code size       29 (0x1d)
  .maxstack  2
  .locals init (VB$AnonymousType_1(Of Integer, Integer) V_0, //x
  Integer V_1,
  VB$AnonymousType_2(Of Integer) V_2, //z
  VB$AnonymousType_3(Of Integer) V_3) //y
  IL_0000:  nop
  IL_0001:  ldc.i4.3
  IL_0002:  ldc.i4.4
  IL_0003:  newobj     "Sub VB$AnonymousType_1(Of Integer, Integer)..ctor(Integer, Integer)"
  IL_0008:  stloc.0
  IL_0009:  ldloc.0
  IL_000a:  callvirt   "Function VB$AnonymousType_1(Of Integer, Integer).get_A() As Integer"
  IL_000f:  newobj     "Sub VB$AnonymousType_3(Of Integer)..ctor(Integer)"
  IL_0014:  stloc.3
  IL_0015:  ldc.i4.5
  IL_0016:  newobj     "Sub VB$AnonymousType_2(Of Integer)..ctor(Integer)"
  IL_001b:  stloc.2
  IL_001c:  ret
}
]]>.Value)
                End Using
            End Using
        End Sub

        <Fact()>
        Public Sub AnonymousDelegates()
            Dim sources0 = <compilation>
                               <file name="a.vb"><![CDATA[
Class C
    Sub M()
        Dim s = Sub() Return
        Dim t = Sub(o As C) o.M() 
    End Sub
    Shared Sub N()
        Dim x = New With {.P = 0}
        Dim s = Function(o As Object) o
        Dim t = Sub(o As Object) Return
        Dim u = Sub(c As C) c.GetHashCode() 
    End Sub
End Class
]]></file>
                           </compilation>
            Dim sources1 = <compilation>
                               <file name="a.vb"><![CDATA[
Class C
    Sub M()
        Dim s = Sub() Return
        Dim t = Sub(o As C) o.M() 
    End Sub
    Shared Sub N()
        Dim x = New With {.Q = 1}
        Dim s = Function(c As Object) c
        Dim t = Sub(c as Object) Return
        Dim u = Sub(c As C) c.GetHashCode() 
    End Sub
End Class
]]></file>
                           </compilation>

            Dim compilation0 = CreateCompilationWithMscorlib(sources0, UnoptimizedDll)
            Dim compilation1 = CreateCompilationWithMscorlib(sources1, UnoptimizedDll)

            Dim bytes0 = compilation0.EmitToArray(debug:=True)
            Using md0 = ModuleMetadata.CreateFromImage(bytes0)
                Dim generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), Function(m) ImmutableArray.Create("x", "s", "t", "u"))
                Dim method0 = compilation0.GetMember(Of MethodSymbol)("C.N")
                Dim reader0 = md0.MetadataReader
                CheckNamesSorted({reader0}, reader0.GetTypeDefNames(), "<Module>", "C", "VB$AnonymousType_0`1", "VB$AnonymousDelegate_0", "VB$AnonymousDelegate_1`1", "VB$AnonymousDelegate_2`2", "VB$AnonymousDelegate_3`1")
                Dim method1 = compilation1.GetMember(Of MethodSymbol)("C.N")
                Dim diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(New SemanticEdit(SemanticEditKind.Update, method0, method1, GetLocalMap(method1, method0), preserveLocalVariables:=True)))
                Using md1 = diff1.GetMetadata()
                    Dim reader1 = md1.Reader
                    CheckNamesSorted({reader0, reader1}, reader1.GetTypeDefNames(), "VB$AnonymousDelegate_4`2", "VB$AnonymousType_1`1")
                    diff1.VerifyIL("C.N", <![CDATA[
{
  // Code size      111 (0x6f)
  .maxstack  2
  .locals init (VB$AnonymousType_0(Of Integer) V_0,
  VB$AnonymousDelegate_2(Of Object, Object) V_1,
  VB$AnonymousDelegate_1(Of Object) V_2,
  VB$AnonymousDelegate_3(Of C) V_3, //u
  VB$AnonymousType_1(Of Integer) V_4, //x
  VB$AnonymousDelegate_4(Of Object, Object) V_5, //s
  VB$AnonymousDelegate_3(Of Object) V_6) //t
  IL_0000:  nop
  IL_0001:  ldc.i4.1
  IL_0002:  newobj     "Sub VB$AnonymousType_1(Of Integer)..ctor(Integer)"
  IL_0007:  stloc.s    V_4
  IL_0009:  ldsfld     "C._ClosureCache$__2 As <generated method>"
  IL_000e:  brfalse.s  IL_0017
  IL_0010:  ldsfld     "C._ClosureCache$__2 As <generated method>"
  IL_0015:  br.s       IL_0029
  IL_0017:  ldnull
  IL_0018:  ldftn      "Function C._Lambda$__1(Object) As Object"
  IL_001e:  newobj     "Sub VB$AnonymousDelegate_4(Of Object, Object)..ctor(Object, System.IntPtr)"
  IL_0023:  dup
  IL_0024:  stsfld     "C._ClosureCache$__2 As <generated method>"
  IL_0029:  stloc.s    V_5
  IL_002b:  ldsfld     "C._ClosureCache$__4 As <generated method>"
  IL_0030:  brfalse.s  IL_0039
  IL_0032:  ldsfld     "C._ClosureCache$__4 As <generated method>"
  IL_0037:  br.s       IL_004b
  IL_0039:  ldnull
  IL_003a:  ldftn      "Sub C._Lambda$__3(Object)"
  IL_0040:  newobj     "Sub VB$AnonymousDelegate_3(Of Object)..ctor(Object, System.IntPtr)"
  IL_0045:  dup
  IL_0046:  stsfld     "C._ClosureCache$__4 As <generated method>"
  IL_004b:  stloc.s    V_6
  IL_004d:  ldsfld     "C._ClosureCache$__6 As <generated method>"
  IL_0052:  brfalse.s  IL_005b
  IL_0054:  ldsfld     "C._ClosureCache$__6 As <generated method>"
  IL_0059:  br.s       IL_006d
  IL_005b:  ldnull
  IL_005c:  ldftn      "Sub C._Lambda$__5(C)"
  IL_0062:  newobj     "Sub VB$AnonymousDelegate_3(Of C)..ctor(Object, System.IntPtr)"
  IL_0067:  dup
  IL_0068:  stsfld     "C._ClosureCache$__6 As <generated method>"
  IL_006d:  stloc.3
  IL_006e:  ret
}
]]>.Value)
                End Using
            End Using
        End Sub

        ''' <summary>
        ''' Update method with anonymous type that was
        ''' not directly referenced in previous generation.
        ''' </summary>
        <Fact()>
        Public Sub AnonymousTypes_SkipGeneration()
            Dim sources0 = <compilation>
                               <file name="a.vb"><![CDATA[
Class A
End Class
Class B
    Shared Function F() As Object
        Dim x As New With {.A = 1}
        Return x.A
    End Function
    Shared Function G() As Object
        Dim x As Integer = 1
        Return x
    End Function
End Class
]]></file>
                           </compilation>
            Dim sources1 = <compilation>
                               <file name="a.vb"><![CDATA[
Class A
End Class
Class B
    Shared Function F() As Object
        Dim x As New With {.A = 1}
        Return x.A
    End Function
    Shared Function G() As Object
        Dim x As Integer = 1
        Return x + 1
    End Function
End Class
]]></file>
                           </compilation>
            Dim sources2 = <compilation>
                               <file name="a.vb"><![CDATA[
Class A
End Class
Class B
    Shared Function F() As Object
        Dim x As New With {.A = 1}
        Return x.A
    End Function
    Shared Function G() As Object
        Dim x As New With {.A = New A()}
        Dim y As New With {.B = 2}
        Return x.A
    End Function
End Class
]]></file>
                           </compilation>
            Dim sources3 = <compilation>
                               <file name="a.vb"><![CDATA[
Class A
End Class
Class B
    Shared Function F() As Object
        Dim x As New With {.A = 1}
        Return x.A
    End Function
    Shared Function G() As Object
        Dim x As New With {.A = New A()}
        Dim y As New With {.B = 3}
        Return y.B
    End Function
End Class
]]></file>
                           </compilation>

            Dim compilation0 = CreateCompilationWithMscorlib(sources0, UnoptimizedDll)
            Dim compilation1 = CreateCompilationWithMscorlib(sources1, UnoptimizedDll)
            Dim compilation2 = CreateCompilationWithMscorlib(sources2, UnoptimizedDll)
            Dim compilation3 = CreateCompilationWithMscorlib(sources3, UnoptimizedDll)

            Dim bytes0 = compilation0.EmitToArray(debug:=True)
            Using md0 = ModuleMetadata.CreateFromImage(bytes0)
                Dim generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), Function(m) ImmutableArray.Create("x"))
                Dim method0 = compilation0.GetMember(Of MethodSymbol)("B.G")
                Dim reader0 = md0.MetadataReader
                CheckNames(reader0, reader0.GetTypeDefNames(), "<Module>", "VB$AnonymousType_0`1", "A", "B")
                Dim method1 = compilation1.GetMember(Of MethodSymbol)("B.G")
                Dim diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(New SemanticEdit(SemanticEditKind.Update, method0, method1, GetLocalMap(method1, method0), preserveLocalVariables:=True)))
                Using md1 = diff1.GetMetadata()
                    Dim reader1 = md1.Reader
                    CheckNames({reader0, reader1}, reader1.GetTypeDefNames()) ' no additional types
                    diff1.VerifyIL("B.G", <![CDATA[
{
  // Code size       16 (0x10)
  .maxstack  2
  .locals init (Object V_0,
  Integer V_1,
  Object V_2, //G
  Integer V_3) //x
  IL_0000:  nop
  IL_0001:  ldc.i4.1
  IL_0002:  stloc.3
  IL_0003:  ldloc.3
  IL_0004:  ldc.i4.1
  IL_0005:  add.ovf
  IL_0006:  box        "Integer"
  IL_000b:  stloc.2
  IL_000c:  br.s       IL_000e
  IL_000e:  ldloc.2
  IL_000f:  ret
}
]]>.Value)

                    Dim method2 = compilation2.GetMember(Of MethodSymbol)("B.G")
                    Dim diff2 = compilation2.EmitDifference(
                        diff1.NextGeneration,
                        ImmutableArray.Create(New SemanticEdit(SemanticEditKind.Update, method1, method2, GetLocalMap(method2, method1), preserveLocalVariables:=True)))
                    Using md2 = diff2.GetMetadata()
                        Dim reader2 = md2.Reader
                        CheckNames({reader0, reader1, reader2}, reader2.GetTypeDefNames(), "VB$AnonymousType_1`1") ' one additional type
                        diff2.VerifyIL("B.G", <![CDATA[
{
  // Code size       35 (0x23)
  .maxstack  1
  .locals init (Object V_0,
  Integer V_1,
  Object V_2,
  Integer V_3,
  Object V_4, //G
  VB$AnonymousType_0(Of A) V_5, //x
  VB$AnonymousType_1(Of Integer) V_6) //y
  IL_0000:  nop
  IL_0001:  newobj     "Sub A..ctor()"
  IL_0006:  newobj     "Sub VB$AnonymousType_0(Of A)..ctor(A)"
  IL_000b:  stloc.s    V_5
  IL_000d:  ldc.i4.2
  IL_000e:  newobj     "Sub VB$AnonymousType_1(Of Integer)..ctor(Integer)"
  IL_0013:  stloc.s    V_6
  IL_0015:  ldloc.s    V_5
  IL_0017:  callvirt   "Function VB$AnonymousType_0(Of A).get_A() As A"
  IL_001c:  stloc.s    V_4
  IL_001e:  br.s       IL_0020
  IL_0020:  ldloc.s    V_4
  IL_0022:  ret
}
]]>.Value)

                        Dim method3 = compilation3.GetMember(Of MethodSymbol)("B.G")
                        Dim diff3 = compilation3.EmitDifference(
                        diff2.NextGeneration,
                        ImmutableArray.Create(New SemanticEdit(SemanticEditKind.Update, method2, method3, GetLocalMap(method3, method2), preserveLocalVariables:=True)))
                        Using md3 = diff3.GetMetadata()
                            Dim reader3 = md3.Reader
                            CheckNames({reader0, reader1, reader2, reader3}, reader3.GetTypeDefNames()) ' no additional types
                            diff3.VerifyIL("B.G", <![CDATA[
{
  // Code size       40 (0x28)
  .maxstack  1
  .locals init (Object V_0,
  Integer V_1,
  Object V_2,
  Integer V_3,
  Object V_4, //G
  VB$AnonymousType_0(Of A) V_5, //x
  VB$AnonymousType_1(Of Integer) V_6) //y
  IL_0000:  nop
  IL_0001:  newobj     "Sub A..ctor()"
  IL_0006:  newobj     "Sub VB$AnonymousType_0(Of A)..ctor(A)"
  IL_000b:  stloc.s    V_5
  IL_000d:  ldc.i4.3
  IL_000e:  newobj     "Sub VB$AnonymousType_1(Of Integer)..ctor(Integer)"
  IL_0013:  stloc.s    V_6
  IL_0015:  ldloc.s    V_6
  IL_0017:  callvirt   "Function VB$AnonymousType_1(Of Integer).get_B() As Integer"
  IL_001c:  box        "Integer"
  IL_0021:  stloc.s    V_4
  IL_0023:  br.s       IL_0025
  IL_0025:  ldloc.s    V_4
  IL_0027:  ret
}
]]>.Value)
                        End Using
                    End Using
                End Using
            End Using
        End Sub

        ''' <summary>
        ''' Update another method (without directly referencing
        ''' anonymous type) after updating method with anonymous type.
        ''' </summary>
        <Fact()>
        Public Sub AnonymousTypes_SkipGeneration_2()
            Dim sources0 = <compilation>
                               <file name="a.vb"><![CDATA[
Class C
    Shared Function F() As Object
        Dim x As New With {.A = 1}
        Return x.A
    End Function
    Shared Function G() As Object
        Dim x As Integer = 1
        Return x
    End Function
End Class
]]></file>
                           </compilation>
            Dim sources1 = <compilation>
                               <file name="a.vb"><![CDATA[
Class C
    Shared Function F() As Object
        Dim x As New With {.A = 2, .B = 3}
        Return x.A
    End Function
    Shared Function G() As Object
        Dim x As Integer = 1
        Return x
    End Function
End Class
]]></file>
                           </compilation>
            Dim sources2 = <compilation>
                               <file name="a.vb"><![CDATA[
Class C
    Shared Function F() As Object
        Dim x As New With {.A = 2, .B = 3}
        Return x.A
    End Function
    Shared Function G() As Object
        Dim x As Integer = 1
        Return x + 1
    End Function
End Class
]]></file>
                           </compilation>
            Dim sources3 = <compilation>
                               <file name="a.vb"><![CDATA[
Class C
    Shared Function F() As Object
        Dim x As New With {.A = 2, .B = 3}
        Return x.A
    End Function
    Shared Function G() As Object
        Dim x As New With {.A = DirectCast(Nothing, Object)}
        Dim y As New With {.A = "a"c, .B = "b"c}
        Return x
    End Function
End Class
]]></file>
                           </compilation>

            Dim compilation0 = CreateCompilationWithMscorlib(sources0, UnoptimizedDll)
            Dim compilation1 = CreateCompilationWithMscorlib(sources1, UnoptimizedDll)
            Dim compilation2 = CreateCompilationWithMscorlib(sources2, UnoptimizedDll)
            Dim compilation3 = CreateCompilationWithMscorlib(sources3, UnoptimizedDll)

            Dim bytes0 = compilation0.EmitToArray(debug:=True)
            Using md0 = ModuleMetadata.CreateFromImage(bytes0)
                Dim generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), Function(m) ImmutableArray.Create("x"))
                Dim method0F = compilation0.GetMember(Of MethodSymbol)("C.F")
                Dim reader0 = md0.MetadataReader
                CheckNames(reader0, reader0.GetTypeDefNames(), "<Module>", "VB$AnonymousType_0`1", "C")
                Dim method1F = compilation1.GetMember(Of MethodSymbol)("C.F")
                Dim method1G = compilation1.GetMember(Of MethodSymbol)("C.G")
                Dim diff1 = compilation1.EmitDifference(
                    generation0,
                    ImmutableArray.Create(New SemanticEdit(SemanticEditKind.Update, method0F, method1F, GetLocalMap(method1F, method0F), preserveLocalVariables:=True)))
                Using md1 = diff1.GetMetadata()
                    Dim reader1 = md1.Reader
                    CheckNames({reader0, reader1}, reader1.GetTypeDefNames(), "VB$AnonymousType_1`2") ' one additional type

                    Dim method2G = compilation2.GetMember(Of MethodSymbol)("C.G")
                    Dim diff2 = compilation2.EmitDifference(
                        diff1.NextGeneration,
                        ImmutableArray.Create(New SemanticEdit(SemanticEditKind.Update, method1G, method2G, GetLocalMap(method2G, method1G), preserveLocalVariables:=True)))
                    Using md2 = diff2.GetMetadata()
                        Dim reader2 = md2.Reader
                        CheckNames({reader0, reader1, reader2}, reader2.GetTypeDefNames()) ' no additional types

                        Dim method3G = compilation3.GetMember(Of MethodSymbol)("C.G")
                        Dim diff3 = compilation3.EmitDifference(
                        diff2.NextGeneration,
                        ImmutableArray.Create(New SemanticEdit(SemanticEditKind.Update, method2G, method3G, GetLocalMap(method3G, method2G), preserveLocalVariables:=True)))
                        Using md3 = diff3.GetMetadata()
                            Dim reader3 = md3.Reader
                            CheckNames({reader0, reader1, reader2, reader3}, reader3.GetTypeDefNames()) ' no additional types
                        End Using
                    End Using
                End Using
            End Using
        End Sub

        ''' <summary>
        ''' Should not re-use locals with custom modifiers.
        ''' </summary>
        <Fact()>
        Public Sub LocalType_CustomModifiers()
            ' Equivalent method signature to VB, but
            ' with optional modifiers on locals.
            Dim ilSource = <![CDATA[
.assembly extern mscorlib { .ver 4:0:0:0 .publickeytoken = (B7 7A 5C 56 19 34 E0 89) }
.assembly '<<GeneratedFileName>>' { }
.class public C
{
  .method public specialname rtspecialname instance void .ctor()
  {
    ret
  }
  .method public static object F(class [mscorlib]System.IDisposable d)
  {
    .locals init ([0] object F,
             [1] class C modopt(int32) c,
             [2] class [mscorlib]System.IDisposable modopt(object) VB$Using,
             [3] bool V_3)
    ldnull
    ret
  }
}
]]>.Value
            Dim source =
                <compilation>
                    <file name="c.vb"><![CDATA[
Class C
    Shared Function F(d As System.IDisposable) As Object
        Dim c As C
        Using d
            c = DirectCast(d, C)
        End Using
        Return c
    End Function
End Class
]]>
                    </file>
                </compilation>
            Dim metadata0 = DirectCast(CompileIL(ilSource, appendDefaultHeader:=False), MetadataImageReference)
            ' Still need a compilation with source for the initial
            ' generation - to get a MethodSymbol and syntax map.
            Dim compilation0 = CreateCompilationWithMscorlib(source, options:=UnoptimizedDll.WithDebugInformationKind(DebugInformationKind.Full))
            Dim compilation1 = CreateCompilationWithMscorlib(source, options:=UnoptimizedDll.WithDebugInformationKind(DebugInformationKind.Full))

            Dim moduleMetadata0 = DirectCast(metadata0.GetMetadata(), AssemblyMetadata).Modules(0)
            Dim method0 = compilation0.GetMember(Of MethodSymbol)("C.F")
            Dim generation0 = EmitBaseline.CreateInitialBaseline(
                moduleMetadata0,
                Function(m) ImmutableArray.Create("F", "c", "VB$Using", Nothing))
            Dim testData1 = New CompilationTestData()
            Dim method1 = compilation1.GetMember(Of MethodSymbol)("C.F")
            Dim edit = New SemanticEdit(SemanticEditKind.Update, method0, method1, GetLocalMap(method1, method0), preserveLocalVariables:=True)
            Dim diff1 = compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(edit))
            diff1.VerifyIL("C.F", <![CDATA[
{
  // Code size       45 (0x2d)
  .maxstack  2
  .locals init (Object V_0,
  C V_1,
  System.IDisposable V_2,
  Boolean V_3,
  Object V_4, //F
  C V_5, //c
  System.IDisposable V_6, //VB$Using
  Boolean V_7)
  IL_0000:  nop
  IL_0001:  nop
  IL_0002:  ldarg.0
  IL_0003:  stloc.s    V_6
  .try
{
  IL_0005:  ldarg.0
  IL_0006:  castclass  "C"
  IL_000b:  stloc.s    V_5
  IL_000d:  leave.s    IL_0024
}
  finally
{
  IL_000f:  nop
  IL_0010:  ldloc.s    V_6
  IL_0012:  ldnull
  IL_0013:  ceq
  IL_0015:  stloc.s    V_7
  IL_0017:  ldloc.s    V_7
  IL_0019:  brtrue.s   IL_0023
  IL_001b:  ldloc.s    V_6
  IL_001d:  callvirt   "Sub System.IDisposable.Dispose()"
  IL_0022:  nop
  IL_0023:  endfinally
}
  IL_0024:  ldloc.s    V_5
  IL_0026:  stloc.s    V_4
  IL_0028:  br.s       IL_002a
  IL_002a:  ldloc.s    V_4
  IL_002c:  ret
}
]]>.Value)
        End Sub

        <WorkItem(839414)>
        <Fact>
        Public Sub Bug839414()
            Dim source0 =
<compilation>
    <file name="a.vb">
Module M
    Function F() As Object
        Static x = 1
        Return x
    End Function
End Module
</file>
</compilation>
            Dim source1 =
<compilation>
    <file name="a.vb">
Module M
    Function F() As Object
        Static x = "2"
        Return x
    End Function
End Module
</file>
</compilation>
            Dim compilation0 = CreateCompilationWithMscorlibAndVBRuntime(source0, UnoptimizedDll)
            Dim compilation1 = CreateCompilationWithMscorlibAndVBRuntime(source1, UnoptimizedDll)
            Dim bytes0 = compilation0.EmitToArray(debug:=True)
            Dim method0 = compilation0.GetMember(Of MethodSymbol)("M.F")
            Dim method1 = compilation1.GetMember(Of MethodSymbol)("M.F")
            Dim generation0 = EmitBaseline.CreateInitialBaseline(ModuleMetadata.CreateFromImage(bytes0), Function(m) ImmutableArray(Of String).Empty)
            compilation1.EmitDifference(
                generation0,
                ImmutableArray.Create(New SemanticEdit(SemanticEditKind.Update, method0, method1)))
        End Sub

#End Region

#Region "Helpers"
        Private Shared ReadOnly EmptyLocalsProvider As LocalVariableNameProvider = Function(token) ImmutableArray(Of String).Empty

        Private Shared Function GetAllLocals(compilation As VisualBasicCompilation, method As MethodSymbol) As ImmutableArray(Of LocalSymbol)
            Dim methodSyntax = method.DeclaringSyntaxReferences(0).GetSyntax().Parent
            Dim model = compilation.GetSemanticModel(methodSyntax.SyntaxTree)
            Dim locals = ArrayBuilder(Of LocalSymbol).GetInstance()

            For Each node In methodSyntax.DescendantNodes()
                If node.VisualBasicKind = SyntaxKind.VariableDeclarator Then
                    For Each name In DirectCast(node, VariableDeclaratorSyntax).Names
                        Dim local = DirectCast(model.GetDeclaredSymbol(name), LocalSymbol)
                        locals.Add(local)
                    Next
                End If
            Next

            Return locals.ToImmutableAndFree()
        End Function

        Private Shared Function GetAllLocals(compilation As VisualBasicCompilation, method As IMethodSymbol) As ImmutableArray(Of KeyValuePair(Of ILocalSymbol, Integer))
            Dim locals = GetAllLocals(compilation, DirectCast(method, MethodSymbol))
            Return locals.SelectAsArray(Function(local, index, arg) New KeyValuePair(Of ILocalSymbol, Integer)(local, index), DirectCast(Nothing, Object))
        End Function

        Private Shared Function GetAllLocals(method As MethodSymbol) As ImmutableArray(Of VisualBasicSyntaxNode)
            Dim names = From name In VisualBasicCompilation.GetLocalVariableDeclaratorsVisitor.GetDeclarators(method).OfType(Of ModifiedIdentifierSyntax)
                        Select DirectCast(name, VisualBasicSyntaxNode)

            Return names.AsImmutableOrEmpty
        End Function

        Private Shared Function GetLocalNames(method As MethodSymbol) As ImmutableArray(Of String)
            Dim locals = GetAllLocals(method)
            Return locals.SelectAsArray(AddressOf GetLocalName)
        End Function

        Private Shared Function GetLocalName(node As SyntaxNode) As String
            If node.VisualBasicKind = SyntaxKind.ModifiedIdentifier Then
                Return DirectCast(node, ModifiedIdentifierSyntax).Identifier.ToString()
            End If

            Throw New NotImplementedException()
        End Function

        Private Shared Function GetLocalNames(methodData As CompilationTestData.MethodData) As ImmutableArray(Of String)
            Dim locals = methodData.ILBuilder.LocalSlotManager.LocalsInOrder()
            Return locals.SelectAsArray(Function(l) l.Name)
        End Function

        Private Shared Function GetLocalMap(method1 As MethodSymbol, method0 As MethodSymbol) As Func(Of SyntaxNode, SyntaxNode)
            Dim tree1 = method1.Locations(0).SourceTree
            Dim tree0 = method0.Locations(0).SourceTree
            Assert.NotEqual(tree1, tree0)

            Dim locals0 = GetAllLocals(method0)
            Return Function(s As SyntaxNode)
                       Dim s1 = s
                       Assert.Equal(s1.SyntaxTree, tree1)
                       For Each s0 In locals0
                           If Not SyntaxFactory.AreEquivalent(s0, s1) Then
                               Continue For
                           End If
                           ' Make sure the containing statements are the same.
                           Dim p0 = GetNearestStatement(s0)
                           Dim p1 = GetNearestStatement(s1)
                           If SyntaxFactory.AreEquivalent(p0, p1) Then
                               Return s0
                           End If
                       Next
                       Return Nothing
                   End Function
        End Function

        Private Shared Function GetNearestStatement(node As SyntaxNode) As StatementSyntax
            While node IsNot Nothing
                Dim statement = TryCast(node, StatementSyntax)
                If statement IsNot Nothing Then
                    Return statement
                End If

                node = node.Parent
            End While
            Return Nothing
        End Function

        Private Shared Sub CheckNames(reader As MetadataReader, [handles] As StringHandle(), ParamArray expectedNames As String())
            CheckNames({reader}, [handles], expectedNames)
        End Sub

        Private Shared Sub CheckNames(readers As MetadataReader(), [handles] As StringHandle(), ParamArray expectedNames As String())
            Dim actualNames = readers.GetStrings([handles])
            AssertEx.Equal(actualNames, expectedNames)
        End Sub

        Private Shared Sub CheckNamesSorted(readers As MetadataReader(), [handles] As StringHandle(), ParamArray expectedNames As String())
            Dim actualNames = readers.GetStrings([handles])
            Array.Sort(actualNames)
            Array.Sort(expectedNames)
            AssertEx.Equal(actualNames, expectedNames)
        End Sub

#End Region
    End Class

End Namespace
