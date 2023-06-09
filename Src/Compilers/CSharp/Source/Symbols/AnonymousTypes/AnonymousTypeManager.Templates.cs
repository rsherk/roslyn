﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading;
using Microsoft.CodeAnalysis.Collections;
using Microsoft.CodeAnalysis.CSharp.Emit;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.Symbols
{
    /// <summary>
    /// Manages anonymous types created on module level. All requests for anonymous type symbols 
    /// go via the instance of this class, the symbol will be either created or returned from cache.
    /// </summary>
    internal sealed partial class AnonymousTypeManager
    {
        /// <summary>
        /// Cache of created anonymous type templates used as an implementation of anonymous 
        /// types in emit phase.
        /// </summary>
        private ConcurrentDictionary<string, AnonymousTypeTemplateSymbol> lazyAnonymousTypeTemplates;

        /// <summary>
        /// We should not see new anonymous types from source after we finished emit phase. 
        /// If this field is true, the collection is sealed; in DEBUG it also is used to check the assertion.
        /// </summary>
        private ThreeState anonymousTypeTemplatesIsSealed = ThreeState.False;

        /// <summary>
        /// Collection of anonymous type templates is sealed 
        /// </summary>
        internal bool AreTemplatesSealed
        {
            get { return anonymousTypeTemplatesIsSealed == ThreeState.True; }
        }

        /// <summary>
        /// Maps delegate signature shape (number of parameters and their ref-ness) to a synthesized generic delegate symbol.
        /// Unlike anonymous types synthesized delegates are not available thru symbol APIs. They are only used in lowered bound trees.
        /// Currently used for dynamic call-site sites whose signature doesn't match any of the well-known Func or Action types.
        /// </summary>
        private ConcurrentDictionary<SynthesizedDelegateKey, SynthesizedDelegateValue> lazySynthesizedDelegates;

        private struct SynthesizedDelegateKey : IEquatable<SynthesizedDelegateKey>
        {
            private readonly BitArray byRefs;
            private readonly ushort parameterCount;
            private readonly byte returnsVoid;

            public SynthesizedDelegateKey(int parameterCount, BitArray byRefs, bool returnsVoid)
            {
                this.parameterCount = (ushort)parameterCount;
                this.returnsVoid = (byte)(returnsVoid ? 1 : 0);
                this.byRefs = byRefs;
            }

            /// <summary>
            /// Produces name of the synthesized delegate symbol that encodes the parameter byref-ness and return type of the delegate.
            /// The arity is appended via `N suffix since in MetadataName calculation since the delegate is generic.
            /// </summary>
            public string MakeTypeName()
            {
                var pooledBuilder = PooledStringBuilder.GetInstance();
                pooledBuilder.Builder.Append(returnsVoid != 0 ? "<>A" : "<>F");

                if (!byRefs.IsNull)
                {
                    pooledBuilder.Builder.Append("{");

                    int i = 0;
                    foreach (int byRefIndex in byRefs.Words())
                    {
                        if (i > 0)
                        {
                            pooledBuilder.Builder.Append(",");
                        }

                        pooledBuilder.Builder.AppendFormat("{0:x8}", byRefIndex);
                        i++;
                    }

                    pooledBuilder.Builder.Append("}");
                    Debug.Assert(i > 0);
                }

                return pooledBuilder.ToStringAndFree();
            }

            public override bool Equals(object obj)
            {
                return obj is SynthesizedDelegateKey && Equals((SynthesizedDelegateKey)obj);
            }

            public bool Equals(SynthesizedDelegateKey other)
            {
                return parameterCount == other.parameterCount
                    && returnsVoid == other.returnsVoid
                    && byRefs.Equals(other.byRefs);
            }

            public override int GetHashCode()
            {
                return Hash.Combine((int)parameterCount, Hash.Combine((int)returnsVoid, byRefs.GetHashCode()));
            }
        }

        private struct SynthesizedDelegateValue
        {
            public readonly SynthesizedDelegateSymbol Delegate;

            // the manager that created this delegate:
            public readonly AnonymousTypeManager Manager;

            public SynthesizedDelegateValue(AnonymousTypeManager manager, SynthesizedDelegateSymbol @delegate)
            {
                Debug.Assert(manager != null && (object)@delegate != null);
                this.Manager = manager;
                this.Delegate = @delegate;
            }
        }

#if DEBUG
        /// <summary>
        /// Holds a collection of all the locations of anonymous types and delegates from source
        /// </summary>
        private ConcurrentDictionary<Location, bool> _sourceLocationsSeen = new ConcurrentDictionary<Location, bool>();
#endif

        [Conditional("DEBUG")]
        private void CheckSourceLocationSeen(AnonymousTypePublicSymbol anonymous)
        {
#if DEBUG
            Location location = anonymous.Locations[0];
            if (location.IsInSource)
            {
                if (this.anonymousTypeTemplatesIsSealed == ThreeState.True)
                {
                    Debug.Assert(this._sourceLocationsSeen.ContainsKey(location));
                }
                else
                {
                    this._sourceLocationsSeen.TryAdd(location, true);
                }
            }
#endif
        }

        private ConcurrentDictionary<string, AnonymousTypeTemplateSymbol> AnonymousTypeTemplates
        {
            get
            {
                // Lazily create a template types cache
                if (this.lazyAnonymousTypeTemplates == null)
                {
                    CSharpCompilation previousSubmission = this.Compilation.PreviousSubmission;

                    // TODO (tomat): avoid recursion
                    var previousCache = (previousSubmission == null) ? null : previousSubmission.AnonymousTypeManager.AnonymousTypeTemplates;

                    Interlocked.CompareExchange(ref this.lazyAnonymousTypeTemplates,
                                                previousCache == null
                                                    ? new ConcurrentDictionary<string, AnonymousTypeTemplateSymbol>()
                                                    : new ConcurrentDictionary<string, AnonymousTypeTemplateSymbol>(previousCache),
                                                null);
                }

                return this.lazyAnonymousTypeTemplates;
            }
        }

        private ConcurrentDictionary<SynthesizedDelegateKey, SynthesizedDelegateValue> SynthesizedDelegates
        {
            get
            {
                if (this.lazySynthesizedDelegates == null)
                {
                    CSharpCompilation previousSubmission = this.Compilation.PreviousSubmission;

                    // TODO (tomat): avoid recursion
                    var previousCache = (previousSubmission == null) ? null : previousSubmission.AnonymousTypeManager.lazySynthesizedDelegates;

                    Interlocked.CompareExchange(ref this.lazySynthesizedDelegates,
                                                previousCache == null
                                                    ? new ConcurrentDictionary<SynthesizedDelegateKey, SynthesizedDelegateValue>()
                                                    : new ConcurrentDictionary<SynthesizedDelegateKey, SynthesizedDelegateValue>(previousCache),
                                                null);
                }

                return this.lazySynthesizedDelegates;
            }
        }

        internal SynthesizedDelegateSymbol SynthesizeDelegate(int parameterCount, BitArray byRefParameters, bool returnsVoid)
        {
            // parameterCount doesn't include return type
            Debug.Assert(byRefParameters.IsNull || parameterCount == byRefParameters.Capacity);

            var key = new SynthesizedDelegateKey(parameterCount, byRefParameters, returnsVoid);

            SynthesizedDelegateValue result;
            if (this.SynthesizedDelegates.TryGetValue(key, out result))
            {
                return result.Delegate;
            }

            // NOTE: the newly created template may be thrown away if another thread wins
            return this.SynthesizedDelegates.GetOrAdd(key,
                new SynthesizedDelegateValue(
                    this,
                    new SynthesizedDelegateSymbol(
                        this.Compilation.Assembly.GlobalNamespace,
                        key.MakeTypeName(),
                        this.System_Object,
                        Compilation.GetSpecialType(SpecialType.System_IntPtr),
                        returnsVoid ? Compilation.GetSpecialType(SpecialType.System_Void) : null,
                        parameterCount,
                        byRefParameters))).Delegate;
        }

        /// <summary>
        /// Given anonymous type provided constructs an implementation type symbol to be used in emit phase; 
        /// if the anonymous type has at least one field the implementation type symbol will be created based on 
        /// a generic type template generated for each 'unique' anonymous type structure, otherwise the template
        /// type will be non-generic.
        /// </summary>
        private NamedTypeSymbol ConstructAnonymousTypeImplementationSymbol(AnonymousTypePublicSymbol anonymous)
        {
            Debug.Assert(ReferenceEquals(this, anonymous.Manager));

            CheckSourceLocationSeen(anonymous);

            AnonymousTypeDescriptor typeDescr = anonymous.TypeDescriptor;
            typeDescr.AssertIsGood();

            // Get anonymous type template
            AnonymousTypeTemplateSymbol template;
            if (!this.AnonymousTypeTemplates.TryGetValue(typeDescr.Key, out template))
            {
                // NOTE: the newly created template may be thrown away if another thread wins
                template = this.AnonymousTypeTemplates.GetOrAdd(typeDescr.Key, new AnonymousTypeTemplateSymbol(this, typeDescr));
            }

            // Adjust template location if the template is owned by this manager
            if (ReferenceEquals(template.Manager, this))
            {
                template.AdjustLocation(typeDescr.Location);
            }

            // In case template is not generic, just return it
            if (template.Arity == 0)
            {
                return template;
            }

            // otherwise construct type using the field types
            var typeArguments = typeDescr.Fields.SelectAsArray(f => f.Type);
            return template.Construct(typeArguments);
        }

        private AnonymousTypeTemplateSymbol CreatePlaceholderTemplate(Microsoft.CodeAnalysis.Emit.AnonymousTypeKey key)
        {
            var fields = key.Names.SelectAsArray(n => new AnonymousTypeField(n, Location.None, (TypeSymbol)null));
            var typeDescr = new AnonymousTypeDescriptor(fields, Location.None);
            return new AnonymousTypeTemplateSymbol(this, typeDescr);
        }

        /// <summary>
        /// Resets numbering in anonymous type names and compiles the
        /// anonymous type methods. Also seals the collection of templates.
        /// </summary>
        public void AssignTemplatesNamesAndCompile(MethodBodyCompiler compiler, PEModuleBuilder moduleBeingBuilt, DiagnosticBag diagnostics)
        {
            var previousGeneration = moduleBeingBuilt.PreviousGeneration;

            if (previousGeneration != null)
            {
                // Ensure all previous anonymous type templates are included so the
                // types are available for subsequent edit and continue generations.
                foreach (var key in previousGeneration.AnonymousTypeMap.Keys)
                {
                    var templateKey = AnonymousTypeDescriptor.ComputeKey(key.Names, f => f);
                    this.AnonymousTypeTemplates.GetOrAdd(templateKey, k => this.CreatePlaceholderTemplate(key));
                }
            }

            // Get all anonymous types owned by this manager
            var builder = ArrayBuilder<AnonymousTypeTemplateSymbol>.GetInstance();

            var anonymousTypes = lazyAnonymousTypeTemplates;
            if (anonymousTypes != null)
            {
                foreach (var template in anonymousTypes.Values)
                {
                    // NOTE: in interactive scenarios the cache may contain templates 
                    //       from other compilation, those should be discarded here
                    if (ReferenceEquals(template.Manager, this))
                    {
                        builder.Add(template);
                    }
                }
            }

            // If the collection is not sealed yet we should assign 
            // new indexes to the created anonymous type templates
            if (this.anonymousTypeTemplatesIsSealed != ThreeState.True)
            {
                // Sort type templates using smallest location
                builder.Sort(new AnonymousTypeComparer(this.Compilation));

                // If we are emitting .NET module, include module's name into type's name to ensure
                // uniqueness across added modules.
                string moduleId;

                if (moduleBeingBuilt.OutputKind == OutputKind.NetModule)
                {
                    moduleId = moduleBeingBuilt.Name;

                    string extension = OutputKind.NetModule.GetDefaultExtension();

                    if (moduleId.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    {
                        moduleId = moduleId.Substring(0, moduleId.Length - extension.Length);
                    }

                    moduleId = moduleId.Replace('.', '_');
                }
                else
                {
                    moduleId = string.Empty;
                }

                int nextIndex = (previousGeneration == null) ? 0 : previousGeneration.GetNextAnonymousTypeIndex();
                foreach (var template in builder)
                {
                    string name;
                    int index;
                    if (!moduleBeingBuilt.TryGetAnonymousTypeName(template, out name, out index))
                    {
                        index = nextIndex++;
                        name = GeneratedNames.MakeAnonymousTypeTemplateName(index, this.Compilation.GetSubmissionSlotIndex(), moduleId);
                    }
                    // normally it should only happen once, but in case there is a race
                    // NameAndIndex.set has an assert which guarantees that the
                    // template name provided is the same as the one already assigned
                    template.NameAndIndex = new NameAndIndex(name, index);
                }

                this.anonymousTypeTemplatesIsSealed = ThreeState.True;
            }

            if (builder.Count > 0 && !ReportMissingOrErroneousSymbols(diagnostics))
            {
                // Process all the templates
                foreach (var template in builder)
                {
                    foreach (var method in template.SpecialMembers)
                    {
                        moduleBeingBuilt.AddCompilerGeneratedDefinition(template, method);
                    }

                    compiler.Visit(template, null);
                }
            }

            builder.Free();

            var delegates = lazySynthesizedDelegates;
            if (delegates != null)
            {
                foreach (var template in delegates.Values)
                {
                    // NOTE: in interactive scenarios the cache may contain templates 
                    //       from other compilation, those should be discarded here
                    if (ReferenceEquals(template.Manager, this))
                    {
                        compiler.Visit(template.Delegate, null);
                    }
                }
            }
        }

        internal static ImmutableArray<string> GetTemplatePropertyNames(NamedTypeSymbol type)
        {
            return ((AnonymousTypeTemplateSymbol)type).GetPropertyNames();
        }

        internal IReadOnlyDictionary<Microsoft.CodeAnalysis.Emit.AnonymousTypeKey, Microsoft.CodeAnalysis.Emit.AnonymousTypeValue> GetAnonymousTypeMap()
        {
            var result = new Dictionary<Microsoft.CodeAnalysis.Emit.AnonymousTypeKey, Microsoft.CodeAnalysis.Emit.AnonymousTypeValue>();
            var templates = GetAllCreatedTemplates();
            foreach (AnonymousTypeTemplateSymbol template in templates)
            {
                var nameAndIndex = template.NameAndIndex;
                var key = new Microsoft.CodeAnalysis.Emit.AnonymousTypeKey(template.GetPropertyNames());
                var value = new Microsoft.CodeAnalysis.Emit.AnonymousTypeValue(nameAndIndex.Name, nameAndIndex.Index, template);
                result.Add(key, value);
            }
            return result;
        }

        /// <summary>
        /// Returns all templates owned by this type manager
        /// </summary>
        internal ImmutableArray<NamedTypeSymbol> GetAllCreatedTemplates()
        {
            // NOTE: templates may not be sealed in case metadata is being emitted without IL

            var builder = ArrayBuilder<NamedTypeSymbol>.GetInstance();

            var anonymousTypes = lazyAnonymousTypeTemplates;
            if (anonymousTypes != null)
            {
                foreach (var template in anonymousTypes.Values)
                {
                    if (ReferenceEquals(template.Manager, this))
                    {
                        builder.Add(template);
                    }
                }
            }

            var delegates = SynthesizedDelegates;
            if (delegates != null)
            {
                foreach (var template in delegates.Values)
                {
                    if (ReferenceEquals(template.Manager, this))
                    {
                        builder.Add(template.Delegate);
                    }
                }
            }

            return builder.ToImmutableAndFree();
        }

        /// <summary>
        /// Returns true if the named type is an implementation template for an anonymous type
        /// </summary>
        internal static bool IsAnonymousTypeTemplate(NamedTypeSymbol type)
        {
            return type is AnonymousTypeTemplateSymbol;
        }

        /// <summary>
        /// Retrieves methods of anonymous type template which are not placed to symbol table.
        /// In current implementation those are overriden 'ToString', 'Equals' and 'GetHashCode'
        /// </summary>
        internal static ImmutableArray<MethodSymbol> GetAnonymousTypeHiddenMethods(NamedTypeSymbol type)
        {
            Debug.Assert((object)type != null);
            return ((AnonymousTypeTemplateSymbol)type).SpecialMembers;
        }

        /// <summary>
        /// Translates anonymous type public symbol into an implementation type symbol to be used in emit.
        /// </summary>
        internal static NamedTypeSymbol TranslateAnonymousTypeSymbol(NamedTypeSymbol type)
        {
            Debug.Assert((object)type != null);
            Debug.Assert(type.IsAnonymousType);

            var anonymous = (AnonymousTypePublicSymbol)type;
            return anonymous.Manager.ConstructAnonymousTypeImplementationSymbol(anonymous);
        }

        /// <summary>
        /// Translates anonymous type method symbol into an implementation method symbol to be used in emit.
        /// </summary>
        internal static MethodSymbol TranslateAnonymousTypeMethodSymbol(MethodSymbol method)
        {
            Debug.Assert((object)method != null);
            NamedTypeSymbol translatedType = TranslateAnonymousTypeSymbol(method.ContainingType);
            // find a method in anonymous type template by name
            foreach (var member in ((NamedTypeSymbol)translatedType.OriginalDefinition).GetMembers(method.Name))
            {
                if (member.Kind == SymbolKind.Method)
                {
                    // found a method definition, get a constructed method
                    return ((MethodSymbol)member).AsMember(translatedType);
                }
            }
            throw ExceptionUtilities.Unreachable;
        }

        /// <summary> 
        /// Comparator being used for stable ordering in anonymous type indices.
        /// </summary>
        private sealed class AnonymousTypeComparer : IComparer<AnonymousTypeTemplateSymbol>
        {
            private readonly CSharpCompilation compilation;

            public AnonymousTypeComparer(CSharpCompilation compilation)
            {
                this.compilation = compilation;
            }

            public int Compare(AnonymousTypeTemplateSymbol x, AnonymousTypeTemplateSymbol y)
            {
                if ((object)x == (object)y)
                {
                    return 0;
                }

                // We compare two anonymous type templated by comparing their locations and descriptor keys

                // NOTE: If anonymous type got to this phase it must have the location set
                int result = this.CompareLocations(x.SmallestLocation, y.SmallestLocation);

                if (result == 0)
                {
                    // It is still possible for two templates to have the same smallest location 
                    // in case they are implicitly created and use the same syntax for location
                    result = string.CompareOrdinal(x.TypeDescriptorKey, y.TypeDescriptorKey);
                }

                return result;
            }

            private int CompareLocations(Location x, Location y)
            {
                if (x == y)
                {
                    return 0;
                }
                else if (x == Location.None)
                {
                    return -1;
                }
                else if (y == Location.None)
                {
                    return 1;
                }
                else
                {
                    return this.compilation.CompareSourceLocations(x, y);
                }
            }
        }
    }
}
