﻿' Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Threading
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Formatting
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Microsoft.CodeAnalysis.VisualBasic.Extensions
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax
Imports Microsoft.VisualBasic

Namespace Microsoft.CodeAnalysis.VisualBasic.Formatting
    Partial Friend Class VisualBasicTriviaFormatter
        Inherits AbstractTriviaFormatter(Of SyntaxTrivia)

        Dim lineContinuationTrivia As SyntaxTrivia = SyntaxFactory.LineContinuationTrivia("_")

        Private _succeeded As Boolean = True

        Public Sub New(context As FormattingContext,
                       formattingRules As ChainedFormattingRules,
                       token1 As SyntaxToken,
                       token2 As SyntaxToken,
                       originalString As String,
                       lineBreaks As Integer,
                       spaces As Integer)
            MyBase.New(context, formattingRules, token1, token2, originalString, lineBreaks, spaces)
        End Sub

        Protected Overrides Function Succeeded() As Boolean
            Return _succeeded
        End Function

        Protected Overrides Function IsWhitespace(trivia As SyntaxTrivia) As Boolean
            Return trivia.RawKind = SyntaxKind.WhitespaceTrivia
        End Function

        Protected Overrides Function IsEndOfLine(trivia As SyntaxTrivia) As Boolean
            Return trivia.RawKind = SyntaxKind.EndOfLineTrivia
        End Function

        Protected Overrides Function IsNullOrWhitespace(text As String) As Boolean
            Return text.IsNullOrVisualBasicWhitespace()
        End Function

        Protected Overrides Function IsWhitespace(ch As Char) As Boolean
            Return Char.IsWhiteSpace(ch) OrElse SyntaxFacts.IsWhitespace(ch)
        End Function

        Protected Overrides Function IsNewLine(ch As Char) As Boolean
            Return ch = vbCr OrElse ch = vbLf OrElse SyntaxFacts.IsNewLine(ch)
        End Function

        Protected Overrides Function Convert(trivia As SyntaxTrivia) As SyntaxTrivia
            Return trivia
        End Function

        Protected Overrides Function CreateWhitespace(text As String) As SyntaxTrivia
            Return SyntaxFactory.Whitespace(text, elastic:=False)
        End Function

        Protected Overrides Function CreateEndOfLine() As SyntaxTrivia
            Return SyntaxFactory.CarriageReturnLineFeed
        End Function

        Protected Overrides Function GetLineColumnRuleBetween(trivia1 As SyntaxTrivia, existingWhitespaceBetween As LineColumnDelta, trivia2 As SyntaxTrivia) As LineColumnRule

            ' line continuation
            If trivia2.VisualBasicKind = SyntaxKind.LineContinuationTrivia Then
                Return LineColumnRule.ForceSpacesOrUseAbsoluteIndentation(spacesOrIndentation:=1)
            End If

            If IsStartOrEndOfFile(trivia1, trivia2) Then
                Return LineColumnRule.PreserveLinesWithAbsoluteIndentation(lines:=0, indentation:=0)
            End If

            ' :: case
            If trivia1.VisualBasicKind = SyntaxKind.ColonTrivia AndAlso
               trivia2.VisualBasicKind = SyntaxKind.ColonTrivia Then
                Return LineColumnRule.ForceSpacesOrUseDefaultIndentation(spaces:=0)
            End If

            ' : after : token
            If Token1.VisualBasicKind = SyntaxKind.ColonToken AndAlso trivia2.VisualBasicKind = SyntaxKind.ColonTrivia Then
                Return LineColumnRule.ForceSpacesOrUseDefaultIndentation(spaces:=0)
            End If

            ' : [token]
            If trivia1.VisualBasicKind = SyntaxKind.ColonTrivia AndAlso trivia2.VisualBasicKind = 0 AndAlso
               Token2.VisualBasicKind <> SyntaxKind.None AndAlso Token2.VisualBasicKind <> SyntaxKind.EndOfFileToken Then
                Return LineColumnRule.ForceSpacesOrUseDefaultIndentation(spaces:=1)
            End If

            If trivia1.VisualBasicKind = SyntaxKind.ColonTrivia OrElse
               trivia2.VisualBasicKind = SyntaxKind.ColonTrivia Then
                Return LineColumnRule.ForceSpacesOrUseDefaultIndentation(spaces:=1)
            End If

            ' [trivia] [whitespace] [token] case
            If trivia2.VisualBasicKind = SyntaxKind.None Then
                Dim insertNewLine = Me.FormattingRules.GetAdjustNewLinesOperation(Me.Token1, Me.Token2) IsNot Nothing

                If insertNewLine Then
                    Return LineColumnRule.PreserveLinesWithDefaultIndentation(lines:=0)
                End If

                Return LineColumnRule.PreserveLinesWithGivenIndentation(lines:=0)
            End If

            ' preprocessor case
            If SyntaxFacts.IsPreprocessorDirective(trivia2.VisualBasicKind) Then
                ' if this is the first line of the file, don't put extra line 1
                Dim firstLine = (trivia1.RawKind = SyntaxKind.None) AndAlso (Token1.VisualBasicKind = SyntaxKind.None)

                Dim lines = If(firstLine, 0, 1)
                Return LineColumnRule.PreserveLinesWithAbsoluteIndentation(lines, indentation:=0)
            End If

            ' comment case
            If trivia2.VisualBasicKind = SyntaxKind.CommentTrivia OrElse
               trivia2.VisualBasicKind = SyntaxKind.DocumentationCommentTrivia Then

                ' [token] [whitepsace] [trivia] case
                If Me.Token1.IsLastTokenOfStatementWithEndOfLine() AndAlso trivia1.VisualBasicKind = SyntaxKind.None Then
                    Return LineColumnRule.PreserveSpacesOrUseDefaultIndentation(spaces:=1)
                End If

                If trivia1.VisualBasicKind = SyntaxKind.LineContinuationTrivia Then
                    Return LineColumnRule.PreserveSpacesOrUseDefaultIndentation(spaces:=1)
                End If

                If Me.FormattingRules.GetAdjustNewLinesOperation(Me.Token1, Me.Token2) IsNot Nothing Then
                    Return LineColumnRule.PreserveLinesWithDefaultIndentation(lines:=0)
                End If

                Return LineColumnRule.PreserveLinesWithGivenIndentation(lines:=0)
            End If

            ' skipped tokens
            If trivia2.VisualBasicKind = SyntaxKind.SkippedTokensTrivia Then
                _succeeded = False
            End If

            Return LineColumnRule.Preserve()
        End Function

        Private Function IsStartOrEndOfFile(trivia1 As SyntaxTrivia, trivia2 As SyntaxTrivia) As Boolean
            Return (Token1.VisualBasicKind = 0 OrElse Token2.VisualBasicKind = 0) AndAlso (trivia1.VisualBasicKind = 0 OrElse trivia2.VisualBasicKind = 0)
        End Function

        Protected Overloads Overrides Function Format(lineColumn As LineColumn,
                                                      trivia As SyntaxTrivia,
                                                      changes As List(Of SyntaxTrivia),
                                                      cancellationToken As CancellationToken) As LineColumnDelta
            If trivia.HasStructure Then
                Return FormatStructuredTrivia(lineColumn, trivia, changes, cancellationToken)
            End If

            If trivia.VisualBasicKind = SyntaxKind.LineContinuationTrivia Then
                trivia = FormatLineContinuationTrivia(trivia)
            End If

            changes.Add(trivia)
            Return GetLineColumnDelta(lineColumn, trivia)
        End Function

        Protected Overloads Overrides Function Format(lineColumn As LineColumn,
                                                      trivia As SyntaxTrivia,
                                                      changes As List(Of TextChange),
                                                      cancellationToken As CancellationToken) As LineColumnDelta
            If trivia.HasStructure Then
                Return FormatStructuredTrivia(lineColumn, trivia, changes, cancellationToken)
            End If

            If trivia.VisualBasicKind = SyntaxKind.LineContinuationTrivia Then
                Dim lineContinuation = FormatLineContinuationTrivia(trivia)

                If trivia <> lineContinuation Then
                    changes.Add(New TextChange(trivia.FullSpan, lineContinuation.ToFullString()))
                End If

                Return GetLineColumnDelta(lineColumn, lineContinuation)
            End If

            Return GetLineColumnDelta(lineColumn, trivia)
        End Function

        Private Function FormatLineContinuationTrivia(trivia As SyntaxTrivia) As SyntaxTrivia
            If trivia.ToFullString() <> lineContinuationTrivia.ToFullString() Then
                Return lineContinuationTrivia
            End If

            Return trivia
        End Function

        Private Function FormatStructuredTrivia(lineColumn As LineColumn,
                                                trivia As SyntaxTrivia,
                                                changes As List(Of SyntaxTrivia),
                                                cancellationToken As CancellationToken) As LineColumnDelta
            If trivia.VisualBasicKind = SyntaxKind.SkippedTokensTrivia Then
                ' don't touch anything if it contains skipped tokens
                _succeeded = False

                changes.Add(trivia)
                Return GetLineColumnDelta(lineColumn, trivia)
            End If

            ' TODO : make document comment to be formatted by structured trivia formatter as well.
            If trivia.VisualBasicKind <> SyntaxKind.DocumentationCommentTrivia Then
                Dim result = VisualBasicStructuredTriviaFormatEngine.FormatTrivia(trivia, Me.InitialLineColumn.Column, Me.OptionSet, Me.FormattingRules, cancellationToken)
                Dim formattedTrivia = SyntaxFactory.Trivia(DirectCast(result.GetFormattedRoot(cancellationToken), StructuredTriviaSyntax))

                changes.Add(formattedTrivia)
                Return GetLineColumnDelta(lineColumn, formattedTrivia)
            End If

            Dim docComment = FormatDocumnetComment(lineColumn, trivia)
            changes.Add(docComment)

            Return GetLineColumnDelta(lineColumn, docComment)
        End Function

        Private Function FormatStructuredTrivia(lineColumn As LineColumn,
                                                trivia As SyntaxTrivia,
                                                changes As List(Of TextChange),
                                                cancellationToken As CancellationToken) As LineColumnDelta
            If trivia.VisualBasicKind = SyntaxKind.SkippedTokensTrivia Then
                ' don't touch anything if it contains skipped tokens
                _succeeded = False
                Return GetLineColumnDelta(lineColumn, trivia)
            End If

            ' TODO : make document comment to be formatted by structured trivia formatter as well.
            If trivia.VisualBasicKind <> SyntaxKind.DocumentationCommentTrivia Then
                Dim result = VisualBasicStructuredTriviaFormatEngine.FormatTrivia(
                    trivia, Me.InitialLineColumn.Column, Me.OptionSet, Me.FormattingRules, cancellationToken)

                If result.GetTextChanges(cancellationToken).Count = 0 Then
                    Return GetLineColumnDelta(lineColumn, trivia)
                End If

                changes.AddRange(result.GetTextChanges(cancellationToken))

                Dim formattedTrivia = SyntaxFactory.Trivia(DirectCast(result.GetFormattedRoot(cancellationToken), StructuredTriviaSyntax))
                Return GetLineColumnDelta(lineColumn, formattedTrivia)
            End If

            Dim docComment = FormatDocumnetComment(lineColumn, trivia)
            If docComment <> trivia Then
                changes.Add(New TextChange(trivia.FullSpan, docComment.ToFullString()))
            End If

            Return GetLineColumnDelta(lineColumn, docComment)
        End Function

        Private Function FormatDocumnetComment(lineColumn As LineColumn, trivia As SyntaxTrivia) As SyntaxTrivia

            Dim indentation = Me.Context.GetBaseIndentation(trivia.SpanStart)

            Dim text = trivia.ToFullString()

            ' When the doc comment is parsed from source, even if it is only one
            ' line long, the end-of-line will get sucked into the trivia text.
            ' If the doc comment was parsed from a text fragment, there may not be
            ' an end-of-line at all. We need to trim the end before we check the
            ' number of line breaks in the text.
            Dim textWithoutFinalNewLine = text.TrimEnd()
            If textWithoutFinalNewLine.GetNumberOfLineBreaks() = 0 Then
                Return trivia
            End If

            Dim singlelineDocComments = text.ReindentStartOfXmlDocumentationComment(
                forceIndentation:=True,
                indentation:=indentation,
                indentationDelta:=0,
                useTab:=Me.OptionSet.GetOption(FormattingOptions.UseTabs, LanguageNames.VisualBasic),
                tabSize:=Me.OptionSet.GetOption(FormattingOptions.TabSize, LanguageNames.VisualBasic))

            If text = singlelineDocComments Then
                Return trivia
            End If

            Dim singlelineDocCommentTrivia = SyntaxFactory.ParseLeadingTrivia(singlelineDocComments)
            Contract.ThrowIfFalse(singlelineDocCommentTrivia.Count = 1)

            Return singlelineDocCommentTrivia.ElementAt(0)
        End Function

    End Class
End Namespace