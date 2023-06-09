﻿' Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports System.Threading
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax

Namespace Microsoft.CodeAnalysis.CodeCleanup.Providers
#If MEF Then
    <ExportCodeCleanupProvider(PredefinedCodeCleanupProviderNames.AddMissingTokens, LanguageNames.VisualBasic)>
    <ExtensionOrder(After:=PredefinedCodeCleanupProviderNames.CaseCorrection, Before:=PredefinedCodeCleanupProviderNames.Format)>
    Friend Class AddMissingTokensCodeCleanupProvider
#Else
    Friend Class AddMissingTokensCodeCleanupProvider
#End If
        Inherits AbstractTokensCodeCleanupProvider

        Public Overrides ReadOnly Property Name As String
            Get
                Return PredefinedCodeCleanupProviderNames.AddMissingTokens
            End Get
        End Property

        Protected Overrides Function GetRewriter(document As Document, root As SyntaxNode, spans As IEnumerable(Of TextSpan), workspace As Workspace, cancellationToken As CancellationToken) As AbstractTokensCodeCleanupProvider.Rewriter
            Return New AddMissingTokensRewriter(document, spans, cancellationToken)
        End Function

        Private Class AddMissingTokensRewriter
            Inherits AbstractTokensCodeCleanupProvider.Rewriter

            Private ReadOnly document As Document
            Private ReadOnly modifiedSpan As TextSpan

            Private model As SemanticModel = Nothing

            Public Sub New(document As Document, spans As IEnumerable(Of TextSpan), cancellationToken As CancellationToken)
                MyBase.New(spans, cancellationToken)

                Me.document = document
                Me.modifiedSpan = spans.Collapse()
            End Sub

            Private ReadOnly Property SemanticModel As SemanticModel
                Get
                    If document Is Nothing Then
                        Return Nothing
                    End If

                    If model Is Nothing Then
                        ' don't want to create semantic model when it is not needed. so get it synchronously when needed
                        ' most of cases, this will run on UI thread, so it shouldn't matter
                        model = document.GetSemanticModelForSpanAsync(modifiedSpan, Me._cancellationToken).WaitAndGetResult(Me._cancellationToken)
                    End If

                    Contract.Requires(model IsNot Nothing)
                    Return model
                End Get
            End Property

            Public Overrides Function Visit(node As SyntaxNode) As SyntaxNode
                If TypeOf node Is ExpressionSyntax Then
                    Return VisitExpression(DirectCast(node, ExpressionSyntax))
                Else
                    Return MyBase.Visit(node)
                End If
            End Function

            Private Function VisitExpression(node As ExpressionSyntax) As SyntaxNode
                If Not ShouldRewrite(node) Then
                    Return node
                End If

                Return AddParenthesesTransform(node, MyBase.Visit(node),
                                                 Function()
                                                     ' we only care whole name not part of dotted names
                                                     Dim name As NameSyntax = TryCast(node, NameSyntax)
                                                     If name Is Nothing OrElse TypeOf name.Parent Is NameSyntax Then
                                                         Return False
                                                     End If

                                                     Return CheckName(name)
                                                 End Function,
                                                 Function(n) DirectCast(n, InvocationExpressionSyntax).ArgumentList,
                                                 Function(n) SyntaxFactory.InvocationExpression(n, SyntaxFactory.ArgumentList()),
                                                 Function(n) IsMethodSymbol(DirectCast(n, ExpressionSyntax)))
            End Function

            Private Function CheckName(name As NameSyntax) As Boolean
                If _underStructuredTrivia OrElse name.IsStructuredTrivia() OrElse name.IsMissing Then
                    Return False
                End If

                ' can't/don't try to transform member access to invocation
                If TypeOf name.Parent Is MemberAccessExpressionSyntax OrElse
                   name.CheckParent(Of AttributeSyntax)(Function(p) p.Name Is name) OrElse
                   name.CheckParent(Of ImplementsClauseSyntax)(Function(p) p.InterfaceMembers.Any(Function(i) i Is name)) OrElse
                   name.CheckParent(Of UnaryExpressionSyntax)(Function(p) p.VisualBasicKind = SyntaxKind.AddressOfExpression AndAlso p.Operand Is name) OrElse
                   name.CheckParent(Of InvocationExpressionSyntax)(Function(p) p.Expression Is name) OrElse
                   name.CheckParent(Of NamedFieldInitializerSyntax)(Function(p) p.Name Is name) OrElse
                   name.CheckParent(Of ImplementsStatementSyntax)(Function(p) p.Types.Any(Function(t) t Is name)) OrElse
                   name.CheckParent(Of HandlesClauseItemSyntax)(Function(p) p.EventMember Is name) OrElse
                   name.CheckParent(Of ObjectCreationExpressionSyntax)(Function(p) p.Type Is name) OrElse
                   name.CheckParent(Of ArrayCreationExpressionSyntax)(Function(p) p.Type Is name) OrElse
                   name.CheckParent(Of ArrayTypeSyntax)(Function(p) p.ElementType Is name) OrElse
                   name.CheckParent(Of SimpleAsClauseSyntax)(Function(p) p.Type Is name) OrElse
                   name.CheckParent(Of TypeConstraintSyntax)(Function(p) p.Type Is name) OrElse
                   name.CheckParent(Of GetTypeExpressionSyntax)(Function(p) p.Type Is name) OrElse
                   name.CheckParent(Of TypeOfExpressionSyntax)(Function(p) p.Type Is name) OrElse
                   name.CheckParent(Of CastExpressionSyntax)(Function(p) p.Type Is name) OrElse
                   name.CheckParent(Of ForEachStatementSyntax)(Function(p) p.ControlVariable Is name) OrElse
                   name.CheckParent(Of ForStatementSyntax)(Function(p) p.ControlVariable Is name) OrElse
                   name.CheckParent(Of AssignmentStatementSyntax)(Function(p) p.Left Is name) OrElse
                   name.CheckParent(Of TypeArgumentListSyntax)(Function(p) p.Arguments.Any(Function(i) i Is name)) OrElse
                   name.CheckParent(Of NamedArgumentSyntax)(Function(p) p.IdentifierName Is name) OrElse
                   name.CheckParent(Of CastExpressionSyntax)(Function(p) p.Type Is name) OrElse
                   name.CheckParent(Of SimpleArgumentSyntax)(Function(p) p.Expression Is name) Then
                    Return False
                End If

                Return True
            End Function

            Private Function IsMethodSymbol(expression As ExpressionSyntax) As Boolean
                If Me.SemanticModel Is Nothing Then
                    Return False
                End If

                Dim symbols = Me.SemanticModel.GetSymbolInfo(expression, _cancellationToken).GetAllSymbols()
                Return symbols.Any() AndAlso symbols.All(Function(s) s.TypeSwitch(Function(m As IMethodSymbol) m.MethodKind = MethodKind.Ordinary))
            End Function

            Private Function IsDelegateType(expression As ExpressionSyntax) As Boolean
                If Me.SemanticModel Is Nothing Then
                    Return False
                End If

                Dim type = Me.SemanticModel.GetTypeInfo(expression, _cancellationToken).Type
                Return type.IsDelegateType
            End Function

            Public Overrides Function VisitInvocationExpression(node As InvocationExpressionSyntax) As SyntaxNode
                Dim newNode = MyBase.VisitInvocationExpression(node)

                ' make sure we are not under structured triva
                If _underStructuredTrivia Then
                    Return newNode
                End If

                If Not TypeOf node.Expression Is NameSyntax AndAlso
                   Not TypeOf node.Expression Is ParenthesizedExpressionSyntax AndAlso
                   Not TypeOf node.Expression Is MemberAccessExpressionSyntax Then
                    Return newNode
                End If

                Dim semanticChecker As Func(Of InvocationExpressionSyntax, Boolean) =
                    Function(n) IsMethodSymbol(n.Expression) OrElse IsDelegateType(n.Expression)

                Return AddParenthesesTransform(
                        node, newNode, Function(n) n.Expression.Span.Length > 0, Function(n) n.ArgumentList, Function(n) n.WithArgumentList(SyntaxFactory.ArgumentList()), semanticChecker)
            End Function

            Public Overrides Function VisitObjectCreationExpression(node As ObjectCreationExpressionSyntax) As SyntaxNode
                Dim newNode = MyBase.VisitObjectCreationExpression(node)

                If node.CheckParent(Of AsNewClauseSyntax)(Function(p) p.NewExpression Is node) Then
                    Return newNode
                End If

                If node.Type Is Nothing OrElse
                   node.Type.TypeSwitch(Function(n As GenericNameSyntax)
                                            Return n.TypeArgumentList Is Nothing OrElse
                                                   n.TypeArgumentList.CloseParenToken.IsMissing OrElse
                                                   n.TypeArgumentList.CloseParenToken.VisualBasicKind = SyntaxKind.None
                                        End Function) Then
                    Return newNode
                End If

                ' we have two different bugs - bug # 12388 and bug # 12588 - that want two distinct and contradicting behaviors for this case.
                ' for now, I will make it to follow dev11 behavior.
                ' commented out to stop auto inserting behavior
                ' Return AddParenthesesTransform(node, newNode, Function(n) n.ArgumentList, Function(n) n.WithArgumentList(Syntax.ArgumentList()))

                Return newNode
            End Function

            Public Overrides Function VisitRaiseEventStatement(node As RaiseEventStatementSyntax) As SyntaxNode
                Return AddParenthesesTransform(
                    node, MyBase.VisitRaiseEventStatement(node), Function(n) Not n.Name.IsMissing, Function(n) n.ArgumentList, Function(n) n.WithArgumentList(SyntaxFactory.ArgumentList()))
            End Function

            Public Overrides Function VisitMethodStatement(node As MethodStatementSyntax) As SyntaxNode
                Dim rewrittenMethod = DirectCast(AddParameterListTransform(node, MyBase.VisitMethodStatement(node), Function(n) Not n.Identifier.IsMissing), MethodStatementSyntax)
                Return AsyncOrIteratorFunctionReturnTypeFixer.RewriteMethodStatement(rewrittenMethod, Me.SemanticModel, Me._cancellationToken, node)
            End Function

            Public Overrides Function VisitSubNewStatement(node As SubNewStatementSyntax) As SyntaxNode
                Return AddParameterListTransform(node, MyBase.VisitSubNewStatement(node), Function(n) Not n.NewKeyword.IsMissing)
            End Function

            Public Overrides Function VisitDeclareStatement(node As DeclareStatementSyntax) As SyntaxNode
                Return AddParameterListTransform(node, MyBase.VisitDeclareStatement(node), Function(n) Not n.Identifier.IsMissing)
            End Function

            Public Overrides Function VisitDelegateStatement(node As DelegateStatementSyntax) As SyntaxNode
                Return AddParameterListTransform(node, MyBase.VisitDelegateStatement(node), Function(n) Not n.Identifier.IsMissing)
            End Function

            Public Overrides Function VisitEventStatement(node As EventStatementSyntax) As SyntaxNode
                If node.AsClause IsNot Nothing Then
                    Return MyBase.VisitEventStatement(node)
                End If

                Return AddParameterListTransform(node, MyBase.VisitEventStatement(node), Function(n) Not n.Identifier.IsMissing)
            End Function

            Public Overrides Function VisitAccessorStatement(node As AccessorStatementSyntax) As SyntaxNode
                Dim newNode = MyBase.VisitAccessorStatement(node)
                If node.Keyword.VisualBasicKind <> SyntaxKind.AddHandlerKeyword AndAlso
                   node.Keyword.VisualBasicKind <> SyntaxKind.RemoveHandlerKeyword AndAlso
                   node.Keyword.VisualBasicKind <> SyntaxKind.RaiseEventKeyword Then
                    Return newNode
                End If

                Return AddParameterListTransform(node, newNode, Function(n) Not n.Keyword.IsMissing)
            End Function

            Public Overrides Function VisitAttribute(node As AttributeSyntax) As SyntaxNode
                ' we decide not to auto insert parenthese for attribute
                Return MyBase.VisitAttribute(node)
            End Function

            Public Overrides Function VisitOperatorStatement(node As OperatorStatementSyntax) As SyntaxNode
                ' don't auto insert parentheses
                ' these methods are okay to be removed. but it is here to show other cases where parse tree node can have parentheses
                Return MyBase.VisitOperatorStatement(node)
            End Function

            Public Overrides Function VisitPropertyStatement(node As PropertyStatementSyntax) As SyntaxNode
                ' don't auto insert parentheses
                ' these methods are okay to be removed. but it is here to show other cases where parse tree node can have parentheses
                Return MyBase.VisitPropertyStatement(node)
            End Function

            Public Overrides Function VisitLambdaHeader(node As LambdaHeaderSyntax) As SyntaxNode
                Dim rewrittenLambdaHeader = DirectCast(MyBase.VisitLambdaHeader(node), LambdaHeaderSyntax)
                rewrittenLambdaHeader = AsyncOrIteratorFunctionReturnTypeFixer.RewriteLambdaHeader(rewrittenLambdaHeader, Me.SemanticModel, Me._cancellationToken, node)
                Return AddParameterListTransform(node, rewrittenLambdaHeader, Function(n) True)
            End Function

            Private Function TryFixupTrivia(Of T As SyntaxNode)(node As T, previousToken As SyntaxToken, lastToken As SyntaxToken, ByRef newNode As T) As Boolean
                ' initialize to initial value
                newNode = Nothing

                ' hold onto the trivia
                Dim prevTrailingTrivia = previousToken.TrailingTrivia

                ' if previous token is not part of node and if it has any trivia, don't do anything
                If Not node.DescendantTokens().Any(Function(token) token = previousToken) AndAlso prevTrailingTrivia.Count > 0 Then
                    Return False
                End If

                ' remove the trivia from the token
                Dim previousTokenWithoutTrailingTrivia = previousToken.WithTrailingTrivia(SyntaxFactory.ElasticMarker)

                ' If previousToken has trailing WhitespaceTrivia, strip off the trailing WhitespaceTrivia from the lastToken.
                Dim lastTrailingTrivia = lastToken.TrailingTrivia
                If prevTrailingTrivia.Any(SyntaxKind.WhitespaceTrivia) Then
                    lastTrailingTrivia = lastTrailingTrivia.WithoutLeadingWhitespace()
                End If

                ' get the trivia and attach it to the last token
                Dim lastTokenWithTrailingTrivia = lastToken.WithTrailingTrivia(prevTrailingTrivia.Concat(lastTrailingTrivia))

                ' replace tokens
                newNode = node.ReplaceTokens(SpecializedCollections.SingletonEnumerable(previousToken).Concat(lastToken),
                                              Function(o, m)
                                                  If o = previousToken Then
                                                      Return previousTokenWithoutTrailingTrivia
                                                  ElseIf o = lastToken Then
                                                      Return lastTokenWithTrailingTrivia
                                                  End If

                                                  Return Contract.FailWithReturn(Of SyntaxToken)("Shouldn't reach here")
                                              End Function)

                Return True
            End Function

            Private Function AddParameterListTransform(Of T As MethodBaseSyntax)(node As T, newNode As SyntaxNode, nameChecker As Func(Of T, Boolean)) As T
                Dim transform As Func(Of T, T) = Function(n As T)
                                                     Dim newParamList = SyntaxFactory.ParameterList()
                                                     If n.ParameterList IsNot Nothing Then
                                                         If n.ParameterList.HasLeadingTrivia Then
                                                             newParamList = newParamList.WithLeadingTrivia(n.ParameterList.GetLeadingTrivia)
                                                         End If
                                                         If n.ParameterList.HasTrailingTrivia Then
                                                             newParamList = newParamList.WithTrailingTrivia(n.ParameterList.GetTrailingTrivia)
                                                         End If
                                                     End If

                                                     Dim nodeWithParams = DirectCast(n.WithParameterList(newParamList), T)
                                                     If n.HasTrailingTrivia AndAlso nodeWithParams.GetLastToken() = nodeWithParams.ParameterList.CloseParenToken Then
                                                         Dim trailing = n.GetTrailingTrivia
                                                         nodeWithParams = DirectCast(n _
                                                             .WithTrailingTrivia() _
                                                             .WithParameterList(newParamList) _
                                                             .WithTrailingTrivia(trailing), T)
                                                     End If

                                                     Return nodeWithParams
                                                 End Function
                Return AddParenthesesTransform(node, newNode, nameChecker, Function(n) n.ParameterList, transform)
            End Function

            Private Function AddParenthesesTransform(Of T As SyntaxNode)(
                originalNode As T,
                node As SyntaxNode,
                nameChecker As Func(Of T, Boolean),
                listGetter As Func(Of T, SyntaxNode),
                withTransform As Func(Of T, T),
                Optional semanticPredicate As Func(Of T, Boolean) = Nothing
            ) As T
                Dim newNode = DirectCast(node, T)
                If Not nameChecker(newNode) Then
                    Return newNode
                End If

                Dim syntaxPredicate As Func(Of Boolean) = Function()
                                                              Dim list = listGetter(originalNode)
                                                              If list Is Nothing Then
                                                                  Return True
                                                              End If

                                                              Dim paramList = TryCast(list, ParameterListSyntax)
                                                              If paramList IsNot Nothing Then
                                                                  Return paramList.Parameters = Nothing AndAlso
                                                                         paramList.OpenParenToken.IsMissing AndAlso
                                                                         paramList.CloseParenToken.IsMissing
                                                              End If

                                                              Dim argsList = TryCast(list, ArgumentListSyntax)
                                                              Return argsList IsNot Nothing AndAlso
                                                                     argsList.Arguments = Nothing AndAlso
                                                                     argsList.OpenParenToken.IsMissing AndAlso
                                                                     argsList.CloseParenToken.IsMissing
                                                          End Function

                Return AddParenthesesTransform(originalNode, node, syntaxPredicate, listGetter, withTransform, semanticPredicate)
            End Function

            Private Function AddParenthesesTransform(Of T As SyntaxNode)(
                originalNode As T,
                node As SyntaxNode,
                syntaxPredicate As Func(Of Boolean),
                listGetter As Func(Of T, SyntaxNode),
                transform As Func(Of T, T),
                Optional semanticPredicate As Func(Of T, Boolean) = Nothing
            ) As T
                Dim span = originalNode.Span

                If syntaxPredicate() AndAlso
                   _spans.GetContainingIntervals(span.Start, span.Length).Any() AndAlso
                   CheckSkippedTriviaForMissingToken(originalNode, SyntaxKind.OpenParenToken, SyntaxKind.CloseParenToken) Then

                    Dim transformedNode = transform(DirectCast(node, T))

                    ' previous token can be different per different node types. 
                    ' it could be name or close paren of type parameter list and etc. also can be different based on
                    ' what token is omitted
                    ' get one that actually exist and get trailing trivia of that token
                    Dim fixedUpNode As T = Nothing

                    Dim list = listGetter(transformedNode)
                    Dim previousToken = list.GetFirstToken(includeZeroWidth:=True).GetPreviousToken(includeZeroWidth:=True)
                    Dim lastToken = list.GetLastToken(includeZeroWidth:=True)

                    If Not TryFixupTrivia(transformedNode, previousToken, lastToken, fixedUpNode) Then
                        Return DirectCast(node, T)
                    End If

                    ' semanticPredicate is invoked at the last step as it is the most expensive operation which requires building the compilation for semantic validations.
                    If semanticPredicate Is Nothing OrElse semanticPredicate(originalNode) Then
                        Return DirectCast(fixedUpNode, T)
                    End If
                End If

                Return DirectCast(node, T)
            End Function

            Private Function CheckSkippedTriviaForMissingToken(node As SyntaxNode, ParamArray kinds As SyntaxKind()) As Boolean
                Dim lastToken = node.GetLastToken(includeZeroWidth:=True)
                If lastToken.TrailingTrivia.Count = 0 Then
                    Return True
                End If

                Return Not lastToken _
                           .TrailingTrivia _
                           .Where(Function(t) t.VisualBasicKind = SyntaxKind.SkippedTokensTrivia) _
                           .SelectMany(Function(t) DirectCast(t.GetStructure(), SkippedTokensTriviaSyntax).Tokens) _
                           .Any(Function(t) kinds.Contains(t.VisualBasicKind))
            End Function

            Private Function TryGetStringLiteralText(token As SyntaxToken, ByRef literalText As String) As Boolean
                ' define local const value
                Const QuotationCharacter As Char = """"c

                ' get actual text in the buffer and check whether it is quotation
                Dim actualText = token.ToString()
                Dim firstCharacter = actualText.FirstOrDefault()

                ' there must be actual quotation before text (it might not if the token is generated)
                If firstCharacter = Nothing Or firstCharacter <> QuotationCharacter Then
                    Return False
                End If

                ' now check special cases
                ' contains only the first quotation
                If actualText.Length = 1 Then
                    literalText = """"""
                    Return True
                End If

                If actualText.Length = 2 Then
                    If actualText(actualText.Length - 1) = QuotationCharacter Then
                        ' already in good shape
                        Return False
                    Else
                        ' missing closing quotation
                        literalText = actualText + QuotationCharacter
                        Return True
                    End If
                End If

                ' now, normal case
                ' backward count number of quotation
                Dim quotationCount = 0
                For i = actualText.Length - 1 To 1 Step -1
                    If actualText(i) = QuotationCharacter Then
                        quotationCount += 1
                    Else
                        Exit For
                    End If
                Next

                ' if quotation is even number, then we need closing quotation
                If quotationCount Mod 2 = 0 Then
                    literalText = actualText + QuotationCharacter
                    Return True
                End If

                ' already in good shape
                Return False
            End Function

            Public Overrides Function VisitIfStatement(node As IfStatementSyntax) As SyntaxNode
                Return AddMissingOrOmittedTokenTransform(node, MyBase.VisitIfStatement(node), Function(n) n.ThenKeyword, SyntaxKind.ThenKeyword)
            End Function

            Public Overrides Function VisitIfDirectiveTrivia(node As IfDirectiveTriviaSyntax) As SyntaxNode
                Return AddMissingOrOmittedTokenTransform(node, MyBase.VisitIfDirectiveTrivia(node), Function(n) n.ThenKeyword, SyntaxKind.ThenKeyword)
            End Function

            Public Overrides Function VisitTypeArgumentList(node As TypeArgumentListSyntax) As SyntaxNode
                Return AddMissingOrOmittedTokenTransform(node, MyBase.VisitTypeArgumentList(node), Function(n) n.OfKeyword, SyntaxKind.OfKeyword)
            End Function

            Public Overrides Function VisitTypeParameterList(node As TypeParameterListSyntax) As SyntaxNode
                Return AddMissingOrOmittedTokenTransform(node, MyBase.VisitTypeParameterList(node), Function(n) n.OfKeyword, SyntaxKind.OfKeyword)
            End Function

            Public Overrides Function VisitContinueStatement(node As ContinueStatementSyntax) As SyntaxNode
                Return AddMissingOrOmittedTokenTransform(node, MyBase.VisitContinueStatement(node), Function(n) n.BlockKeyword, SyntaxKind.DoKeyword, SyntaxKind.ForKeyword, SyntaxKind.WhileKeyword)
            End Function

            Public Overrides Function VisitSelectStatement(node As SelectStatementSyntax) As SyntaxNode
                Dim newNode = DirectCast(MyBase.VisitSelectStatement(node), SelectStatementSyntax)
                Return If(newNode.CaseKeyword.VisualBasicKind = SyntaxKind.None,
                           newNode.WithCaseKeyword(SyntaxFactory.Token(SyntaxKind.CaseKeyword)),
                           newNode)
            End Function

            Public Overrides Function VisitToken(originalToken As SyntaxToken) As SyntaxToken
                ' we need to override VisitToken for string literal token since things like "Region"
                ' uses StringLiteralToken for its title rather than LiteralExpressionSyntax
                ' otherwise we don't use VisitToken directly to insert or replace missing/omitted tokens
                ' since we would need to touch outside of token boundary for re-attaching trivia
                Dim token = MyBase.VisitToken(originalToken)
                If token.VisualBasicKind <> SyntaxKind.StringLiteralToken Then
                    Return token
                End If

                Dim span = originalToken.Span
                If Not _spans.GetContainingIntervals(span.Start, span.Length).Any() Then
                    Return token
                End If

                Dim literalText As String = Nothing
                Dim stringLiteralToken = token
                If Not TryGetStringLiteralText(stringLiteralToken, literalText) Then
                    Return token
                End If

                ' create new token with good pair of quotation
                Return SyntaxFactory.StringLiteralToken(stringLiteralToken.LeadingTrivia, literalText, stringLiteralToken.ValueText, stringLiteralToken.TrailingTrivia)
            End Function

            Private Function AddMissingOrOmittedTokenTransform(Of T As SyntaxNode)(
                originalNode As T, node As SyntaxNode, tokenGetter As Func(Of T, SyntaxToken), ParamArray kinds As SyntaxKind()) As T

                Dim newNode = DirectCast(node, T)
                If Not CheckSkippedTriviaForMissingToken(originalNode, kinds) Then
                    Return newNode
                End If

                Dim newToken = tokenGetter(newNode)
                Dim processedToken = ProcessToken(tokenGetter(originalNode), newToken, newNode)
                If processedToken <> newToken Then
                    Dim replacedNode = ReplaceOrSetToken(newNode, newToken, processedToken)

                    Dim replacedToken = tokenGetter(replacedNode)
                    Dim previousToken = replacedToken.GetPreviousToken(includeZeroWidth:=True)

                    Dim fixedupNode As T = Nothing
                    If Not TryFixupTrivia(replacedNode, previousToken, replacedToken, fixedupNode) Then
                        Return newNode
                    End If

                    Return fixedupNode
                End If

                Return newNode
            End Function

            Private Function ProcessToken(originalToken As SyntaxToken, token As SyntaxToken, parent As SyntaxNode) As SyntaxToken
                ' special case omitted token case
                If IsOmitted(originalToken) Then
                    Return ProcessOmittedToken(originalToken, token, parent)
                End If

                Dim span = originalToken.Span
                If Not _spans.GetContainingIntervals(span.Start, span.Length).Any() Then
                    ' token is outside of the provided span
                    Return token
                End If

                ' token is not missing or if missing token is identifier there is not much we can do
                If Not originalToken.IsMissing OrElse
                   originalToken.VisualBasicKind = SyntaxKind.None OrElse
                   originalToken.VisualBasicKind = SyntaxKind.IdentifierToken Then
                    Return token
                End If

                Return ProcessMissingToken(originalToken, token)
            End Function

            Private Function ReplaceOrSetToken(Of T As SyntaxNode)(originalParent As T, tokenToFix As SyntaxToken, replacementToken As SyntaxToken) As T
                If Not IsOmitted(tokenToFix) Then
                    Return originalParent.ReplaceToken(tokenToFix, replacementToken)
                Else
                    Return DirectCast(SetOmittedToken(originalParent, replacementToken), T)
                End If
            End Function

            Private Function SetOmittedToken(originalParent As SyntaxNode, newToken As SyntaxToken) As SyntaxNode
                Select Case newToken.VisualBasicKind
                    Case SyntaxKind.ThenKeyword

                        ' this can be regular If or an If directive
                        Dim regularIf = TryCast(originalParent, IfStatementSyntax)
                        If regularIf IsNot Nothing Then
                            Dim previousToken = regularIf.Condition.GetLastToken(includeZeroWidth:=True)
                            Dim nextToken = regularIf.GetLastToken.GetNextToken

                            If Not InvalidOmittedToken(previousToken, nextToken) Then
                                Return regularIf.WithThenKeyword(newToken)
                            End If

                        Else
                            Dim ifDirective = TryCast(originalParent, IfDirectiveTriviaSyntax)
                            If ifDirective IsNot Nothing Then
                                Dim previousToken = ifDirective.Condition.GetLastToken(includeZeroWidth:=True)
                                Dim nextToken = ifDirective.GetLastToken.GetNextToken

                                If Not InvalidOmittedToken(previousToken, nextToken) Then
                                    Return ifDirective.WithThenKeyword(newToken)
                                End If
                            End If

                        End If
                End Select

                Return originalParent
            End Function


            Private Function IsOmitted(token As SyntaxToken) As Boolean
                Return token.VisualBasicKind = SyntaxKind.None
            End Function

            Private Function ProcessOmittedToken(originalToken As SyntaxToken, token As SyntaxToken, parent As SyntaxNode) As SyntaxToken
                ' multiline if statement with missing then keyword case
                If parent.TypeSwitch(Function(p As IfStatementSyntax) Exist(p.Condition) AndAlso p.ThenKeyword = originalToken) Then
                    Return If(parent.GetAncestor(Of MultiLineIfBlockSyntax)() IsNot Nothing, CreateOmittedToken(token, SyntaxKind.ThenKeyword), token)
                ElseIf parent.TypeSwitch(Function(p As IfDirectiveTriviaSyntax) p.ThenKeyword = originalToken) Then
                    Return CreateOmittedToken(token, SyntaxKind.ThenKeyword)
                End If

                Return token
            End Function

            Private Function InvalidOmittedToken(previousToken As SyntaxToken, nextToken As SyntaxToken) As Boolean
                ' if previous token has a problem, don't bother
                If previousToken.IsMissing OrElse previousToken.IsSkipped OrElse previousToken.VisualBasicKind = 0 Then
                    Return True
                End If

                ' if next token has a problem, do little bit more check
                ' if there is no next token, it is okay to insert the missing token
                If nextToken.VisualBasicKind = 0 Then
                    Return False
                End If

                ' if next token is missing or skipped, check whether it has EOL
                If nextToken.IsMissing OrElse nextToken.IsSkipped Then
                    Return Not previousToken.TrailingTrivia.Any(SyntaxKind.EndOfLineTrivia) And
                           Not nextToken.LeadingTrivia.Any(SyntaxKind.EndOfLineTrivia)
                End If

                Return False
            End Function

            Private Function GetPreviousAndNextToken(token As SyntaxToken) As ValueTuple(Of SyntaxToken, SyntaxToken)
                ' we need this special method because we can't use reguler previous/next token on the omitted token since
                ' omitted token logically doesnt exist in the tree
                Debug.Assert(token.Span.IsEmpty)
                Dim node = token.GetAncestors(Of SyntaxNode).FirstOrDefault(Function(n) n.FullSpan.IntersectsWith(token.Span))
                If node Is Nothing Then
                    Return ValueTuple.Create(Of SyntaxToken, SyntaxToken)(Nothing, Nothing)
                End If

                Dim previousToken = token
                Dim nextToken = token
                For Each current In node.DescendantTokens()
                    If token = current Then
                        Continue For
                    End If

                    If token.Span.End <= current.SpanStart Then
                        nextToken = current
                        Exit For
                    End If

                    If current.Span.End <= token.SpanStart Then
                        previousToken = current
                    End If
                Next

                previousToken = If(previousToken.VisualBasicKind = 0, node.GetFirstToken(includeZeroWidth:=True).GetPreviousToken(includeZeroWidth:=True), previousToken)
                nextToken = If(nextToken.VisualBasicKind = 0, node.GetLastToken(includeZeroWidth:=True).GetNextToken(includeZeroWidth:=True), nextToken)

                Return ValueTuple.Create(previousToken, nextToken)
            End Function

            Private Function Exist(node As SyntaxNode) As Boolean
                Return node IsNot Nothing AndAlso node.Span.Length > 0
            End Function

            Private Function ProcessMissingToken(originalToken As SyntaxToken, token As SyntaxToken) As SyntaxToken
                ' auto insert missing "Of" keyword in type argument list
                If originalToken.Parent.TypeSwitch(Function(p As TypeArgumentListSyntax) p.OfKeyword = originalToken) Then
                    Return CreateMissingToken(token)
                ElseIf originalToken.Parent.TypeSwitch(Function(p As TypeParameterListSyntax) p.OfKeyword = originalToken) Then
                    Return CreateMissingToken(token)
                ElseIf originalToken.Parent.TypeSwitch(Function(p As ContinueStatementSyntax) p.BlockKeyword = originalToken) Then
                    Return CreateMissingToken(token)
                End If

                Return token
            End Function

            Private Function CreateMissingToken(token As SyntaxToken) As SyntaxToken
                Return CreateToken(token, token.VisualBasicKind)
            End Function

            Private Function CreateOmittedToken(token As SyntaxToken, kind As SyntaxKind) As SyntaxToken
                Return CreateToken(token, kind)
            End Function
        End Class
    End Class
End Namespace
