﻿' Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports System.Collections.Generic
Imports System.Collections.Immutable
Imports System.Diagnostics
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax

Namespace Microsoft.CodeAnalysis.VisualBasic.Symbols

    ''' <summary>
    ''' Represents a compiler "MyGroupCollection" property.
    ''' </summary>
    Friend Class SynthesizedMyGroupCollectionPropertySymbol
        Inherits SynthesizedPropertyBase

        Private ReadOnly m_name As String
        Private ReadOnly m_field As SynthesizedMyGroupCollectionPropertyBackingFieldSymbol
        Private ReadOnly m_getMethod As SynthesizedMyGroupCollectionPropertyGetAccessorSymbol
        Private ReadOnly m_setMethodOpt As SynthesizedMyGroupCollectionPropertySetAccessorSymbol
        Public ReadOnly AttributeSyntax As SyntaxReference
        Public ReadOnly DefaultInstanceAlias As String

        Public Sub New(
            container As SourceNamedTypeSymbol,
            attributeSyntax As AttributeSyntax,
            propertyName As String,
            fieldName As String,
            type As NamedTypeSymbol,
            createMethod As String,
            disposeMethod As String,
            defaultInstanceAlias As String
        )
            Me.AttributeSyntax = attributeSyntax.SyntaxTree.GetReference(attributeSyntax)
            Me.DefaultInstanceAlias = defaultInstanceAlias

            m_name = propertyName
            m_field = New SynthesizedMyGroupCollectionPropertyBackingFieldSymbol(container, Me, type, fieldName)
            m_getMethod = New SynthesizedMyGroupCollectionPropertyGetAccessorSymbol(container, Me, createMethod)

            If disposeMethod.Length > 0 Then
                m_setMethodOpt = New SynthesizedMyGroupCollectionPropertySetAccessorSymbol(container, Me, disposeMethod)
            End If
        End Sub

        Public Overrides ReadOnly Property Name As String
            Get
                Return m_name
            End Get
        End Property

        Public Overrides ReadOnly Property Type As TypeSymbol
            Get
                Return m_field.Type
            End Get
        End Property

        Public Overrides ReadOnly Property GetMethod As MethodSymbol
            Get
                Return m_getMethod
            End Get
        End Property

        Public Overrides ReadOnly Property SetMethod As MethodSymbol
            Get
                Return m_setMethodOpt
            End Get
        End Property

        Friend Overrides Function GetLexicalSortKey() As LexicalSortKey
            Return LexicalSortKey.NotInSource
        End Function

        Public Overrides ReadOnly Property ContainingSymbol As Symbol
            Get
                Return m_field.ContainingSymbol
            End Get
        End Property

        Public Overrides ReadOnly Property ContainingType As NamedTypeSymbol
            Get
                Return m_field.ContainingType
            End Get
        End Property

        Friend Overrides ReadOnly Property AssociatedField As FieldSymbol
            Get
                Return m_field
            End Get
        End Property

        Public Overrides ReadOnly Property IsImplicitlyDeclared As Boolean
            Get
                Return True
            End Get
        End Property

        Public Overrides ReadOnly Property DeclaringSyntaxReferences As ImmutableArray(Of SyntaxReference)
            Get
                Return ImmutableArray(Of SyntaxReference).Empty
            End Get
        End Property

        Public Overrides ReadOnly Property Locations As ImmutableArray(Of Location)
            Get
                Return ImmutableArray(Of Location).Empty
            End Get
        End Property

        Friend Overrides ReadOnly Property IsMyGroupCollectionProperty As Boolean
            Get
                Return True
            End Get
        End Property

        Public Sub RelocateDiagnostics(source As DiagnosticBag, destination As DiagnosticBag)
            If source.IsEmptyWithoutResolution Then
                Return
            End If

            Dim diagnosticLocation As Location = AttributeSyntax.Location

            For Each diag As VBDiagnostic In source.AsEnumerable
                destination.Add(diag.WithLocation(diagnosticLocation))
            Next
        End Sub
    End Class

End Namespace
