﻿' Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.Runtime.InteropServices
Imports System.Threading
Imports Microsoft.CodeAnalysis.RuntimeMembers
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax
Imports Roslyn.Utilities
Imports TypeKind = Microsoft.CodeAnalysis.TypeKind

Namespace Microsoft.CodeAnalysis.VisualBasic

    ''' <summary>
    ''' Provides context for binding body of a MethodSymbol. 
    ''' </summary>
    Friend NotInheritable Class MethodBodyBinder
        Inherits SubOrFunctionBodyBinder

        Private ReadOnly _functionValue As LocalSymbol

        ''' <summary>
        ''' Create binder for binding the body of a method. 
        ''' </summary>
        Public Sub New(methodSymbol As MethodSymbol, root As VisualBasicSyntaxNode, containingBinder As Binder)
            MyBase.New(methodSymbol, root, containingBinder)

            ' this could be a synthetic method that does not have syntax for the method body
            _functionValue = CreateFunctionValueLocal(methodSymbol, root)

            If _functionValue IsNot Nothing AndAlso Not methodSymbol.IsUserDefinedOperator() Then
                Dim parameterName = _functionValue.Name
                '  Note: the name can be empty in case syntax errors in function/property definition
                If Not String.IsNullOrEmpty(parameterName) Then
                    ' Note that, if there is a parameter with this name, we are overriding it in the map.
                    _parameterMap(parameterName) = _functionValue
                End If
            End If
        End Sub

        Private Function CreateFunctionValueLocal(methodSymbol As MethodSymbol, root As VisualBasicSyntaxNode) As LocalSymbol
            Dim methodBlock = TryCast(root, MethodBlockBaseSyntax)

            Debug.Assert(Not TypeOf methodSymbol Is SourceMethodSymbol OrElse
                         Me.IsSemanticModelBinder OrElse
                         (methodBlock Is DirectCast(methodSymbol, SourceMethodSymbol).BlockSyntax AndAlso
                          methodBlock IsNot Nothing))

            If methodBlock Is Nothing Then
                Return Nothing
            End If

            ' Create a local for the function return value. The local's type is the same as the function's return type
            Select Case methodBlock.Kind
                Case SyntaxKind.FunctionBlock
                    Dim begin As MethodStatementSyntax = DirectCast(methodBlock, MethodBlockSyntax).Begin

                    ' Note, it is an error if a parameter has the same name as the function.  
                    Dim identifier = begin.Identifier
                    Return LocalSymbol.Create(methodSymbol, Me, identifier, LocalSymbol.LocalDeclarationKind.FunctionValue,
                                              If(methodSymbol.ReturnType.IsVoidType(), ErrorTypeSymbol.UnknownResultType, methodSymbol.ReturnType))

                Case SyntaxKind.PropertyGetBlock
                    If methodBlock.Parent IsNot Nothing AndAlso
                       methodBlock.Parent.Kind = SyntaxKind.PropertyBlock Then

                        Dim propertySyntax As PropertyStatementSyntax = DirectCast(methodBlock.Parent, PropertyBlockSyntax).PropertyStatement
                        Dim identifier = propertySyntax.Identifier
                        Return LocalSymbol.Create(methodSymbol, Me, identifier, LocalSymbol.LocalDeclarationKind.FunctionValue,
                                                  If(methodSymbol.ReturnType.IsVoidType(), ErrorTypeSymbol.UnknownResultType, methodSymbol.ReturnType))
                    End If

                Case SyntaxKind.OperatorBlock
                    ' in case of an operator, we need to create a symbol that uses the identifier token for error messages, but also
                    ' has an "alias" (the CLR operator name) which is used as the name of this local.
                    Return LocalSymbol.Create(methodSymbol, Me,
                                              GeneratedNames.MakeOperatorLocalName(methodSymbol.Name),
                                              DirectCast(methodBlock, OperatorBlockSyntax).Begin.OperatorToken,
                                              LocalSymbol.LocalDeclarationKind.FunctionValue,
                                              If(methodSymbol.ReturnType.IsVoidType(), ErrorTypeSymbol.UnknownResultType, methodSymbol.ReturnType))

                Case SyntaxKind.AddHandlerBlock
                    If DirectCast(methodSymbol.AssociatedPropertyOrEvent, EventSymbol).IsWindowsRuntimeEvent AndAlso
                       methodBlock.Parent IsNot Nothing AndAlso
                       methodBlock.Parent.Kind = SyntaxKind.EventBlock Then

                        Dim eventSyntax As EventStatementSyntax = DirectCast(methodBlock.Parent, EventBlockSyntax).EventStatement
                        Dim identifier = eventSyntax.Identifier
                        ' NOTE: To avoid a breaking change, we reproduce the dev11 behavior - the name of the local is
                        ' taken from the name of the accessor, rather than the name of the event (as it would be for a property).
                        Return LocalSymbol.Create(methodSymbol, Me, methodSymbol.Name, identifier, LocalSymbol.LocalDeclarationKind.FunctionValue,
                                                  If(methodSymbol.ReturnType.IsVoidType(), ErrorTypeSymbol.UnknownResultType, methodSymbol.ReturnType))
                    End If

                Case Else
                    Debug.Assert(methodSymbol.IsSub)

            End Select

            Return Nothing
        End Function

        Public Overrides Function GetLocalForFunctionValue() As LocalSymbol
            Return _functionValue
        End Function

        Public Overrides ReadOnly Property IsInQuery As Boolean
            Get
                Return False
            End Get
        End Property

        Friend Overrides ReadOnly Property SuppressCallerInfo As Boolean
            Get
                Return DirectCast(ContainingMember, MethodSymbol).IsImplicitlyDeclared AndAlso TypeOf ContainingMember Is SynthesizedMyGroupCollectionPropertyAccessorSymbol
            End Get
        End Property
    End Class

End Namespace

