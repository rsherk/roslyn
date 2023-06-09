﻿' Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports System.Collections.Generic
Imports System.Reflection.Metadata
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax

Namespace Microsoft.CodeAnalysis.VisualBasic.Symbols.Metadata.PE

    ''' <summary>
    ''' The class to represent top level types imported from a PE/module.
    ''' </summary>
    Friend NotInheritable Class PENamedTypeSymbolWithEmittedNamespaceName
        Inherits PENamedTypeSymbol

        Private ReadOnly m_EmittedNamespaceName As String

        Private ReadOnly m_CorTypeId As SpecialType

        Friend Sub New(
            moduleSymbol As PEModuleSymbol,
            containingNamespace As PENamespaceSymbol,
            typeDef As TypeHandle,
            emittedNamespaceName As String
        )
            MyBase.New(moduleSymbol, containingNamespace, typeDef)

            Debug.Assert(emittedNamespaceName IsNot Nothing)
            Debug.Assert(emittedNamespaceName.Length > 0)
            m_EmittedNamespaceName = emittedNamespaceName

            ' check if this is one of the COR library types
            If (Arity = 0 OrElse MangleName) AndAlso (moduleSymbol.ContainingAssembly.KeepLookingForDeclaredSpecialTypes) Then
                Debug.Assert(emittedNamespaceName.Length > 0)
                m_CorTypeId = SpecialTypes.GetTypeFromMetadataName(MetadataHelpers.BuildQualifiedName(emittedNamespaceName, MetadataName))
            Else
                m_CorTypeId = SpecialType.None
            End If
        End Sub

        Public Overrides ReadOnly Property SpecialType As SpecialType
            Get
                Return m_CorTypeId
            End Get
        End Property

        Friend Overrides Function GetEmittedNamespaceName() As String
            Return m_EmittedNamespaceName
        End Function

    End Class

End Namespace
