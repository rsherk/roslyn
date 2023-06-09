﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.CodeAnalysis
{
    /// <summary>
    /// Represents a method or method-like symbol (including constructor,
    /// destructor, operator, or property/event accessor).
    /// </summary>
    public interface IMethodSymbol : ISymbol
    {
        /// <summary>
        /// Gets what kind of method this is. There are several different kinds of things in the
        /// C# language that are represented as methods. This property allow distinguishing those things
        /// without having to decode the name of the method.
        /// </summary>
        MethodKind MethodKind { get; }

        /// <summary>
        /// Returns the arity of this method, or the number of type parameters it takes.
        /// A non-generic method has zero arity.
        /// </summary>
        int Arity { get; }

        /// <summary>
        /// Returns whether this method is generic; i.e., does it have any type parameters?
        /// </summary>
        bool IsGenericMethod { get; }

        /// <summary>
        /// Returns true if this method is an extension method. 
        /// </summary>
        bool IsExtensionMethod { get; }

        /// <summary>
        /// Returns true if this method is an async method
        /// </summary>
        bool IsAsync { get; }

        /// <summary>
        /// Returns whether this method is using CLI VARARG calling convention. This is used for
        /// C-style variable argument lists. This is used extremely rarely in C# code and is
        /// represented using the undocumented “__arglist” keyword.
        ///
        /// Note that methods with “params” on the last parameter are indicated with the “IsParams”
        /// property on ParameterSymbol, and are not represented with this property.
        /// </summary>
        bool IsVararg { get; }

        /// <summary>
        /// Returns whether this built-in operator checks for integer overflow.
        /// </summary>
        bool IsCheckedBuiltin { get; }

        /// <summary>
        /// Returns true if this method hides base methods by name. This cannot be specified directly
        /// in the C# language, but can be true for methods defined in other languages imported from
        /// metadata. The equivalent of the "hidebyname" flag in metadata. 
        /// </summary>
        bool HidesBaseMethodsByName { get; }

        /// <summary>
        /// Returns true if this method has no return type; i.e., returns "void".
        /// </summary>
        bool ReturnsVoid { get; }

        /// <summary>
        /// Gets the return type of the method.
        /// </summary>
        ITypeSymbol ReturnType { get; }

        /// <summary>
        /// Returns the type arguments that have been substituted for the type parameters. 
        /// If nothing has been substituted for a given type parameter,
        /// then the type parameter itself is consider the type argument.
        /// </summary>
        ImmutableArray<ITypeSymbol> TypeArguments { get; }

        /// <summary>
        /// Get the type parameters on this method. If the method has not generic,
        /// returns an empty list.
        /// </summary>
        ImmutableArray<ITypeParameterSymbol> TypeParameters { get; }

        /// <summary>
        /// Gets the parameters of this method. If this method has no parameters, returns
        /// an empty list.
        /// </summary>
        ImmutableArray<IParameterSymbol> Parameters { get; }

        /// <summary>
        /// Returns the method symbol that this method was constructed from. The resulting
        /// method symbol
        /// has the same containing type (if any), but has type arguments that are the same
        /// as the type parameters (although its containing type might not).
        /// </summary>
        IMethodSymbol ConstructedFrom { get; }

        /// <summary>
        /// Get the original definition of this symbol. If this symbol is derived from another
        /// symbol by (say) type substitution, this gets the original symbol, as it was defined in
        /// source or metadata.
        /// </summary>
        new IMethodSymbol OriginalDefinition { get; }

        /// <summary>
        /// If this method overrides another method (because it both had the override modifier
        /// and there correctly was a method to override), returns the overridden method.
        /// </summary>
        IMethodSymbol OverriddenMethod { get; }

        /// <summary>
        /// If this method can be applied to an object, returns the type of object it is applied to.
        /// </summary>
        ITypeSymbol ReceiverType { get; }

        /// <summary>
        /// If this method is a reduced extension method, returns the definition of extension
        /// method from which this was reduced. Otherwise, returns null.
        /// </summary>
        IMethodSymbol ReducedFrom { get; }

        /// <summary>
        /// If this method is a reduced extension method, returns a type inferred during reduction process for the type parameter. 
        /// </summary>
        /// <param name="reducedFromTypeParameter">Type parameter of the corresponding <see cref="ReducedFrom"/> method.</param>
        /// <returns>Inferred type or Nothing if nothing was inferred.</returns>
        /// <exception cref="System.InvalidOperationException">If this is not a reduced extension method.</exception>
        /// <exception cref="System.ArgumentNullException">If <paramref name="reducedFromTypeParameter"/> is null.</exception>
        /// <exception cref="System.ArgumentException">If <paramref name="reducedFromTypeParameter"/> doesn't belong to the corresponding <see cref="ReducedFrom"/> method.</exception>
        ITypeSymbol GetTypeInferredDuringReduction(ITypeParameterSymbol reducedFromTypeParameter);

        /// <summary>
        /// If this is an extension method that can be applied to a receiver of the given type,
        /// returns a reduced extension method symbol thus formed. Otherwise, returns null.
        /// </summary>
        IMethodSymbol ReduceExtensionMethod(ITypeSymbol receiverType);

        /// <summary>
        /// Returns interface methods explicitly implemented by this method.
        /// </summary>
        /// <remarks>
        /// Methods imported from metadata can explicitly implement more than one method, 
        /// that is why return type is ImmutableArray.
        /// </remarks>
        ImmutableArray<IMethodSymbol> ExplicitInterfaceImplementations { get; }

        /// <summary>
        /// Returns the list of custom modifiers, if any, associated with the returned value. 
        /// </summary>
        ImmutableArray<CustomModifier> ReturnTypeCustomModifiers { get; }

        /// <summary>
        /// Returns the list of custom attributes, if any, associated with the returned value. 
        /// </summary>
        ImmutableArray<AttributeData> GetReturnTypeAttributes();

        /// <summary>
        /// If this method has MethodKind of MethodKind.PropertyGet or MethodKind.PropertySet,
        /// returns the property that this method is the getter or setter for.
        /// If this method has MethodKind of MethodKind.EventAdd or MethodKind.EventRemove,
        /// returns the event that this method is the adder or remover for.
        /// </summary>
        ISymbol AssociatedPropertyOrEvent { get; }

        /// <summary>
        /// Returns a constructed method given its type arguments.
        /// </summary>
        /// <param name="typeArguments">The immediate type arguments to be replaced for type
        /// parameters in the method.</param>
        IMethodSymbol Construct(params ITypeSymbol[] typeArguments);

        /// <summary>
        /// If this is a partial method implementation part, returns the corresponding
        /// definition part.  Otherwise null.
        /// </summary>
        IMethodSymbol PartialDefinitionPart { get; }

        /// <summary>
        /// If this is a partial method declaration without a body, and the method is
        /// implemented with a body, returns that implementing definition.  Otherwise
        /// null.
        /// </summary>
        IMethodSymbol PartialImplementationPart { get; }

        /// <summary>
        /// Platform invoke information, or null if the method isn't a P/Invoke.
        /// </summary>
        DllImportData GetDllImportData();

        /// <summary>
        /// If this method is a Lambda method (MethodKind = MethodKind.LambdaMethod) and 
        /// there is an anonymous delegate associated with it, returns this delegate.
        /// 
        /// Returns null if the symbol is not a lambda or if it does not have an
        /// anonymous delegate associated with it.
        /// </summary>
        INamedTypeSymbol AssociatedAnonymousDelegate { get; }
    }
}