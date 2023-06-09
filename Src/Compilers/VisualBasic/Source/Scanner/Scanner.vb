﻿' Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

'-----------------------------------------------------------------------------
' Contains the definition of the Scanner, which produces tokens from text 
'-----------------------------------------------------------------------------

Option Compare Binary
Option Strict On
Imports System.Collections.Immutable
Imports System.Globalization
Imports System.Runtime.InteropServices
Imports System.Text
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Microsoft.CodeAnalysis.VisualBasic.SyntaxFacts
Imports Microsoft.CodeAnalysis.Collections

Namespace Microsoft.CodeAnalysis.VisualBasic.Syntax.InternalSyntax

    ''' <summary>
    ''' Creates red tokens for a stream of text
    ''' </summary>
    Friend Class Scanner
        Implements IDisposable

        Private Delegate Function ScanTriviaFunc() As SyntaxList(Of VisualBasicSyntaxNode)

        Private Shared ReadOnly _scanNoTriviaFunc As ScanTriviaFunc = Function() Nothing
        Private ReadOnly _scanSingleLineTriviaFunc As ScanTriviaFunc = AddressOf ScanSingleLineTrivia

        Protected _lineBufferOffset As Integer ' marks the next character to read from _LineBuffer
        Private _endOfTerminatorTrivia As Integer ' marks how far scanner may have scanned ahead for terminator trivia. This may be greater than _lineBufferOffset

        Private ReadOnly _sbPooled As PooledStringBuilder = PooledStringBuilder.GetInstance
        ''' <summary>
        ''' DO NOT USE DIRECTLY. 
        ''' USE GetScratch() 
        ''' </summary>
        Private ReadOnly _sb As StringBuilder = _sbPooled.Builder
        Private ReadOnly triviaListPool As New SyntaxListPool
        Private ReadOnly _options As VisualBasicParseOptions

        Private ReadOnly _stringTable As StringTable = StringTable.GetInstance()
        Private ReadOnly _quickTokenTable As TextKeyedCache(Of SyntaxToken) = TextKeyedCache(Of SyntaxToken).GetInstance

        Public Const TABLE_LIMIT = 512
        Private Shared ReadOnly keywordKindFactory As Func(Of String, SyntaxKind) =
            Function(spelling) KeywordTable.TokenOfString(spelling)

        Private Shared ReadOnly _KeywordsObjsPool As ObjectPool(Of CachingIdentityFactory(Of String, SyntaxKind)) = CachingIdentityFactory(Of String, SyntaxKind).CreatePool(TABLE_LIMIT, keywordKindFactory)
        Private ReadOnly _KeywordsObjs As CachingIdentityFactory(Of String, SyntaxKind) = _KeywordsObjsPool.Allocate()

        Private Shared ReadOnly _idTablePool As New ObjectPool(Of CachingFactory(Of TokenParts, IdentifierTokenSyntax))(
            Function() New CachingFactory(Of TokenParts, IdentifierTokenSyntax)(TABLE_LIMIT, Nothing, tokenKeyHasher, tokenKeyEquality))

        Private ReadOnly _idTable As CachingFactory(Of TokenParts, IdentifierTokenSyntax) = _idTablePool.Allocate()

        Private Shared ReadOnly _kwTablePool As New ObjectPool(Of CachingFactory(Of TokenParts, KeywordSyntax))(
            Function() New CachingFactory(Of TokenParts, KeywordSyntax)(TABLE_LIMIT, Nothing, tokenKeyHasher, tokenKeyEquality))

        Private ReadOnly _kwTable As CachingFactory(Of TokenParts, KeywordSyntax) = _kwTablePool.Allocate

        Private Shared ReadOnly _punctTablePool As New ObjectPool(Of CachingFactory(Of TokenParts, PunctuationSyntax))(
            Function() New CachingFactory(Of TokenParts, PunctuationSyntax)(TABLE_LIMIT, Nothing, tokenKeyHasher, tokenKeyEquality))

        Private ReadOnly _punctTable As CachingFactory(Of TokenParts, PunctuationSyntax) = _punctTablePool.Allocate()

        Private Shared ReadOnly _literalTablePool As New ObjectPool(Of CachingFactory(Of TokenParts, SyntaxToken))(
            Function() New CachingFactory(Of TokenParts, SyntaxToken)(TABLE_LIMIT, Nothing, tokenKeyHasher, tokenKeyEquality))

        Private ReadOnly _literalTable As CachingFactory(Of TokenParts, SyntaxToken) = _literalTablePool.Allocate

        Private Shared ReadOnly _wslTablePool As New ObjectPool(Of CachingFactory(Of SyntaxListBuilder, SyntaxList(Of VisualBasicSyntaxNode)))(
            Function() New CachingFactory(Of SyntaxListBuilder, SyntaxList(Of VisualBasicSyntaxNode))(TABLE_LIMIT, wsListFactory, wsListKeyHasher, wsListKeyEquality))

        Private ReadOnly _wslTable As CachingFactory(Of SyntaxListBuilder, SyntaxList(Of VisualBasicSyntaxNode)) = _wslTablePool.Allocate

        Private Shared ReadOnly _wsTablePool As New ObjectPool(Of CachingFactory(Of TriviaKey, SyntaxTrivia))(
            Function() CreateWsTable())

        Private ReadOnly _wsTable As CachingFactory(Of TriviaKey, SyntaxTrivia) = _wsTablePool.Allocate

        Private _isDisposed As Boolean

        Private Function GetScratch() As StringBuilder
            ' the normal pattern is that we clean scratch after use.
            ' hitting this asert very likely indicates that you 
            ' did not release scratch content or worse trying to use
            ' scratch in two places at a time.
            Debug.Assert(_sb.Length = 0, "trying to use dirty buffer?")
            Return _sb
        End Function

#Region "Public interface"
        Friend Sub New(textToScan As SourceText, options As VisualBasicParseOptions)
            Debug.Assert(textToScan IsNot Nothing)

            _lineBufferOffset = 0
            _buffer = textToScan
            _bufferLen = textToScan.Length
            _curPage = GetPage(0)
            _options = options

            _scannerPreprocessorState = New PreprocessorState(AsPreprocessorConstants(options.PreprocessorSymbols))
        End Sub
        Friend Sub Dispose() Implements IDisposable.Dispose
            If Not _isDisposed Then
                _isDisposed = True

                _KeywordsObjs.Free()
                _quickTokenTable.Free()
                _stringTable.Free()
                _sbPooled.Free()

                _idTablePool.Free(_idTable)
                _kwTablePool.Free(_kwTable)
                _punctTablePool.Free(_punctTable)
                _literalTablePool.Free(_literalTable)
                _wslTablePool.Free(_wslTable)
                _wsTablePool.Free(_wsTable)

                For Each p As Page In Me._pages
                    If p IsNot Nothing Then
                        p.Free()
                    End If
                Next

                Array.Clear(Me._pages, 0, Me._pages.Length)
            End If
        End Sub
        Friend ReadOnly Property Options As VisualBasicParseOptions
            Get
                Return _options
            End Get
        End Property

        Friend Shared Function AsPreprocessorConstants(symbols As ImmutableArray(Of KeyValuePair(Of String, Object))) As ImmutableDictionary(Of String, CConst)
            Dim consts = ImmutableDictionary.CreateBuilder(Of String, CConst)(IdentifierComparison.Comparer)
            For Each pair In symbols
                consts(pair.Key) = CConst.Create(pair.Value)
            Next

            Return consts.ToImmutable()
        End Function

        Private Function GetNextToken(Optional allowLeadingMultilineTrivia As Boolean = False) As SyntaxToken
            ' Use quick token scanning to see if we can scan a token quickly. 
            Dim quickToken = QuickScanToken(allowLeadingMultilineTrivia)

            If quickToken.Succeeded Then
                Dim token = _quickTokenTable.FindItem(quickToken.Chars, quickToken.Start, quickToken.Length, quickToken.HashCode)
                If token IsNot Nothing Then
                    AdvanceChar(quickToken.Length)
                    If quickToken.TerminatorLength <> 0 Then
                        Me._endOfTerminatorTrivia = Me._lineBufferOffset
                        Me._lineBufferOffset -= quickToken.TerminatorLength
                    End If

                    Return token
                End If
            End If

            Dim scannedToken = ScanNextToken(allowLeadingMultilineTrivia)

            ' If we quick-scanned a token, but didn't have a actual token cached for it, cache the token we created
            ' from the regular scanner.
            If quickToken.Succeeded Then
                Debug.Assert(quickToken.Length = scannedToken.FullWidth)

                _quickTokenTable.AddItem(quickToken.Chars, quickToken.Start, quickToken.Length, quickToken.HashCode, scannedToken)
            End If

            Return scannedToken
        End Function

        Private Function ScanNextToken(allowLeadingMultilineTrivia As Boolean) As SyntaxToken
#If DEBUG Then
            Dim oldOffset = _lineBufferOffset
#End If
            Dim leadingTrivia As SyntaxList(Of VisualBasicSyntaxNode)

            If allowLeadingMultilineTrivia Then
                leadingTrivia = ScanMultilineTrivia()
            Else
                leadingTrivia = ScanLeadingTrivia()

                ' Special case where the remainder of the line is a comment.
                Dim length = PeekStartComment(0)
                If length > 0 Then
                    Return MakeEmptyToken(leadingTrivia)
                End If
            End If

            Dim token = TryScanToken(leadingTrivia)

            If token Is Nothing Then
                token = ScanNextCharAsToken(leadingTrivia)
            End If

            If _lineBufferOffset > _endOfTerminatorTrivia Then
                _endOfTerminatorTrivia = _lineBufferOffset
            End If

#If DEBUG Then
            ' we must always consume as much as returned token's full length or things will go very bad
            Debug.Assert(oldOffset + token.FullWidth = _lineBufferOffset OrElse
                         oldOffset + token.FullWidth = _endOfTerminatorTrivia OrElse
                         token.FullWidth = 0)
#End If
            Return token
        End Function

        Private Function ScanNextCharAsToken(leadingTrivia As SyntaxList(Of VisualBasicSyntaxNode)) As SyntaxToken
            Dim token As SyntaxToken

            If Not CanGetChar() Then
                token = MakeEofToken(leadingTrivia)
            Else
                ' // Don't break up surrogate pairs
                Dim c = PeekChar()
                Dim length = If(IsHighSurrogate(c) AndAlso CanGetCharAtOffset(1) AndAlso IsLowSurrogate(PeekAheadChar(1)), 2, 1)
                token = MakeBadToken(leadingTrivia, length, ERRID.ERR_IllegalChar)
            End If

            Return token
        End Function

        ' // SkipToNextConditionalLine advances through the input stream until it finds a (logical)
        ' // line that has a '#' character as its first non-whitespace, non-continuation character.
        ' // SkipToNextConditionalLine ignores explicit line continuation.

        ' TODO: this could be vastly simplified if we could ignore line continuations.
        Public Function SkipToNextConditionalLine() As TextSpan
            ' start at current token
            ResetLineBufferOffset()

            Dim start = _lineBufferOffset

            ' if starting not from line start, skip to the next one.
            Dim prev = PrevToken
            If Not IsAtNewLine() OrElse
                (PrevToken IsNot Nothing AndAlso PrevToken.EndsWithEndOfLineOrColonTrivia) Then

                EatThroughLine()
            End If

            Dim condLineStart = _lineBufferOffset

            While (CanGetChar())
                Dim c As Char = PeekChar()

                Select Case (c)

                    Case UCH_CR, UCH_LF
                        EatThroughLineBreak(c)
                        condLineStart = _lineBufferOffset
                        Continue While

                    Case UCH_SPACE, UCH_TAB
                        Debug.Assert(IsWhitespace(PeekChar()))
                        EatWhitespace()
                        Continue While

                    Case _
                        "a"c, "b"c, "c"c, "d"c, "e"c, "f"c, "g"c, "h"c, "i"c, "j"c, "k"c, "l"c,
                        "m"c, "n"c, "o"c, "p"c, "q"c, "r"c, "s"c, "t"c, "u"c, "v"c, "w"c, "x"c,
                        "y"c, "z"c, "A"c, "B"c, "C"c, "D"c, "E"c, "F"c, "G"c, "H"c, "I"c, "J"c,
                        "K"c, "L"c, "M"c, "N"c, "O"c, "P"c, "Q"c, "R"c, "S"c, "T"c, "U"c, "V"c,
                        "W"c, "X"c, "Y"c, "Z"c, "'"c, "_"c

                        EatThroughLine()
                        condLineStart = _lineBufferOffset
                        Continue While

                    Case "#"c, FULLWIDTH_HASH
                        Exit While

                    Case Else
                        If IsWhitespace(c) Then
                            EatWhitespace()
                            Continue While

                        ElseIf IsNewLine(c) Then
                            EatThroughLineBreak(c)
                            condLineStart = _lineBufferOffset
                            Continue While

                        End If

                        EatThroughLine()
                        condLineStart = _lineBufferOffset
                        Continue While
                End Select
            End While

            ' we did not find # or we have hit EoF.
            _lineBufferOffset = condLineStart
            Debug.Assert(_lineBufferOffset >= start AndAlso _lineBufferOffset >= 0)

            ResetTokens()
            Return TextSpan.FromBounds(start, condLineStart)
        End Function

        Private Sub EatThroughLine()
            While CanGetChar()
                Dim c As Char = PeekChar()

                If IsNewLine(c) Then
                    EatThroughLineBreak(c)
                    Return
                Else
                    AdvanceChar()
                End If
            End While
        End Sub

        ''' <summary>
        ''' Gets a chunk of text as a DisabledCode node.
        ''' </summary>
        ''' <param name="span">The range of text.</param>
        ''' <returns>The DisabledCode node.</returns> 
        Friend Function GetDisabledTextAt(span As TextSpan) As SyntaxTrivia
            If span.Start >= 0 AndAlso span.End <= _bufferLen Then
                Return SyntaxFactory.DisabledTextTrivia(GetTextNotInterned(span.Start, span.Length))
            End If

            ' TODO: should this be a Require?
            Throw New ArgumentOutOfRangeException("span")
        End Function
