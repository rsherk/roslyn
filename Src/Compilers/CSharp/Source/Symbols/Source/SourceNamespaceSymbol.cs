﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;
using Microsoft.CodeAnalysis;

namespace Microsoft.CodeAnalysis.CSharp.Symbols
{
    internal partial class SourceNamespaceSymbol : NamespaceSymbol
    {
        private readonly SourceModuleSymbol module;
        private readonly Symbol container;
        private readonly MergedNamespaceDeclaration mergedDeclaration;

        private SymbolCompletionState state;
        private ImmutableArray<Location> locations;
        private Dictionary<string, ImmutableArray<NamespaceOrTypeSymbol>> nameToMembersMap;
        private Dictionary<string, ImmutableArray<NamedTypeSymbol>> nameToTypeMembersMap;
        private ImmutableArray<Symbol> lazyAllMembers;
        private ImmutableArray<NamedTypeSymbol> lazyTypeMembersUnordered;

        private const int LazyAllMembersIsSorted = 0x1;   // Set if "lazyAllMembers" is sorted.

        private int flags;


        private LexicalSortKey lazyLexicalSortKey = LexicalSortKey.NotInitialized;

        internal SourceNamespaceSymbol(SourceModuleSymbol module, Symbol container, MergedNamespaceDeclaration mergedDeclaration)
        {
            this.module = module;
            this.container = container;
            this.mergedDeclaration = mergedDeclaration;
        }

        internal MergedNamespaceDeclaration MergedDeclaration
        {
            get { return mergedDeclaration; }
        }

        public override Symbol ContainingSymbol
        {
            get
            {
                return container;
            }
        }

        public override AssemblySymbol ContainingAssembly
        {
            get
            {
                return module.ContainingAssembly;
            }
        }

        internal IEnumerable<Imports> GetBoundImportsMerged()
        {
            var compilation = this.DeclaringCompilation;
            foreach (var declaration in mergedDeclaration.Declarations)
            {
                if (declaration.HasUsings || declaration.HasExternAliases)
                {
                    yield return compilation.GetImports(declaration);
                }
            }
        }

        public override string Name
        {
            get
            {
                return mergedDeclaration.Name;
            }
        }

        internal override LexicalSortKey GetLexicalSortKey()
        {
            if (!lazyLexicalSortKey.IsInitialized)
            {
                lazyLexicalSortKey.SetFrom(mergedDeclaration.GetLexicalSortKey(this.DeclaringCompilation));
            }
            return lazyLexicalSortKey;
        }

        public override ImmutableArray<Location> Locations
        {
            get
            {
                if (locations.IsDefault)
                {
                    ImmutableInterlocked.InterlockedCompareExchange(ref locations,
                        mergedDeclaration.NameLocations,
                        default(ImmutableArray<Location>));
                }

                return locations;
            }
        }

        private static readonly Func<SyntaxReference, CSharpSyntaxNode> NamespaceDeclarationGetter = r =>
            {
                var node = (CSharpSyntaxNode)r.GetSyntax();

                // If the node is a name syntax, it's something like "X" or "X.Y" in :
                //    namespace X.Y.Z
                // We want to return the full NamespaceDeclarationSyntax.
                while (node is NameSyntax)
                {
                    node = node.Parent;
                }

                Debug.Assert(node is CompilationUnitSyntax || node is NamespaceDeclarationSyntax);
                return node;
            };

        public override ImmutableArray<SyntaxReference> DeclaringSyntaxReferences
        {
            get
            {
                // SyntaxReference in the namespace declaration points to the name node of the namespace decl node not
                // namespace decl node we want to return. here we will wrap the original syntax reference in 
                // the translation syntax reference so that we can lazily manipulate a node return to the caller
                ArrayBuilder<SyntaxReference> builder = ArrayBuilder<SyntaxReference>.GetInstance();

                foreach (SingleNamespaceDeclaration decl in mergedDeclaration.Declarations)
                {
                    builder.Add(new TranslationSyntaxReference(decl.SyntaxReference, NamespaceDeclarationGetter));
                }

                return builder.ToImmutableAndFree();
            }
        }

