﻿' Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Shared.Collections
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax

Namespace Microsoft.CodeAnalysis.CodeCleanup.Providers
#If MEF Then
    <ExportCodeCleanupProvider(PredefinedCodeCleanupProviderNames.NormalizeModifiersOrOperators, LanguageNames.VisualBasic)>
    <ExtensionOrder(After:=PredefinedCodeCleanupProviderNames.AddMissingTokens, Before:=PredefinedCodeCleanupProviderNames.Format)>
    Friend Class NormalizeModifiersOrOperatorsCodeCleanupProvider
#Else
    Friend Class NormalizeModifiersOrOperatorsCodeCleanupProvider
#End If
        Implements ICodeCleanupProvider

        Public ReadOnly Property Name As String Implements ICodeCleanupProvider.Name
            Get
                Return PredefinedCodeCleanupProviderNames.NormalizeModifiersOrOperators
            End Get
        End Property

        Public Async Function CleanupAsync(document As Document, spans As IEnumerable(Of TextSpan), Optional cancellationToken As CancellationToken = Nothing) As Task(Of Document) Implements ICodeCleanupProvider.CleanupAsync
            Dim root = Await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(False)
            Dim newRoot = Cleanup(root, spans, document.Project.Solution.Workspace, cancellationToken)

            Return If(root Is newRoot, document, document.WithSyntaxRoot(newRoot))
        End Function

        Public Function Cleanup(root As SyntaxNode, spans As IEnumerable(Of TextSpan), workspace As Workspace, Optional cancellationToken As CancellationToken = Nothing) As SyntaxNode Implements ICodeCleanupProvider.Cleanup
            Dim rewriter = New Rewriter(spans, cancellationToken)
            Dim newRoot = rewriter.Visit(root)

            Return If(root Is newRoot, root, newRoot)
        End Function

        Private Class Rewriter
            Inherits VisualBasicSyntaxRewriter

            ' list of modifier syntax kinds in order
            ' this order will be used when the rewriter re-order modifiers
            Private Shared ReadOnly ModifierKindsInOrder As List(Of SyntaxKind) = New List(Of SyntaxKind) From {
                SyntaxKind.PartialKeyword, SyntaxKind.DefaultKeyword, SyntaxKind.PrivateKeyword, SyntaxKind.ProtectedKeyword,
                SyntaxKind.PublicKeyword, SyntaxKind.FriendKeyword, SyntaxKind.NotOverridableKeyword, SyntaxKind.OverridableKeyword,
                SyntaxKind.MustOverrideKeyword, SyntaxKind.OverloadsKeyword, SyntaxKind.OverridesKeyword, SyntaxKind.MustInheritKeyword,
                SyntaxKind.NotInheritableKeyword, SyntaxKind.StaticKeyword, SyntaxKind.SharedKeyword, SyntaxKind.ShadowsKeyword,
                SyntaxKind.ReadOnlyKeyword, SyntaxKind.WriteOnlyKeyword, SyntaxKind.DimKeyword, SyntaxKind.ConstKeyword,
                SyntaxKind.WithEventsKeyword, SyntaxKind.WideningKeyword, SyntaxKind.NarrowingKeyword, SyntaxKind.CustomKeyword,
                SyntaxKind.AsyncKeyword, SyntaxKind.IteratorKeyword}

            Private Shared ReadOnly RemoveDimKeywordSet As HashSet(Of SyntaxKind) = New HashSet(Of SyntaxKind) From {
                SyntaxKind.PrivateKeyword, SyntaxKind.ProtectedKeyword, SyntaxKind.PublicKeyword, SyntaxKind.FriendKeyword,
                SyntaxKind.SharedKeyword, SyntaxKind.ShadowsKeyword, SyntaxKind.ReadOnlyKeyword}

            Private Shared ReadOnly NormalizeOperatorsSet As Dictionary(Of SyntaxKind, List(Of SyntaxKind)) = New Dictionary(Of SyntaxKind, List(Of SyntaxKind)) From {
                    {SyntaxKind.LessThanGreaterThanToken, New List(Of SyntaxKind) From {SyntaxKind.GreaterThanToken, SyntaxKind.LessThanToken}},
                    {SyntaxKind.GreaterThanEqualsToken, New List(Of SyntaxKind) From {SyntaxKind.EqualsToken, SyntaxKind.GreaterThanToken}},
                    {SyntaxKind.LessThanEqualsToken, New List(Of SyntaxKind) From {SyntaxKind.EqualsToken, SyntaxKind.LessThanToken}}
                }

            Private ReadOnly _spans As SimpleIntervalTree(Of TextSpan)
            Private ReadOnly _cancellationToken As CancellationToken

            Public Sub New(spans As IEnumerable(Of TextSpan), cancellationToken As CancellationToken)
                MyBase.New(visitIntoStructuredTrivia:=True)

                _spans = New SimpleIntervalTree(Of TextSpan)(TextSpanIntervalIntrospector.Instance, spans)
                _cancellationToken = cancellationToken
            End Sub

            Public Overrides Function Visit(node As SyntaxNode) As SyntaxNode
                _cancellationToken.ThrowIfCancellationRequested()

                ' if there is no overlapping spans, no need to walk down this noe
                If node Is Nothing OrElse
                   Not _spans.GetOverlappingIntervals(node.FullSpan.Start, node.FullSpan.Length).Any() Then
                    Return node
                End If

                ' walk down this path
                Return MyBase.Visit(node)
            End Function

            Public Overrides Function VisitModuleStatement(node As ModuleStatementSyntax) As SyntaxNode
                Return NormalizeModifiers(node, MyBase.VisitModuleStatement(node), Function(n) n.Modifiers, Function(n, modifiers) n.WithModifiers(modifiers))
            End Function

            Public Overrides Function VisitStructureStatement(node As StructureStatementSyntax) As SyntaxNode
                Return NormalizeModifiers(node, MyBase.VisitStructureStatement(node), Function(n) n.Modifiers, Function(n, modifiers) n.WithModifiers(modifiers))
            End Function

            Public Overrides Function VisitInterfaceStatement(node As InterfaceStatementSyntax) As SyntaxNode
                Return NormalizeModifiers(node, MyBase.VisitInterfaceStatement(node), Function(n) n.Modifiers, Function(n, modifiers) n.WithModifiers(modifiers))
            End Function

            Public Overrides Function VisitClassStatement(node As ClassStatementSyntax) As SyntaxNode
                Return NormalizeModifiers(node, MyBase.VisitClassStatement(node), Function(n) n.Modifiers, Function(n, modifiers) n.WithModifiers(modifiers))
            End Function

            Public Overrides Function VisitEnumStatement(node As EnumStatementSyntax) As SyntaxNode
                Return NormalizeModifiers(node, MyBase.VisitEnumStatement(node), Function(n) n.Modifiers, Function(n, modifiers) n.WithModifiers(modifiers))
            End Function

            Public Overrides Function VisitMethodStatement(node As MethodStatementSyntax) As SyntaxNode
                Return NormalizeModifiers(node, MyBase.VisitMethodStatement(node), Function(n) n.Modifiers, Function(n, modifiers) n.WithModifiers(modifiers))
            End Function

            Public Overrides Function VisitSubNewStatement(node As SubNewStatementSyntax) As SyntaxNode
                Return NormalizeModifiers(node, MyBase.VisitSubNewStatement(node), Function(n) n.Modifiers, Function(n, modifiers) n.WithModifiers(modifiers))
            End Function

            Public Overrides Function VisitDeclareStatement(node As DeclareStatementSyntax) As SyntaxNode
                Return NormalizeModifiers(node, MyBase.VisitDeclareStatement(node), Function(n) n.Modifiers, Function(n, modifiers) n.WithModifiers(modifiers))
            End Function

            Public Overrides Function VisitDelegateStatement(node As DelegateStatementSyntax) As SyntaxNode
                Return NormalizeModifiers(node, MyBase.VisitDelegateStatement(node), Function(n) n.Modifiers, Function(n, modifiers) n.WithModifiers(modifiers))
            End Function

            Public Overrides Function VisitEventStatement(node As EventStatementSyntax) As SyntaxNode
                Return NormalizeModifiers(node, MyBase.VisitEventStatement(node), Function(n) n.Modifiers, Function(n, modifiers) n.WithModifiers(modifiers))
            End Function

            Public Overrides Function VisitPropertyStatement(node As PropertyStatementSyntax) As SyntaxNode
                Return NormalizeModifiers(node, MyBase.VisitPropertyStatement(node), Function(n) n.Modifiers, Function(n, modifiers) n.WithModifiers(modifiers))
            End Function

            Public Overrides Function VisitAccessorStatement(node As AccessorStatementSyntax) As SyntaxNode
                Return NormalizeModifiers(node, MyBase.VisitAccessorStatement(node), Function(n) n.Modifiers, Function(n, modifiers) n.WithModifiers(modifiers))
            End Function

            Public Overrides Function VisitIncompleteMember(node As IncompleteMemberSyntax) As SyntaxNode
                ' don't do anything
                Return MyBase.VisitIncompleteMember(node)
            End Function

            Public Overrides Function VisitFieldDeclaration(node As FieldDeclarationSyntax) As SyntaxNode
                Return NormalizeModifiers(node, MyBase.VisitFieldDeclaration(node), Function(n) n.Modifiers, Function(n, modifiers) n.WithModifiers(modifiers))
            End Function

            Public Overrides Function VisitLocalDeclarationStatement(node As LocalDeclarationStatementSyntax) As SyntaxNode
                Return NormalizeModifiers(node, MyBase.VisitLocalDeclarationStatement(node), Function(n) n.Modifiers, Function(n, modifiers) n.WithModifiers(modifiers))
            End Function

            Public Overrides Function VisitParameterList(node As ParameterListSyntax) As SyntaxNode
                ' whole node must be under the span. otherwise, we just return
                Dim newNode = MyBase.VisitParameterList(node)

                ' bug # 12898
                ' decide not to automatically remove "ByVal"
