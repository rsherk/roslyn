﻿' Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports System.Collections.Generic
Imports System.Linq
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.CodeGeneration
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Microsoft.CodeAnalysis.VisualBasic.Extensions
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax

Namespace Microsoft.CodeAnalysis.VisualBasic.CodeGeneration
    Friend Class EventGenerator
        Inherits AbstractVisualBasicCodeGenerator

        Private Shared Function AfterMember(
                members As SyntaxList(Of StatementSyntax),
                eventDeclaration As StatementSyntax) As StatementSyntax
            If eventDeclaration.VisualBasicKind = SyntaxKind.EventStatement Then
                ' Field style events go after the last field event, or after the last field.
                Dim lastEvent = members.LastOrDefault(Function(m) TypeOf m Is EventStatementSyntax)

                Return If(lastEvent, LastField(members))
            End If

            If eventDeclaration.VisualBasicKind = SyntaxKind.EventBlock Then
                ' Property style events go after existing events, then after existing constructors.
                Dim lastEvent = members.LastOrDefault(Function(m) m.VisualBasicKind = SyntaxKind.EventBlock)

                Return If(lastEvent, LastConstructor(members))
            End If

            Return Nothing
        End Function

        Private Shared Function BeforeMember(
                members As SyntaxList(Of StatementSyntax),
                eventDeclaration As StatementSyntax) As StatementSyntax
            ' If it's a field style event, then it goes before everything else if we don't have any
            ' existing fields/events.
            If eventDeclaration.VisualBasicKind = SyntaxKind.FieldDeclaration Then
                Return members.FirstOrDefault()
            End If

            ' Otherwise just place it before the methods.
            Return FirstMethod(members)
        End Function

        Friend Shared Function AddEventTo(destination As TypeBlockSyntax,
                                    [event] As IEventSymbol,
                                    options As CodeGenerationOptions,
                                    availableIndices As IList(Of Boolean)) As TypeBlockSyntax
            Dim eventDeclaration = GenerateEventDeclaration([event], GetDestination(destination), options)

            Dim members = Insert(destination.Members, eventDeclaration, options, availableIndices,
                                 after:=Function(list) AfterMember(list, eventDeclaration),
                                 before:=Function(list) BeforeMember(list, eventDeclaration))

            ' Find the best place to put the field.  It should go after the last field if we already
            ' have fields, or at the beginning of the file if we don't.
            Return FixTerminators(destination.WithMembers(members))
        End Function

        Public Shared Function GenerateEventDeclaration([event] As IEventSymbol,
                                                 destination As CodeGenerationDestination,
                                                 options As CodeGenerationOptions) As DeclarationStatementSyntax
            Dim reusableSyntax = GetReuseableSyntaxNodeForSymbol(Of DeclarationStatementSyntax)([event], options)
            If reusableSyntax IsNot Nothing Then
                Return reusableSyntax
            End If

            Dim declaration = GenerateEventDeclarationWorker([event], destination, options)

            Return AddCleanupAnnotationsTo(ConditionallyAddDocumentationCommentTo(declaration, [event], options))
        End Function

        Private Shared Function GenerateEventDeclarationWorker([event] As IEventSymbol,
                                                       destination As CodeGenerationDestination,
                                                       options As CodeGenerationOptions) As DeclarationStatementSyntax
            ' TODO(cyrusn): Handle Add/Remove/Raise events
            Dim eventType = TryCast([event].Type, INamedTypeSymbol)
            If eventType.IsDelegateType() AndAlso eventType.AssociatedEvent IsNot Nothing Then
                ' This is a declaration style event like "Event E(x As String)".  This event will
                ' have a type that is unmentionable.  So we should not generate it as "Event E() As
                ' SomeType", but should instead inline the delegate type into the event itself.
                Return SyntaxFactory.EventStatement(
                    attributeLists:=AttributeGenerator.GenerateAttributeBlocks([event].GetAttributes(), options),
                    modifiers:=GenerateModifiers([event], destination, options),
                    identifier:=[event].Name.ToIdentifierToken,
                    parameterList:=ParameterGenerator.GenerateParameterList(eventType.DelegateInvokeMethod.Parameters.Select(Function(p) RemoveOptionalOrParamArray(p)).ToList(), options),
                    asClause:=Nothing,
                    implementsClause:=GenerateImplementsClause([event].ExplicitInterfaceImplementations.FirstOrDefault()))
            ElseIf TypeOf [event] Is CodeGenerationEventSymbol AndAlso TryCast([event], CodeGenerationEventSymbol).ParameterList IsNot Nothing Then
                ' We'll try to generate a parameter list
                Return SyntaxFactory.EventStatement(
                    attributeLists:=AttributeGenerator.GenerateAttributeBlocks([event].GetAttributes(), options),
                    modifiers:=GenerateModifiers([event], destination, options),
                    identifier:=[event].Name.ToIdentifierToken,
                    parameterList:=ParameterGenerator.GenerateParameterList(TryCast([event], CodeGenerationEventSymbol).ParameterList.Select(Function(p) RemoveOptionalOrParamArray(p)).ToArray(), options),
                    asClause:=Nothing,
                    implementsClause:=GenerateImplementsClause([event].ExplicitInterfaceImplementations.FirstOrDefault()))
            Else
                Return SyntaxFactory.EventStatement(
                    attributeLists:=AttributeGenerator.GenerateAttributeBlocks([event].GetAttributes(), options),
                    modifiers:=GenerateModifiers([event], destination, options),
                    identifier:=[event].Name.ToIdentifierToken,
                    parameterList:=Nothing,
                    asClause:=GenerateAsClause([event]),
                    implementsClause:=GenerateImplementsClause([event].ExplicitInterfaceImplementations.FirstOrDefault()))
            End If
        End Function

        Private Shared Function GenerateModifiers([event] As IEventSymbol,
                                                  destination As CodeGenerationDestination,
                                                  options As CodeGenerationOptions) As SyntaxTokenList
            Dim tokens = New List(Of SyntaxToken)()

            If destination <> CodeGenerationDestination.InterfaceType Then
                AddAccessibilityModifiers([event].DeclaredAccessibility, tokens, destination, options, Accessibility.Public)

                If [event].IsStatic Then
                    tokens.Add(SyntaxFactory.Token(SyntaxKind.SharedKeyword))
                End If

                If [event].IsAbstract Then
                    tokens.Add(SyntaxFactory.Token(SyntaxKind.MustOverrideKeyword))
                End If
            End If

            Return SyntaxFactory.TokenList(tokens)
        End Function

        Private Shared Function GenerateAsClause([event] As IEventSymbol) As SimpleAsClauseSyntax
            ' TODO: Someday support events without as clauses (with parameter lists instead)
            Return SyntaxFactory.SimpleAsClause([event].Type.GenerateTypeSyntax())
        End Function

        Private Shared Function RemoveOptionalOrParamArray(parameter As IParameterSymbol) As IParameterSymbol
            If Not parameter.IsOptional AndAlso Not parameter.IsParams Then
                Return parameter
            Else
                Return CodeGenerationSymbolFactory.CreateParameterSymbol(parameter.GetAttributes(), parameter.RefKind, isParams:=False, type:=parameter.Type, name:=parameter.Name, hasDefaultValue:=False)
            End If
        End Function
    End Class
End Namespace