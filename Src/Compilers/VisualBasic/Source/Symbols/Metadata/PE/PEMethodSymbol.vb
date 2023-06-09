﻿' Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports System.Collections.Generic
Imports System.Collections.Immutable
Imports System.Collections.ObjectModel
Imports System.Globalization
Imports System.Threading
Imports System.Reflection
Imports System.Reflection.Metadata
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax

Namespace Microsoft.CodeAnalysis.VisualBasic.Symbols.Metadata.PE

    ''' <summary>
    ''' The class to represent all methods imported from a PE/module.
    ''' </summary>
    Friend NotInheritable Class PEMethodSymbol
        Inherits MethodSymbol

        Private Const UninitializedMethodKind As Integer = -1

        Private ReadOnly m_Handle As MethodHandle
        Private ReadOnly m_Name As String
        Private ReadOnly m_ImplFlags As MethodImplAttributes
        Private ReadOnly m_Flags As MethodAttributes
        Private ReadOnly m_ContainingType As PENamedTypeSymbol

        Private m_associatedPropertyOrEventOpt As Symbol

        Private m_lazyMethodKind As Integer = UninitializedMethodKind ' really a MethodKind, but Interlocked.CompareExchange doesn't handle those

        Private m_lazyTypeParameters As ImmutableArray(Of TypeParameterSymbol)

        Private m_lazyDocComment As Tuple(Of CultureInfo, String)

        Private m_lazyExplicitMethodImplementations As ImmutableArray(Of MethodSymbol)

        Private m_lazyCustomAttributes As ImmutableArray(Of VisualBasicAttributeData)
        Private m_lazyConditionalAttributeSymbols As ImmutableArray(Of String)

        Private m_lazyUseSiteErrorInfo As DiagnosticInfo = ErrorFactory.EmptyErrorInfo ' Indicates unknown state. 

        Private m_lazyIsExtensionMethod As Byte = ThreeState.Unknown
        Private m_lazyObsoleteAttributeData As ObsoleteAttributeData = ObsoleteAttributeData.Uninitialized


#Region "Signature data"
        Private m_lazySignature As SignatureData

        Private Class SignatureData
            Public ReadOnly CallingConvention As Byte
            Public ReadOnly Parameters As ImmutableArray(Of ParameterSymbol)
            Public ReadOnly ReturnParam As PEParameterSymbol

            Public Sub New(callingConvention As Byte, parameters As ImmutableArray(Of ParameterSymbol), returnParam As PEParameterSymbol)
                Me.CallingConvention = callingConvention
                Me.Parameters = parameters
                Me.ReturnParam = returnParam
            End Sub
        End Class
#End Region

        Friend Sub New(
            moduleSymbol As PEModuleSymbol,
            containingType As PENamedTypeSymbol,
            handle As MethodHandle
        )
            Debug.Assert(moduleSymbol IsNot Nothing)
            Debug.Assert(containingType IsNot Nothing)
            Debug.Assert(Not handle.IsNil)

            m_Handle = handle
            m_ContainingType = containingType

            Try
                Dim rva As Integer
                moduleSymbol.Module.GetMethodDefPropsOrThrow(handle, m_Name, m_ImplFlags, m_Flags, rva)
            Catch mrEx As BadImageFormatException
                If m_Name Is Nothing Then
                    m_Name = String.Empty
                End If

                m_lazyUseSiteErrorInfo = ErrorFactory.ErrorInfo(ERRID.ERR_UnsupportedMethod1, CustomSymbolDisplayFormatter.ShortErrorName(Me))
            End Try
        End Sub

        Public Overrides ReadOnly Property ContainingSymbol As Symbol
            Get
                Return m_ContainingType
            End Get
        End Property

        Public Overrides ReadOnly Property ContainingType As NamedTypeSymbol
            Get
                Return m_ContainingType
            End Get
        End Property

        Public Overrides ReadOnly Property Name As String
            Get
                Return m_Name
            End Get
        End Property

        Friend Overrides ReadOnly Property HasSpecialName As Boolean
            Get
                Return (m_Flags And MethodAttributes.SpecialName) <> 0
            End Get
        End Property

        Friend Overrides ReadOnly Property HasRuntimeSpecialName As Boolean
            Get
                Return (m_Flags And MethodAttributes.RTSpecialName) <> 0
            End Get
        End Property

        Friend Overrides ReadOnly Property HasFinalFlag As Boolean
            Get
                Return (m_Flags And MethodAttributes.Final) <> 0
            End Get
        End Property

        Friend ReadOnly Property MethodImplFlags As MethodImplAttributes
            Get
                Return m_ImplFlags
            End Get
        End Property

        Friend ReadOnly Property MethodFlags As MethodAttributes
            Get
                Return m_Flags
            End Get
        End Property

        Public Overrides ReadOnly Property MethodKind As MethodKind
            Get
                If m_lazyMethodKind = UninitializedMethodKind Then
                    Dim computed As MethodKind = ComputeMethodKind()
                    Dim oldValue As Integer = Interlocked.CompareExchange(m_lazyMethodKind, CType(computed, Integer), UninitializedMethodKind)
                    Debug.Assert(oldValue = UninitializedMethodKind OrElse oldValue = computed)
                End If

                Return CType(m_lazyMethodKind, MethodKind)
            End Get

        End Property

        Friend Overrides ReadOnly Property IsMethodKindBasedOnSyntax As Boolean
            Get
                Return False
            End Get
        End Property

        Private Function ComputeMethodKind() As MethodKind
            Dim name As String = Me.Name

            If HasSpecialName Then
                If name.StartsWith("."c, StringComparison.Ordinal) Then

                    ' 10.5.1 Instance constructor
                    ' An instance constructor shall be an instance (not static or virtual) method,
                    ' it shall be named .ctor, and marked instance, rtspecialname, and specialname (§15.4.2.6).
                    ' An instance constructor can have parameters, but shall not return a value.
                    ' An instance constructor cannot take generic type parameters.

                    ' 10.5.3 Type initializer
                    ' This method shall be static, take no parameters, return no value,
                    ' be marked with rtspecialname and specialname (§15.4.2.6), and be named .cctor.

                    If (m_Flags And (MethodAttributes.RTSpecialName Or MethodAttributes.Virtual)) = MethodAttributes.RTSpecialName AndAlso
                       String.Equals(name, If(IsShared, WellKnownMemberNames.StaticConstructorName, WellKnownMemberNames.InstanceConstructorName), StringComparison.Ordinal) AndAlso
                       IsSub AndAlso Arity = 0 Then

                        If IsShared Then
                            If Parameters.Length = 0 Then
                                Return MethodKind.SharedConstructor
                            End If
                        Else
                            Return MethodKind.Constructor
                        End If
                    End If

                    Return MethodKind.Ordinary

                ElseIf IsShared AndAlso DeclaredAccessibility = Accessibility.Public AndAlso Not IsSub AndAlso Arity = 0 Then
                    Dim opInfo As OverloadResolution.OperatorInfo = OverloadResolution.GetOperatorInfo(name)

                    If opInfo.ParamCount <> 0 Then
                        ' Combination of all conditions that should be met to get here must match implementation of 
                        ' IsPotentialOperatorOrConversion (with exception of ParameterCount matching).

                        If OverloadResolution.ValidateOverloadedOperator(Me, opInfo) Then
                            Return ComputeMethodKindForPotentialOperatorOrConversion(opInfo)
                        End If
                    End If

                    Return MethodKind.Ordinary
                End If
            End If

            If Not IsShared AndAlso String.Equals(name, WellKnownMemberNames.DelegateInvokeName, StringComparison.Ordinal) AndAlso m_ContainingType.TypeKind = TypeKind.Delegate Then
                Return MethodKind.DelegateInvoke
            End If

            Return MethodKind.Ordinary
        End Function

        Friend Overrides Function IsParameterlessConstructor() As Boolean
            If m_lazyMethodKind <> UninitializedMethodKind Then
                Return m_lazyMethodKind = MethodKind.Constructor AndAlso ParameterCount = 0
            End If

            ' 10.5.1 Instance constructor
            ' An instance constructor shall be an instance (not static or virtual) method,
            ' it shall be named .ctor, and marked instance, rtspecialname, and specialname (§15.4.2.6).
            ' An instance constructor can have parameters, but shall not return a value.
            ' An instance constructor cannot take generic type parameters.

            If (m_Flags And (MethodAttributes.SpecialName Or MethodAttributes.RTSpecialName Or MethodAttributes.Static Or MethodAttributes.Virtual)) =
                    (MethodAttributes.SpecialName Or MethodAttributes.RTSpecialName) AndAlso
               String.Equals(Me.Name, WellKnownMemberNames.InstanceConstructorName, StringComparison.Ordinal) AndAlso
               ParameterCount = 0 AndAlso
               IsSub AndAlso Arity = 0 Then

                Dim oldValue As Integer = Interlocked.CompareExchange(m_lazyMethodKind, MethodKind.Constructor, UninitializedMethodKind)
                Debug.Assert(oldValue = UninitializedMethodKind OrElse oldValue = MethodKind.Constructor)

                Return True
            End If

            Return False
        End Function

        Private Function ComputeMethodKindForPotentialOperatorOrConversion(opInfo As OverloadResolution.OperatorInfo) As MethodKind
            ' Don't mark methods involved in unsupported overloading as operators.

            If opInfo.IsUnary Then
                Select Case opInfo.UnaryOperatorKind
                    Case UnaryOperatorKind.Implicit
                        Return ComputeMethodKindForPotentialOperatorOrConversion(opInfo, MethodKind.Conversion, WellKnownMemberNames.ExplicitConversionName, True)
                    Case UnaryOperatorKind.Explicit
                        Return ComputeMethodKindForPotentialOperatorOrConversion(opInfo, MethodKind.Conversion, WellKnownMemberNames.ImplicitConversionName, True)
                    Case UnaryOperatorKind.IsFalse, UnaryOperatorKind.IsTrue, UnaryOperatorKind.Minus, UnaryOperatorKind.Plus
                        Return ComputeMethodKindForPotentialOperatorOrConversion(opInfo, MethodKind.UserDefinedOperator, Nothing, False)
                    Case UnaryOperatorKind.Not
                        If IdentifierComparison.Equals(Me.Name, WellKnownMemberNames.OnesComplementOperatorName) Then
                            Return ComputeMethodKindForPotentialOperatorOrConversion(opInfo, MethodKind.UserDefinedOperator, Nothing, False)
                        Else
                            Return ComputeMethodKindForPotentialOperatorOrConversion(opInfo, MethodKind.UserDefinedOperator, WellKnownMemberNames.OnesComplementOperatorName, False)
                        End If
                    Case Else
                        Throw ExceptionUtilities.UnexpectedValue(opInfo.UnaryOperatorKind)
                End Select
            Else
                Debug.Assert(opInfo.IsBinary)
                Select Case opInfo.BinaryOperatorKind
                    Case BinaryOperatorKind.Add,
                         BinaryOperatorKind.Subtract,
                         BinaryOperatorKind.Multiply,
                         BinaryOperatorKind.Divide,
                         BinaryOperatorKind.IntegerDivide,
                         BinaryOperatorKind.Modulo,
                         BinaryOperatorKind.Power,
                         BinaryOperatorKind.Equals,
                         BinaryOperatorKind.NotEquals,
                         BinaryOperatorKind.LessThan,
                         BinaryOperatorKind.GreaterThan,
                         BinaryOperatorKind.LessThanOrEqual,
                         BinaryOperatorKind.GreaterThanOrEqual,
                         BinaryOperatorKind.Like,
                         BinaryOperatorKind.Concatenate,
                         BinaryOperatorKind.Xor
                        Return ComputeMethodKindForPotentialOperatorOrConversion(opInfo, MethodKind.UserDefinedOperator, Nothing, False)

                    Case BinaryOperatorKind.And
                        If IdentifierComparison.Equals(Me.Name, WellKnownMemberNames.BitwiseAndOperatorName) Then
                            Return ComputeMethodKindForPotentialOperatorOrConversion(opInfo, MethodKind.UserDefinedOperator, Nothing, False)
                        Else
                            Return ComputeMethodKindForPotentialOperatorOrConversion(opInfo, MethodKind.UserDefinedOperator, WellKnownMemberNames.BitwiseAndOperatorName, False)
                        End If
                    Case BinaryOperatorKind.Or
                        If IdentifierComparison.Equals(Me.Name, WellKnownMemberNames.BitwiseOrOperatorName) Then
                            Return ComputeMethodKindForPotentialOperatorOrConversion(opInfo, MethodKind.UserDefinedOperator, Nothing, False)
                        Else
                            Return ComputeMethodKindForPotentialOperatorOrConversion(opInfo, MethodKind.UserDefinedOperator, WellKnownMemberNames.BitwiseOrOperatorName, False)
                        End If
                    Case BinaryOperatorKind.LeftShift
                        If IdentifierComparison.Equals(Me.Name, WellKnownMemberNames.LeftShiftOperatorName) Then
                            Return ComputeMethodKindForPotentialOperatorOrConversion(opInfo, MethodKind.UserDefinedOperator, Nothing, False)
                        Else
                            Return ComputeMethodKindForPotentialOperatorOrConversion(opInfo, MethodKind.UserDefinedOperator, WellKnownMemberNames.LeftShiftOperatorName, False)
                        End If
                    Case BinaryOperatorKind.RightShift
                        If IdentifierComparison.Equals(Me.Name, WellKnownMemberNames.RightShiftOperatorName) Then
                            Return ComputeMethodKindForPotentialOperatorOrConversion(opInfo, MethodKind.UserDefinedOperator, Nothing, False)
                        Else
                            Return ComputeMethodKindForPotentialOperatorOrConversion(opInfo, MethodKind.UserDefinedOperator, WellKnownMemberNames.RightShiftOperatorName, False)
                        End If
                    Case Else
                        Throw ExceptionUtilities.UnexpectedValue(opInfo.BinaryOperatorKind)
                End Select
            End If
        End Function

        Private Function IsPotentialOperatorOrConversion(opInfo As OverloadResolution.OperatorInfo) As Boolean
            Return HasSpecialName AndAlso
                   IsShared AndAlso DeclaredAccessibility = Accessibility.Public AndAlso
                   Not IsSub AndAlso Arity = 0 AndAlso
                   ParameterCount = opInfo.ParamCount
        End Function

        Private Function ComputeMethodKindForPotentialOperatorOrConversion(
            opInfo As OverloadResolution.OperatorInfo,
            potentialMethodKind As MethodKind,
            additionalNameOpt As String,
            adjustContendersOfAdditionalName As Boolean
        ) As MethodKind
            Debug.Assert(potentialMethodKind = MethodKind.Conversion OrElse potentialMethodKind = MethodKind.UserDefinedOperator)

            Dim result As MethodKind = potentialMethodKind
            Dim inputParams As ImmutableArray(Of ParameterSymbol) = Parameters
            Dim outputType As TypeSymbol = ReturnType

            For i As Integer = 0 To If(additionalNameOpt Is Nothing, 0, 1)
                For Each m In m_ContainingType.GetMembers(If(i = 0, Me.Name, additionalNameOpt))
                    If m Is Me Then
                        Continue For
                    End If

                    If m.Kind <> SymbolKind.Method Then
                        Continue For
                    End If

                    Dim contender = TryCast(m, PEMethodSymbol)

                    If contender Is Nothing OrElse Not contender.IsPotentialOperatorOrConversion(opInfo) Then
                        Continue For
                    End If

                    Select Case contender.m_lazyMethodKind
                        Case UninitializedMethodKind, MethodKind.Ordinary
                            ' Need to check against our method
                        Case potentialMethodKind
                            If i = 0 OrElse adjustContendersOfAdditionalName Then
                                ' Contender was already cleared, so it cannot conflict with this operator.
                                Continue For
                            End If
                        Case Else
                            ' Cannot be an operator of the target kind.
                            Continue For
                    End Select

                    If potentialMethodKind = MethodKind.Conversion AndAlso Not outputType.IsSameTypeIgnoringCustomModifiers(contender.ReturnType) Then
                        Continue For
                    End If

                    Dim j As Integer
                    For j = 0 To inputParams.Length - 1
                        If Not inputParams(j).Type.IsSameTypeIgnoringCustomModifiers(contender.Parameters(j).Type) Then
                            Exit For
                        End If
                    Next

                    If j < inputParams.Length Then
                        Continue For
                    End If

                    ' Unsupported overloading
                    result = MethodKind.Ordinary

                    ' Mark the contender too.
                    If i = 0 OrElse adjustContendersOfAdditionalName Then
                        Dim oldValue As Integer = Interlocked.CompareExchange(contender.m_lazyMethodKind, MethodKind.Ordinary, UninitializedMethodKind)
                        Debug.Assert(oldValue = UninitializedMethodKind OrElse oldValue = MethodKind.Ordinary)
                    End If
                Next
            Next

            Return result
        End Function

        Public Overrides ReadOnly Property AssociatedPropertyOrEvent As Symbol
            Get
                Return m_associatedPropertyOrEventOpt
            End Get
        End Property

        Public Overrides ReadOnly Property DeclaredAccessibility As Accessibility
            Get
                Dim access As Accessibility = Accessibility.Private

                Select Case m_Flags And MethodAttributes.MemberAccessMask
                    Case MethodAttributes.Assembly
                        access = Accessibility.Friend

                    Case MethodAttributes.FamORAssem
                        access = Accessibility.ProtectedOrFriend

                    Case MethodAttributes.FamANDAssem
                        access = Accessibility.ProtectedAndFriend

                    Case MethodAttributes.Private,
                         MethodAttributes.PrivateScope
                        access = Accessibility.Private

                    Case MethodAttributes.Public
                        access = Accessibility.Public

                    Case MethodAttributes.Family
                        access = Accessibility.Protected

                    Case Else
                        Debug.Assert(False, "Unexpected!!!")
                End Select

                Return access

            End Get
        End Property

        Public Overloads Overrides Function GetAttributes() As ImmutableArray(Of VisualBasicAttributeData)
            If m_lazyCustomAttributes.IsDefault Then
                Dim containingPEModuleSymbol = DirectCast(ContainingModule(), PEModuleSymbol)
                containingPEModuleSymbol.LoadCustomAttributes(Me.Handle, m_lazyCustomAttributes)
            End If
            Return m_lazyCustomAttributes
        End Function

        Friend Overrides Function GetCustomAttributesToEmit() As IEnumerable(Of VisualBasicAttributeData)
            Return GetAttributes()
        End Function

        Public Overrides ReadOnly Property IsExtensionMethod As Boolean
            Get
                If m_lazyIsExtensionMethod = ThreeState.Unknown Then

                    Dim result As Boolean = False

                    If Me.IsShared AndAlso
                       Me.ParameterCount > 0 AndAlso
                       Me.MethodKind = MethodKind.Ordinary AndAlso
                       m_ContainingType.MightContainExtensionMethods AndAlso
                       m_ContainingType.ContainingPEModule.Module.HasExtensionAttribute(Me.Handle, ignoreCase:=True) AndAlso
                       ValidateGenericConstraintsOnExtensionMethodDefinition() Then

                        Dim firstParam As ParameterSymbol = Me.Parameters(0)

                        result = Not (firstParam.IsOptional OrElse firstParam.IsParamArray)
                    End If

                    If result Then
                        m_lazyIsExtensionMethod = ThreeState.True
                    Else
                        m_lazyIsExtensionMethod = ThreeState.False
                    End If
                End If

                Return m_lazyIsExtensionMethod = ThreeState.True
            End Get
        End Property

        Public Overrides ReadOnly Property IsExternalMethod As Boolean
            Get
                Return (m_Flags And MethodAttributes.PinvokeImpl) <> 0 OrElse
                       (m_ImplFlags And (MethodImplAttributes.InternalCall Or MethodImplAttributes.Runtime)) <> 0
            End Get
        End Property

        Public Overrides Function GetDllImportData() As DllImportData
            If (m_Flags And MethodAttributes.PinvokeImpl) = 0 Then
                Return Nothing
            End If

            ' do not cache the result, the compiler doesn't use this (it's only exposed thru public API):
            Return m_ContainingType.ContainingPEModule.Module.GetDllImportData(Me.m_Handle)
        End Function

        Friend Overrides Function IsMetadataNewSlot(Optional ignoreInterfaceImplementationChanges As Boolean = False) As Boolean
            Return (m_Flags And MethodAttributes.NewSlot) <> 0
        End Function

        Friend Overrides ReadOnly Property IsExternal As Boolean
            Get
                Return IsExternalMethod OrElse
                    (m_ImplFlags And MethodImplAttributes.Runtime) <> 0
            End Get
        End Property

        Friend Overrides ReadOnly Property IsAccessCheckedOnOverride As Boolean
            Get
                Return (m_Flags And MethodAttributes.CheckAccessOnOverride) <> 0
            End Get
        End Property

        Friend Overrides ReadOnly Property ReturnValueIsMarshalledExplicitly As Boolean
            Get
                Return m_lazySignature.ReturnParam.IsMarshalledExplicitly
            End Get
        End Property

        Friend Overrides ReadOnly Property ReturnTypeMarshallingInformation As MarshalPseudoCustomAttributeData
            Get
                Return m_lazySignature.ReturnParam.MarshallingInformation
            End Get
        End Property

        Friend Overrides ReadOnly Property ReturnValueMarshallingDescriptor As ImmutableArray(Of Byte)
            Get
                Return m_lazySignature.ReturnParam.MarshallingDescriptor
            End Get
        End Property

        Friend Overrides ReadOnly Property ImplementationAttributes As Reflection.MethodImplAttributes
            Get
                Return CType(m_ImplFlags, Reflection.MethodImplAttributes)
            End Get
        End Property

        Friend Overrides ReadOnly Property HasDeclarativeSecurity As Boolean
            Get
                Throw ExceptionUtilities.Unreachable
            End Get
        End Property

        Friend Overrides Function GetSecurityInformation() As IEnumerable(Of Microsoft.Cci.SecurityAttribute)
            Throw ExceptionUtilities.Unreachable
        End Function

        Public Overrides ReadOnly Property IsVararg As Boolean
            Get
                EnsureSignatureIsLoaded()
                Return SignatureHeader.IsVarArgCallSignature(m_lazySignature.CallingConvention)
            End Get
        End Property

        Public Overrides ReadOnly Property IsGenericMethod As Boolean
            Get
                Return Me.Arity > 0
            End Get
        End Property

        Public Overrides ReadOnly Property Arity As Integer
            Get
                If Me.m_lazyTypeParameters.IsDefault Then
                    Try
                        Dim paramCount As Integer = 0
                        Dim typeParamCount As Integer = 0
                        Dim decoder As New MetadataDecoder(Me.m_ContainingType.ContainingPEModule, Me)
                        decoder.GetSignatureCountsOrThrow(Me.m_Handle, paramCount, typeParamCount)
                        Return typeParamCount
                    Catch mrEx As BadImageFormatException
                        Return TypeParameters.Length
                    End Try
                Else
                    Return Me.m_lazyTypeParameters.Length
                End If
            End Get
        End Property

        Friend ReadOnly Property Handle As MethodHandle
            Get
                Return m_Handle
            End Get
        End Property

        Public Overrides ReadOnly Property IsMustOverride As Boolean
            Get
                Return (m_Flags And MethodAttributes.Virtual) <> 0 AndAlso
                    (m_Flags And MethodAttributes.Abstract) <> 0
            End Get
        End Property

        Public Overrides ReadOnly Property IsNotOverridable As Boolean
            Get
                Return (m_Flags And
                            (MethodAttributes.Virtual Or
                             MethodAttributes.Final Or
                             MethodAttributes.Abstract Or
                             MethodAttributes.NewSlot)) =
                        (MethodAttributes.Virtual Or MethodAttributes.Final)
            End Get
        End Property

        Public Overrides ReadOnly Property IsOverloads As Boolean
            Get
                Return (m_Flags And MethodAttributes.HideBySig) <> 0 OrElse
                    IsOverrides ' If overrides is present, then Overloads is implicit

                ' The check for IsOverrides is needed because of bug Dev10 #850631,
                ' VB compiler doesn't emit HideBySig flag for overriding methods that 
                ' aren't marked explicitly with Overrides modifier.
            End Get
        End Property

        Friend Overrides ReadOnly Property IsHiddenBySignature As Boolean
            Get
                Return (m_Flags And MethodAttributes.HideBySig) <> 0
            End Get
        End Property

        Public Overrides ReadOnly Property IsOverridable As Boolean
            Get
                Dim flagsToCheck As MethodAttributes = (m_Flags And
                                                        (MethodAttributes.Virtual Or
                                                         MethodAttributes.Final Or
                                                         MethodAttributes.Abstract Or
                                                         MethodAttributes.NewSlot))

                Return flagsToCheck = (MethodAttributes.Virtual Or MethodAttributes.NewSlot) OrElse
                       (flagsToCheck = MethodAttributes.Virtual AndAlso m_ContainingType.BaseTypeNoUseSiteDiagnostics Is Nothing)
            End Get
        End Property

        Public Overrides ReadOnly Property IsOverrides As Boolean
            Get
                ' ECMA-335 
                ' 10.3.1 Introducing a virtual method
                ' If the definition is not marked newslot, the definition creates a new virtual method only 
                ' if there is not virtual method of the same name and signature inherited from a base class.
                '
                ' This means that a virtual method without NewSlot flag in a type that doesn't have a base
                ' is a new virtual method and doesn't override anything.
                Return (m_Flags And MethodAttributes.Virtual) <> 0 AndAlso
                       (m_Flags And MethodAttributes.NewSlot) = 0 AndAlso
                       m_ContainingType.BaseTypeNoUseSiteDiagnostics IsNot Nothing
            End Get
        End Property

        Public Overrides ReadOnly Property IsShared As Boolean
            Get
                Return (m_Flags And MethodAttributes.Static) <> 0
            End Get
        End Property

        Public Overrides ReadOnly Property IsSub As Boolean
            Get
                Return Me.ReturnType.SpecialType = SpecialType.System_Void
            End Get
        End Property

        Public Overrides ReadOnly Property IsAsync As Boolean
            Get
                Return False
            End Get
        End Property

        Public Overrides ReadOnly Property IsIterator As Boolean
            Get
                Return False
            End Get
        End Property

        Public Overrides ReadOnly Property Locations As ImmutableArray(Of Location)
            Get
                Return StaticCast(Of Location).From(m_ContainingType.ContainingPEModule.MetadataLocation)
            End Get
        End Property

        Public Overrides ReadOnly Property DeclaringSyntaxReferences As ImmutableArray(Of SyntaxReference)
            Get
                Return ImmutableArray(Of SyntaxReference).Empty
            End Get
        End Property

        Friend Overrides ReadOnly Property ParameterCount As Integer
            Get
                If Me.m_lazySignature Is Nothing Then
                    Try
                        Dim paramCount As Integer = 0
                        Dim typeParamCount As Integer = 0
                        Dim decoder As New MetadataDecoder(Me.m_ContainingType.ContainingPEModule, Me)
                        decoder.GetSignatureCountsOrThrow(Me.m_Handle, paramCount, typeParamCount)
                        Return paramCount
                    Catch mrEx As BadImageFormatException
                        Return Parameters.Length
                    End Try
                Else
                    Return Me.m_lazySignature.Parameters.Length
                End If
            End Get
        End Property

        Public Overrides ReadOnly Property Parameters As ImmutableArray(Of ParameterSymbol)
            Get
                EnsureSignatureIsLoaded()
                Return m_lazySignature.Parameters
            End Get
        End Property

        Public Overrides ReadOnly Property ReturnType As TypeSymbol
            Get
                EnsureSignatureIsLoaded()
                Return m_lazySignature.ReturnParam.Type
            End Get
        End Property

        Public Overrides ReadOnly Property ReturnTypeCustomModifiers As ImmutableArray(Of CustomModifier)
            Get
                EnsureSignatureIsLoaded()
                Return m_lazySignature.ReturnParam.CustomModifiers
            End Get
        End Property

        Public Overrides Function GetReturnTypeAttributes() As ImmutableArray(Of VisualBasicAttributeData)
            EnsureSignatureIsLoaded()
            Return m_lazySignature.ReturnParam.GetAttributes()
        End Function

        Friend ReadOnly Property ReturnParam As PEParameterSymbol
            Get
                EnsureSignatureIsLoaded()
                Return m_lazySignature.ReturnParam
            End Get
        End Property

        ''' <summary>
        ''' Associate the method with a particular property. Returns
        ''' false if the method is already associated with a property or event.
        ''' </summary>
        Friend Function SetAssociatedProperty(propertySymbol As PEPropertySymbol, methodKind As MethodKind) As Boolean
            Debug.Assert((methodKind = methodKind.PropertyGet) OrElse (methodKind = methodKind.PropertySet))
            Return Me.SetAssociatedPropertyOrEvent(propertySymbol, methodKind)
        End Function

        ''' <summary>
        ''' Associate the method with a particular event. Returns
        ''' false if the method is already associated with a property or event.
        ''' </summary>
        Friend Function SetAssociatedEvent(eventSymbol As PEEventSymbol, methodKind As MethodKind) As Boolean
            Debug.Assert((methodKind = methodKind.EventAdd) OrElse (methodKind = methodKind.EventRemove) OrElse (methodKind = methodKind.EventRaise))
            Return Me.SetAssociatedPropertyOrEvent(eventSymbol, methodKind)
        End Function

        Private Function SetAssociatedPropertyOrEvent(propertyOrEventSymbol As Symbol, methodKind As MethodKind) As Boolean
            If Me.m_associatedPropertyOrEventOpt Is Nothing Then
                Debug.Assert(propertyOrEventSymbol.ContainingType = Me.ContainingType)
                Me.m_associatedPropertyOrEventOpt = propertyOrEventSymbol
                m_lazyMethodKind = CType(methodKind, Integer)
                Return True
            End If

            Return False
        End Function

        Private Sub EnsureSignatureIsLoaded()
            If m_lazySignature Is Nothing Then

                Dim moduleSymbol = m_ContainingType.ContainingPEModule

                Dim callingConventions As Byte
                Dim mrEx As BadImageFormatException = Nothing
                Dim paramInfo() As MetadataDecoder.ParamInfo =
                    (New MetadataDecoder(moduleSymbol, Me)).GetSignatureForMethod(m_Handle, callingConventions, mrEx)

                ' If method is not generic, let's assign empty list for type parameters
                If Not SignatureHeader.IsGeneric(callingConventions) AndAlso
                    m_lazyTypeParameters.IsDefault Then
                    ImmutableInterlocked.InterlockedCompareExchange(m_lazyTypeParameters,
                                                ImmutableArray(Of TypeParameterSymbol).Empty, Nothing)
                End If

                Dim count As Integer = paramInfo.Length - 1
                Dim params As ImmutableArray(Of ParameterSymbol)
                Dim isBad As Boolean
                Dim hasBadParameter As Boolean = False

                If count > 0 Then
                    Dim parameterCreation(count - 1) As ParameterSymbol

                    For i As Integer = 0 To count - 1 Step 1
                        parameterCreation(i) = New PEParameterSymbol(moduleSymbol, Me, i, paramInfo(i + 1), isBad)

                        If isBad Then
                            hasBadParameter = True
                        End If
                    Next

                    params = parameterCreation.AsImmutableOrNull()
                Else
                    params = ImmutableArray(Of ParameterSymbol).Empty
                End If

                ' paramInfo(0) contains information about return "parameter"
                Debug.Assert(Not paramInfo(0).IsByRef)
                Dim returnParam = New PEParameterSymbol(moduleSymbol, Me, 0, paramInfo(0), isBad)

                If mrEx IsNot Nothing OrElse hasBadParameter OrElse isBad Then
                    Dim old = Interlocked.CompareExchange(m_lazyUseSiteErrorInfo,
                                                          ErrorFactory.ErrorInfo(ERRID.ERR_UnsupportedMethod1, CustomSymbolDisplayFormatter.ShortErrorName(Me)),
                                                          ErrorFactory.EmptyErrorInfo)
                    Debug.Assert(old Is ErrorFactory.EmptyErrorInfo OrElse
                                 (old IsNot Nothing AndAlso old.Code = ERRID.ERR_UnsupportedMethod1))
                End If

                Dim signature As New SignatureData(callingConventions, params, returnParam)
                Interlocked.CompareExchange(m_lazySignature, signature, Nothing)
            End If
        End Sub

        Public Overrides ReadOnly Property TypeParameters As ImmutableArray(Of TypeParameterSymbol)
            Get
                EnsureTypeParametersAreLoaded()
                Return m_lazyTypeParameters
            End Get
        End Property

        Public Overrides ReadOnly Property TypeArguments As ImmutableArray(Of TypeSymbol)
            Get
                If IsGenericMethod Then
                    Return StaticCast(Of TypeSymbol).From(Me.TypeParameters)
                Else
                    Return ImmutableArray(Of TypeSymbol).Empty
                End If
            End Get
        End Property

        Private Sub EnsureTypeParametersAreLoaded()

            If m_lazyTypeParameters.IsDefault Then

                Dim typeParams As ImmutableArray(Of TypeParameterSymbol)

                Try
                    Dim moduleSymbol = m_ContainingType.ContainingPEModule
                    Dim gpHandles = moduleSymbol.Module.GetGenericParametersForMethodOrThrow(m_Handle)


                    If gpHandles.Count = 0 Then
                        typeParams = ImmutableArray(Of TypeParameterSymbol).Empty
                    Else
                        Dim ownedParams(gpHandles.Count - 1) As PETypeParameterSymbol

                        For i = 0 To ownedParams.Length - 1
                            ownedParams(i) = New PETypeParameterSymbol(moduleSymbol, Me, CUShort(i), gpHandles(i))
                        Next

                        typeParams = StaticCast(Of TypeParameterSymbol).From(ownedParams.AsImmutableOrNull)
                    End If
                Catch mrEx As BadImageFormatException
                    Dim old = Interlocked.CompareExchange(m_lazyUseSiteErrorInfo,
                                                          ErrorFactory.ErrorInfo(ERRID.ERR_UnsupportedMethod1, CustomSymbolDisplayFormatter.ShortErrorName(Me)),
                                                          ErrorFactory.EmptyErrorInfo)
                    Debug.Assert(old Is ErrorFactory.EmptyErrorInfo OrElse
                                 (old IsNot Nothing AndAlso old.Code = ERRID.ERR_UnsupportedMethod1))

                    typeParams = ImmutableArray(Of TypeParameterSymbol).Empty
                End Try

                ImmutableInterlocked.InterlockedCompareExchange(m_lazyTypeParameters, typeParams, Nothing)
            End If

        End Sub

        Friend Overrides ReadOnly Property CallingConvention As Microsoft.Cci.CallingConvention
            Get
                EnsureSignatureIsLoaded()
                Return CType(m_lazySignature.CallingConvention, Microsoft.Cci.CallingConvention)
            End Get
        End Property

        Public Overrides ReadOnly Property ExplicitInterfaceImplementations As ImmutableArray(Of MethodSymbol)
            Get
                If m_lazyExplicitMethodImplementations.IsDefault Then
                    Dim moduleSymbol = m_ContainingType.ContainingPEModule

                    ' Context: we need the containing type of this method as context so that we can substitute appropriately into
                    ' any generic interfaces that we might be explicitly implementing.  There is no reason to pass in the method
                    ' context, however, because any method type parameters will belong to the implemented (i.e. interface) method,
                    ' which we do not yet know.
                    Dim explicitlyOverriddenMethods = New MetadataDecoder(
                        moduleSymbol,
                        m_ContainingType).GetExplicitlyOverriddenMethods(m_ContainingType.Handle, Me.m_Handle, Me.ContainingType)

                    'avoid allocating a builder in the common case
                    Dim anyToRemove = False
                    For Each method In explicitlyOverriddenMethods
                        If Not method.ContainingType.IsInterface Then
                            anyToRemove = True
                            Exit For
                        End If

                    Next

                    Dim explicitImplementations = explicitlyOverriddenMethods
                    If anyToRemove Then
                        Dim explicitInterfaceImplementationsBuilder = ArrayBuilder(Of MethodSymbol).GetInstance()
                        For Each method In explicitlyOverriddenMethods
                            If method.ContainingType.IsInterface Then
                                explicitInterfaceImplementationsBuilder.Add(method)
                            End If

                        Next

                        explicitImplementations = explicitInterfaceImplementationsBuilder.ToImmutableAndFree()
                    End If

                    ImmutableInterlocked.InterlockedCompareExchange(m_lazyExplicitMethodImplementations, explicitImplementations, Nothing)
                End If

                Return m_lazyExplicitMethodImplementations
            End Get

        End Property


        Public Overrides Function GetDocumentationCommentXml(Optional preferredCulture As CultureInfo = Nothing, Optional expandIncludes As Boolean = False, Optional cancellationToken As CancellationToken = Nothing) As String
            ' Note: m_lazyDocComment is passed ByRef
            Return PEDocumentationCommentUtils.GetDocumentationComment(
                Me, m_ContainingType.ContainingPEModule, preferredCulture, cancellationToken, m_lazyDocComment)
        End Function

        Friend Overrides ReadOnly Property Syntax As VisualBasicSyntaxNode
            Get
                Return Nothing
            End Get
        End Property

        Friend Overrides Function GetUseSiteErrorInfo() As DiagnosticInfo
            If m_lazyUseSiteErrorInfo Is ErrorFactory.EmptyErrorInfo Then
                Dim errorInfo As DiagnosticInfo = CalculateUseSiteErrorInfo()
                EnsureTypeParametersAreLoaded()
                Interlocked.CompareExchange(m_lazyUseSiteErrorInfo, errorInfo, ErrorFactory.EmptyErrorInfo)
            End If

            Return m_lazyUseSiteErrorInfo
        End Function

        Friend Overrides ReadOnly Property ObsoleteAttributeData As ObsoleteAttributeData
            Get
                ObsoleteAttributeHelpers.InitializeObsoleteDataFromMetadata(m_lazyObsoleteAttributeData, m_Handle, DirectCast(ContainingModule, PEModuleSymbol))
                Return m_lazyObsoleteAttributeData
            End Get
        End Property

        Friend Overrides Function GetAppliedConditionalSymbols() As ImmutableArray(Of String)
            If Me.m_lazyConditionalAttributeSymbols.IsDefaultOrEmpty Then
                Dim moduleSymbol As PEModuleSymbol = m_ContainingType.ContainingPEModule
                Dim conditionalSymbols As ImmutableArray(Of String) = moduleSymbol.Module.GetConditionalAttributeValues(m_Handle)
                Debug.Assert(Not conditionalSymbols.IsDefault)
                ImmutableInterlocked.InterlockedCompareExchange(m_lazyConditionalAttributeSymbols, conditionalSymbols, Nothing)
            End If

            Return Me.m_lazyConditionalAttributeSymbols
        End Function

        Friend Overrides ReadOnly Property GenerateDebugInfoImpl As Boolean
            Get
                Return False
            End Get
        End Property

        ''' <remarks>
        ''' This is for perf, not for correctness.
        ''' </remarks>
        Friend Overrides ReadOnly Property DeclaringCompilation As VisualBasicCompilation
            Get
                Return Nothing
            End Get
        End Property
    End Class

End Namespace