#If False Then
                Dim span = node.Span
                If Not _spans.GetContainingIntervals(span.Start, span.Length).Any() Then
                    Return newNode
                End If

                ' remove any existing ByVal keyword
                Dim currentNode = DirectCast(newNode, ParameterListSyntax)
                For i = 0 To node.Parameters.Count - 1
                    currentNode = RemoveByValKeyword(currentNode, i)
                Next

                ' no changes
                If newNode Is currentNode Then
                    Return newNode
                End If

                ' replace whole parameter list
                _textChanges.Add(node.FullSpan, currentNode.GetFullText())

                Return currentNode
#End If

                Return newNode
            End Function

            Public Overrides Function VisitLambdaHeader(node As LambdaHeaderSyntax) As SyntaxNode
                ' lambda can have async and iterator modifiers but we currently don't support those
                Return node
            End Function

            Public Overrides Function VisitOperatorStatement(node As OperatorStatementSyntax) As SyntaxNode
                Dim visitedNode = DirectCast(MyBase.VisitOperatorStatement(node), OperatorStatementSyntax)

                Dim span = node.Span
                If Not _spans.GetContainingIntervals(span.Start, span.Length).Any() Then
                    Return visitedNode
                End If

                ' operator sometimes requires a fix up outside of modifiers
                Dim fixedUpNode = OperatorStatementSpecialFixup(visitedNode)

                ' now, normalize modifiers
                Dim newNode = NormalizeModifiers(node, fixedUpNode, Function(n) n.Modifiers, Function(n, modifiers) n.WithModifiers(modifiers))

                Dim [operator] = NormalizeOperator(
                                    newNode.OperatorToken,
                                    Function(t) t.VisualBasicKind = SyntaxKind.GreaterThanToken,
                                    Function(t) t.TrailingTrivia,
                                    Function(t) New List(Of SyntaxKind) From {SyntaxKind.LessThanToken},
                                    Function(t, i)
                                        Return t.CopyAnnotationsTo(
                                            SyntaxFactory.Token(
                                                t.LeadingTrivia.Concat(t.TrailingTrivia.Take(i)).ToSyntaxTriviaList(), _
                                                SyntaxKind.LessThanGreaterThanToken, _
                                                t.TrailingTrivia.Skip(i + 1).ToSyntaxTriviaList()))
                                    End Function)

                If [operator].VisualBasicKind = SyntaxKind.None Then
                    Return newNode
                End If

                Return newNode.WithOperatorToken([operator])
            End Function

            Public Overrides Function VisitBinaryExpression(node As BinaryExpressionSyntax) As SyntaxNode
                ' normalize binary operators
                Dim binaryOperator = DirectCast(MyBase.VisitBinaryExpression(node), BinaryExpressionSyntax)

                ' quick check. operator must be missing
                If Not binaryOperator.OperatorToken.IsMissing Then
                    Return binaryOperator
                End If

                Dim span = node.Span
                If Not _spans.GetContainingIntervals(span.Start, span.Length).Any() Then
                    Return binaryOperator
                End If

                ' and the operator must be one of kinds that we are interested in
                Dim [operator] = NormalizeOperator(
                                    binaryOperator.OperatorToken,
                                    Function(t) NormalizeOperatorsSet.ContainsKey(t.VisualBasicKind),
                                    Function(t) t.LeadingTrivia,
                                    Function(t) NormalizeOperatorsSet(t.VisualBasicKind),
                                    Function(t, i)
                                        Return t.CopyAnnotationsTo(
                                            SyntaxFactory.Token(
                                                t.LeadingTrivia.Take(i).ToSyntaxTriviaList(), _
                                                t.VisualBasicKind, _
                                                t.LeadingTrivia.Skip(i + 1).Concat(t.TrailingTrivia).ToSyntaxTriviaList()))
                                    End Function)

                If [operator].VisualBasicKind = SyntaxKind.None Then
                    Return binaryOperator
                End If

                Return binaryOperator.WithOperatorToken([operator])
            End Function

            Public Overrides Function VisitToken(token As SyntaxToken) As SyntaxToken
                Dim newToken = MyBase.VisitToken(token)

                Dim span = token.Span
                If Not _spans.GetContainingIntervals(span.Start, span.Length).Any() Then
                    Return newToken
                End If

                If token.IsMissing OrElse Not SyntaxFacts.IsOperator(token.VisualBasicKind) Then
                    Return newToken
                End If

                Dim actualText = token.ToString()
                Dim expectedText = SyntaxFacts.GetText(token.VisualBasicKind)

                If String.IsNullOrWhiteSpace(expectedText) OrElse actualText = expectedText Then
                    Return newToken
                End If

                Return SyntaxFactory.Token(newToken.LeadingTrivia, newToken.VisualBasicKind, newToken.TrailingTrivia, expectedText)
            End Function

            ''' <summary>
            ''' this will put operator token and modifier tokens in right order
            ''' </summary>
            Private Function OperatorStatementSpecialFixup(node As OperatorStatementSyntax) As OperatorStatementSyntax
                ' first check whether operator is missing
                If Not node.OperatorToken.IsMissing Then
                    Return node
                End If

                ' check whether operator has missing stuff in skipped token list
                Dim skippedTokens = node.OperatorToken.TrailingTrivia _
                                                 .Where(Function(t) t.VisualBasicKind = SyntaxKind.SkippedTokensTrivia) _
                                                 .Select(Function(t) DirectCast(t.GetStructure(), SkippedTokensTriviaSyntax)) _
                                                 .SelectMany(Function(t) t.Tokens)

                ' there must be 2 skipped tokens
                If skippedTokens.Count <> 2 Then
                    Return node
                End If

                Dim last = skippedTokens.Last()
                If Not SyntaxFacts.IsOperatorStatementOperatorToken(last.VisualBasicKind) Then
                    Return node
                End If

                ' reorder some tokens
                Dim newNode = node.WithModifiers(node.Modifiers.AddRange(skippedTokens.Take(skippedTokens.Count - 1).ToArray())).WithOperatorToken(last)
                If Not ValidOperatorStatement(newNode) Then
                    Return node
                End If

                Return newNode
            End Function

            ''' <summary>
            ''' check whether given operator statement is valid or not
            ''' </summary>
            Private Function ValidOperatorStatement(node As OperatorStatementSyntax) As Boolean
                Dim parsableStatementText = node.NormalizeWhitespace().ToString()
                Dim parsableCompilationUnit = "Class C" + vbCrLf + parsableStatementText + vbCrLf + "End Operator" + vbCrLf + "End Class"
                Dim parsedNode = SyntaxFactory.ParseCompilationUnit(parsableCompilationUnit)

                Return Not parsedNode.ContainsDiagnostics()
            End Function

            ''' <summary>
            ''' normalize operator
            ''' </summary>
            Private Function NormalizeOperator(
                [operator] As SyntaxToken,
                checker As Func(Of SyntaxToken, Boolean),
                triviaListGetter As Func(Of SyntaxToken, SyntaxTriviaList),
                tokenKindsGetter As Func(Of SyntaxToken, List(Of SyntaxKind)),
                operatorCreator As Func(Of SyntaxToken, Integer, SyntaxToken)) As SyntaxToken

                If Not checker([operator]) Then
                    Return Nothing
                End If

                ' now, it should have skipped token trivia in trivia list
                Dim skippedTokenTrivia = triviaListGetter([operator]).FirstOrDefault(Function(t) t.VisualBasicKind = SyntaxKind.SkippedTokensTrivia)
                If skippedTokenTrivia.VisualBasicKind = SyntaxKind.None Then
                    Return Nothing
                End If

                ' token in the skipped token list must match what we are expecting
                Dim skippedTokensList = DirectCast(skippedTokenTrivia.GetStructure(), SkippedTokensTriviaSyntax)

                Dim actual = skippedTokensList.Tokens
                Dim expected = tokenKindsGetter([operator])
                If actual.Count <> expected.Count Then
                    Return Nothing
                End If

                Dim i = -1
                For Each token In actual
                    i = i + 1
                    If token.VisualBasicKind <> expected(i) Then
                        Return Nothing
                    End If
                Next

                ' okay, looks like it is what we are expecting. let's fix it up
                ' move everything after skippedTokenTrivia to trailing trivia
                Dim index = -1
                Dim list = triviaListGetter([operator])
                For i = 0 To list.Count - 1
                    If list(i) = skippedTokenTrivia Then
                        index = i
                        Exit For
                    End If
                Next

                ' it must exist
                Contract.ThrowIfFalse(index >= 0)

                Return operatorCreator([operator], index)
            End Function

            ''' <summary>
            ''' reorder modifiers in the list
            ''' </summary>
            Private Function ReorderModifiers(modifiers As SyntaxTokenList) As SyntaxTokenList
                ' quick check - if there is only one or less modifier, return as it is
                If modifiers.Count <= 1 Then
                    Return modifiers
                End If

                ' do quick check to see whether modifiers are already in right order
                If IsModifiersInRightOrder(modifiers) Then
                    Return modifiers
                End If

                ' re-create the list with trivia from old modifier token list
                Dim currentModifierIndex = 0
                Dim result = New List(Of SyntaxToken)(modifiers.Count)

                Dim modifierList = modifiers.ToList()
                For Each k In ModifierKindsInOrder
                    ' we found all modifiers
                    If currentModifierIndex = modifierList.Count Then
                        Exit For
                    End If

                    Dim tokenInRightOrder = modifierList.FirstOrDefault(Function(m) m.VisualBasicKind = k)

                    ' if we didn't find, move on to next one
                    If tokenInRightOrder.VisualBasicKind = SyntaxKind.None Then
                        Continue For
                    End If

                    ' we found a modifier, re-create list in right order with right trivia from right original token
                    Dim originalToken = modifierList(currentModifierIndex)
                    result.Add(tokenInRightOrder.With(originalToken.LeadingTrivia, originalToken.TrailingTrivia))

                    currentModifierIndex += 1
                Next

                ' Verify that all unique modifiers were added to the result.
                ' The number added to the result count is the duplicate modifier count in the input modifierList.
                Debug.Assert(modifierList.Count = result.Count +
                             modifierList.GroupBy(Function(token) token.VisualBasicKind).SelectMany(Function(grp) grp.Skip(1)).Count)
                Return SyntaxFactory.TokenList(result)
            End Function

            ''' <summary>
            ''' normalize modifier list of the node and record changes if there is any change
            ''' </summary>
            Private Function NormalizeModifiers(Of T As SyntaxNode)(originalNode As T, node As SyntaxNode, modifiersGetter As Func(Of T, SyntaxTokenList), withModifiers As Func(Of T, SyntaxTokenList, T)) As T
                Return NormalizeModifiers(originalNode, DirectCast(node, T), modifiersGetter, withModifiers)
            End Function

            ''' <summary>
            ''' normalize modifier list of the node and record changes if there is any change
            ''' </summary>
            Private Function NormalizeModifiers(Of T As SyntaxNode)(originalNode As T, node As T, modifiersGetter As Func(Of T, SyntaxTokenList), withModifiers As Func(Of T, SyntaxTokenList, T)) As T
                Dim modifiers = modifiersGetter(node)

                ' if number of modifiers are less than 1, we don't need to do anything
                If modifiers.Count <= 1 Then
                    Return node
                End If

                ' whole node must be under span, otherwise, we will just return
                Dim span = originalNode.Span
                If Not _spans.GetContainingIntervals(span.Start, span.Length).Any() Then
                    Return node
                End If

                ' try normalize modifier list
                Dim newNode = withModifiers(node, ReorderModifiers(modifiers))

                ' new modifier list
                Dim newModifiers = modifiersGetter(newNode)

                ' check whether we need to remove "Dim" keyword or not
                If newModifiers.Any(Function(m) RemoveDimKeywordSet.Contains(m.VisualBasicKind)) Then
                    newNode = RemoveDimKeyword(newNode, modifiersGetter)
                End If

                ' no change
                If newNode Is node Then
                    Return node
                End If

                ' add text change
                Dim originalModifiers = modifiersGetter(originalNode)
                Contract.ThrowIfFalse(originalModifiers.Count > 0)

                Return newNode
            End Function

            ''' <summary>
            ''' remove "Dim" keyword if present
            ''' </summary>
            Private Function RemoveDimKeyword(Of T As SyntaxNode)(node As T, modifiersGetter As Func(Of T, SyntaxTokenList)) As T
                Return RemoveModifierKeyword(node, modifiersGetter, SyntaxKind.DimKeyword)
            End Function

            ''' <summary>
            ''' remove ByVal keyword from parameter list
            ''' </summary>
            Private Function RemoveByValKeyword(node As ParameterListSyntax, parameterIndex As Integer) As ParameterListSyntax
                Return RemoveModifierKeyword(node, Function(n) n.Parameters(parameterIndex).Modifiers, SyntaxKind.ByValKeyword)
            End Function

            ''' <summary>
            ''' remove a modifier from the given node
            ''' </summary>
            Private Function RemoveModifierKeyword(Of T As SyntaxNode)(node As T, modifiersGetter As Func(Of T, SyntaxTokenList), modifierKind As SyntaxKind) As T
                Dim modifiers = modifiersGetter(node)

                ' "Dim" doesn't exist
                Dim modifier = modifiers.FirstOrDefault(Function(m) m.VisualBasicKind = modifierKind)
                If modifier.VisualBasicKind = SyntaxKind.None Then
                    Return node
                End If

                ' merge trivia belong to the modifier to be deleted
                Dim trivia = modifier.LeadingTrivia.Concat(modifier.TrailingTrivia)

                ' we have node which owns tokens around modifiers. just replace tokens in the node in case we need to
                ' touch tokens outside of the modifier list
                Dim previousToken = modifier.GetPreviousToken(includeZeroWidth:=True)
                Dim newPreviousToken = previousToken.WithAppendedTrailingTrivia(trivia)

                ' replace previous token and remove "Dim"
                Return node.ReplaceTokens(SpecializedCollections.SingletonEnumerable(modifier).Concat(previousToken),
                                   Function(o, n)
                                       If o = modifier Then
                                           Return Nothing
                                       ElseIf o = previousToken Then
                                           Return newPreviousToken
                                       End If

                                       Return Contract.FailWithReturn(Of SyntaxToken)("shouldn't reach here")
                                   End Function)
            End Function

            ''' <summary>
            ''' check whether given modifiers are in right order (in sync with ModifierKindsInOrder list)
            ''' </summary>
            Private Function IsModifiersInRightOrder(modifiers As SyntaxTokenList) As Boolean
                Dim startIndex = 0
                For Each modifier In modifiers
                    Dim newIndex = ModifierKindsInOrder.IndexOf(modifier.VisualBasicKind, startIndex)
                    If newIndex = 0 AndAlso startIndex = 0 Then
                        ' very first search with matching the very first modifier in the modifier orders
                        startIndex = newIndex + 1
                    ElseIf startIndex < newIndex Then
                        ' new one is after the previous one in order
                        startIndex = newIndex + 1
                    Else
                        ' oops, in wrong order
                        Return False
                    End If
                Next

                Return True
            End Function
        End Class
    End Class
End Namespace
