﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeGeneration;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Extensions;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.LanguageServices;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.CodeGeneration
{
    internal partial class CSharpCodeGenerationService : AbstractCodeGenerationService
    {
        public CSharpCodeGenerationService(ILanguageServiceProvider languageServices)
            : base(languageServices.GetService<ISymbolDeclarationService>())
        {
        }

        protected override AbstractImportsAdder CreateImportsAdder(
            Document document)
        {
            return new UsingDirectivesAdder(document);
        }

        protected override IList<bool> GetAvailableInsertionIndices(SyntaxNode destination, CancellationToken cancellationToken)
        {
            if (destination is TypeDeclarationSyntax)
            {
                return GetInsertionIndices((TypeDeclarationSyntax)destination, cancellationToken);
            }

            // TODO(cyrusn): This will make is so that we can't generate into an enum, namespace, or
            // compilation unit, if it overlaps a hidden region.  We can consider relaxing that
            // restriction in the future.
            return null;
        }

        private IList<bool> GetInsertionIndices(TypeDeclarationSyntax destination, CancellationToken cancellationToken)
        {
            return destination.GetInsertionIndices(cancellationToken);
        }

        protected override TDeclarationNode AddEvent<TDeclarationNode>(TDeclarationNode destination, IEventSymbol @event, CodeGenerationOptions options, IList<bool> availableIndices)
        {
            CheckDeclarationNode<TypeDeclarationSyntax>(destination);

            return Cast<TDeclarationNode>(EventGenerator.AddEventTo(Cast<TypeDeclarationSyntax>(destination), @event, options, availableIndices));
        }

        protected override TDeclarationNode AddField<TDeclarationNode>(TDeclarationNode destination, IFieldSymbol field, CodeGenerationOptions options, IList<bool> availableIndices)
        {
            CheckDeclarationNode<EnumDeclarationSyntax, TypeDeclarationSyntax, CompilationUnitSyntax>(destination);

            if (destination is EnumDeclarationSyntax)
            {
                return Cast<TDeclarationNode>(EnumMemberGenerator.AddEnumMemberTo(Cast<EnumDeclarationSyntax>(destination), field, options));
            }
            else if (destination is TypeDeclarationSyntax)
            {
                return Cast<TDeclarationNode>(FieldGenerator.AddFieldTo(Cast<TypeDeclarationSyntax>(destination), field, options, availableIndices));
            }
            else
            {
                return Cast<TDeclarationNode>(FieldGenerator.AddFieldTo(Cast<CompilationUnitSyntax>(destination), field, options, availableIndices));
            }
        }

        protected override TDeclarationNode AddMethod<TDeclarationNode>(TDeclarationNode destination, IMethodSymbol method, CodeGenerationOptions options, IList<bool> availableIndices)
        {
            CheckDeclarationNode<TypeDeclarationSyntax, CompilationUnitSyntax, NamespaceDeclarationSyntax>(destination);

            // Synthesized methods for properties/events are not things we actually generate 
            // declarations for.
            if (method.AssociatedPropertyOrEvent is IEventSymbol)
            {
                return destination;
            }

            if (method.AssociatedPropertyOrEvent is IPropertySymbol)
            {
                // we will ignore the method if the associated property can be generated.
                var property = (IPropertySymbol)method.AssociatedPropertyOrEvent;
                if (PropertyGenerator.CanBeGenerated(property))
                {
                    return destination;
                }
            }

            var typeDeclaration = destination as TypeDeclarationSyntax;
            if (typeDeclaration != null)
            {
                if (method.IsConstructor())
                {
                    return Cast<TDeclarationNode>(ConstructorGenerator.AddConstructorTo(typeDeclaration, method, options, availableIndices));
                }

                if (method.IsDestructor())
                {
                    return Cast<TDeclarationNode>(DestructorGenerator.AddDestructorTo(typeDeclaration, method, options, availableIndices));
                }

                if (method.MethodKind == MethodKind.Conversion)
                {
                    return Cast<TDeclarationNode>(ConversionGenerator.AddConversionTo(typeDeclaration, method, options, availableIndices));
                }

                if (method.MethodKind == MethodKind.UserDefinedOperator)
                {
                    return Cast<TDeclarationNode>(OperatorGenerator.AddOperatorTo(typeDeclaration, method, options, availableIndices));
                }

                return Cast<TDeclarationNode>(MethodGenerator.AddMethodTo(typeDeclaration, method, options, availableIndices));
            }

            if (method.IsConstructor() ||
                method.IsDestructor())
            {
                return destination;
            }

            var compilationUnit = destination as CompilationUnitSyntax;
            if (compilationUnit != null)
            {
                return Cast<TDeclarationNode>(MethodGenerator.AddMethodTo(compilationUnit, method, options, availableIndices));
            }

            var ns = Cast<NamespaceDeclarationSyntax>(destination);
            return Cast<TDeclarationNode>(MethodGenerator.AddMethodTo(ns, method, options, availableIndices));
        }

        protected override TDeclarationNode AddProperty<TDeclarationNode>(TDeclarationNode destination, IPropertySymbol property, CodeGenerationOptions options, IList<bool> availableIndices)
        {
            CheckDeclarationNode<TypeDeclarationSyntax, CompilationUnitSyntax>(destination);

            // Can't generate a property with parameters.  So generate the setter/getter individually.
            if (!PropertyGenerator.CanBeGenerated(property))
            {
                var members = new List<ISymbol>();
                if (property.GetMethod != null)
                {
                    var getMethod = property.GetMethod;

                    if (property is CodeGenerationSymbol)
                    {
                        foreach (var annotation in ((CodeGenerationSymbol)property).GetAnnotations())
                        {
                            getMethod = annotation.AddAnnotationToSymbol(getMethod);
                        }
                    }

                    members.Add(getMethod);
                }

                if (property.SetMethod != null)
                {
                    var setMethod = property.SetMethod;

                    if (property is CodeGenerationSymbol)
                    {
                        foreach (var annotation in ((CodeGenerationSymbol)property).GetAnnotations())
                        {
                            setMethod = annotation.AddAnnotationToSymbol(setMethod);
                        }
                    }

                    members.Add(setMethod);
                }

                if (members.Count > 1)
                {
                    options = CreateOptionsForMultipleMembers(options);
                }

                return AddMembers(destination, members, availableIndices, options);
            }

            if (destination is TypeDeclarationSyntax)
            {
                return Cast<TDeclarationNode>(PropertyGenerator.AddPropertyTo(Cast<TypeDeclarationSyntax>(destination), property, options, availableIndices));
            }
            else
            {
                return Cast<TDeclarationNode>(PropertyGenerator.AddPropertyTo(Cast<CompilationUnitSyntax>(destination), property, options, availableIndices));
            }
        }

        protected override TDeclarationNode AddNamedType<TDeclarationNode>(TDeclarationNode destination, INamedTypeSymbol namedType, CodeGenerationOptions options, IList<bool> availableIndices)
        {
            CheckDeclarationNode<TypeDeclarationSyntax, NamespaceDeclarationSyntax, CompilationUnitSyntax>(destination);

            if (destination is TypeDeclarationSyntax)
            {
                return Cast<TDeclarationNode>(NamedTypeGenerator.AddNamedTypeTo(this, Cast<TypeDeclarationSyntax>(destination), namedType, options, availableIndices));
            }
            else if (destination is NamespaceDeclarationSyntax)
            {
                return Cast<TDeclarationNode>(NamedTypeGenerator.AddNamedTypeTo(this, Cast<NamespaceDeclarationSyntax>(destination), namedType, options, availableIndices));
            }
            else
            {
                return Cast<TDeclarationNode>(NamedTypeGenerator.AddNamedTypeTo(this, Cast<CompilationUnitSyntax>(destination), namedType, options, availableIndices));
            }
        }

        protected override TDeclarationNode AddNamespace<TDeclarationNode>(TDeclarationNode destination, INamespaceSymbol @namespace, CodeGenerationOptions options, IList<bool> availableIndices)
        {
            CheckDeclarationNode<CompilationUnitSyntax, NamespaceDeclarationSyntax>(destination);

            if (destination is CompilationUnitSyntax)
            {
                return Cast<TDeclarationNode>(NamespaceGenerator.AddNamespaceTo(this, Cast<CompilationUnitSyntax>(destination), @namespace, options, availableIndices));
            }
            else
            {
                return Cast<TDeclarationNode>(NamespaceGenerator.AddNamespaceTo(this, Cast<NamespaceDeclarationSyntax>(destination), @namespace, options, availableIndices));
            }
        }

        public override TDeclarationNode AddParameters<TDeclarationNode>(
            TDeclarationNode destinationMember,
            IEnumerable<IParameterSymbol> parameters,
            CodeGenerationOptions options,
            CancellationToken cancellationToken)
        {
            var memberDeclaration = destinationMember as MemberDeclarationSyntax;
            if (memberDeclaration == null)
            {
                return destinationMember;
            }

            var currentParameterList = memberDeclaration.GetParameterList();
            if (currentParameterList == null)
            {
                return destinationMember;
            }

            int currentParamsCount = currentParameterList.Parameters.Count;
            bool seenOptional = currentParamsCount > 0 && currentParameterList.Parameters[currentParamsCount - 1].Default != null;
            bool isFirstParam = currentParamsCount == 0;

            var parameterNodesAndTokens = currentParameterList.Parameters.GetWithSeparators().ToList();
            foreach (var parameter in parameters)
            {
                if (parameterNodesAndTokens.Count > 0 && parameterNodesAndTokens.Last().CSharpKind() != SyntaxKind.CommaToken)
                {
                    parameterNodesAndTokens.Add(SyntaxFactory.Token(SyntaxKind.CommaToken));
                }

                var parameterSyntax = ParameterGenerator.GetParameter(parameter, options, isExplicit: false, isFirstParam: isFirstParam, seenOptional: seenOptional);
                parameterNodesAndTokens.Add(parameterSyntax);
                isFirstParam = false;
                seenOptional = seenOptional || parameterSyntax.Default != null;
            }

            var finalParameterList = currentParameterList.WithParameters(SyntaxFactory.SeparatedList<ParameterSyntax>(parameterNodesAndTokens));
            var finalMember = memberDeclaration.WithParameterList(finalParameterList);

            return Cast<TDeclarationNode>(finalMember);
        }

        public override TDeclarationNode AddAttributes<TDeclarationNode>(
            TDeclarationNode destination,
            IEnumerable<AttributeData> attributes,
            SyntaxToken? target,
            CodeGenerationOptions options,
            CancellationToken cancellationToken)
        {
            if (target.HasValue && !target.Value.IsValidAttributeTarget())
            {
                throw new ArgumentException("target");
            }

            var attributeSyntaxList = AttributeGenerator.GenerateAttributeLists(attributes.ToImmutableArray(), options, target).ToArray();

            // Handle all members including types.
            var member = destination as MemberDeclarationSyntax;
            if (member != null)
            {
                return Cast<TDeclarationNode>(member.AddAttributeLists(attributeSyntaxList));
            }

            // Handle accessors
            var accessor = destination as AccessorDeclarationSyntax;
            if (accessor != null)
            {
                return Cast<TDeclarationNode>(accessor.AddAttributeLists(attributeSyntaxList));
            }

            // Handle global attributes
            var compilationUnit = destination as CompilationUnitSyntax;
            if (compilationUnit != null)
            {
                return Cast<TDeclarationNode>(compilationUnit.AddAttributeLists(attributeSyntaxList));
            }

            // Handle parameters
            var parameter = destination as ParameterSyntax;
            if (parameter != null)
            {
                return Cast<TDeclarationNode>(parameter.AddAttributeLists(attributeSyntaxList));
            }

            var typeParameter = destination as TypeParameterSyntax;
            if (typeParameter != null)
            {
                return Cast<TDeclarationNode>(typeParameter.AddAttributeLists(attributeSyntaxList));
            }

            return destination;
        }

        public override TDeclarationNode RemoveAttribute<TDeclarationNode>(
            TDeclarationNode destination,
            AttributeData attributeToRemove,
            CodeGenerationOptions options,
            CancellationToken cancellationToken)
        {
            if (attributeToRemove.ApplicationSyntaxReference == null)
            {
                throw new ArgumentException("attributeToRemove");
            }

            var attributeSyntaxToRemove = attributeToRemove.ApplicationSyntaxReference.GetSyntax(cancellationToken);
            return RemoveAttribute(destination, attributeSyntaxToRemove, options, cancellationToken);
        }

        public override TDeclarationNode RemoveAttribute<TDeclarationNode>(
            TDeclarationNode destination,
            SyntaxNode attributeToRemove,
            CodeGenerationOptions options,
            CancellationToken cancellationToken)
        {
            if (attributeToRemove == null)
            {
                throw new ArgumentException("attributeToRemove");
            }

            // Removed node could be AttributeSyntax or AttributeListSyntax.
            int positionOfRemovedNode;
            SyntaxTriviaList triviaOfRemovedNode;

            // Handle all members including types.
            var member = destination as MemberDeclarationSyntax;
            if (member != null)
            {
                var newAttributeLists = RemoveAttributeFromAttributeLists(member.GetAttributes(), attributeToRemove, options, out positionOfRemovedNode, out triviaOfRemovedNode);
                var newMember = member.WithAttributeLists(newAttributeLists);
                return Cast<TDeclarationNode>(AppendTriviaAtPosition(newMember, positionOfRemovedNode - destination.FullSpan.Start, triviaOfRemovedNode));
            }

            // Handle accessors
            var accessor = destination as AccessorDeclarationSyntax;
            if (accessor != null)
            {
                var newAttributeLists = RemoveAttributeFromAttributeLists(accessor.AttributeLists, attributeToRemove, options, out positionOfRemovedNode, out triviaOfRemovedNode);
                var newAccessor = accessor.WithAttributeLists(newAttributeLists);
                return Cast<TDeclarationNode>(AppendTriviaAtPosition(newAccessor, positionOfRemovedNode - destination.FullSpan.Start, triviaOfRemovedNode));
            }

            // Handle global attributes
            var compilationUnit = destination as CompilationUnitSyntax;
            if (compilationUnit != null)
            {
                var newAttributeLists = RemoveAttributeFromAttributeLists(compilationUnit.AttributeLists, attributeToRemove, options, out positionOfRemovedNode, out triviaOfRemovedNode);
                var newCompilationUnit = compilationUnit.WithAttributeLists(newAttributeLists);
                return Cast<TDeclarationNode>(AppendTriviaAtPosition(newCompilationUnit, positionOfRemovedNode - destination.FullSpan.Start, triviaOfRemovedNode));
            }

            // Handle parameters
            var parameter = destination as ParameterSyntax;
            if (parameter != null)
            {
                var newAttributeLists = RemoveAttributeFromAttributeLists(parameter.AttributeLists, attributeToRemove, options, out positionOfRemovedNode, out triviaOfRemovedNode);
                var newParameter = parameter.WithAttributeLists(newAttributeLists);
                return Cast<TDeclarationNode>(AppendTriviaAtPosition(newParameter, positionOfRemovedNode - destination.FullSpan.Start, triviaOfRemovedNode));
            }

            var typeParameter = destination as TypeParameterSyntax;
            if (typeParameter != null)
            {
                var newAttributeLists = RemoveAttributeFromAttributeLists(typeParameter.AttributeLists, attributeToRemove, options, out positionOfRemovedNode, out triviaOfRemovedNode);
                var newTypeParameter = typeParameter.WithAttributeLists(newAttributeLists);
                return Cast<TDeclarationNode>(AppendTriviaAtPosition(newTypeParameter, positionOfRemovedNode - destination.FullSpan.Start, triviaOfRemovedNode));
            }

            return destination;
        }

        private static SyntaxList<AttributeListSyntax> RemoveAttributeFromAttributeLists(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxNode attributeToRemove,
            CodeGenerationOptions options,
            out int positionOfRemovedNode,
            out SyntaxTriviaList triviaOfRemovedNode)
        {
            foreach (var attributeList in attributeLists)
            {
                var attributes = attributeList.Attributes;
                if (attributes.Contains(attributeToRemove))
                {
                    IEnumerable<SyntaxTrivia> trivia;
                    IEnumerable<AttributeListSyntax> newAttributeLists;
                    if (attributes.Count == 1)
                    {
                        // Remove the entire attribute list.
                        ComputePositionAndTriviaForRemoveAttributeList(attributeList, (SyntaxTrivia t) => t.IsKind(SyntaxKind.EndOfLineTrivia), out positionOfRemovedNode, out trivia);
                        newAttributeLists = attributeLists.Where(aList => aList != attributeList);
                    }
                    else
                    {
                        // Remove just the given attribute from the attribute list.
                        ComputePositionAndTriviaForRemoveAttributeFromAttributeList(attributeToRemove, (SyntaxToken t) => t.IsKind(SyntaxKind.CommaToken), out positionOfRemovedNode, out trivia);
                        var newAttributes = SyntaxFactory.SeparatedList(attributes.Where(a => a != attributeToRemove));
                        var newAttributeList = attributeList.WithAttributes(newAttributes);
                        newAttributeLists = attributeLists.Select(attrList => attrList == attributeList ? newAttributeList : attrList);
                    }

                    triviaOfRemovedNode = trivia.ToSyntaxTriviaList();
                    return newAttributeLists.ToSyntaxList();
                }
            }

            throw new ArgumentException("attributeToRemove");
        }

        public override TDeclarationNode AddStatements<TDeclarationNode>(
            TDeclarationNode destinationMember,
            IEnumerable<SyntaxNode> statements,
            CodeGenerationOptions options,
            CancellationToken cancellationToken)
        {
            var memberDeclaration = destinationMember as MemberDeclarationSyntax;
            if (memberDeclaration != null)
            {
                return AddStatementsToMemberDeclaration<TDeclarationNode>(destinationMember, statements, memberDeclaration);
            }
            else
            {
                return AddStatementsWorker(destinationMember, statements, options, cancellationToken);
            }
        }

        private TDeclarationNode AddStatementsWorker<TDeclarationNode>(
            TDeclarationNode destinationMember,
            IEnumerable<SyntaxNode> statements,
            CodeGenerationOptions options,
            CancellationToken cancellationToken) where TDeclarationNode : SyntaxNode
        {
            var location = options.BestLocation;
            CheckLocation<TDeclarationNode>(destinationMember, location);

            var token = location.FindToken(cancellationToken);

            var block = token.Parent.GetAncestorsOrThis<BlockSyntax>().FirstOrDefault();
            if (block != null)
            {
                var blockStatements = block.Statements.ToSet();
                var containingStatement = token.GetAncestors<StatementSyntax>().Single(blockStatements.Contains);
                var index = block.Statements.IndexOf(containingStatement);

                var newStatements = statements.OfType<StatementSyntax>().ToArray();
                BlockSyntax newBlock;
                if (options.BeforeThisLocation != null)
                {
                    IEnumerable<SyntaxTrivia> strippedTrivia;
                    var newContainingStatement = containingStatement.GetNodeWithoutLeadingBlankLines(out strippedTrivia);

                    newStatements[0] = newStatements[0].WithLeadingTrivia(strippedTrivia);

                    newBlock = block.ReplaceNode(containingStatement, newContainingStatement)
                                    .WithStatements(block.Statements.InsertRange(index, newStatements));
                }
                else
                {
                    newBlock = block.WithStatements(block.Statements.InsertRange(index + 1, newStatements));
                }

                return destinationMember.ReplaceNode(block, newBlock);
            }

            throw new ArgumentException(CSharpWorkspaceResources.NoAvailableLocationFoundTo);
        }

        private static TDeclarationNode AddStatementsToMemberDeclaration<TDeclarationNode>(TDeclarationNode destinationMember, IEnumerable<SyntaxNode> statements, MemberDeclarationSyntax memberDeclaration) where TDeclarationNode : SyntaxNode
        {
            var body = memberDeclaration.GetBody();
            if (body == null)
            {
                return destinationMember;
            }

            var statementNodes = body.Statements.ToList();
            statementNodes.AddRange(StatementGenerator.GenerateStatements(statements));

            var finalBody = body.WithStatements(SyntaxFactory.List<StatementSyntax>(statementNodes));
            var finalMember = memberDeclaration.WithBody(finalBody);

            return Cast<TDeclarationNode>(finalMember);
        }

        public override SyntaxNode CreateEventDeclaration(
            IEventSymbol @event, CodeGenerationDestination destination, CodeGenerationOptions options)
        {
            return EventGenerator.GenerateEventDeclaration(@event, destination, options);
        }

        public override SyntaxNode CreateFieldDeclaration(IFieldSymbol field, CodeGenerationDestination destination, CodeGenerationOptions options)
        {
            return destination == CodeGenerationDestination.EnumType
                ? (SyntaxNode)EnumMemberGenerator.GenerateEnumMemberDeclaration(field, null, options)
                : (SyntaxNode)FieldGenerator.GenerateFieldDeclaration(field, destination, options);
        }

        public override SyntaxNode CreateMethodDeclaration(
            IMethodSymbol method, CodeGenerationDestination destination, CodeGenerationOptions options)
        {
            if (method.IsConstructor())
            {
                return ConstructorGenerator.GenerateConstructorDeclaration(method, destination, options);
            }
            else if (method.IsDestructor())
            {
                return DestructorGenerator.GenerateDestructorDeclaration(method, destination, options);
            }
            else
            {
                return MethodGenerator.GenerateMethodDeclaration(method, destination, options);
            }
        }

        public override SyntaxNode CreatePropertyDeclaration(
            IPropertySymbol property, CodeGenerationDestination destination, CodeGenerationOptions options)
        {
            return PropertyGenerator.GeneratePropertyDeclaration(property, destination, options);
        }

        public override SyntaxNode CreateNamedTypeDeclaration(
            INamedTypeSymbol namedType, CodeGenerationDestination destination, CodeGenerationOptions options)
        {
            return NamedTypeGenerator.GenerateNamedTypeDeclaration(this, namedType, destination, options);
        }

        public override SyntaxNode CreateNamespaceDeclaration(
            INamespaceSymbol @namespace, CodeGenerationDestination destination, CodeGenerationOptions options)
        {
            return NamespaceGenerator.GenerateNamespaceDeclaration(this, @namespace, options);
        }

        private static TDeclarationNode UpdateDeclarationModifiers<TDeclarationNode>(TDeclarationNode declaration, Func<SyntaxTokenList, SyntaxTokenList> computeNewModifiersList, CodeGenerationOptions options, CancellationToken cancellationToken)
        {
            // Handle type declarations.
            var typeDeclaration = declaration as BaseTypeDeclarationSyntax;
            if (typeDeclaration != null)
            {
                return Cast<TDeclarationNode>(typeDeclaration.WithModifiers(computeNewModifiersList(typeDeclaration.Modifiers)));
            }

            // Handle field declarations.
            var fieldDeclaration = declaration as BaseFieldDeclarationSyntax;
            if (fieldDeclaration != null)
            {
                return Cast<TDeclarationNode>(fieldDeclaration.WithModifiers(computeNewModifiersList(fieldDeclaration.Modifiers)));
            }

            // Handle method declarations.
            var methodDeclaration = declaration as BaseMethodDeclarationSyntax;
            if (methodDeclaration != null)
            {
                return Cast<TDeclarationNode>(methodDeclaration.WithModifiers(computeNewModifiersList(methodDeclaration.Modifiers)));
            }

            // Handle properties.
            var propertyDeclaration = declaration as BasePropertyDeclarationSyntax;
            if (propertyDeclaration != null)
            {
                return Cast<TDeclarationNode>(propertyDeclaration.WithModifiers(computeNewModifiersList(propertyDeclaration.Modifiers)));
            }

            return declaration;
        }

        public override TDeclarationNode UpdateDeclarationModifiers<TDeclarationNode>(TDeclarationNode declaration, IEnumerable<SyntaxToken> newModifiers, CodeGenerationOptions options, CancellationToken cancellationToken)
        {
            Func<SyntaxTokenList, SyntaxTokenList> computeNewModifiersList = (SyntaxTokenList modifiersList) => newModifiers.ToSyntaxTokenList();
            return UpdateDeclarationModifiers(declaration, computeNewModifiersList, options, cancellationToken);
        }

        public override TDeclarationNode UpdateDeclarationAccessibility<TDeclarationNode>(TDeclarationNode declaration, Accessibility newAccesibility, CodeGenerationOptions options, CancellationToken cancellationToken)
        {
            Func<SyntaxTokenList, SyntaxTokenList> computeNewModifiersList = (SyntaxTokenList modifiersList) => UpdateDeclarationAccessibility(modifiersList, newAccesibility, options);
            return UpdateDeclarationModifiers(declaration, computeNewModifiersList, options, cancellationToken);
        }

        private static SyntaxTokenList UpdateDeclarationAccessibility(SyntaxTokenList modifiersList, Accessibility newAccesibility, CodeGenerationOptions options)
        {
            var newModifierTokens = new List<SyntaxToken>();
            AbstractCSharpCodeGenerator.AddAccessibilityModifiers(newAccesibility, newModifierTokens, options, Accessibility.NotApplicable);
            if (newModifierTokens.Count == 0)
            {
                return modifiersList;
            }

            return GetUpdatedDeclarationAccessibilityModifiers(newModifierTokens, modifiersList, (SyntaxToken modifier) => SyntaxFacts.IsAccessibilityModifier(modifier.CSharpKind()))
                .ToSyntaxTokenList();
        }

        public override TDeclarationNode UpdateDeclarationType<TDeclarationNode>(TDeclarationNode declaration, ITypeSymbol newType, CodeGenerationOptions options, CancellationToken cancellationToken)
        {
            var syntaxNode = declaration as CSharpSyntaxNode;
            if (syntaxNode == null)
            {
                return declaration;
            }

            TypeSyntax newTypeSyntax;
            switch (syntaxNode.CSharpKind())
            {
                case SyntaxKind.DelegateDeclaration:
                    // Handle delegate declarations.
                    var delegateDeclarationSyntax = declaration as DelegateDeclarationSyntax;
                    newTypeSyntax = newType.GenerateTypeSyntax()
                        .WithLeadingTrivia(delegateDeclarationSyntax.ReturnType.GetLeadingTrivia())
                        .WithTrailingTrivia(delegateDeclarationSyntax.ReturnType.GetTrailingTrivia());
                    return Cast<TDeclarationNode>(delegateDeclarationSyntax.WithReturnType(newTypeSyntax));

                case SyntaxKind.MethodDeclaration:
                    // Handle method declarations.
                    var methodDeclarationSyntax = declaration as MethodDeclarationSyntax;
                    newTypeSyntax = newType.GenerateTypeSyntax()
                        .WithLeadingTrivia(methodDeclarationSyntax.ReturnType.GetLeadingTrivia())
                        .WithTrailingTrivia(methodDeclarationSyntax.ReturnType.GetTrailingTrivia());
                    return Cast<TDeclarationNode>(methodDeclarationSyntax.WithReturnType(newTypeSyntax));

                case SyntaxKind.OperatorDeclaration:
                    // Handle operator declarations.
                    var operatorDeclarationSyntax = declaration as OperatorDeclarationSyntax;
                    newTypeSyntax = newType.GenerateTypeSyntax()
                        .WithLeadingTrivia(operatorDeclarationSyntax.ReturnType.GetLeadingTrivia())
                        .WithTrailingTrivia(operatorDeclarationSyntax.ReturnType.GetTrailingTrivia());
                    return Cast<TDeclarationNode>(operatorDeclarationSyntax.WithReturnType(newTypeSyntax));

                case SyntaxKind.ConversionOperatorDeclaration:
                    // Handle conversion operator declarations.
                    var conversionOperatorDeclarationSyntax = declaration as ConversionOperatorDeclarationSyntax;
                    newTypeSyntax = newType.GenerateTypeSyntax()
                        .WithLeadingTrivia(conversionOperatorDeclarationSyntax.Type.GetLeadingTrivia())
                        .WithTrailingTrivia(conversionOperatorDeclarationSyntax.Type.GetTrailingTrivia());
                    return Cast<TDeclarationNode>(conversionOperatorDeclarationSyntax.WithType(newTypeSyntax));

                case SyntaxKind.PropertyDeclaration:
                    // Handle properties.
                    var propertyDeclaration = declaration as PropertyDeclarationSyntax;
                    newTypeSyntax = newType.GenerateTypeSyntax()
                        .WithLeadingTrivia(propertyDeclaration.Type.GetLeadingTrivia())
                        .WithTrailingTrivia(propertyDeclaration.Type.GetTrailingTrivia());
                    return Cast<TDeclarationNode>(propertyDeclaration.WithType(newTypeSyntax));

                case SyntaxKind.EventDeclaration:
                    // Handle events.
                    var eventDeclarationSyntax = declaration as EventDeclarationSyntax;
                    newTypeSyntax = newType.GenerateTypeSyntax()
                        .WithLeadingTrivia(eventDeclarationSyntax.Type.GetLeadingTrivia())
                        .WithTrailingTrivia(eventDeclarationSyntax.Type.GetTrailingTrivia());
                    return Cast<TDeclarationNode>(eventDeclarationSyntax.WithType(newTypeSyntax));

                case SyntaxKind.IndexerDeclaration:
                    // Handle indexers.
                    var indexerDeclarationSyntax = declaration as IndexerDeclarationSyntax;
                    newTypeSyntax = newType.GenerateTypeSyntax()
                        .WithLeadingTrivia(indexerDeclarationSyntax.Type.GetLeadingTrivia())
                        .WithTrailingTrivia(indexerDeclarationSyntax.Type.GetTrailingTrivia());
                    return Cast<TDeclarationNode>(indexerDeclarationSyntax.WithType(newTypeSyntax));

                case SyntaxKind.Parameter:
                    // Handle parameters.
                    var parameterSyntax = declaration as ParameterSyntax;
                    newTypeSyntax = newType.GenerateTypeSyntax()
                        .WithLeadingTrivia(parameterSyntax.Type.GetLeadingTrivia())
                        .WithTrailingTrivia(parameterSyntax.Type.GetTrailingTrivia());
                    return Cast<TDeclarationNode>(parameterSyntax.WithType(newTypeSyntax));

                case SyntaxKind.IncompleteMember:
                    // Handle incomplete members.
                    var incompleteMemberSyntax = declaration as IncompleteMemberSyntax;
                    newTypeSyntax = newType.GenerateTypeSyntax()
                        .WithLeadingTrivia(incompleteMemberSyntax.Type.GetLeadingTrivia())
                        .WithTrailingTrivia(incompleteMemberSyntax.Type.GetTrailingTrivia());
                    return Cast<TDeclarationNode>(incompleteMemberSyntax.WithType(newTypeSyntax));

                case SyntaxKind.ArrayType:
                    // Handle array type.
                    var arrayTypeSyntax = declaration as ArrayTypeSyntax;
                    newTypeSyntax = newType.GenerateTypeSyntax()
                        .WithLeadingTrivia(arrayTypeSyntax.ElementType.GetLeadingTrivia())
                        .WithTrailingTrivia(arrayTypeSyntax.ElementType.GetTrailingTrivia());
                    return Cast<TDeclarationNode>(arrayTypeSyntax.WithElementType(newTypeSyntax));

                case SyntaxKind.PointerType:
                    // Handle pointer type.
                    var pointerTypeSyntax = declaration as PointerTypeSyntax;
                    newTypeSyntax = newType.GenerateTypeSyntax()
                        .WithLeadingTrivia(pointerTypeSyntax.ElementType.GetLeadingTrivia())
                        .WithTrailingTrivia(pointerTypeSyntax.ElementType.GetTrailingTrivia());
                    return Cast<TDeclarationNode>(pointerTypeSyntax.WithElementType(newTypeSyntax));

                case SyntaxKind.VariableDeclaration:
                    // Handle variable declarations.
                    var variableDeclarationSyntax = declaration as VariableDeclarationSyntax;
                    newTypeSyntax = newType.GenerateTypeSyntax()
                        .WithLeadingTrivia(variableDeclarationSyntax.Type.GetLeadingTrivia())
                        .WithTrailingTrivia(variableDeclarationSyntax.Type.GetTrailingTrivia());
                    return Cast<TDeclarationNode>(variableDeclarationSyntax.WithType(newTypeSyntax));

                case SyntaxKind.CatchDeclaration:
                    // Handle catch declarations.
                    var catchDeclarationSyntax = declaration as CatchDeclarationSyntax;
                    newTypeSyntax = newType.GenerateTypeSyntax()
                        .WithLeadingTrivia(catchDeclarationSyntax.Type.GetLeadingTrivia())
                        .WithTrailingTrivia(catchDeclarationSyntax.Type.GetTrailingTrivia());
                    return Cast<TDeclarationNode>(catchDeclarationSyntax.WithType(newTypeSyntax));

                default:
                    return declaration;
            }
        }

        public override TDeclarationNode UpdateDeclarationMembers<TDeclarationNode>(TDeclarationNode declaration, IList<ISymbol> newMembers, CodeGenerationOptions options = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            var memberDeclaration = declaration as MemberDeclarationSyntax;
            if (memberDeclaration != null)
            {
                return Cast<TDeclarationNode>(NamedTypeGenerator.UpdateNamedTypeDeclaration(this, memberDeclaration, newMembers, options, cancellationToken));
            }

            var syntaxNode = declaration as CSharpSyntaxNode;
            if (syntaxNode != null)
            {
                switch (syntaxNode.CSharpKind())
                {
                    case SyntaxKind.CompilationUnit:
                    case SyntaxKind.NamespaceDeclaration:
                        return Cast<TDeclarationNode>(NamespaceGenerator.UpdateCompilationUnitOrNamespaceDeclaration(this, syntaxNode, newMembers, options, cancellationToken));
                }
            }

            return declaration;
        }
    }
}