#End Region

#Region "Interning"
        Friend Function GetScratchTextInterned(sb As StringBuilder) As String
            Dim str = _stringTable.Add(sb)
            sb.Clear()
            Return str
        End Function

        Friend Shared Function GetScratchText(sb As StringBuilder) As String
            ' PERF: Special case for the very common case of a string containing a single space
            Dim str As String
            If sb.Length = 1 AndAlso sb(0) = " "c Then
                str = " "
            Else
                str = sb.ToString
            End If
            sb.Clear()
            Return str
        End Function

        ' This overload of GetScratchText first examines the contents of the StringBuilder to
        ' see if it matches the given string. If so, then the given string is returned, saving
        ' the allocation.
        Private Shared Function GetScratchText(sb As StringBuilder, text As String) As String
            Dim str As String
            If StringTable.TextEquals(text, sb) Then
                str = text
            Else
                str = sb.ToString
            End If
            sb.Clear()
            Return str
        End Function

        Friend Function Intern(s As String, start As Integer, length As Integer) As String
            Return _stringTable.Add(s, start, length)
        End Function

        Friend Function Intern(s As Char(), start As Integer, length As Integer) As String
            Return _stringTable.Add(s, start, length)
        End Function

        Friend Function Intern(ch As Char) As String
            Return _stringTable.Add(ch)
        End Function
        Friend Function Intern(arr As Char()) As String
            Return _stringTable.Add(arr)
        End Function
#End Region

#Region "Buffer helpers"
        Private Function CanGetChar() As Boolean
            Return _lineBufferOffset < _bufferLen
        End Function

        Private Function CanGetCharAtOffset(num As Integer) As Boolean
            Debug.Assert(_lineBufferOffset + num >= 0)
            Debug.Assert(num >= -MaxCharsLookBehind)

            Return _lineBufferOffset + num < _bufferLen
        End Function

        Private Function GetText(length As Integer) As String
            Debug.Assert(length > 0)
            Debug.Assert(CanGetCharAtOffset(length - 1))

            If length = 1 Then
                Return GetNextChar()
            End If

            Dim str = GetText(_lineBufferOffset, length)
            AdvanceChar(length)
            Return str
        End Function

        Private Function GetTextNotInterned(length As Integer) As String
            Debug.Assert(length > 0)
            Debug.Assert(CanGetCharAtOffset(length - 1))

            If length = 1 Then
                ' we will still intern single chars. There could not be too many.
                Return GetNextChar()
            End If

            Dim str = GetTextNotInterned(_lineBufferOffset, length)
            AdvanceChar(length)
            Return str
        End Function

        Private Sub AdvanceChar(Optional howFar As Integer = 1)
            Debug.Assert(howFar > 0)
            Debug.Assert(CanGetCharAtOffset(howFar - 1))

            _lineBufferOffset += howFar
        End Sub

        Private Function GetNextChar() As String
            Debug.Assert(CanGetChar)

            Dim ch = GetChar()
            _lineBufferOffset += 1

            Return ch
        End Function

        Private Sub EatThroughLineBreak(StartCharacter As Char)
            AdvanceChar(LengthOfLineBreak(StartCharacter))
        End Sub

        Private Function SkipLineBreak(StartCharacter As Char, index As Integer) As Integer
            Return index + LengthOfLineBreak(StartCharacter, index)
        End Function

        Private Function LengthOfLineBreak(StartCharacter As Char, Optional here As Integer = 0) As Integer
            Debug.Assert(CanGetCharAtOffset(here))
            Debug.Assert(IsNewLine(StartCharacter))

            Debug.Assert(StartCharacter = PeekAheadChar(here))

            If StartCharacter = UCH_CR AndAlso
                CanGetCharAtOffset(here + 1) AndAlso
                PeekAheadChar(here + 1) = UCH_LF Then

                Return 2
            End If
            Return 1
        End Function
#End Region

#Region "New line and explicit line continuation."
        ''' <summary>
        ''' Accept a CR/LF pair or either in isolation as a newline.
        ''' Make it a statement separator
        ''' </summary>        
        Private Function ScanNewlineAsStatementTerminator(startCharacter As Char, precedingTrivia As SyntaxList(Of VisualBasicSyntaxNode)) As SyntaxToken
            If _lineBufferOffset < _endOfTerminatorTrivia Then
                Dim width = LengthOfLineBreak(startCharacter)
                Return MakeStatementTerminatorToken(precedingTrivia, width)
            Else
                Return MakeEmptyToken(precedingTrivia)
            End If
        End Function

        Private Function ScanColonAsStatementTerminator(precedingTrivia As SyntaxList(Of VisualBasicSyntaxNode), charIsFullWidth As Boolean) As SyntaxToken
            If _lineBufferOffset < _endOfTerminatorTrivia Then
                Return MakeColonToken(precedingTrivia, charIsFullWidth)
            Else
                Return MakeEmptyToken(precedingTrivia)
            End If
        End Function

        ''' <summary>
        ''' Accept a CR/LF pair or either in isolation as a newline.
        ''' Make it a whitespace
        ''' </summary>
        Private Function ScanNewlineAsTrivia(StartCharacter As Char) As SyntaxTrivia
            If LengthOfLineBreak(StartCharacter) = 2 Then
                Return MakeEndOfLineTriviaCRLF()
            End If
            Return MakeEndOfLineTrivia(GetNextChar)
        End Function

        Private Function ScanLineContinuation(tList As SyntaxListBuilder) As Boolean
            If Not CanGetChar() Then
                Return False
            End If

            If Not IsAfterWhitespace() Then
                Return False
            End If

            Dim ch As Char = PeekChar()
            If Not IsUnderscore(ch) Then
                Return False
            End If

            Dim Here = 1
            While CanGetCharAtOffset(Here)
                ch = PeekAheadChar(Here)
                If IsWhitespace(ch) Then
                    Here += 1
                Else
                    Exit While
                End If
            End While

            ' Line continuation is valid at the end of the
            ' line or at the end of file only.
            Dim atNewLine = IsNewLine(ch)
            If Not atNewLine AndAlso CanGetCharAtOffset(Here) Then
                Return False
            End If

            tList.Add(MakeLineContinuationTrivia(GetText(1)))
            If Here > 1 Then
                tList.Add(MakeWhiteSpaceTrivia(GetText(Here - 1)))
            End If

            If atNewLine Then
                Dim newLine = SkipLineBreak(ch, 0)
                Here = GetWhitespaceLength(newLine)
                Dim spaces = Here - newLine
                Dim startComment = PeekStartComment(Here)

                ' If the line following the line continuation is blank, or blank with a comment,
                ' do not include the new line character since that would confuse code handling
                ' implicit line continuations. (See Scanner::EatLineContinuation.) Otherwise,
                ' include the new line and any additional spaces as trivia.
                If startComment = 0 AndAlso
                    CanGetCharAtOffset(Here) AndAlso
                    Not IsNewLine(PeekAheadChar(Here)) Then

                    tList.Add(MakeEndOfLineTrivia(GetText(newLine)))
                    If spaces > 0 Then
                        tList.Add(MakeWhiteSpaceTrivia(GetText(spaces)))
                    End If
                End If

            End If

            Return True
        End Function

#End Region

