﻿' Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports System.Collections.Generic
Imports System.Collections.Immutable
Imports System.Text
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax

Namespace Microsoft.CodeAnalysis.VisualBasic.Symbols

    ''' <summary>
    ''' Immutable. Thread-safe.
    ''' 
    ''' Represents a type substitution, with substitutions of types for a set of type parameters.
    ''' Each TypeSubstitution object has three pieces of information:
    '''    - OriginalDefinition of generic symbol the substitution is targeting.
    '''    - An array of pairs that provide a mapping from symbol's type parameters to type arguments.
    '''      identity substitutions are omitted.
    '''    - TypeSubstitution object for containing type to provide mapping for its type
    '''      parameters, if any. 
    ''' 
    ''' The identity substitution (for the whole type hierarchy) is represented by Nothing. That said,
    ''' top level parent of non-Nothing instance of TypeSubstitution is guaranteed to be non-identity 
    ''' substitution. The instance may still be an identity substitution just for target generic definition,
    ''' which will be represented by an empty mapping array. 
    ''' 
    ''' The chain of TypeSubstitution objects is guaranteed to not skip any type in the containership hierarchy,
    ''' even types with zero arity contained in generic type will have corresponding TypeSubstitution object with
    ''' empty mapping array.
    ''' 
    ''' Example:
    '''     Class A(Of T,S)
    '''          Class B
    '''              Class C(Of U)
    '''              End Class
    '''          End Class
    '''     End Class 
    ''' 
    ''' TypeSubstitution for A(Of Integer, S).B.C(Of Byte) is C{U->Byte}=>B{}=>A{T->Integer}
    ''' TypeSubstitution for A(Of T, S).B.C(Of Byte) is C{U->Byte}
    ''' TypeSubstitution for A(Of Integer, S).B is B{}=>A{T->Integer}
    ''' TypeSubstitution for A(Of Integer, S).B.C(Of U) is C{}=>B{}=>A{T->Integer}
    ''' 
    ''' CONSIDER:
    '''     An array of KeyValuePair(Of TypeParameterSymbol, TypeSymbol)objects is used to represent type 
    '''     parameter substitution mostly due to historical reasons. It might be more convenient and more 
    '''     efficient to use ordinal based array of TypeSymbol objects instead.
    '''
    ''' There is a Construct method that can be called on original definition with TypeSubstitution object as
    ''' an argument. The advantage of that method is the ability to substitute type parameters of several types  
    ''' in the containership hierarchy in one call. What type the TypeSubstitution parameter targets makes a 
    ''' difference.
    ''' 
    ''' For example:
    '''      C.Construct(C{}=>B{}=>A{T->Integer}) == A(Of Integer, S).B.C(Of U)
    '''      C.Construct(B{}=>A{T->Integer}) == A(Of Integer, S).B.C(Of )
    '''      B.Construct(B{}=>A{T->Integer}) == A(Of Integer, S).B
    ''' 
    ''' See comment for IsValidToApplyTo method as well.
    ''' </summary>
    Friend Class TypeSubstitution

        ''' <summary>
        ''' A map between type parameters of _targetGenericDefinition and corresponding type arguments.
        ''' Represented by an array of Key-Value pairs. Keys are type parameters of _targetGenericDefinition 
        ''' in no particular order. Identity substitutions are omitted. 
        ''' </summary>
        Private ReadOnly _pairs As ImmutableArray(Of KeyValuePair(Of TypeParameterSymbol, TypeSymbol))

        ''' <summary>
        ''' Definition of a symbol which this instance of TypeSubstitution primarily targets.
        ''' </summary>
        Private ReadOnly _targetGenericDefinition As Symbol

        ''' <summary>
        ''' An instance of TypeSubstitution describing substitution for containing type.
        ''' </summary>
        Private ReadOnly _parent As TypeSubstitution

        Public ReadOnly Property Pairs As ImmutableArray(Of KeyValuePair(Of TypeParameterSymbol, TypeSymbol))
            Get
                Return _pairs
            End Get
        End Property

        ''' <summary>
        ''' Get all the pairs of substitutions, including from the parent substitutations. The substutitions
        ''' are in order from outside-in (parent substutitions before child substitutions).
        ''' </summary>
        Public ReadOnly Property PairsIncludingParent As ImmutableArray(Of KeyValuePair(Of TypeParameterSymbol, TypeSymbol))
            Get
                If _parent Is Nothing Then
                    Return Pairs
                Else
                    Dim pairBuilder = ArrayBuilder(Of KeyValuePair(Of TypeParameterSymbol, TypeSymbol)).GetInstance()
                    AddPairsIncludingParentToBuilder(pairBuilder)
                    Return pairBuilder.ToImmutableAndFree()
                End If
            End Get
        End Property

        'Add pairs (including parent pairs) to the given array builder.
        Private Sub AddPairsIncludingParentToBuilder(pairBuilder As ArrayBuilder(Of KeyValuePair(Of TypeParameterSymbol, TypeSymbol)))
            If _parent IsNot Nothing Then
                _parent.AddPairsIncludingParentToBuilder(pairBuilder)
            End If
            pairBuilder.AddRange(_pairs)
        End Sub

        Public ReadOnly Property Parent As TypeSubstitution
            Get
                Return _parent
            End Get
        End Property

        Public ReadOnly Property TargetGenericDefinition As Symbol
            Get
                Return _targetGenericDefinition
            End Get
        End Property

        ' If this substitution contains the given type parameter, return the substituted type.
        ' Otherwise, returns the type parameter itself.
        Public Function GetSubstitutionFor(tp As TypeSymbol) As TypeSymbol
            Debug.Assert(tp IsNot Nothing)
            Debug.Assert(tp.IsDefinition OrElse TargetGenericDefinition Is tp.ContainingSymbol)

            Dim containingSymbol As Symbol = tp.ContainingSymbol

            Dim current As TypeSubstitution = Me

            Do
                If current.TargetGenericDefinition Is containingSymbol Then
                    For Each p In current.Pairs
                        If p.Key.Equals(tp) Then Return p.Value
                    Next

                    ' not found, return the passed in type parameters
                    Return tp
                End If

                current = current.Parent
            Loop While current IsNot Nothing

            ' not found, return the passed in type parameters
            Return tp
        End Function

        ''' <summary>
        ''' Verify TypeSubstitution to make sure it dosn't map any 
        ''' type parameter to an alpha-renamed type parameter.
        ''' </summary>
        ''' <remarks></remarks>
        Public Sub ThrowIfSubstitutingToAlphaRenamedTypeParameter()
            Dim toCheck As TypeSubstitution = Me

            Do
                For Each pair In toCheck.Pairs
                    Dim value As TypeSymbol = pair.Value

                    If value.IsTypeParameter() AndAlso Not value.IsDefinition Then
                        Throw New ArgumentException()
                    End If
                Next

                toCheck = toCheck.Parent
            Loop While toCheck IsNot Nothing
        End Sub

        ''' <summary>
        ''' Return TypeSubstitution instance that targets particular generic definition.
        ''' </summary>
        Public Function GetSubstitutionForGenericDefinition(
            targetGenericDefinition As Symbol
        ) As TypeSubstitution

            Dim current As TypeSubstitution = Me

            Do
                If current.TargetGenericDefinition Is targetGenericDefinition Then
                    Return current
                End If

                current = current.Parent
            Loop While current IsNot Nothing

            Return Nothing
        End Function

        ''' <summary>
        ''' Return TypeSubstitution instance that targets particular
        ''' generic definition or one of its containers.
        ''' </summary>
        Public Function GetSubstitutionForGenericDefinitionOrContainers(
            targetGenericDefinition As Symbol
        ) As TypeSubstitution

            Dim current As TypeSubstitution = Me

            Do
                If current.IsValidToApplyTo(targetGenericDefinition) Then
                    Return current
                End If

                current = current.Parent
            Loop While current IsNot Nothing

            Return Nothing
        End Function

        ''' <summary>
        ''' Does substitution target either genericDefinition or 
        ''' one of its containers?
        ''' </summary>
        Public Function IsValidToApplyTo(genericDefinition As Symbol) As Boolean
            Debug.Assert(genericDefinition.IsDefinition)

            Dim current As Symbol = genericDefinition

            Do
                If current Is Me.TargetGenericDefinition Then
                    Return True
                End If

                current = current.ContainingType
            Loop While current IsNot Nothing

            Return False
        End Function

        ''' <summary>
        ''' Combine two substitutions into one by concatinating. 
        ''' 
        ''' They may not directly or indirectly (through Parent) target the same generic definition.
        ''' sub2 is expected to target types lower in the containership hierarchy.
        ''' Either or both can be Nothing. 
        ''' 
        ''' targetGenericDefinition specifies target generic definition for the result. 
        ''' If sub2 is not Nothing, it must target targetGenericDefinition.
        ''' If sub2 is Nothing, sub1 will be "extended" with identity substitutions to target 
        ''' targetGenericDefinition.
        ''' </summary>
        Public Shared Function Concat(targetGenericDefinition As Symbol, sub1 As TypeSubstitution, sub2 As TypeSubstitution) As TypeSubstitution
            Debug.Assert(targetGenericDefinition.IsDefinition)
            Debug.Assert(sub2 Is Nothing OrElse sub2.TargetGenericDefinition Is targetGenericDefinition)

            If sub1 Is Nothing Then
                Return sub2
            Else
                Debug.Assert(sub1.TargetGenericDefinition.IsDefinition)

                If sub2 Is Nothing Then
                    If targetGenericDefinition Is sub1.TargetGenericDefinition Then
                        Return sub1
                    End If

                    Return Concat(sub1, targetGenericDefinition, ImmutableArray(Of KeyValuePair(Of TypeParameterSymbol, TypeSymbol)).Empty)
                Else
                    Return ConcatNotNulls(sub1, sub2)
                End If
            End If
        End Function

        Private Shared Function ConcatNotNulls(sub1 As TypeSubstitution, sub2 As TypeSubstitution) As TypeSubstitution
            If sub2.Parent Is Nothing Then
                Return Concat(sub1, sub2.TargetGenericDefinition, sub2.Pairs)
            Else
                Return Concat(ConcatNotNulls(sub1, sub2.Parent), sub2.TargetGenericDefinition, sub2.Pairs)
            End If
        End Function

        ''' <summary>
        ''' Create a substitution. If the substitution is the identity substitution, Nothing is returned.
        ''' </summary>
        ''' <param name="targetGenericDefinition">Generic definition the result should target.</param>
        ''' <param name="params">
        ''' Type parameter definitions. Duplicates aren't allowed. Type parameters of containing type
        ''' must precede type parameters of a nested type.  
        ''' </param>
        ''' <param name="args">Corresponding type arguments.</param>
        ''' <returns></returns>
        Public Shared Function Create(
            targetGenericDefinition As Symbol,
            params() As TypeParameterSymbol,
            args() As TypeSymbol,
            Optional allowAlphaRenamedTypeParametersAsArguments As Boolean = False
        ) As TypeSubstitution
            Return Create(targetGenericDefinition, params.AsImmutableOrNull, args.AsImmutableOrNull, allowAlphaRenamedTypeParametersAsArguments)
        End Function

        ''' <summary>
        ''' Create a substitution. If the substitution is the identity substitution, Nothing is returned.
        ''' </summary>
        ''' <param name="targetGenericDefinition">Generic definition the result should target.</param>
        ''' <param name="params">
        ''' Type parameter definitions. Duplicates aren't allowed. Type parameters of containing type
        ''' must precede type parameters of a nested type.  
        ''' </param>
        ''' <param name="args">Corresponding type arguments.</param>
        ''' <returns></returns>
        Public Shared Function Create(
            targetGenericDefinition As Symbol,
            params As ImmutableArray(Of TypeParameterSymbol),
            args As ImmutableArray(Of TypeSymbol),
            Optional allowAlphaRenamedTypeParametersAsArguments As Boolean = False
        ) As TypeSubstitution
            Debug.Assert(targetGenericDefinition.IsDefinition)

            If params.Length <> args.Length Then
                Throw New ArgumentException(VBResources.NumberOfTypeParametersAndArgumentsMustMatch)
            End If

            Dim currentParent As TypeSubstitution = Nothing
            Dim currentContainer As Symbol = Nothing
#If DEBUG Then
            Dim haveSubstitutionForOrdinal = BitArray.Create(params.Length)
#End If

            Dim pairs = ArrayBuilder(Of KeyValuePair(Of TypeParameterSymbol, TypeSymbol)).GetInstance()
            Try
                For i = 0 To params.Length - 1
                    Dim param As TypeParameterSymbol = params(i)
                    Dim arg As TypeSymbol = args(i)

                    Debug.Assert(param.IsDefinition)

                    If currentContainer IsNot param.ContainingSymbol Then
                        ' starting new segment, finish the current one
                        If pairs.Count > 0 Then
                            currentParent = Concat(currentParent, currentContainer, pairs.ToImmutable())
                            pairs.Clear()
                        End If

                        currentContainer = param.ContainingSymbol
#If DEBUG Then
                        haveSubstitutionForOrdinal.Clear()
#End If
                    End If

#If DEBUG Then
                    Debug.Assert(Not haveSubstitutionForOrdinal(param.Ordinal))
                    haveSubstitutionForOrdinal(param.Ordinal) = True
#End If

                    If param.Equals(arg) Then
                        Continue For
                    End If

                    If Not allowAlphaRenamedTypeParametersAsArguments Then
                        ' Can't use alpha-renamed type parameters as arguments
                        If arg.IsTypeParameter() AndAlso Not arg.IsDefinition Then
                            Throw New ArgumentException()
                        End If
                    End If

                    pairs.Add(New KeyValuePair(Of TypeParameterSymbol, TypeSymbol)(param, arg))
                Next

                ' finish the current segment
                If pairs.Count > 0 Then
                    currentParent = Concat(currentParent, currentContainer, pairs.ToImmutable())
                End If

            Finally
                pairs.Free()
            End Try

            If currentParent IsNot Nothing AndAlso currentParent.TargetGenericDefinition IsNot targetGenericDefinition Then
                currentParent = Concat(currentParent, targetGenericDefinition, ImmutableArray(Of KeyValuePair(Of TypeParameterSymbol, TypeSymbol)).Empty)
#If DEBUG Then
            ElseIf currentContainer IsNot Nothing AndAlso currentContainer IsNot targetGenericDefinition Then
                ' currentContainer must be either targetGenericDefinition or a container of targetGenericDefinition
                Dim container As NamedTypeSymbol = targetGenericDefinition.ContainingType

                While container IsNot Nothing AndAlso container IsNot currentContainer
                    container = container.ContainingType
                End While

                Debug.Assert(container Is currentContainer)
#End If
            End If

            Return currentParent
        End Function

        ''' <summary>
        ''' Private helper to make sure identity substitutions are injected for types between 
        ''' targetGenericDefinition and parent.TargetGenericDefinition.
        ''' </summary>
        Private Shared Function Concat(
            parent As TypeSubstitution,
            targetGenericDefinition As Symbol,
            pairs As ImmutableArray(Of KeyValuePair(Of TypeParameterSymbol, TypeSymbol))
        ) As TypeSubstitution
            If parent Is Nothing OrElse parent.TargetGenericDefinition Is targetGenericDefinition.ContainingType Then
                Return New TypeSubstitution(targetGenericDefinition, pairs, parent)
            End If

            Dim containingType As NamedTypeSymbol = targetGenericDefinition.ContainingType

            Debug.Assert(containingType IsNot Nothing)

            Return New TypeSubstitution(
                targetGenericDefinition,
                pairs,
                Concat(parent, containingType, ImmutableArray(Of KeyValuePair(Of TypeParameterSymbol, TypeSymbol)).Empty))

        End Function

        Public Overrides Function ToString() As String
            Dim builder As New StringBuilder()

            builder.AppendFormat("{0} : ", TargetGenericDefinition)
            ToString(builder)

            Return builder.ToString()
        End Function

        Private Overloads Sub ToString(builder As StringBuilder)
            If _parent IsNot Nothing Then
                _parent.ToString(builder)
                builder.Append(", ")
            End If

            builder.Append("{"c)
            For i = 0 To _pairs.Length - 1
                If i <> 0 Then
                    builder.Append(", ")
                End If

                builder.AppendFormat("{0}->{1}", _pairs(i).Key.ToString(), _pairs(i).Value.ToString())
            Next
            builder.Append("}"c)
        End Sub


        Private Sub New(targetGenericDefinition As Symbol, pairs As ImmutableArray(Of KeyValuePair(Of TypeParameterSymbol, TypeSymbol)), parent As TypeSubstitution)
            Debug.Assert(Not pairs.IsDefault)
            Debug.Assert(pairs.All(Function(p) p.Key IsNot Nothing))
            Debug.Assert(pairs.All(Function(p) p.Value IsNot Nothing))
            Debug.Assert(targetGenericDefinition IsNot Nothing AndAlso
                            (targetGenericDefinition.IsDefinition OrElse
                                (targetGenericDefinition.Kind = SymbolKind.Method AndAlso
                                 DirectCast(targetGenericDefinition, MethodSymbol).ConstructedFrom Is targetGenericDefinition AndAlso
                                 parent Is Nothing)))
            Debug.Assert((targetGenericDefinition.Kind = SymbolKind.Method AndAlso
                         (DirectCast(targetGenericDefinition, MethodSymbol).IsGenericMethod OrElse
                            (targetGenericDefinition.ContainingType.IsOrInGenericType() AndAlso parent IsNot Nothing))) OrElse
                         ((targetGenericDefinition.Kind = SymbolKind.NamedType OrElse targetGenericDefinition.Kind = SymbolKind.ErrorType) AndAlso
                          DirectCast(targetGenericDefinition, NamedTypeSymbol).IsOrInGenericType()))
            Debug.Assert(parent Is Nothing OrElse targetGenericDefinition.ContainingSymbol Is parent.TargetGenericDefinition)

            _pairs = pairs
            _parent = parent
            _targetGenericDefinition = targetGenericDefinition
        End Sub


        ''' <summary>
        ''' Create substitution to handle alpha-renaming of type parameters. 
        ''' It maps type parameter definition to corresponding alpha-renamed type parameter.
        ''' </summary>
        ''' <param name="alphaRenamedTypeParameters">Alpha-renamed type parameters.</param>
        Public Shared Function CreateForAlphaRename(
            parent As TypeSubstitution,
            alphaRenamedTypeParameters As ImmutableArray(Of TypeParameterSymbol)
        ) As TypeSubstitution
            Debug.Assert(parent IsNot Nothing)
            Debug.Assert(Not alphaRenamedTypeParameters.IsEmpty)

            Dim memberDefinition As Symbol = alphaRenamedTypeParameters(0).OriginalDefinition.ContainingSymbol

            Debug.Assert(parent.TargetGenericDefinition Is memberDefinition.ContainingSymbol)

            Dim typeParametersDefinitions As ImmutableArray(Of TypeParameterSymbol)

            If memberDefinition.Kind = SymbolKind.Method Then
                typeParametersDefinitions = DirectCast(memberDefinition, MethodSymbol).TypeParameters
            Else
                typeParametersDefinitions = DirectCast(memberDefinition, NamedTypeSymbol).TypeParameters
            End If

            Debug.Assert(Not typeParametersDefinitions.IsEmpty AndAlso
                         alphaRenamedTypeParameters.Length = typeParametersDefinitions.Length)

            ' Build complete map for memberDefinition's type parameters
            Dim pairs(typeParametersDefinitions.Length - 1) As KeyValuePair(Of TypeParameterSymbol, TypeSymbol)

            For i As Integer = 0 To typeParametersDefinitions.Length - 1 Step 1
                Debug.Assert(Not alphaRenamedTypeParameters(i).Equals(typeParametersDefinitions(i)))
                Debug.Assert(alphaRenamedTypeParameters(i).OriginalDefinition Is typeParametersDefinitions(i))
                pairs(i) = New KeyValuePair(Of TypeParameterSymbol, TypeSymbol)(typeParametersDefinitions(i), alphaRenamedTypeParameters(i))
            Next

            Return Concat(parent, memberDefinition, pairs.AsImmutableOrNull())
        End Function


        ''' <summary>
        ''' Create TypeSubstitution that can be used to substitute method's type parameters
        ''' in types involved in method's signature. 
        ''' 
        ''' Unlike for other contruction methods in this class, targetMethod doesn't have to be 
        ''' original definition, it is allowed to be specialized unconstructed generic method.
        ''' 
        ''' An item in typeArguments can be an alpha-renamed type parameter, but it must belong
        ''' to the targetMethod and can only appear at its ordinal position to represent the lack
        ''' of substitution for it.
        ''' </summary>
        Public Shared Function CreateAdditionalMethodTypeParameterSubstitution(
            targetMethod As MethodSymbol,
            typeArguments As ImmutableArray(Of TypeSymbol)
        ) As TypeSubstitution
            Debug.Assert(targetMethod.Arity > 0 AndAlso typeArguments.Length = targetMethod.Arity AndAlso
                         targetMethod.ConstructedFrom Is targetMethod)

            Dim typeParametersDefinitions As ImmutableArray(Of TypeParameterSymbol) = targetMethod.TypeParameters

            Dim argument As TypeSymbol
            Dim countOfMeaningfulPairs As Integer = 0

            For i As Integer = 0 To typeArguments.Length - 1 Step 1
                argument = typeArguments(i)

                If argument.IsTypeParameter() Then
                    Dim typeParameter = DirectCast(argument, TypeParameterSymbol)

                    If typeParameter.Ordinal = i AndAlso typeParameter.ContainingSymbol Is targetMethod Then
                        Debug.Assert(typeParameter Is typeParametersDefinitions(i))
                        Continue For
                    End If

                    Debug.Assert(typeParameter.IsDefinition) ' Can't be an alpha renamed type parameter.
                End If

                countOfMeaningfulPairs += 1
            Next

            If countOfMeaningfulPairs = 0 Then
                'Identity substitution
                Return Nothing
            End If

            ' Build the map
            Dim pairs(countOfMeaningfulPairs - 1) As KeyValuePair(Of TypeParameterSymbol, TypeSymbol)
            countOfMeaningfulPairs = 0

            For i As Integer = 0 To typeArguments.Length - 1 Step 1
                argument = typeArguments(i)

                If argument.IsTypeParameter() Then
                    Dim typeParameter = DirectCast(argument, TypeParameterSymbol)

                    If typeParameter.Ordinal = i AndAlso typeParameter.ContainingSymbol Is targetMethod Then
                        Continue For
                    End If
                End If

                pairs(countOfMeaningfulPairs) = New KeyValuePair(Of TypeParameterSymbol, TypeSymbol)(typeParametersDefinitions(i), argument)
                countOfMeaningfulPairs += 1
            Next

            Debug.Assert(countOfMeaningfulPairs = pairs.Length)
            Return New TypeSubstitution(targetMethod, pairs.AsImmutableOrNull(), Nothing)
        End Function


        ''' <summary>
        ''' Adjust substitution for construction.
        ''' This has the following effects:
        '''     1) The passed in additionalSubstitution is used on each type argument.
        '''     2) If any parameters in the given additionalSubstitution are not present in oldConstructSubstitution, they are added.
        '''     3) Parent substitution in oldConstructSubstitution is replaced with adjustedParent. 
        ''' 
        ''' oldConstructSubstitution can be cancelled out by additionalSubstitution. In this case, 
        ''' if the adjustedParent is Nothing, Nothing is returned.
        ''' </summary>
        Public Shared Function AdjustForConstruct(
            adjustedParent As TypeSubstitution,
            oldConstructSubstitution As TypeSubstitution,
            additionalSubstitution As TypeSubstitution
        ) As TypeSubstitution
            Debug.Assert(oldConstructSubstitution IsNot Nothing AndAlso oldConstructSubstitution.TargetGenericDefinition.IsDefinition)
            Debug.Assert(additionalSubstitution IsNot Nothing)
            Debug.Assert(adjustedParent Is Nothing OrElse
                             (adjustedParent.TargetGenericDefinition.IsDefinition AndAlso
                                 (oldConstructSubstitution.Parent Is Nothing OrElse
                                    adjustedParent.TargetGenericDefinition Is oldConstructSubstitution.Parent.TargetGenericDefinition)))

            Dim pairs = ArrayBuilder(Of KeyValuePair(Of TypeParameterSymbol, TypeSymbol)).GetInstance()
            Dim pairsHaveChanged As Boolean = PrivateAdjustForConstruct(pairs, oldConstructSubstitution, additionalSubstitution)

            Dim result As TypeSubstitution

            ' glue new parts together
            If pairsHaveChanged OrElse oldConstructSubstitution.Parent IsNot adjustedParent Then

                If pairs.Count = 0 AndAlso adjustedParent Is Nothing Then
                    result = Nothing
                Else
                    result = Concat(adjustedParent,
                                    oldConstructSubstitution.TargetGenericDefinition,
                                    If(pairsHaveChanged, pairs.ToImmutable(), oldConstructSubstitution.Pairs))
                End If
            Else
                result = oldConstructSubstitution
            End If

            pairs.Free()
            Return result

        End Function

        ''' <summary>
        ''' This has the following effects:
        '''     1) The passed in additionalSubstitution is used on each type argument.
        '''     2) If any parameters in the given additionalSubstitution are not present in oldConstructSubstitution, they are added.
        ''' 
        ''' Result is placed into pairs. Identity substitutions are omitted.
        ''' 
        ''' Returns True if the set of pairs have changed, False otherwise.
        ''' </summary>
        Private Shared Function PrivateAdjustForConstruct(
            pairs As ArrayBuilder(Of KeyValuePair(Of TypeParameterSymbol, TypeSymbol)),
            oldConstructSubstitution As TypeSubstitution,
            additionalSubstitution As TypeSubstitution
        ) As Boolean
            ' Substitute into target of each existing substitution.
            Dim pairsHaveChanged As Boolean = False
            Dim oldPairs = oldConstructSubstitution.Pairs

            Dim haveSubstitutionForOrdinal As BitArray = Nothing
            Dim targetGenericDefinition As Symbol = oldConstructSubstitution.TargetGenericDefinition

            If oldPairs.Length > 0 Then
                Dim arity As Integer

                If targetGenericDefinition.Kind = SymbolKind.Method Then
                    arity = DirectCast(targetGenericDefinition, MethodSymbol).Arity
                Else
                    arity = DirectCast(targetGenericDefinition, NamedTypeSymbol).Arity
                End If

                haveSubstitutionForOrdinal = BitArray.Create(arity)
            End If

            For i = 0 To oldPairs.Length - 1 Step 1
                Dim newValue As TypeSymbol = oldPairs(i).Value.InternalSubstituteTypeParameters(additionalSubstitution)

                ' Mark that we had this substitution even if it is going to disappear.
                ' We still don't want to append substitution for this guy from additionalSubstitution.
                haveSubstitutionForOrdinal(oldPairs(i).Key.Ordinal) = True

                If Not newValue.Equals(oldPairs(i).Value) Then
                    pairsHaveChanged = True
                End If

                ' Do not add identity mapping.
                If Not oldPairs(i).Key.Equals(newValue) Then
                    pairs.Add(New KeyValuePair(Of TypeParameterSymbol, TypeSymbol)(oldPairs(i).Key, newValue))
                End If
            Next

            Dim append As TypeSubstitution = additionalSubstitution.GetSubstitutionForGenericDefinition(targetGenericDefinition)
            ' append new pairs
            If append IsNot Nothing Then
                For Each additionalPair In append.Pairs
                    If haveSubstitutionForOrdinal.IsNull OrElse Not haveSubstitutionForOrdinal(additionalPair.Key.Ordinal) Then
                        pairsHaveChanged = True
                        pairs.Add(additionalPair)
                    End If
                Next
            End If

            Return pairsHaveChanged
        End Function


        ''' <summary>
        ''' Create substitution for targetGenericDefinition based on its type 
        ''' arguments (matched to type parameters by position) and TypeSubstitution
        ''' for direct or indirect container.
        ''' </summary>
        Public Shared Function Create(
            parent As TypeSubstitution,
            targetGenericDefinition As Symbol,
            args As ImmutableArray(Of TypeSymbol),
            Optional allowAlphaRenamedTypeParametersAsArguments As Boolean = False
        ) As TypeSubstitution
            Debug.Assert(parent IsNot Nothing)
            Debug.Assert(targetGenericDefinition.IsDefinition)

            Dim typeParametersDefinitions As ImmutableArray(Of TypeParameterSymbol)

            If targetGenericDefinition.Kind = SymbolKind.Method Then
                typeParametersDefinitions = DirectCast(targetGenericDefinition, MethodSymbol).TypeParameters
            Else
                typeParametersDefinitions = DirectCast(targetGenericDefinition, NamedTypeSymbol).TypeParameters
            End If

            Dim n = typeParametersDefinitions.Length
            Debug.Assert(n > 0)
            If args.Length <> n Then
                Throw New ArgumentException(VBResources.NumberOfTypeParametersAndArgumentsMustMatch)
            End If

            Dim significantMaps As Integer = 0

            For i As Integer = 0 To n - 1 Step 1
                Dim arg = args(i)

                If Not typeParametersDefinitions(i).Equals(arg) Then
                    significantMaps += 1
                End If

                If Not allowAlphaRenamedTypeParametersAsArguments Then
                    ' Can't use alpha-renamed type parameters as arguments
                    If arg.IsTypeParameter() AndAlso Not arg.IsDefinition Then
                        Throw New ArgumentException()
                    End If
                End If
            Next

            If significantMaps = 0 Then
                Return Concat(targetGenericDefinition, parent, Nothing)
            End If

            Dim pairIndex = 0
            Dim pairs(significantMaps - 1) As KeyValuePair(Of TypeParameterSymbol, TypeSymbol)

            For i As Integer = 0 To n - 1 Step 1
                If Not typeParametersDefinitions(i).Equals(args(i)) Then
                    pairs(pairIndex) = New KeyValuePair(Of TypeParameterSymbol, TypeSymbol)(typeParametersDefinitions(i), args(i))
                    pairIndex += 1
                End If
            Next

            Debug.Assert(pairIndex = significantMaps)
            Return Concat(parent, targetGenericDefinition, pairs.AsImmutableOrNull())
        End Function

    End Class
End Namespace