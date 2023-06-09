﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp.Extensions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.Formatting
{
    internal partial class CSharpTriviaFormatter : AbstractTriviaFormatter<SyntaxTrivia>
    {
        private bool succeeded = true;

        public CSharpTriviaFormatter(
            FormattingContext context,
            ChainedFormattingRules formattingRules,
            SyntaxToken token1,
            SyntaxToken token2,
            string originalString,
            int lineBreaks,
            int spaces) :
            base(context, formattingRules, token1, token2, originalString, lineBreaks, spaces)
        {
        }

        protected override bool Succeeded()
        {
            return succeeded;
        }

        protected override bool IsWhitespace(SyntaxTrivia trivia)
        {
            return trivia.RawKind == (int)SyntaxKind.WhitespaceTrivia;
        }

        protected override bool IsEndOfLine(SyntaxTrivia trivia)
        {
            return trivia.RawKind == (int)SyntaxKind.EndOfLineTrivia;
        }

        protected override bool IsNullOrWhitespace(string text)
        {
            return text.IsNullOrCSharpWhitespace();
        }

        protected override bool IsWhitespace(char ch)
        {
            return SyntaxFacts.IsWhitespace(ch);
        }

        protected override bool IsNewLine(char ch)
        {
            return SyntaxFacts.IsNewLine(ch);
        }

        protected override SyntaxTrivia CreateWhitespace(string text)
        {
            return SyntaxFactory.Whitespace(text, elastic: false);
        }

        protected override SyntaxTrivia CreateEndOfLine()
        {
            return SyntaxFactory.CarriageReturnLineFeed;
        }

        protected override SyntaxTrivia Convert(SyntaxTrivia trivia)
        {
            return (SyntaxTrivia)trivia;
        }

        protected override LineColumnRule GetLineColumnRuleBetween(SyntaxTrivia trivia1, LineColumnDelta existingWhitespaceBetween, SyntaxTrivia trivia2)
        {
            if (IsStartOrEndOfFile(trivia1, trivia2))
            {
                return LineColumnRule.PreserveLinesWithAbsoluteIndentation(lines: 0, indentation: 0);
            }

            // [trivia] [whitespace] [token] case
            if (trivia2.IsKind(SyntaxKind.None))
            {
                var insertNewLine = this.FormattingRules.GetAdjustNewLinesOperation(this.Token1, this.Token2) != null;

                if (IsMultilineComment(trivia1))
                {
                    return LineColumnRule.PreserveLinesWithGivenIndentation(lines: insertNewLine ? 1 : 0);
                }

                if (insertNewLine)
                {
                    return LineColumnRule.PreserveLinesWithDefaultIndentation(lines: 0);
                }

                return LineColumnRule.Preserve();
            }

            // preprocessor case
            if (SyntaxFacts.IsPreprocessorDirective(trivia2.CSharpKind()))
            {
                // Check for immovable preprocessor directives, which are bad directive trivia 
                // without a preceding line break
                if (trivia2.IsKind(SyntaxKind.BadDirectiveTrivia) && existingWhitespaceBetween.Lines == 0)
                {
                    this.succeeded = false;
                    return LineColumnRule.Preserve();
                }

                // if current line is the first line of the file, don't put extra line 1
                var lines = (trivia1.IsKind(SyntaxKind.None) && this.Token1.IsKind(SyntaxKind.None)) ? 0 : 1;

                if (trivia2.IsKind(SyntaxKind.RegionDirectiveTrivia) || trivia2.IsKind(SyntaxKind.EndRegionDirectiveTrivia))
                {
                    return LineColumnRule.PreserveLinesWithDefaultIndentation(lines);
                }

                return LineColumnRule.PreserveLinesWithAbsoluteIndentation(lines, indentation: 0);
            }

            // comments case
            if (trivia2.IsRegularOrDocComment())
            {
                // start of new comments group
                if (!trivia1.IsRegularComment() || existingWhitespaceBetween.Lines > 1)
                {
                    if (this.FormattingRules.GetAdjustNewLinesOperation(this.Token1, this.Token2) != null)
                    {
                        return LineColumnRule.PreserveLinesWithDefaultIndentation(lines: 0);
                    }

                    return LineColumnRule.PreserveLinesWithGivenIndentation(lines: 0);
                }

                // comments after existing commet
                if (existingWhitespaceBetween.Lines == 0)
                {
                    return LineColumnRule.PreserveLinesWithGivenIndentation(lines: 0);
                }

                return LineColumnRule.PreserveLinesWithFollowingPreceedingIndentation();
            }

            if (trivia2.IsKind(SyntaxKind.SkippedTokensTrivia))
            {
                // if there is any skipped tokens, it is not possible to format this trivia range.
                this.succeeded = false;
            }

            return LineColumnRule.Preserve();
        }

        private bool IsStartOrEndOfFile(SyntaxTrivia trivia1, SyntaxTrivia trivia2)
        {
            return (this.Token1.RawKind == 0 || this.Token2.RawKind == 0) && (trivia1.CSharpKind() == 0 || trivia2.CSharpKind() == 0);
        }

        private static bool IsMultilineComment(SyntaxTrivia trivia1)
        {
            return trivia1.IsMultiLineComment() || trivia1.IsMultiLineDocComment();
        }

        private bool TryFormatMultiLineCommentTrivia(LineColumn lineColumn, SyntaxTrivia trivia, out SyntaxTrivia result)
        {
            result = default(SyntaxTrivia);

            if (trivia.CSharpKind() != SyntaxKind.MultiLineCommentTrivia)
            {
                return false;
            }

            var indentation = lineColumn.Column;
            var indentationDelta = indentation - GetExistingIndentation(trivia);
            if (indentationDelta != 0)
            {
                var multiLineComment = trivia.ToFullString().ReindentStartOfXmlDocumentationComment(
                    false /* forceIndentation */,
                    indentation,
                    indentationDelta,
                    this.OptionSet.GetOption(FormattingOptions.UseTabs, LanguageNames.CSharp),
                    this.OptionSet.GetOption(FormattingOptions.TabSize, LanguageNames.CSharp));

                var multilineCommentTrivia = SyntaxFactory.ParseLeadingTrivia(multiLineComment);
                Contract.ThrowIfFalse(multilineCommentTrivia.Count == 1);

                result = multilineCommentTrivia.ElementAt(0);
                return true;
            }

            return false;
        }

        protected override LineColumnDelta Format(
            LineColumn lineColumn, SyntaxTrivia commonTrivia, List<SyntaxTrivia> changes,
            CancellationToken cancellationToken)
        {
            var trivia = (SyntaxTrivia)commonTrivia;

            if (trivia.HasStructure)
            {
                return FormatStructuredTrivia(lineColumn, trivia, changes, cancellationToken);
            }

            var newComment = default(SyntaxTrivia);
            if (TryFormatMultiLineCommentTrivia(lineColumn, trivia, out newComment))
            {
                changes.Add(newComment);
                return GetLineColumnDelta(lineColumn, newComment);
            }

            changes.Add(trivia);
            return GetLineColumnDelta(lineColumn, trivia);
        }

        protected override LineColumnDelta Format(
            LineColumn lineColumn, SyntaxTrivia commonTrivia, List<TextChange> changes, CancellationToken cancellationToken)
        {
            var trivia = (SyntaxTrivia)commonTrivia;

            if (trivia.HasStructure)
            {
                return FormatStructuredTrivia(lineColumn, trivia, changes, cancellationToken);
            }

            var newComment = default(SyntaxTrivia);
            if (TryFormatMultiLineCommentTrivia(lineColumn, trivia, out newComment))
            {
                changes.Add(new TextChange(trivia.FullSpan, newComment.ToFullString()));
                return GetLineColumnDelta(lineColumn, newComment);
            }

            return GetLineColumnDelta(lineColumn, commonTrivia);
        }

        private SyntaxTrivia FormatDocumentComment(LineColumn lineColumn, SyntaxTrivia trivia)
        {
            var indentation = lineColumn.Column;

            if (trivia.IsSingleLineDocComment())
            {
                var text = trivia.ToFullString();

                // When the doc comment is parsed from source, even if it is only one
                // line long, the end-of-line will get sucked into the trivia text.
                // If the doc comment was parsed from a text fragment, there may not be
                // an end-of-line at all. We need to trim the end before we check the
                // number of line breaks in the text.
                var textWithoutFinalNewLine = text.TrimEnd();
                if (textWithoutFinalNewLine.GetNumberOfLineBreaks() == 0)
                {
                    return trivia;
                }

                var singleLineDocumentationCommentExteriorCommentRewriter = new DocumentationCommentExteriorCommentRewriter(
                    true /* forceIndentation */,
                    indentation,
                    0 /* indentationDelta */,
                    this.OptionSet);
                var newTrivia = singleLineDocumentationCommentExteriorCommentRewriter.VisitTrivia(trivia);

                return newTrivia;
            }

            var indentationDelta = indentation - GetExistingIndentation(trivia);
            if (indentationDelta == 0)
            {
                return trivia;
            }

            var multiLineDocumentationCommentExteriorCommentRewriter = new DocumentationCommentExteriorCommentRewriter(
                    false /* forceIndentation */,
                    indentation,
                    indentationDelta,
                    this.OptionSet);
            var newMultiLineTrivia = multiLineDocumentationCommentExteriorCommentRewriter.VisitTrivia(trivia);

            return newMultiLineTrivia;
        }

        private LineColumnDelta FormatStructuredTrivia(
            LineColumn lineColumn, SyntaxTrivia trivia, List<SyntaxTrivia> changes, CancellationToken cancellationToken)
        {
            if (trivia.CSharpKind() == SyntaxKind.SkippedTokensTrivia)
            {
                // don't touch anything if it contains skipped tokens
                this.succeeded = false;
                changes.Add(trivia);

                return GetLineColumnDelta(lineColumn, trivia);
            }

            // TODO : make document comment to be formatted by structured trivia formatter as well.
            if (!trivia.IsDocComment())
            {
                var result = CSharpStructuredTriviaFormatEngine.Format(
                    trivia, this.InitialLineColumn.Column, this.OptionSet, this.FormattingRules, cancellationToken);
                var formattedTrivia = SyntaxFactory.Trivia((StructuredTriviaSyntax)result.GetFormattedRoot(cancellationToken));

                changes.Add(formattedTrivia);
                return GetLineColumnDelta(lineColumn, formattedTrivia);
            }

            var docComment = FormatDocumentComment(lineColumn, trivia);
            changes.Add(docComment);

            return GetLineColumnDelta(lineColumn, docComment);
        }

        private LineColumnDelta FormatStructuredTrivia(
            LineColumn lineColumn, SyntaxTrivia trivia, List<TextChange> changes, CancellationToken cancellationToken)
        {
            if (trivia.CSharpKind() == SyntaxKind.SkippedTokensTrivia)
            {
                // don't touch anything if it contains skipped tokens
                this.succeeded = false;
                return GetLineColumnDelta(lineColumn, trivia);
            }

            // TODO : make document comment to be formatted by structured trivia formatter as well.
            if (!trivia.IsDocComment())
            {
                var result = CSharpStructuredTriviaFormatEngine.Format(
                    trivia, this.InitialLineColumn.Column, this.OptionSet, this.FormattingRules, cancellationToken);
                if (result.GetTextChanges(cancellationToken).Count == 0)
                {
                    return GetLineColumnDelta(lineColumn, trivia);
                }

                changes.AddRange(result.GetTextChanges(cancellationToken));

                var formattedTrivia = SyntaxFactory.Trivia((StructuredTriviaSyntax)result.GetFormattedRoot(cancellationToken));
                return GetLineColumnDelta(lineColumn, formattedTrivia);
            }

            var docComment = FormatDocumentComment(lineColumn, trivia);
            if (docComment != trivia)
            {
                changes.Add(new TextChange(trivia.FullSpan, docComment.ToFullString()));
            }

            return GetLineColumnDelta(lineColumn, docComment);
        }
    }
}