#Region "Trivia"

        ''' <summary>
        ''' Consumes all trivia until a nontrivia char is found
        ''' </summary>
        Friend Function ScanMultilineTrivia() As SyntaxList(Of VisualBasicSyntaxNode)
            If Not CanGetChar() Then
                Return Nothing
            End If

            Dim ch = PeekChar()

            ' optimization for a common case
            ' the ASCII range between ': and ~ , with exception of except "'", "_" and R cannot start trivia
            If ch > ":"c AndAlso ch <= "~"c AndAlso ch <> "'"c AndAlso ch <> "_"c AndAlso ch <> "R"c AndAlso ch <> "r"c Then
                Return Nothing
            End If

            Dim triviaList = triviaListPool.Allocate()
            While TryScanSinglePieceOfMultilineTrivia(triviaList)
            End While

            Dim result = MakeTriviaArray(triviaList)
            triviaListPool.Free(triviaList)
            Return result
        End Function

        ''' <summary>
        ''' Scans a single piece of trivia
        ''' </summary>
        Private Function TryScanSinglePieceOfMultilineTrivia(tList As SyntaxListBuilder) As Boolean
            If CanGetChar() Then

                Dim atNewLine = IsAtNewLine()

                ' check for XmlDocComment and directives
                If atNewLine Then
                    If StartsXmlDoc(0) Then
                        Return TryScanXmlDocComment(tList)
                    End If

                    If StartsDirective(0) Then
                        Return TryScanDirective(tList)
                    End If
                End If

                Dim ch = PeekChar()
                If IsWhitespace(ch) Then
                    ' eat until linebreak or nonwhitespace
                    Dim wslen = GetWhitespaceLength(1)

                    If atNewLine Then
                        If StartsXmlDoc(wslen) Then
                            Return TryScanXmlDocComment(tList)
                        End If

                        If StartsDirective(wslen) Then
                            Return TryScanDirective(tList)
                        End If
                    End If
                    tList.Add(MakeWhiteSpaceTrivia(GetText(wslen)))
                    Return True
                ElseIf IsNewLine(ch) Then
                    tList.Add(ScanNewlineAsTrivia(ch))
                    Return True
                ElseIf IsUnderscore(ch) Then
                    Return ScanLineContinuation(tList)
                ElseIf IsColonAndNotColonEquals(ch, offset:=0) Then
                    tList.Add(ScanColonAsTrivia())
                    Return True
                End If

                ' try get a comment
                Return ScanCommentIfAny(tList)
            End If

            Return False
        End Function

        ' check for '''(~')
        Private Function StartsXmlDoc(Here As Integer) As Boolean
            Return _options.DocumentationMode >= DocumentationMode.Parse AndAlso
                CanGetCharAtOffset(Here + 3) AndAlso
                IsSingleQuote(PeekAheadChar(Here)) AndAlso
                IsSingleQuote(PeekAheadChar(Here + 1)) AndAlso
                IsSingleQuote(PeekAheadChar(Here + 2)) AndAlso
                Not IsSingleQuote(PeekAheadChar(Here + 3))
        End Function

        ' check for #
        Private Function StartsDirective(Here As Integer) As Boolean
            If CanGetCharAtOffset(Here) Then
                Dim ch = PeekAheadChar(Here)
                Return IsHash(ch)
            End If
            Return False
        End Function

        Private Function IsAtNewLine() As Boolean
            Return _lineBufferOffset = 0 OrElse IsNewLine(PeekAheadChar(-1))
        End Function

        Private Function IsAfterWhitespace() As Boolean
            If _lineBufferOffset = 0 Then
                Return True
            End If

            Dim prevChar = PeekAheadChar(-1)
            Return IsWhitespace(prevChar)
        End Function

        ''' <summary>
        ''' Scan trivia on one LOGICAL line
        ''' Will check for whitespace, comment, EoL, implicit line break
        ''' EoL may be consumed as whitespace only as a part of line continuation ( _ )
        ''' </summary>
        Friend Function ScanSingleLineTrivia() As SyntaxList(Of VisualBasicSyntaxNode)
            Dim tList = triviaListPool.Allocate()
            ScanSingleLineTrivia(tList)
            Dim result = MakeTriviaArray(tList)
            triviaListPool.Free(tList)
            Return result
        End Function

        Private Sub ScanSingleLineTrivia(tList As SyntaxListBuilder)
            If Me.IsScanningXmlDoc Then
                ScanSingleLineTriviaInXmlDoc(tList)
            Else
                ScanWhitespaceAndLineContinuations(tList)
                ScanCommentIfAny(tList)
                ScanTerminatorTrivia(tList)
            End If
        End Sub

        Private Sub ScanSingleLineTriviaInXmlDoc(tList As SyntaxListBuilder)
            If CanGetChar() Then
                Dim c As Char = PeekChar()
                Select Case (c)
                    ' // Whitespace
                    ' //  S    ::=    (#x20 | #x9 | #xD | #xA)+
                    Case UCH_CR, UCH_LF, " "c, UCH_TAB
                        Dim offsets = CreateOffsetRestorePoint()
                        Dim triviaList = triviaListPool.Allocate(Of VisualBasicSyntaxNode)()
                        Dim continueLine = ScanXmlTriviaInXmlDoc(c, triviaList)
                        If Not continueLine Then
                            triviaListPool.Free(triviaList)
                            offsets.Restore()
                            Return
                        End If

                        For i = 0 To triviaList.Count - 1
                            tList.Add(triviaList(i))
                        Next
                        triviaListPool.Free(triviaList)

                End Select
            End If
        End Sub

        Private Function ScanLeadingTrivia() As SyntaxList(Of VisualBasicSyntaxNode)
            Dim tList = triviaListPool.Allocate()
            ScanWhitespaceAndLineContinuations(tList)
            Dim result = MakeTriviaArray(tList)
            triviaListPool.Free(tList)
            Return result
        End Function

        Private Sub ScanWhitespaceAndLineContinuations(tList As SyntaxListBuilder)
            If CanGetChar() AndAlso IsWhitespace(PeekChar()) Then
                tList.Add(ScanWhitespace(1))
                ' collect { lineCont, ws }
                While ScanLineContinuation(tList)
                End While
            End If
        End Sub

        ''' <summary>
        ''' Return True if the builder is a (possibly empty) list of
        ''' WhitespaceTrivia followed by an EndOfLineTrivia.
        ''' </summary>
        Private Shared Function IsBlankLine(tList As SyntaxListBuilder) As Boolean
            Dim n = tList.Count
            If n = 0 OrElse tList(n - 1).Kind <> SyntaxKind.EndOfLineTrivia Then
                Return False
            End If
            For i = 0 To n - 2
                If tList(i).Kind <> SyntaxKind.WhitespaceTrivia Then
                    Return False
                End If
            Next
            Return True
        End Function

        Private Sub ScanTerminatorTrivia(tList As SyntaxListBuilder)
            ' Check for statement terminators
            ' There are 4 special cases

            '   1. [colon ws+]* colon -> colon terminator
            '   2. new line -> new line terminator
            '   3. colon followed by new line -> colon terminator + new line terminator
            '   4. new line followed by new line -> new line terminator + new line terminator

            ' Case 3 is required to parse single line if's and numeric labels. 
            ' Case 4 is required to limit explicit line continuations to single new line

            If CanGetChar() Then

                Dim ch As Char = PeekChar()
                Dim startOfTerminatorTrivia = _lineBufferOffset

                If IsNewLine(ch) Then
                    tList.Add(ScanNewlineAsTrivia(ch))

                ElseIf IsColonAndNotColonEquals(ch, offset:=0) Then
                    tList.Add(ScanColonAsTrivia())

                    ' collect { ws, colon }
                    Do
                        Dim len = GetWhitespaceLength(0)
                        If Not CanGetCharAtOffset(len) Then
                            Exit Do
                        End If

                        ch = PeekAheadChar(len)
                        If Not IsColonAndNotColonEquals(ch, offset:=len) Then
                            Exit Do
                        End If

                        If len > 0 Then
                            tList.Add(MakeWhiteSpaceTrivia(GetText(len)))
                        End If

                        startOfTerminatorTrivia = _lineBufferOffset
                        tList.Add(ScanColonAsTrivia())
                    Loop
                End If

                _endOfTerminatorTrivia = _lineBufferOffset
                ' Reset _lineBufferOffset to the start of the terminator trivia.
                ' When the scanner is asked for the next token, it will return a 0 length terminator or colon token.
                _lineBufferOffset = startOfTerminatorTrivia
            End If

        End Sub

        Private Function ScanCommentIfAny(tList As SyntaxListBuilder) As Boolean
            If CanGetChar() Then
                ' check for comment
                Dim comment = ScanComment()
                If comment IsNot Nothing Then
                    tList.Add(comment)
                    Return True
                End If
            End If
            Return False
        End Function

        Private Function GetWhitespaceLength(len As Integer) As Integer
            ' eat until linebreak or nonwhitespace
            While CanGetCharAtOffset(len) AndAlso IsWhitespace(PeekAheadChar(len))
                len += 1
            End While
            Return len
        End Function

        Private Function GetXmlWhitespaceLength(len As Integer) As Integer
            ' eat until linebreak or nonwhitespace
            While CanGetCharAtOffset(len) AndAlso IsXmlWhitespace(PeekAheadChar(len))
                len += 1
            End While
            Return len
        End Function

        Private Function ScanWhitespace(Optional len As Integer = 0) As VisualBasicSyntaxNode
            len = GetWhitespaceLength(len)
            If len > 0 Then
                Return MakeWhiteSpaceTrivia(GetText(len))
            End If
            Return Nothing
        End Function

        Private Function ScanXmlWhitespace(Optional len As Integer = 0) As VisualBasicSyntaxNode
            len = GetXmlWhitespaceLength(len)
            If len > 0 Then
                Return MakeWhiteSpaceTrivia(GetText(len))
            End If
            Return Nothing
        End Function

        Private Sub EatWhitespace()
            Debug.Assert(CanGetChar)
            Debug.Assert(IsWhitespace(PeekChar()))

            AdvanceChar()

            ' eat until linebreak or nonwhitespace
            While CanGetChar() AndAlso IsWhitespace(PeekChar)
                AdvanceChar()
            End While
        End Sub

        Private Function PeekStartComment(i As Integer) As Integer

            If CanGetCharAtOffset(i) Then
                Dim ch = PeekAheadChar(i)

                If IsSingleQuote(ch) Then
                    Return 1
                ElseIf MatchOneOrAnotherOrFullwidth(ch, "R"c, "r"c) AndAlso
                    CanGetCharAtOffset(i + 2) AndAlso MatchOneOrAnotherOrFullwidth(PeekAheadChar(i + 1), "E"c, "e"c) AndAlso
                    MatchOneOrAnotherOrFullwidth(PeekAheadChar(i + 2), "M"c, "m"c) Then

                    If Not CanGetCharAtOffset(i + 3) OrElse IsNewLine(PeekAheadChar(i + 3)) Then
                        ' have only 'REM'
                        Return 3
                    ElseIf Not IsIdentifierPartCharacter(PeekAheadChar(i + 3)) Then
                        ' have 'REM '
                        Return 4
                    End If
                End If
            End If

            Return 0
        End Function

        Private Function ScanComment() As SyntaxTrivia
            Debug.Assert(CanGetChar())

            Dim length = PeekStartComment(0)
            If length > 0 Then
                Dim looksLikeDocComment As Boolean = StartsXmlDoc(0)

                ' eat all chars until EoL
                While CanGetCharAtOffset(length) AndAlso
                    Not IsNewLine(PeekAheadChar(length))

                    length += 1
                End While

                Dim commentTrivia As SyntaxTrivia = MakeCommentTrivia(GetTextNotInterned(length))

                If looksLikeDocComment AndAlso _options.DocumentationMode >= DocumentationMode.Diagnose Then
                    commentTrivia = commentTrivia.WithDiagnostics(ErrorFactory.ErrorInfo(ERRID.WRN_XMLDocNotFirstOnLine))
                End If

                Return commentTrivia
            End If

            Return Nothing
        End Function

        ''' <summary>
        ''' Return True if the character is a colon, and not part of ":=".
        ''' </summary>
        Private Function IsColonAndNotColonEquals(ch As Char, offset As Integer) As Boolean
            Return IsColon(ch) AndAlso Not TrySkipFollowingEquals(offset + 1)
        End Function

        Private Function ScanColonAsTrivia() As SyntaxTrivia
            Debug.Assert(CanGetChar())
            Debug.Assert(IsColonAndNotColonEquals(PeekChar(), offset:=0))

            Return MakeColonTrivia(GetText(1))
        End Function

#End Region

        ' at this point it is very likely that we are located at 
        ' the beginning of a token        
        Private Function TryScanToken(precedingTrivia As SyntaxList(Of VisualBasicSyntaxNode)) As SyntaxToken

            If Not CanGetChar() Then
                Return MakeEofToken(precedingTrivia)
            End If

            Dim ch As Char = PeekChar()
            Select Case ch
                Case UCH_CR, UCH_LF, UCH_NEL, UCH_LS, UCH_PS
                    Return ScanNewlineAsStatementTerminator(ch, precedingTrivia)

                Case " "c, UCH_TAB, "'"c
                    Debug.Fail(String.Format("Unexpected char: &H{0:x}", AscW(ch)))
                    Return Nothing ' trivia cannot start a token

                Case "@"c
                    Return MakeAtToken(precedingTrivia, False)

                Case "("c
                    Return MakeOpenParenToken(precedingTrivia, False)

                Case ")"c
                    Return MakeCloseParenToken(precedingTrivia, False)

                Case "{"c
                    Return MakeOpenBraceToken(precedingTrivia, False)

                Case "}"c
                    Return MakeCloseBraceToken(precedingTrivia, False)

                Case ","c
                    Return MakeCommaToken(precedingTrivia, False)

                Case "#"c
                    Dim dl = ScanDateLiteral(precedingTrivia)
                    If dl IsNot Nothing Then
                        Return dl
                    Else
                        Return MakeHashToken(precedingTrivia, False)
                    End If

                Case "&"c
                    If CanGetCharAtOffset(1) AndAlso BeginsBaseLiteral(PeekAheadChar(1)) Then
                        Return ScanNumericLiteral(precedingTrivia)
                    End If

                    Dim lengthWithMaybeEquals = 1
                    If TrySkipFollowingEquals(lengthWithMaybeEquals) Then
                        Return MakeAmpersandEqualsToken(precedingTrivia, lengthWithMaybeEquals)
                    Else
                        Return MakeAmpersandToken(precedingTrivia, False)
                    End If

                Case "="c
                    Return MakeEqualsToken(precedingTrivia, False)

                Case "<"c
                    Return ScanLeftAngleBracket(precedingTrivia, False, _scanSingleLineTriviaFunc)

                Case ">"c
                    Return ScanRightAngleBracket(precedingTrivia, False)

                Case ":"c
                    Dim lengthWithMaybeEquals = 1
                    If TrySkipFollowingEquals(lengthWithMaybeEquals) Then
                        Return MakeColonEqualsToken(precedingTrivia, lengthWithMaybeEquals)
                    Else
                        Return ScanColonAsStatementTerminator(precedingTrivia, False)
                    End If

                Case "+"c
                    Dim lengthWithMaybeEquals = 1
                    If TrySkipFollowingEquals(lengthWithMaybeEquals) Then
                        Return MakePlusEqualsToken(precedingTrivia, lengthWithMaybeEquals)
                    Else
                        Return MakePlusToken(precedingTrivia, False)
                    End If

                Case "-"c
                    Dim lengthWithMaybeEquals = 1
                    If TrySkipFollowingEquals(lengthWithMaybeEquals) Then
                        Return MakeMinusEqualsToken(precedingTrivia, lengthWithMaybeEquals)
                    Else
                        Return MakeMinusToken(precedingTrivia, False)
                    End If

                Case "*"c
                    Dim lengthWithMaybeEquals = 1
                    If TrySkipFollowingEquals(lengthWithMaybeEquals) Then
                        Return MakeAsteriskEqualsToken(precedingTrivia, lengthWithMaybeEquals)
                    Else
                        Return MakeAsteriskToken(precedingTrivia, False)
                    End If

                Case "/"c
                    Dim lengthWithMaybeEquals = 1
                    If TrySkipFollowingEquals(lengthWithMaybeEquals) Then
                        Return MakeSlashEqualsToken(precedingTrivia, lengthWithMaybeEquals)
                    Else
                        Return MakeSlashToken(precedingTrivia, False)
                    End If

                Case "\"c
                    Dim lengthWithMaybeEquals = 1
                    If TrySkipFollowingEquals(lengthWithMaybeEquals) Then
                        Return MakeBackSlashEqualsToken(precedingTrivia, lengthWithMaybeEquals)
                    Else
                        Return MakeBackslashToken(precedingTrivia, False)
                    End If

                Case "^"c
                    Dim lengthWithMaybeEquals = 1
                    If TrySkipFollowingEquals(lengthWithMaybeEquals) Then
                        Return MakeCaretEqualsToken(precedingTrivia, lengthWithMaybeEquals)
                    Else
                        Return MakeCaretToken(precedingTrivia, False)
                    End If

                Case "!"c
                    Return MakeExclamationToken(precedingTrivia, False)

                Case "."c
                    If CanGetCharAtOffset(1) AndAlso IsDecimalDigit(PeekAheadChar(1)) Then
                        Return ScanNumericLiteral(precedingTrivia)
                    Else
                        Return MakeDotToken(precedingTrivia, False)
                    End If

                Case "0"c,
                      "1"c,
                      "2"c,
                      "3"c,
                      "4"c,
                      "5"c,
                      "6"c,
                      "7"c,
                      "8"c,
                      "9"c
                    Return ScanNumericLiteral(precedingTrivia)

                Case """"c
                    Return ScanStringLiteral(precedingTrivia)

                Case "A"c
                    If CanGetCharAtOffset(2) AndAlso
                       PeekAheadChar(1) = "s"c AndAlso
                       PeekAheadChar(2) = " "c Then

                        ' TODO: do we allow widechars in keywords?
                        Dim spelling = "As"
                        AdvanceChar(2)
                        Return MakeKeyword(SyntaxKind.AsKeyword, spelling, precedingTrivia)
                    Else
                        Return ScanIdentifierOrKeyword(precedingTrivia)
                    End If

                Case "E"c
                    If CanGetCharAtOffset(3) AndAlso
                        PeekAheadChar(1) = "n"c AndAlso
                        PeekAheadChar(2) = "d"c AndAlso
                        PeekAheadChar(3) = " "c Then

                        ' TODO: do we allow widechars in keywords?
                        Dim spelling = "End"
                        AdvanceChar(3)
                        Return MakeKeyword(SyntaxKind.EndKeyword, spelling, precedingTrivia)
                    Else
                        Return ScanIdentifierOrKeyword(precedingTrivia)
                    End If

                Case "I"c
                    If CanGetCharAtOffset(2) AndAlso
                                           PeekAheadChar(1) = "f"c AndAlso
                                           PeekAheadChar(2) = " "c Then

                        ' TODO: do we allow widechars in keywords?
                        Dim spelling = "If"
                        AdvanceChar(2)
                        Return MakeKeyword(SyntaxKind.IfKeyword, spelling, precedingTrivia)
                    Else
                        Return ScanIdentifierOrKeyword(precedingTrivia)
                    End If

                Case "a"c To "z"c
                    Return ScanIdentifierOrKeyword(precedingTrivia)

                Case "B"c, "C"c, "D"c, "F"c, "G"c, "H"c, "J"c, "K"c, "L"c, "M"c, "N"c, "O"c, "P"c, "Q"c,
                      "R"c, "S"c, "T"c, "U"c, "V"c, "W"c, "X"c, "Y"c, "Z"c
                    Return ScanIdentifierOrKeyword(precedingTrivia)

                Case "_"c
                    If CanGetCharAtOffset(1) AndAlso IsIdentifierPartCharacter(PeekAheadChar(1)) Then
                        Return ScanIdentifierOrKeyword(precedingTrivia)
                    End If

                    Dim err As ERRID = ERRID.ERR_ExpectedIdentifier
                    Dim len = GetWhitespaceLength(1)
                    If Not CanGetCharAtOffset(len) OrElse IsNewLine(PeekAheadChar(len)) OrElse PeekStartComment(len) > 0 Then
                        err = ERRID.ERR_LineContWithCommentOrNoPrecSpace
                    End If

                    ' not a line continuation and cannot start identifier.
                    Return MakeBadToken(precedingTrivia, 1, err)

                Case "["c
                    Return ScanBracketedIdentifier(precedingTrivia)

                Case "?"c
                    Return MakeQuestionToken(precedingTrivia, False)

                Case "%"c
                    If CanGetCharAtOffset(1) AndAlso
                        PeekAheadChar(1) = ">"c Then
                        Return XmlMakeEndEmbeddedToken(precedingTrivia, _scanSingleLineTriviaFunc)
                    End If

            End Select

            If IsIdentifierStartCharacter(ch) Then
                Return ScanIdentifierOrKeyword(precedingTrivia)
            End If

            Debug.Assert(Not IsNewLine(ch))

            If IsDoubleQuote(ch) Then
                Return ScanStringLiteral(precedingTrivia)
            End If

            If ISFULLWIDTH(ch) Then
                ch = MAKEHALFWIDTH(ch)
                Return ScanTokenFullWidth(precedingTrivia, ch)
            End If

            Return Nothing
        End Function

        Private Function ScanTokenFullWidth(precedingTrivia As SyntaxList(Of VisualBasicSyntaxNode), ch As Char) As SyntaxToken
            Select Case ch
                Case UCH_CR, UCH_LF
                    Return ScanNewlineAsStatementTerminator(ch, precedingTrivia)

                Case " "c, UCH_TAB, "'"c
                    Debug.Fail(String.Format("Unexpected char: &H{0:x}", AscW(ch)))
                    Return Nothing ' trivia cannot start a token

                Case "@"c
                    Return MakeAtToken(precedingTrivia, True)

                Case "("c
                    Return MakeOpenParenToken(precedingTrivia, True)

                Case ")"c
                    Return MakeCloseParenToken(precedingTrivia, True)

                Case "{"c
                    Return MakeOpenBraceToken(precedingTrivia, True)

                Case "}"c
                    Return MakeCloseBraceToken(precedingTrivia, True)

                Case ","c
                    Return MakeCommaToken(precedingTrivia, True)

                Case "#"c
                    Dim dl = ScanDateLiteral(precedingTrivia)
                    If dl IsNot Nothing Then
                        Return dl
                    Else
                        Return MakeHashToken(precedingTrivia, True)
                    End If

                Case "&"c
                    If CanGetCharAtOffset(1) AndAlso BeginsBaseLiteral(PeekAheadChar(1)) Then
                        Return ScanNumericLiteral(precedingTrivia)
                    End If

                    Dim lengthWithMaybeEquals = 1
                    If TrySkipFollowingEquals(lengthWithMaybeEquals) Then
                        Return MakeAmpersandEqualsToken(precedingTrivia, lengthWithMaybeEquals)
                    Else
                        Return MakeAmpersandToken(precedingTrivia, True)
                    End If

                Case "="c
                    Return MakeEqualsToken(precedingTrivia, True)

                Case "<"c
                    Return ScanLeftAngleBracket(precedingTrivia, True, _scanSingleLineTriviaFunc)

                Case ">"c
                    Return ScanRightAngleBracket(precedingTrivia, True)

                Case ":"c
                    Dim lengthWithMaybeEquals = 1
                    If TrySkipFollowingEquals(lengthWithMaybeEquals) Then
                        Return MakeColonEqualsToken(precedingTrivia, lengthWithMaybeEquals)
                    Else
                        Return ScanColonAsStatementTerminator(precedingTrivia, True)
                    End If

                Case "+"c
                    Dim lengthWithMaybeEquals = 1
                    If TrySkipFollowingEquals(lengthWithMaybeEquals) Then
                        Return MakePlusEqualsToken(precedingTrivia, lengthWithMaybeEquals)
                    Else
                        Return MakePlusToken(precedingTrivia, True)
                    End If

                Case "-"c
                    Dim lengthWithMaybeEquals = 1
                    If TrySkipFollowingEquals(lengthWithMaybeEquals) Then
                        Return MakeMinusEqualsToken(precedingTrivia, lengthWithMaybeEquals)
                    Else
                        Return MakeMinusToken(precedingTrivia, True)
                    End If

                Case "*"c
                    Dim lengthWithMaybeEquals = 1
                    If TrySkipFollowingEquals(lengthWithMaybeEquals) Then
                        Return MakeAsteriskEqualsToken(precedingTrivia, lengthWithMaybeEquals)
                    Else
                        Return MakeAsteriskToken(precedingTrivia, True)
                    End If

                Case "/"c
                    Dim lengthWithMaybeEquals = 1
                    If TrySkipFollowingEquals(lengthWithMaybeEquals) Then
                        Return MakeSlashEqualsToken(precedingTrivia, lengthWithMaybeEquals)
                    Else
                        Return MakeSlashToken(precedingTrivia, True)
                    End If

                Case "\"c
                    Dim lengthWithMaybeEquals = 1
                    If TrySkipFollowingEquals(lengthWithMaybeEquals) Then
                        Return MakeBackSlashEqualsToken(precedingTrivia, lengthWithMaybeEquals)
                    Else
                        Return MakeBackslashToken(precedingTrivia, True)
                    End If

                Case "^"c
                    Dim lengthWithMaybeEquals = 1
                    If TrySkipFollowingEquals(lengthWithMaybeEquals) Then
                        Return MakeCaretEqualsToken(precedingTrivia, lengthWithMaybeEquals)
                    Else
                        Return MakeCaretToken(precedingTrivia, True)
                    End If

                Case "!"c
                    Return MakeExclamationToken(precedingTrivia, True)

                Case "."c
                    If CanGetCharAtOffset(1) AndAlso IsDecimalDigit(PeekAheadChar(1)) Then
                        Return ScanNumericLiteral(precedingTrivia)
                    Else
                        Return MakeDotToken(precedingTrivia, True)
                    End If

                Case "0"c,
                      "1"c,
                      "2"c,
                      "3"c,
                      "4"c,
                      "5"c,
                      "6"c,
                      "7"c,
                      "8"c,
                      "9"c
                    Return ScanNumericLiteral(precedingTrivia)

                Case """"c
                    Return ScanStringLiteral(precedingTrivia)

                Case "A"c
                    If CanGetCharAtOffset(2) AndAlso
                       PeekAheadChar(1) = "s"c AndAlso
                       PeekAheadChar(2) = " "c Then

                        Dim spelling = GetText(2)
                        Return MakeKeyword(SyntaxKind.AsKeyword, spelling, precedingTrivia)
                    Else
                        Return ScanIdentifierOrKeyword(precedingTrivia)
                    End If

                Case "E"c
                    If CanGetCharAtOffset(3) AndAlso
                        PeekAheadChar(1) = "n"c AndAlso
                        PeekAheadChar(2) = "d"c AndAlso
                        PeekAheadChar(3) = " "c Then

                        Dim spelling = GetText(3)
                        Return MakeKeyword(SyntaxKind.EndKeyword, spelling, precedingTrivia)
                    Else
                        Return ScanIdentifierOrKeyword(precedingTrivia)
                    End If

                Case "I"c
                    If CanGetCharAtOffset(2) AndAlso
                                           PeekAheadChar(1) = "f"c AndAlso
                                           PeekAheadChar(2) = " "c Then

                        ' TODO: do we allow widechars in keywords?
                        Dim spelling = GetText(2)
                        Return MakeKeyword(SyntaxKind.IfKeyword, spelling, precedingTrivia)
                    Else
                        Return ScanIdentifierOrKeyword(precedingTrivia)
                    End If

                Case "a"c To "z"c
                    Return ScanIdentifierOrKeyword(precedingTrivia)

                Case "B"c, "C"c, "D"c, "F"c, "G"c, "H"c, "J"c, "K"c, "L"c, "M"c, "N"c, "O"c, "P"c, "Q"c,
                      "R"c, "S"c, "T"c, "U"c, "V"c, "W"c, "X"c, "Y"c, "Z"c
                    Return ScanIdentifierOrKeyword(precedingTrivia)

                Case "_"c
                    If CanGetCharAtOffset(1) AndAlso IsIdentifierPartCharacter(PeekAheadChar(1)) Then
                        Return ScanIdentifierOrKeyword(precedingTrivia)
                    End If

                    Dim err As ERRID = ERRID.ERR_ExpectedIdentifier
                    Dim len = GetWhitespaceLength(1)
                    If Not CanGetCharAtOffset(len) OrElse IsNewLine(PeekAheadChar(len)) OrElse PeekStartComment(len) > 0 Then
                        err = ERRID.ERR_LineContWithCommentOrNoPrecSpace
                    End If

                    ' not a line continuation and cannot start identifier.
                    Return MakeBadToken(precedingTrivia, 1, err)

                Case "["c
                    Return ScanBracketedIdentifier(precedingTrivia)

                Case "?"c
                    Return MakeQuestionToken(precedingTrivia, True)

                Case "%"c
                    If CanGetCharAtOffset(1) AndAlso
                        PeekAheadChar(1) = ">"c Then
                        Return XmlMakeEndEmbeddedToken(precedingTrivia, _scanSingleLineTriviaFunc)
                    End If

            End Select

            If IsIdentifierStartCharacter(ch) Then
                Return ScanIdentifierOrKeyword(precedingTrivia)
            End If

            Debug.Assert(Not IsNewLine(ch))
            Debug.Assert(Not IsDoubleQuote(ch))

            Return Nothing
        End Function

        ' // Allow whitespace between the characters of a two-character token.
        Private Function TrySkipFollowingEquals(ByRef Index As Integer) As Boolean
            Debug.Assert(Index > 0)
            Debug.Assert(CanGetCharAtOffset(Index - 1))

            Dim Here = Index
            Dim eq As Char

            While CanGetCharAtOffset(Here)
                eq = PeekAheadChar(Here)
                Here += 1
                If Not IsWhitespace(eq) Then
                    If eq = "="c OrElse eq = FULLWIDTH_EQ Then
                        Index = Here
                        Return True
                    Else
                        Return False
                    End If
                End If
            End While
            Return False
        End Function

        Private Function ScanRightAngleBracket(precedingTrivia As SyntaxList(Of VisualBasicSyntaxNode), charIsFullWidth As Boolean) As SyntaxToken
            Debug.Assert(CanGetChar)  ' > 
            Debug.Assert(PeekChar() = ">"c OrElse PeekChar() = FULLWIDTH_GT)

            Dim length As Integer = 1

            ' // Allow whitespace between the characters of a two-character token.
            length = GetWhitespaceLength(length)

            If CanGetCharAtOffset(length) Then
                Dim c As Char = PeekAheadChar(length)

                If c = "="c OrElse c = FULLWIDTH_EQ Then
                    length += 1
                    Return MakeGreaterThanEqualsToken(precedingTrivia, length)
                ElseIf c = ">"c OrElse c = FULLWIDTH_GT Then
                    length += 1
                    If TrySkipFollowingEquals(length) Then
                        Return MakeGreaterThanGreaterThanEqualsToken(precedingTrivia, length)
                    Else
                        Return MakeGreaterThanGreaterThanToken(precedingTrivia, length)
                    End If
                End If
            End If
            Return MakeGreaterThanToken(precedingTrivia, charIsFullWidth)
        End Function

        Private Function ScanLeftAngleBracket(precedingTrivia As SyntaxList(Of VisualBasicSyntaxNode), charIsFullWidth As Boolean, scanTrailingTrivia As ScanTriviaFunc) As SyntaxToken
            Debug.Assert(CanGetChar)  ' < 
            Debug.Assert(PeekChar() = "<"c OrElse PeekChar() = FULLWIDTH_LT)

            Dim length As Integer = 1

            ' Check for XML tokens
            If Not charIsFullWidth AndAlso CanGetCharAtOffset(length) Then
                Dim c As Char = PeekAheadChar(length)
                Select Case c
                    Case "!"c
                        If CanGetCharAtOffset(length + 2) Then
                            Select Case (PeekAheadChar(length + 1))
                                Case "-"c
                                    If CanGetCharAtOffset(length + 3) AndAlso PeekAheadChar(length + 2) = "-"c Then
                                        Return XmlMakeBeginCommentToken(precedingTrivia, scanTrailingTrivia)
                                    End If
                                Case "["c
                                    If CanGetCharAtOffset(length + 8) AndAlso
                                        PeekAheadChar(length + 2) = "C"c AndAlso
                                        PeekAheadChar(length + 3) = "D"c AndAlso
                                        PeekAheadChar(length + 4) = "A"c AndAlso
                                        PeekAheadChar(length + 5) = "T"c AndAlso
                                        PeekAheadChar(length + 6) = "A"c AndAlso
                                        PeekAheadChar(length + 7) = "["c Then

                                        Return XmlMakeBeginCDataToken(precedingTrivia, scanTrailingTrivia)
                                    End If
                            End Select
                        End If
                    Case "?"c
                        Return XmlMakeBeginProcessingInstructionToken(precedingTrivia, scanTrailingTrivia)

                    Case "/"c
                        Return XmlMakeBeginEndElementToken(precedingTrivia, _scanSingleLineTriviaFunc)
                End Select
            End If

            ' // Allow whitespace between the characters of a two-character token.
            length = GetWhitespaceLength(length)

            If CanGetCharAtOffset(length) Then
                Dim c As Char = PeekAheadChar(length)

                If c = "="c OrElse c = FULLWIDTH_EQ Then
                    length += 1
                    Return MakeLessThanEqualsToken(precedingTrivia, length)
                ElseIf c = ">"c OrElse c = FULLWIDTH_GT Then
                    length += 1
                    Return MakeLessThanGreaterThanToken(precedingTrivia, length)
                ElseIf c = "<"c OrElse c = FULLWIDTH_LT Then
                    length += 1

                    If CanGetCharAtOffset(length) Then
                        c = PeekAheadChar(length)

                        'if the second "<" is a part of "<%" - like in "<<%" , we do not want to use it.
                        If c <> "%"c AndAlso c <> FULLWIDTH_PERCENT Then
                            If TrySkipFollowingEquals(length) Then
                                Return MakeLessThanLessThanEqualsToken(precedingTrivia, length)
                            Else
                                Return MakeLessThanLessThanToken(precedingTrivia, length)
                            End If
                        End If
                    End If
                End If
            End If

            Return MakeLessThanToken(precedingTrivia, charIsFullWidth)
        End Function

        Friend Shared Function IsIdentifier(spelling As String) As Boolean
            Dim spellingLength As Integer = spelling.Length
            If spellingLength = 0 Then
                Return False
            End If

            Dim c = spelling(0)
            If IsIdentifierStartCharacter(c) Then
                '  SPEC: ... Visual Basic identifiers conform to the Unicode Standard Annex 15 with one 
                '  SPEC:     exception: identifiers may begin with an underscore (connector) character. 
                '  SPEC:     If an identifier begins with an underscore, it must contain at least one other 
                '  SPEC:     valid identifier character to disambiguate it from a line continuation. 
                If IsConnectorPunctuation(c) AndAlso spellingLength = 1 Then
                    Return False
                End If

                For i = 1 To spellingLength - 1
                    If Not IsIdentifierPartCharacter(spelling(i)) Then
                        Return False
                    End If
                Next
            End If

            Return True
        End Function

        Private Function ScanIdentifierOrKeyword(precedingTrivia As SyntaxList(Of VisualBasicSyntaxNode)) As SyntaxToken
            Debug.Assert(CanGetChar)
            Debug.Assert(IsIdentifierStartCharacter(PeekChar))
            Debug.Assert(PeekStartComment(0) = 0) ' comment should be handled by caller

            Dim ch = PeekChar()
            If CanGetCharAtOffset(1) Then
                Dim ch1 = PeekAheadChar(1)
                If IsConnectorPunctuation(ch) AndAlso Not IsIdentifierPartCharacter(ch1) Then
                    Return MakeBadToken(precedingTrivia, 1, ERRID.ERR_ExpectedIdentifier)
                End If
            End If

            Dim len = 1 ' we know that the first char was good

            ' // The C++ compiler refuses to inline IsIdentifierCharacter, so the
            ' // < 128 test is inline here. (This loop gets a *lot* of traffic.)
            ' TODO: make sure we get good perf here
            While CanGetCharAtOffset(len)
                ch = PeekAheadChar(len)

                Dim code = Convert.ToUInt16(ch)
                If code < 128 AndAlso IsNarrowIdentifierCharacter(code) OrElse
                    IsWideIdentifierCharacter(ch) Then

                    len += 1
                Else
                    Exit While
                End If
            End While

            'Check for a type character
            Dim TypeCharacter As TypeCharacter = TypeCharacter.None
            If CanGetCharAtOffset(len) Then
                ch = PeekAheadChar(len)

FullWidthRepeat:
                Select Case ch
                    Case "!"c
                        ' // If the ! is followed by an identifier it is a dictionary lookup operator, not a type character.
                        If CanGetCharAtOffset(len + 1) Then
                            Dim NextChar As Char = PeekAheadChar(len + 1)

                            If IsIdentifierStartCharacter(NextChar) OrElse
                                MatchOneOrAnotherOrFullwidth(NextChar, "["c, "]"c) Then
                                Exit Select
                            End If
                        End If
                        TypeCharacter = TypeCharacter.Single  'typeChars.chType_sR4
                        len += 1

                    Case "#"c
                        TypeCharacter = TypeCharacter.Double ' typeChars.chType_sR8
                        len += 1

                    Case "$"c
                        TypeCharacter = TypeCharacter.String 'typeChars.chType_String
                        len += 1

                    Case "%"c
                        TypeCharacter = TypeCharacter.Integer ' typeChars.chType_sI4
                        len += 1

                    Case "&"c
                        TypeCharacter = TypeCharacter.Long 'typeChars.chType_sI8
                        len += 1

                    Case "@"c
                        TypeCharacter = TypeCharacter.Decimal 'chType_sDecimal
                        len += 1

                    Case Else
                        If ISFULLWIDTH(ch) Then
                            ch = MAKEHALFWIDTH(ch)
                            GoTo FullWidthRepeat
                        End If
                End Select
            End If

            Dim tokenType As SyntaxKind = SyntaxKind.IdentifierToken
            Dim contextualKind As SyntaxKind = SyntaxKind.IdentifierToken
            Dim spelling = GetText(len)

            Dim BaseSpelling = If(TypeCharacter = TypeCharacter.None,
                                   spelling,
                                   Intern(spelling, 0, len - 1))

            ' this can be keyword only if it has no type character, or if it is Mid$
            If TypeCharacter = TypeCharacter.None Then
                tokenType = TokenOfStringCached(spelling)
                If SyntaxFacts.IsContextualKeyword(tokenType) Then
                    contextualKind = tokenType
                    tokenType = SyntaxKind.IdentifierToken
                End If
            ElseIf TokenOfStringCached(BaseSpelling) = SyntaxKind.MidKeyword Then

                contextualKind = SyntaxKind.MidKeyword
                tokenType = SyntaxKind.IdentifierToken
            End If

            If tokenType <> SyntaxKind.IdentifierToken Then
                ' KEYWORD
                Return MakeKeyword(tokenType, spelling, precedingTrivia)
            Else
                ' IDENTIFIER or CONTEXTUAL
                Dim id As SyntaxToken = MakeIdentifier(spelling, contextualKind, False, BaseSpelling, TypeCharacter, precedingTrivia)
                Return id
            End If
        End Function

        Private Function TokenOfStringCached(spelling As String, Optional kind As SyntaxKind = SyntaxKind.IdentifierToken) As SyntaxKind
            If spelling.Length = 1 OrElse spelling.Length > 16 Then
                Return kind
            End If

            Return _KeywordsObjs.GetOrMakeValue(spelling)
        End Function

        Private Function ScanBracketedIdentifier(precedingTrivia As SyntaxList(Of VisualBasicSyntaxNode)) As SyntaxToken
            Debug.Assert(CanGetChar)  ' [
            Debug.Assert(PeekChar() = "["c OrElse PeekChar() = FULLWIDTH_LBR)

            Dim IdStart As Integer = 1
            Dim Here As Integer = IdStart

            Dim InvalidIdentifier As Boolean = False

            If Not CanGetCharAtOffset(Here) Then
                Return MakeBadToken(precedingTrivia, Here, ERRID.ERR_MissingEndBrack)
            End If

            Dim ch = PeekAheadChar(Here)

            ' check if we can start an ident.
            If Not IsIdentifierStartCharacter(ch) OrElse
                (IsConnectorPunctuation(ch) AndAlso
                    Not (CanGetCharAtOffset(Here + 1) AndAlso
                         IsIdentifierPartCharacter(PeekAheadChar(Here + 1)))) Then

                InvalidIdentifier = True
            End If

            ' check ident until ]
            While CanGetCharAtOffset(Here)
                Dim [Next] As Char = PeekAheadChar(Here)

                If [Next] = "]"c OrElse [Next] = FULLWIDTH_RBR Then
                    Dim IdStringLength As Integer = Here - IdStart

                    If IdStringLength > 0 AndAlso Not InvalidIdentifier Then
                        Dim spelling = GetText(IdStringLength + 2)
                        ' TODO: this should be provable?
                        Debug.Assert(spelling.Length > IdStringLength + 1)

                        ' TODO: consider interning.
                        Dim baseText = spelling.Substring(1, IdStringLength)
                        Dim id As SyntaxToken = MakeIdentifier(
                            spelling,
                            SyntaxKind.IdentifierToken,
                            True,
                            baseText,
                            TypeCharacter.None,
                            precedingTrivia)
                        Return id
                    Else
                        ' // The sequence "[]" does not define a valid identifier.
                        Return MakeBadToken(precedingTrivia, Here + 1, ERRID.ERR_ExpectedIdentifier)
                    End If
                ElseIf IsNewLine([Next]) Then
                    Exit While
                ElseIf Not IsIdentifierPartCharacter([Next]) Then
                    InvalidIdentifier = True
                    Exit While
                End If

                Here += 1
            End While

            If Here > 1 Then
                Return MakeBadToken(precedingTrivia, Here, ERRID.ERR_MissingEndBrack)
            Else
                Return MakeBadToken(precedingTrivia, Here, ERRID.ERR_ExpectedIdentifier)
            End If
        End Function

        Private Enum NumericLiteralKind
            Integral
            Float
            [Decimal]
        End Enum

        Private Function ScanNumericLiteral(precedingTrivia As SyntaxList(Of VisualBasicSyntaxNode)) As SyntaxToken
            Debug.Assert(CanGetChar)

            Dim Here As Integer = 0
            Dim IntegerLiteralStart As Integer

            Dim Base As LiteralBase = LiteralBase.Decimal
            Dim literalKind As NumericLiteralKind = NumericLiteralKind.Integral

            ' ####################################################
            ' // Validate literal and find where the number starts and ends.
            ' ####################################################

            ' // First read a leading base specifier, if present, followed by a sequence of zero
            ' // or more digits.
            Dim ch = PeekChar()
            If ch = "&"c OrElse ch = FULLWIDTH_AMP Then
                Here += 1
                ch = If(CanGetCharAtOffset(Here), PeekAheadChar(Here), ChrW(0))

FullWidthRepeat:
                Select Case ch
                    Case "H"c, "h"c
                        Here += 1
                        IntegerLiteralStart = Here
                        Base = LiteralBase.Hexadecimal

                        While CanGetCharAtOffset(Here)
                            ch = PeekAheadChar(Here)
                            If Not IsHexDigit(ch) Then
                                Exit While
                            End If
                            Here += 1
                        End While

                    Case "O"c, "o"c
                        Here += 1
                        IntegerLiteralStart = Here
                        Base = LiteralBase.Octal

                        While CanGetCharAtOffset(Here)
                            ch = PeekAheadChar(Here)
                            If Not IsOctalDigit(ch) Then
                                Exit While
                            End If
                            Here += 1
                        End While

                    Case Else
                        If ISFULLWIDTH(ch) Then
                            ch = MAKEHALFWIDTH(ch)
                            GoTo FullWidthRepeat
                        End If

                        Throw ExceptionUtilities.UnexpectedValue(ch)
                End Select
            Else
                ' no base specifier - just go through decimal digits.
                IntegerLiteralStart = Here
                While CanGetCharAtOffset(Here)
                    ch = PeekAheadChar(Here)
                    If Not IsDecimalDigit(ch) Then
                        Exit While
                    End If
                    Here += 1
                End While
            End If

            ' we may have a dot, and then it is a float, but if this is an integral, then we have seen it all.
            Dim IntegerLiteralEnd As Integer = Here

            ' // Unless there was an explicit base specifier (which indicates an integer literal),
            ' // read the rest of a float literal.
            If Base = LiteralBase.Decimal AndAlso CanGetCharAtOffset(Here) Then
                ' // First read a '.' followed by a sequence of one or more digits.
                ch = PeekAheadChar(Here)
                If (ch = "."c Or ch = FULLWIDTH_DOT) AndAlso
                        CanGetCharAtOffset(Here + 1) AndAlso
                        IsDecimalDigit(PeekAheadChar(Here + 1)) Then

                    Here += 2   ' skip dot and first digit

                    ' all following decimal digits belong to the literal (fractional part)
                    While CanGetCharAtOffset(Here)
                        ch = PeekAheadChar(Here)
                        If Not IsDecimalDigit(ch) Then
                            Exit While
                        End If
                        Here += 1
                    End While
                    literalKind = NumericLiteralKind.Float
                End If

                ' // Read an exponent symbol followed by an optional sign and a sequence of
                ' // one or more digits.
                If CanGetCharAtOffset(Here) AndAlso BeginsExponent(PeekAheadChar(Here)) Then
                    Here += 1

                    If CanGetCharAtOffset(Here) Then
                        ch = PeekAheadChar(Here)

                        If MatchOneOrAnotherOrFullwidth(ch, "+"c, "-"c) Then
                            Here += 1
                        End If
                    End If

                    If CanGetCharAtOffset(Here) AndAlso IsDecimalDigit(PeekAheadChar(Here)) Then
                        Here += 1
                        While CanGetCharAtOffset(Here)
                            ch = PeekAheadChar(Here)
                            If Not IsDecimalDigit(ch) Then
                                Exit While
                            End If
                            Here += 1
                        End While
                    Else
                        Return MakeBadToken(precedingTrivia, Here, ERRID.ERR_InvalidLiteralExponent)
                    End If

                    literalKind = NumericLiteralKind.Float
                End If
            End If

            Dim literalWithoutTypeChar = Here

            ' ####################################################
            ' // Read a trailing type character.
            ' ####################################################

            Dim TypeCharacter As TypeCharacter = TypeCharacter.None

            If CanGetCharAtOffset(Here) Then
                ch = PeekAheadChar(Here)

FullWidthRepeat2:
                Select Case ch
                    Case "!"c
                        If Base = LiteralBase.Decimal Then
                            TypeCharacter = TypeCharacter.Single
                            literalKind = NumericLiteralKind.Float
                            Here += 1
                        End If

                    Case "F"c, "f"c
                        If Base = LiteralBase.Decimal Then
                            TypeCharacter = TypeCharacter.SingleLiteral
                            literalKind = NumericLiteralKind.Float
                            Here += 1
                        End If

                    Case "#"c
                        If Base = LiteralBase.Decimal Then
                            TypeCharacter = TypeCharacter.Double
                            literalKind = NumericLiteralKind.Float
                            Here += 1
                        End If

                    Case "R"c, "r"c
                        If Base = LiteralBase.Decimal Then
                            TypeCharacter = TypeCharacter.DoubleLiteral
                            literalKind = NumericLiteralKind.Float
                            Here += 1
                        End If

                    Case "S"c, "s"c

                        If literalKind <> NumericLiteralKind.Float Then
                            TypeCharacter = TypeCharacter.ShortLiteral
                            Here += 1
                        End If

                    Case "%"c
                        If literalKind <> NumericLiteralKind.Float Then
                            TypeCharacter = TypeCharacter.Integer
                            Here += 1
                        End If

                    Case "I"c, "i"c
                        If literalKind <> NumericLiteralKind.Float Then
                            TypeCharacter = TypeCharacter.IntegerLiteral
                            Here += 1
                        End If

                    Case "&"c
                        If literalKind <> NumericLiteralKind.Float Then
                            TypeCharacter = TypeCharacter.Long
                            Here += 1
                        End If

                    Case "L"c, "l"c
                        If literalKind <> NumericLiteralKind.Float Then
                            TypeCharacter = TypeCharacter.LongLiteral
                            Here += 1
                        End If

                    Case "@"c
                        If Base = LiteralBase.Decimal Then
                            TypeCharacter = TypeCharacter.Decimal
                            literalKind = NumericLiteralKind.Decimal
                            Here += 1
                        End If

                    Case "D"c, "d"c
                        If Base = LiteralBase.Decimal Then
                            TypeCharacter = TypeCharacter.DecimalLiteral
                            literalKind = NumericLiteralKind.Decimal

                            ' check if this was not attempt to use obsolete exponent
                            If CanGetCharAtOffset(Here + 1) Then
                                ch = PeekAheadChar(Here + 1)

                                If IsDecimalDigit(ch) OrElse MatchOneOrAnotherOrFullwidth(ch, "+"c, "-"c) Then
                                    Return MakeBadToken(precedingTrivia, Here, ERRID.ERR_ObsoleteExponent)
                                End If
                            End If

                            Here += 1
                        End If

                    Case "U"c, "u"c
                        If literalKind <> NumericLiteralKind.Float AndAlso CanGetCharAtOffset(Here + 1) Then
                            Dim NextChar As Char = PeekAheadChar(Here + 1)

                            'unsigned suffixes - US, UL, UI
                            If MatchOneOrAnotherOrFullwidth(NextChar, "S"c, "s"c) Then
                                TypeCharacter = TypeCharacter.UShortLiteral
                                Here += 2
                            ElseIf MatchOneOrAnotherOrFullwidth(NextChar, "I"c, "i"c) Then
                                TypeCharacter = TypeCharacter.UIntegerLiteral
                                Here += 2
                            ElseIf MatchOneOrAnotherOrFullwidth(NextChar, "L"c, "l"c) Then
                                TypeCharacter = TypeCharacter.ULongLiteral
                                Here += 2
                            End If
                        End If

                    Case Else
                        If ISFULLWIDTH(ch) Then
                            ch = MAKEHALFWIDTH(ch)
                            GoTo FullWidthRepeat2
                        End If
                End Select
            End If

            ' ####################################################
            ' //  Produce a value for the literal.
            ' ####################################################

            Dim IntegralValue As UInt64
            Dim FloatingValue As Double
            Dim DecimalValue As Decimal
            Dim Overflows As Boolean = False

            If literalKind = NumericLiteralKind.Integral Then
                If IntegerLiteralStart = IntegerLiteralEnd Then
                    Return MakeBadToken(precedingTrivia, Here, ERRID.ERR_Syntax)
                Else
                    IntegralValue = IntegralLiteralCharacterValue(PeekAheadChar(IntegerLiteralStart))

                    If Base = LiteralBase.Decimal Then
                        ' Init For loop
                        For LiteralCharacter As Integer = IntegerLiteralStart + 1 To IntegerLiteralEnd - 1
                            Dim NextCharacterValue As UInteger = IntegralLiteralCharacterValue(PeekAheadChar(LiteralCharacter))

                            If IntegralValue < 1844674407370955161UL OrElse
                              (IntegralValue = 1844674407370955161UL AndAlso NextCharacterValue <= 5UI) Then

                                IntegralValue = (IntegralValue * 10UL) + NextCharacterValue
                            Else
                                Overflows = True
                                Exit For
                            End If
                        Next

                        If TypeCharacter <> TypeCharacter.ULongLiteral AndAlso IntegralValue > Long.MaxValue Then
                            Overflows = True
                        End If
                    Else
                        Dim Shift As Integer = If(Base = LiteralBase.Hexadecimal, 4, 3)
                        Dim OverflowMask As UInt64 = If(Base = LiteralBase.Hexadecimal, &HF000000000000000UL, &HE000000000000000UL)

                        ' Init For loop
                        For LiteralCharacter As Integer = IntegerLiteralStart + 1 To IntegerLiteralEnd - 1
                            If (IntegralValue And OverflowMask) <> 0 Then
                                Overflows = True
                            End If

                            IntegralValue = (IntegralValue << Shift) + IntegralLiteralCharacterValue(PeekAheadChar(LiteralCharacter))
                        Next
                    End If

                    If TypeCharacter = TypeCharacter.None Then
                        ' nothing to do
                    ElseIf TypeCharacter = TypeCharacter.Integer OrElse TypeCharacter = TypeCharacter.IntegerLiteral Then
                        If (Base = LiteralBase.Decimal AndAlso IntegralValue > &H7FFFFFFF) OrElse
                            IntegralValue > &HFFFFFFFFUI Then

                            Overflows = True
                        End If

                    ElseIf TypeCharacter = TypeCharacter.UIntegerLiteral Then
                        If IntegralValue > &HFFFFFFFFUI Then
                            Overflows = True
                        End If

                    ElseIf TypeCharacter = TypeCharacter.ShortLiteral Then
                        If (Base = LiteralBase.Decimal AndAlso IntegralValue > &H7FFF) OrElse
                            IntegralValue > &HFFFF Then

                            Overflows = True
                        End If

                    ElseIf TypeCharacter = TypeCharacter.UShortLiteral Then
                        If IntegralValue > &HFFFF Then
                            Overflows = True
                        End If

                    Else
                        Debug.Assert(TypeCharacter = TypeCharacter.Long OrElse
                                 TypeCharacter = TypeCharacter.LongLiteral OrElse
                                 TypeCharacter = TypeCharacter.ULongLiteral,
                        "Integral literal value computation is lost.")
                    End If
                End If

            Else
                ' // Copy the text of the literal to deal with fullwidth 
                Dim scratch = GetScratch()
                For i = 0 To literalWithoutTypeChar - 1
                    Dim curCh = PeekAheadChar(i)
                    scratch.Append(If(ISFULLWIDTH(curCh), MAKEHALFWIDTH(curCh), curCh))
                Next
                Dim LiteralSpelling = GetScratchTextInterned(scratch)

                If literalKind = NumericLiteralKind.Decimal Then
                    ' Attempt to convert to Decimal.
                    Overflows = Not GetDecimalValue(LiteralSpelling, DecimalValue)
                Else
                    If TypeCharacter = TypeCharacter.Single OrElse TypeCharacter = TypeCharacter.SingleLiteral Then
                        ' // Attempt to convert to single
                        Dim SingleValue As Single
                        If Not Single.TryParse(LiteralSpelling, NumberStyles.Float, CultureInfo.InvariantCulture, SingleValue) Then
                            Overflows = True
                        Else
                            FloatingValue = SingleValue
                        End If
                    Else
                        ' // Attempt to convert to double.
                        If Not Double.TryParse(LiteralSpelling, NumberStyles.Float, CultureInfo.InvariantCulture, FloatingValue) Then
                            Overflows = True
                        End If
                    End If
                End If
            End If

            Dim result As SyntaxToken
            Select Case literalKind
                Case NumericLiteralKind.Integral
                    result = MakeIntegerLiteralToken(precedingTrivia, Base, TypeCharacter, If(Overflows, 0UL, IntegralValue), Here)
                Case NumericLiteralKind.Float
                    result = MakeFloatingLiteralToken(precedingTrivia, TypeCharacter, If(Overflows, 0.0F, FloatingValue), Here)
                Case NumericLiteralKind.Decimal
                    result = MakeDecimalLiteralToken(precedingTrivia, TypeCharacter, If(Overflows, 0D, DecimalValue), Here)
                Case Else
                    Throw ExceptionUtilities.UnexpectedValue(literalKind)
            End Select

            If Overflows Then
                result = DirectCast(result.AddError(ErrorFactory.ErrorInfo(ERRID.ERR_Overflow)), SyntaxToken)
            End If

            Return result
        End Function

        Private Shared Function GetDecimalValue(text As String, <Out()> ByRef value As Decimal) As Boolean

            ' Use Decimal.TryParse to parse value. Note: the behavior of
            ' Decimal.TryParse differs from Dev11 in the following cases:
            '
            ' 1. [-]0eNd where N > 0
            '     The native compiler ignores sign and scale and treats such cases
            '     as 0e0d. Decimal.TryParse fails so these cases are compile errors.
            '     [Bug #568475]
            ' 2. Decimals with significant digits below 1e-49
            '     The native compiler considers digits below 1e-49 when rounding.
            '     Decimal.TryParse ignores digits below 1e-49 when rounding. This
            '     difference is perhaps the most significant since existing code will
            '     continue to compile but constant values may be rounded differently.
            '     [Bug #568494]

            Return Decimal.TryParse(text, NumberStyles.AllowDecimalPoint Or NumberStyles.AllowExponent, CultureInfo.InvariantCulture, value)
        End Function

        Private Function ScanIntLiteral(
               ByRef ReturnValue As Integer,
               ByRef Here As Integer
           ) As Boolean
            Debug.Assert(Here >= 0)

            If Not CanGetCharAtOffset(Here) Then
                Return False
            End If

            Dim ch = PeekAheadChar(Here)
            If Not IsDecimalDigit(ch) Then
                Return False
            End If

            Dim IntegralValue As Integer = IntegralLiteralCharacterValue(ch)
            Here += 1

            While CanGetCharAtOffset(Here)
                ch = PeekAheadChar(Here)

                If Not IsDecimalDigit(ch) Then
                    Exit While
                End If

                Dim nextDigit = IntegralLiteralCharacterValue(ch)
                If IntegralValue < 214748364 OrElse
                    (IntegralValue = 214748364 AndAlso nextDigit < 8) Then

                    IntegralValue = IntegralValue * 10 + nextDigit
                    Here += 1
                Else
                    Return False
                End If
            End While

            ReturnValue = IntegralValue
            Return True
        End Function

        Private Function ScanDateLiteral(precedingTrivia As SyntaxList(Of VisualBasicSyntaxNode)) As SyntaxToken
            Debug.Assert(CanGetChar)
            Debug.Assert(IsHash(PeekChar()))

            Dim Here As Integer = 1 'skip #
            Dim FirstValue As Integer
            Dim YearValue, MonthValue, DayValue, HourValue, MinuteValue, SecondValue As Integer
            Dim HaveDateValue As Boolean = False
            Dim HaveYearValue As Boolean = False
            Dim HaveTimeValue As Boolean = False
            Dim HaveMinuteValue As Boolean = False
            Dim HaveSecondValue As Boolean = False
            Dim HaveAM As Boolean = False
            Dim HavePM As Boolean = False
            Dim DateIsInvalid As Boolean = False
            Dim YearIsTwoDigits As Boolean = False
            Dim DaysToMonth As Integer() = Nothing

            ' // Unfortunately, we can't fall back on OLE Automation's date parsing because
            ' // they don't have the same range as the URT's DateTime class

            ' // First, eat any whitespace
            Here = GetWhitespaceLength(Here)

            Dim FirstValueStart As Integer = Here

            ' // The first thing has to be an integer, although it's not clear what it is yet
            If Not ScanIntLiteral(FirstValue, Here) Then
                Return Nothing

            End If

            ' // If we see a /, then it's a date

            If CanGetCharAtOffset(Here) AndAlso IsDateSeparatorCharacter(PeekAheadChar(Here)) Then
                Dim FirstDateSeparator As Integer = Here

                ' // We've got a date
                HaveDateValue = True
                Here += 1

                ' Is the first value a year? 
                ' It is a year if it consists of exactly 4 digits.
                ' Condition below uses 5 because we already skipped the separator.
                If Here - FirstValueStart = 5 Then
                    HaveYearValue = True
                    YearValue = FirstValue

                    ' // We have to have a month value
                    If Not ScanIntLiteral(MonthValue, Here) Then
                        GoTo baddate
                    End If

                    ' Do we have a day value?
                    If CanGetCharAtOffset(Here) AndAlso IsDateSeparatorCharacter(PeekAheadChar(Here)) Then
                        ' // Check to see they used a consistent separator

                        If PeekAheadChar(Here) <> PeekAheadChar(FirstDateSeparator) Then
                            GoTo baddate
                        End If

                        ' // Yes.
                        Here += 1

                        If Not ScanIntLiteral(DayValue, Here) Then
                            GoTo baddate
                        End If
                    End If
                Else
                    ' First value is month
                    MonthValue = FirstValue

                    ' // We have to have a day value

                    If Not ScanIntLiteral(DayValue, Here) Then
                        GoTo baddate
                    End If

                    ' // Do we have a year value?

                    If CanGetCharAtOffset(Here) AndAlso IsDateSeparatorCharacter(PeekAheadChar(Here)) Then
                        ' // Check to see they used a consistent separator

                        If PeekAheadChar(Here) <> PeekAheadChar(FirstDateSeparator) Then
                            GoTo baddate
                        End If

                        ' // Yes.
                        HaveYearValue = True
                        Here += 1

                        Dim YearStart As Integer = Here

                        If Not ScanIntLiteral(YearValue, Here) Then
                            GoTo baddate
                        End If

                        If (Here - YearStart) = 2 Then
                            YearIsTwoDigits = True
                        End If
                    End If
                End If

                Here = GetWhitespaceLength(Here)
            End If

            ' // If we haven't seen a date, assume it's a time value

            If Not HaveDateValue Then
                HaveTimeValue = True
                HourValue = FirstValue
            Else
                ' // We did see a date. See if we see a time value...

                If ScanIntLiteral(HourValue, Here) Then
                    ' // Yup.
                    HaveTimeValue = True
                End If
            End If

            If HaveTimeValue Then
                ' // Do we see a :?

                If CanGetCharAtOffset(Here) AndAlso IsColon(PeekAheadChar(Here)) Then
                    Here += 1

                    ' // Now let's get the minute value

                    If Not ScanIntLiteral(MinuteValue, Here) Then
                        GoTo baddate
                    End If

                    HaveMinuteValue = True

                    ' // Do we have a second value?

                    If CanGetCharAtOffset(Here) AndAlso IsColon(PeekAheadChar(Here)) Then
                        ' // Yes.
                        HaveSecondValue = True
                        Here += 1

                        If Not ScanIntLiteral(SecondValue, Here) Then
                            GoTo baddate
                        End If
                    End If
                End If

                Here = GetWhitespaceLength(Here)

                ' // Check AM/PM

                If CanGetCharAtOffset(Here) Then
                    If PeekAheadChar(Here) = "A"c OrElse PeekAheadChar(Here) = FULLWIDTH_Ah OrElse
                        PeekAheadChar(Here) = "a"c OrElse PeekAheadChar(Here) = FULLWIDTH_Al Then

                        HaveAM = True
                        Here += 1

                    ElseIf PeekAheadChar(Here) = "P"c OrElse PeekAheadChar(Here) = FULLWIDTH_Ph OrElse
                           PeekAheadChar(Here) = "p"c OrElse PeekAheadChar(Here) = FULLWIDTH_pl Then

                        HavePM = True
                        Here += 1

                    End If

                    If CanGetCharAtOffset(Here) AndAlso (HaveAM OrElse HavePM) Then
                        If PeekAheadChar(Here) = "M"c OrElse PeekAheadChar(Here) = FULLWIDTH_Mh OrElse
                           PeekAheadChar(Here) = "m"c OrElse PeekAheadChar(Here) = FULLWIDTH_ml Then

                            Here = GetWhitespaceLength(Here + 1)

                        Else
                            GoTo baddate
                        End If
                    End If
                End If

                ' // If there's no minute/second value and no AM/PM, it's invalid

                If Not HaveMinuteValue AndAlso Not HaveAM AndAlso Not HavePM Then
                    GoTo baddate
                End If
            End If

            If Not CanGetCharAtOffset(Here) OrElse Not IsHash(PeekAheadChar(Here)) Then
                ' // Oooh, so close. But no cigar
                GoTo baddate
            End If

            Here += 1

            ' // OK, now we've got all the values, let's see if we've got a valid date
            If HaveDateValue Then
                If MonthValue < 1 OrElse MonthValue > 12 Then
                    DateIsInvalid = True
                End If

                ' // We'll check Days in a moment...

                If Not HaveYearValue Then
                    DateIsInvalid = True
                    YearValue = 1
                End If

                ' // Check if not a leap year

                If Not ((YearValue Mod 4 = 0) AndAlso (Not (YearValue Mod 100 = 0) OrElse (YearValue Mod 400 = 0))) Then
                    DaysToMonth = DaysToMonth365
                Else
                    DaysToMonth = DaysToMonth366
                End If

                If DayValue < 1 OrElse
                   (Not DateIsInvalid AndAlso DayValue > DaysToMonth(MonthValue) - DaysToMonth(MonthValue - 1)) Then

                    DateIsInvalid = True
                End If

                If YearIsTwoDigits Then
                    DateIsInvalid = True
                End If

                If YearValue < 1 OrElse YearValue > 9999 Then
                    DateIsInvalid = True
                End If

            Else
                MonthValue = 1
                DayValue = 1
                YearValue = 1
                DaysToMonth = DaysToMonth365
            End If

            If HaveTimeValue Then
                If HaveAM OrElse HavePM Then
                    ' // 12-hour value

                    If HourValue < 1 OrElse HourValue > 12 Then
                        DateIsInvalid = True
                    End If

                    If HaveAM Then
                        HourValue = HourValue Mod 12
                    ElseIf HavePM Then
                        HourValue = HourValue + 12

                        If HourValue = 24 Then
                            HourValue = 12
                        End If
                    End If

                Else
                    If HourValue < 0 OrElse HourValue > 23 Then
                        DateIsInvalid = True
                    End If
                End If

                If HaveMinuteValue Then
                    If MinuteValue < 0 OrElse MinuteValue > 59 Then
                        DateIsInvalid = True
                    End If
                Else
                    MinuteValue = 0
                End If

                If HaveSecondValue Then
                    If SecondValue < 0 OrElse SecondValue > 59 Then
                        DateIsInvalid = True
                    End If
                Else
                    SecondValue = 0
                End If
            Else
                HourValue = 0
                MinuteValue = 0
                SecondValue = 0
            End If

            ' // Ok, we've got a valid value. Now make into an i8.

            If Not DateIsInvalid Then
                Dim DateTimeValue As New DateTime(YearValue, MonthValue, DayValue, HourValue, MinuteValue, SecondValue)
                Return MakeDateLiteralToken(precedingTrivia, DateTimeValue, Here)
            Else
                Return MakeBadToken(precedingTrivia, Here, ERRID.ERR_InvalidDate)
            End If

baddate:
            ' // If we can find a closing #, then assume it's a malformed date,
            ' // otherwise, it's not a date

            While CanGetCharAtOffset(Here)
                Dim ch As Char = PeekAheadChar(Here)
                If IsHash(ch) OrElse IsNewLine(ch) Then
                    Exit While
                End If
                Here += 1
            End While

            If Not CanGetCharAtOffset(Here) OrElse IsNewLine(PeekAheadChar(Here)) Then
                ' // No closing #
                Return Nothing
            Else
                Debug.Assert(IsHash(PeekAheadChar(Here)))
                Here += 1  ' consume trailing #
                Return MakeBadToken(precedingTrivia, Here, ERRID.ERR_InvalidDate)
            End If
        End Function

        Private Function ScanStringLiteral(precedingTrivia As SyntaxList(Of VisualBasicSyntaxNode)) As SyntaxToken
            Debug.Assert(CanGetChar)
            Debug.Assert(IsDoubleQuote(PeekChar))

            Dim length As Integer = 1
            Dim ch As Char
            Dim followingTrivia As SyntaxList(Of VisualBasicSyntaxNode)

            ' // Check for a Char literal, which can be of the form:
            ' // """"c or "<anycharacter-except-">"c

            If CanGetCharAtOffset(3) AndAlso IsDoubleQuote(PeekAheadChar(2)) Then
                If IsDoubleQuote(PeekAheadChar(1)) Then
                    If IsDoubleQuote(PeekAheadChar(3)) AndAlso
                       CanGetCharAtOffset(4) AndAlso
                       IsLetterC(PeekAheadChar(4)) Then

                        ' // Double-quote Char literal: """"c
                        Return MakeCharacterLiteralToken(precedingTrivia, """"c, 5)
                    End If

                ElseIf IsLetterC(PeekAheadChar(3)) Then
                    ' // Char literal.  "x"c
                    Return MakeCharacterLiteralToken(precedingTrivia, PeekAheadChar(1), 4)
                End If
            End If

            If CanGetCharAtOffset(2) AndAlso
               IsDoubleQuote(PeekAheadChar(1)) AndAlso
               IsLetterC(PeekAheadChar(2)) Then

                ' // Error. ""c is not a legal char constant
                Return MakeBadToken(precedingTrivia, 3, ERRID.ERR_IllegalCharConstant)
            End If

            Dim scratch = GetScratch()
            While CanGetCharAtOffset(length)
                ch = PeekAheadChar(length)

                If IsDoubleQuote(ch) Then
                    If CanGetCharAtOffset(length + 1) Then
                        ch = PeekAheadChar(length + 1)

                        If IsDoubleQuote(ch) Then
                            ' // An escaped double quote
                            scratch.Append(""""c)
                            length += 2
                            Continue While
                        Else
                            ' // The end of the char literal.
                            If IsLetterC(ch) Then
                                ' // Error. "aad"c is not a legal char constant

                                ' // +2 to include both " and c in the token span
                                scratch.Clear()
                                Return MakeBadToken(precedingTrivia, length + 2, ERRID.ERR_IllegalCharConstant)
                            End If
                        End If
                    End If

                    ' the double quote was a valid string terminator.
                    length += 1
                    Dim spelling = GetTextNotInterned(length)
                    followingTrivia = ScanSingleLineTrivia()

                    ' NATURAL TEXT, NO INTERNING
                    Return SyntaxFactory.StringLiteralToken(spelling, GetScratchText(scratch), precedingTrivia.Node, followingTrivia.Node)

                ElseIf Me.IsScanningDirective AndAlso IsNewLine(ch) Then
                    Exit While
                End If

                scratch.Append(ch)
                length += 1
            End While

            ' CC has trouble to prove this after the loop
            Debug.Assert(CanGetCharAtOffset(length - 1))

            '// The literal does not have an explicit termination.      
            ' DIFFERENT: here in IDE we used to report string token marked as unterminated

            Dim sp = GetTextNotInterned(length)
            followingTrivia = ScanSingleLineTrivia()
            Dim strTk = SyntaxFactory.StringLiteralToken(sp, GetScratchText(scratch), precedingTrivia.Node, followingTrivia.Node)
            Dim StrTkErr = strTk.SetDiagnostics({ErrorFactory.ErrorInfo(ERRID.ERR_UnterminatedStringLiteral)})

            Debug.Assert(StrTkErr IsNot Nothing)
            Return DirectCast(StrTkErr, SyntaxToken)
        End Function

        Friend Shared Function TryIdentifierAsContextualKeyword(id As IdentifierTokenSyntax, ByRef k As SyntaxKind) As Boolean
            Debug.Assert(id IsNot Nothing)

            If id.PossibleKeywordKind <> SyntaxKind.IdentifierToken Then
                k = id.PossibleKeywordKind
                Return True
            End If

            Return False
        End Function

        ''' <summary>
        ''' Try to convert an Identifier to a Keyword.  Called by the parser when it wants to force
        ''' an identifer to be a keyword.
        ''' </summary>
        Friend Function TryIdentifierAsContextualKeyword(id As IdentifierTokenSyntax, ByRef k As KeywordSyntax) As Boolean
            Debug.Assert(id IsNot Nothing)

            Dim kind As SyntaxKind = SyntaxKind.IdentifierToken
            If TryIdentifierAsContextualKeyword(id, kind) Then
                k = MakeKeyword(id)
                Return True
            End If

            Return False
        End Function

        Friend Function TryTokenAsContextualKeyword(t As SyntaxToken, ByRef k As KeywordSyntax) As Boolean
            If t Is Nothing Then
                Return False
            End If

            If t.Kind = SyntaxKind.IdentifierToken Then
                Return TryIdentifierAsContextualKeyword(DirectCast(t, IdentifierTokenSyntax), k)
            End If

            Return False
        End Function

        Friend Shared Function TryTokenAsKeyword(t As SyntaxToken, ByRef kind As SyntaxKind) As Boolean

            If t Is Nothing Then
                Return False
            End If

            If t.IsKeyword Then
                kind = t.Kind
                Return True
            End If

            If t.Kind = SyntaxKind.IdentifierToken Then
                Return TryIdentifierAsContextualKeyword(DirectCast(t, IdentifierTokenSyntax), kind)
            End If

            Return False
        End Function

        Friend Shared Function IsContextualKeyword(t As SyntaxToken, ParamArray kinds As SyntaxKind()) As Boolean
            Dim kind As SyntaxKind = Nothing
            If TryTokenAsKeyword(t, kind) Then
                Return Array.IndexOf(kinds, kind) >= 0
            End If
            Return False
        End Function
    End Class
End Namespace