        internal override ImmutableArray<Symbol> GetMembersUnordered()
        {
            var result = this.lazyAllMembers;

            if (result.IsDefault)
            {
                var members = StaticCast<Symbol>.From(this.GetNameToMembersMap().Flatten(null));  // don't sort.
                ImmutableInterlocked.InterlockedInitialize(ref this.lazyAllMembers, members);
                result = this.lazyAllMembers;
            }

#if DEBUG
            // In DEBUG, swap first and last elements so that use of Unordered in a place it isn't warranted is caught
            // more obviously.
            return result.DeOrder();
#else
            return result;
#endif
        }

        public override ImmutableArray<Symbol> GetMembers()
        {
            if ((flags & LazyAllMembersIsSorted) != 0)
            {
                return this.lazyAllMembers;
            }
            else
            {
                var allMembers = this.GetMembersUnordered();

                if (allMembers.Length >= 2)
                {
                    // The array isn't sorted. Sort it and remember that we sorted it.
                    Symbol[] membersArray = allMembers.ToArray();
                    Array.Sort(membersArray, LexicalOrderSymbolComparer.Instance);
                    ImmutableInterlocked.InterlockedExchange(ref this.lazyAllMembers, membersArray.AsImmutableOrNull());
                }

                ThreadSafeFlagOperations.Set(ref flags, LazyAllMembersIsSorted);
                return lazyAllMembers;
            }
        }

        public override ImmutableArray<Symbol> GetMembers(string name)
        {
            ImmutableArray<NamespaceOrTypeSymbol> members;
            return this.GetNameToMembersMap().TryGetValue(name, out members)
                ? members.Cast<NamespaceOrTypeSymbol, Symbol>()
                : ImmutableArray<Symbol>.Empty;
        }

        internal override ImmutableArray<NamedTypeSymbol> GetTypeMembersUnordered()
        {
            if (lazyTypeMembersUnordered.IsDefault)
            {
                var members = this.GetNameToTypeMembersMap().Flatten();
                ImmutableInterlocked.InterlockedInitialize(ref lazyTypeMembersUnordered, members);
            }

            return lazyTypeMembersUnordered;
        }

        public override ImmutableArray<NamedTypeSymbol> GetTypeMembers()
        {
            return this.GetNameToTypeMembersMap().Flatten(LexicalOrderSymbolComparer.Instance);
        }

        public override ImmutableArray<NamedTypeSymbol> GetTypeMembers(string name)
        {
            ImmutableArray<NamedTypeSymbol> members;
            return this.GetNameToTypeMembersMap().TryGetValue(name, out members)
                ? members
                : ImmutableArray<NamedTypeSymbol>.Empty;
        }

        public override ImmutableArray<NamedTypeSymbol> GetTypeMembers(string name, int arity)
        {
            return GetTypeMembers(name).WhereAsArray(s => s.Arity == arity);
        }

        internal override ModuleSymbol ContainingModule
        {
            get
            {
                return this.module;
            }
        }

        internal override NamespaceExtent Extent
        {
            get
            {
                return new NamespaceExtent(module);
            }
        }

        private Dictionary<string, ImmutableArray<NamespaceOrTypeSymbol>> GetNameToMembersMap()
        {
            if (nameToMembersMap == null)
            {
                var diagnostics = DiagnosticBag.GetInstance();
                if (Interlocked.CompareExchange(ref nameToMembersMap, MakeNameToMembersMap(diagnostics), null) == null)
                {
                    // NOTE: the following is not cancellable.  Once we've set the
                    // members, we *must* do the following to make sure we're in a consistent state.
                    this.DeclaringCompilation.SemanticDiagnostics.AddRange(diagnostics);

                    RegisterDeclaredCorTypes();
                    state.NotePartComplete(CompletionPart.NameToMembersMap);
                }

                diagnostics.Free();
            }

            return nameToMembersMap;
        }

        private Dictionary<string, ImmutableArray<NamedTypeSymbol>> GetNameToTypeMembersMap()
        {
            if (nameToTypeMembersMap == null)
            {
                // NOTE: This method depends on MakeNameToMembersMap() on creating a proper 
                // NOTE: type of the array, see comments in MakeNameToMembersMap() for details

                var dictionary = new Dictionary<String, ImmutableArray<NamedTypeSymbol>>();

                Dictionary<String, ImmutableArray<NamespaceOrTypeSymbol>> map = this.GetNameToMembersMap();
                foreach (var kvp in map)
                {
                    ImmutableArray<NamespaceOrTypeSymbol> members = kvp.Value;

                    bool hasType = false;
                    bool hasNamespace = false;

                    foreach (var symbol in members)
                    {
                        if (symbol.Kind == SymbolKind.NamedType)
                        {
                            hasType = true;
                            if (hasNamespace)
                            {
                                break;
                            }
                        }
                        else
                        {
                            Debug.Assert(symbol.Kind == SymbolKind.Namespace);
                            hasNamespace = true;
                            if (hasType)
                            {
                                break;
                            }
                        }
                    }

                    if (hasType)
                    {
                        if (hasNamespace)
                        {
                            dictionary.Add(kvp.Key, members.OfType<NamedTypeSymbol>().AsImmutable());
                        }
                        else
                        {
                            dictionary.Add(kvp.Key, members.As<NamedTypeSymbol>());
                        }
                    }
                }

                Interlocked.CompareExchange(ref nameToTypeMembersMap, dictionary, null);
            }

            return nameToTypeMembersMap;
        }

        private Dictionary<string, ImmutableArray<NamespaceOrTypeSymbol>> MakeNameToMembersMap(DiagnosticBag diagnostics)
        {
            // NOTE: Even though the resulting map stores ImmutableArray<NamespaceOrTypeSymbol> as 
            // NOTE: values if the name is mapped into an array of named types, which is frequently 
            // NOTE: the case, we actually create an array of NamedTypeSymbol[] and wrap it in 
            // NOTE: ImmutableArray<NamespaceOrTypeSymbol>
            // NOTE: 
            // NOTE: This way we can save time and memory in GetNameToTypeMembersMap() -- when we see that
            // NOTE: a name maps into values collection containing types only instead of allocating another 
            // NOTE: array of NamedTypeSymbol[] we downcast the array to ImmutableArray<NamedTypeSymbol>

            var builder = new NameToSymbolMapBuilder(mergedDeclaration.Children.Length);
            foreach (var declaration in mergedDeclaration.Children)
            {
                builder.Add(BuildSymbol(declaration, diagnostics));
            }
            var result = builder.CreateMap();

            var memberOfArity = new Symbol[10];
            MergedNamespaceSymbol mergedAssemblyNamespace = null;

            if (this.ContainingAssembly.Modules.Length > 1)
            {
                mergedAssemblyNamespace = this.ContainingAssembly.GetAssemblyNamespace(this) as MergedNamespaceSymbol;
            }

            foreach (var name in result.Keys)
            {
                Array.Clear(memberOfArity, 0, memberOfArity.Length);
                foreach (var symbol in result[name])
                {
                    var nts = symbol as NamedTypeSymbol;
                    var arity = ((object)nts != null) ? nts.Arity : 0;
                    if (arity >= memberOfArity.Length)
                    {
                        Array.Resize(ref memberOfArity, arity + 1);
                    }

                    var other = memberOfArity[arity];

                    if ((object)other == null && (object)mergedAssemblyNamespace != null)
                    {
                        // Check for collision with declarations from added modules.
                        foreach (NamespaceSymbol constituent in mergedAssemblyNamespace.ConstituentNamespaces)
                        {
                            if ((object)constituent != (object)this)
                            {
                                // For whatever reason native compiler only detects conflicts agains types.
                                // It doesn't complain when source declares a type with the same name as 
                                // a namespace in added module, but complains when source declares a namespace 
                                // with the same name as a type in added module.
                                var types = constituent.GetTypeMembers(symbol.Name, arity);

                                if (types.Length > 0)
                                {
                                    other = types[0];
                                    // Since the error doesn't specify what added module this type belongs to, we can stop searching
                                    // at the first match.
                                    break;
                                }
                            }
                        }
                    }

                    if ((object)other != null)
                    {
                        if (nts is SourceNamedTypeSymbol && other is SourceNamedTypeSymbol &&
                            (nts as SourceNamedTypeSymbol).IsPartial && (other as SourceNamedTypeSymbol).IsPartial)
                        {
                            diagnostics.Add(ErrorCode.ERR_PartialTypeKindConflict, symbol.Locations[0], symbol);
                        }
                        else
                        {
                            diagnostics.Add(ErrorCode.ERR_DuplicateNameInNS, symbol.Locations[0], name, this);
                        }
                    }

                    memberOfArity[arity] = symbol;

                    if ((object)nts != null)
                    {
                        //types declared at the namespace level may only have declared accessibility of public or internal (Section 3.5.1)
                        Accessibility declaredAccessibility = nts.DeclaredAccessibility;
                        if ((declaredAccessibility & (Accessibility.Public | Accessibility.Internal)) != declaredAccessibility)
                        {
                            diagnostics.Add(ErrorCode.ERR_NoNamespacePrivate, symbol.Locations[0]);
                        }
                    }
                }
            }

            return result;
        }

        private NamespaceOrTypeSymbol BuildSymbol(MergedNamespaceOrTypeDeclaration declaration, DiagnosticBag diagnostics)
        {
            switch (declaration.Kind)
            {
                case DeclarationKind.Namespace:
                    return new SourceNamespaceSymbol(this.module, this, (MergedNamespaceDeclaration)declaration);

                case DeclarationKind.Struct:
                case DeclarationKind.Interface:
                case DeclarationKind.Enum:
                case DeclarationKind.Delegate:
                case DeclarationKind.Class:
                    return new SourceNamedTypeSymbol(this, (MergedTypeDeclaration)declaration, diagnostics);

                case DeclarationKind.Script:
                case DeclarationKind.Submission:
                case DeclarationKind.ImplicitClass:
                    return new ImplicitNamedTypeSymbol(this, (MergedTypeDeclaration)declaration, diagnostics);

                default:
                    throw ExceptionUtilities.UnexpectedValue(declaration.Kind);
            }
        }

        /// <summary>
        /// Register COR types declared in this namespace, if any, in the COR types cache.
        /// </summary>
        private void RegisterDeclaredCorTypes()
        {
            AssemblySymbol containingAssembly = ContainingAssembly;

            if (containingAssembly.KeepLookingForDeclaredSpecialTypes)
            {
                // Register newly declared COR types
                foreach (var array in nameToMembersMap.Values)
                {
                    foreach (var member in array)
                    {
                        var type = member as NamedTypeSymbol;

                        if ((object)type != null && type.SpecialType != SpecialType.None)
                        {
                            containingAssembly.RegisterDeclaredSpecialType(type);

                            if (!containingAssembly.KeepLookingForDeclaredSpecialTypes)
                            {
                                return;
                            }
                        }
                    }
                }
            }
        }

        private struct NameToSymbolMapBuilder
        {
            private readonly Dictionary<string, object> dictionary;

            public NameToSymbolMapBuilder(int capacity)
            {
                this.dictionary = new Dictionary<string, object>(capacity);
            }

            public void Add(NamespaceOrTypeSymbol symbol)
            {
                string name = symbol.Name;
                object item;
                if (this.dictionary.TryGetValue(name, out item))
                {
                    var builder = item as ArrayBuilder<NamespaceOrTypeSymbol>;
                    if (builder == null)
                    {
                        builder = ArrayBuilder<NamespaceOrTypeSymbol>.GetInstance();
                        builder.Add((NamespaceOrTypeSymbol)item);
                        this.dictionary[name] = builder;
                    }
                    builder.Add(symbol);
                }
                else
                {
                    this.dictionary[name] = symbol;
                }
            }

            public Dictionary<String, ImmutableArray<NamespaceOrTypeSymbol>> CreateMap()
            {
                var result = new Dictionary<String, ImmutableArray<NamespaceOrTypeSymbol>>(this.dictionary.Count);

                foreach (var kvp in this.dictionary)
                {
                    object value = kvp.Value;
                    ImmutableArray<NamespaceOrTypeSymbol> members;

                    var builder = value as ArrayBuilder<NamespaceOrTypeSymbol>;
                    if (builder != null)
                    {
                        Debug.Assert(builder.Count > 1);
                        bool hasNamespaces = false;
                        for (int i = 0; (i < builder.Count) && !hasNamespaces; i++)
                        {
                            hasNamespaces |= (builder[i].Kind == SymbolKind.Namespace);
                        }

                        members = hasNamespaces
                            ? builder.ToImmutable()
                            : StaticCast<NamespaceOrTypeSymbol>.From(builder.ToDowncastedImmutable<NamedTypeSymbol>());

                        builder.Free();
                    }
                    else
                    {
                        NamespaceOrTypeSymbol symbol = (NamespaceOrTypeSymbol)value;
                        members = symbol.Kind == SymbolKind.Namespace
                            ? ImmutableArray.Create<NamespaceOrTypeSymbol>(symbol)
                            : StaticCast<NamespaceOrTypeSymbol>.From(ImmutableArray.Create<NamedTypeSymbol>((NamedTypeSymbol)symbol));
                    }

                    result.Add(kvp.Key, members);
                }

                return result;
            }
        }
    }
}
