﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp.Symbols.Metadata.PE;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Instrumentation;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp
{
    using MetadataOrDiagnostic = System.Object;

    public partial class CSharpCompilation
    {
        /// <summary>
        /// ReferenceManager encapsulates functionality to create an underlying SourceAssemblySymbol 
        /// (with underlying ModuleSymbols) for Compilation and AssemblySymbols for referenced
        /// assemblies (with underlying ModuleSymbols) all properly linked together based on
        /// reference resolution between them.
        /// 
        /// ReferenceManager is also responsible for reuse of metadata readers for imported modules
        /// and assemblies as well as existing AssemblySymbols for referenced assemblies. In order
        /// to do that, it maintains global cache for metadata readers and AssemblySymbols
        /// associated with them. The cache uses WeakReferences to refer to the metadata readers and
        /// AssemblySymbols to allow memory and resources being reclaimed once they are no longer
        /// used. The tricky part about reusing existing AssemblySymbols is to find a set of
        /// AssemblySymbols that are created for the referenced assemblies, which (the
        /// AssemblySymbols from the set) are linked in a way, consistent with the reference
        /// resolution between the referenced assemblies.
        /// 
        /// When existing Compilation is used as a metadata reference, there are scenarios when its
        /// underlying SourceAssemblySymbol cannot be used to provide symbols in context of the new
        /// Compilation. Consider classic multi-targeting scenario: compilation C1 references v1 of
        /// Lib.dll and compilation C2 references C1 and v2 of Lib.dll. In this case,
        /// SourceAssemblySymbol for C1 is linked to AssemblySymbol for v1 of Lib.dll. However,
        /// given the set of references for C2, the same reference for C1 should be resolved against
        /// v2 of Lib.dll. In other words, in context of C2, all types from v1 of Lib.dll leaking
        /// through C1 (through method signatures, etc.) must be retargeted to the types from v2 of
        /// Lib.dll. In this case, ReferenceManager creates a special RetargetingAssemblySymbol for
        /// C1, which is responsible for the type retargeting. The RetargetingAssemblySymbols could
        /// also be reused for different Compilations, ReferenceManager maintains a cache of
        /// RetargetingAssemblySymbols (WeakReferences) for each Compilation.
        /// 
        /// The only public entry point of this class is CreateSourceAssembly() method.
        /// </summary>
        internal sealed class ReferenceManager : CommonReferenceManager<CSharpCompilation, AssemblySymbol>
        {
            public ReferenceManager(string simpleAssemblyName, AssemblyIdentityComparer identityComparer, Dictionary<MetadataReference, MetadataOrDiagnostic> observedMetadata)
                : base(simpleAssemblyName, identityComparer, observedMetadata)
            {
            }

            protected override CommonMessageProvider MessageProvider
            {
                get { return CSharp.MessageProvider.Instance; }
            }

            protected override AssemblyData CreateAssemblyDataForFile(
                PEAssembly assembly,
                WeakList<IAssemblySymbol> cachedSymbols,
                DocumentationProvider documentationProvider,
                string sourceAssemblySimpleName,
                MetadataImportOptions importOptions,
                bool embedInteropTypes)
            {
                return new AssemblyDataForFile(
                    assembly,
                    cachedSymbols,
                    embedInteropTypes,
                    documentationProvider,
                    sourceAssemblySimpleName,
                    importOptions);
            }

            protected override AssemblyData CreateAssemblyDataForCompilation(CompilationReference compilationReference)
            {
                var csReference = compilationReference as CSharpCompilationReference;
                if (csReference == null)
                {
                    throw new NotSupportedException(string.Format(CSharpResources.CantReferenceCompilationOf, compilationReference.GetType(), "C#"));
                }

                var result = new AssemblyDataForCompilation(csReference.Compilation, csReference.Properties.EmbedInteropTypes);
                Debug.Assert((object)csReference.Compilation.lazyAssemblySymbol != null);
                return result;
            }

            /// <summary>
            /// Checks if the properties of <paramref name="duplicateReference"/> are compatible with properties of <paramref name="primaryReference"/>.
            /// Reports inconsistencies to the given diagnostic bag.
            /// </summary>
            /// <returns>True if the properties are compatible and hence merged, false if the duplicate reference should not merge it's properties with primary reference.</returns>
            protected override bool CheckPropertiesConsistency(MetadataReference primaryReference, MetadataReference duplicateReference, DiagnosticBag diagnostics)
            {
                if (primaryReference.Properties.EmbedInteropTypes != duplicateReference.Properties.EmbedInteropTypes)
                {
                    diagnostics.Add(ErrorCode.ERR_AssemblySpecifiedForLinkAndRef, NoLocation.Singleton, duplicateReference.Display, primaryReference.Display);
                    return false;
                }

                return true;
            }

            /// <summary>
            /// C# only considers culture when comparing weak identities.
            /// It ignores versions of weak identities and reports an error if there are two weak assembly 
            /// references passed to a compilation that have the same simple name.
            /// </summary>
            protected override bool WeakIdentityPropertiesEquivalent(AssemblyIdentity identity1, AssemblyIdentity identity2)
            {
                Debug.Assert(AssemblyIdentityComparer.SimpleNameComparer.Equals(identity1.Name, identity2.Name));
                return AssemblyIdentityComparer.CultureComparer.Equals(identity1.CultureName, identity2.CultureName);
            }

            protected override AssemblySymbol[] GetActualBoundReferencesUsedBy(AssemblySymbol assemblySymbol)
            {
                var refs = new List<AssemblySymbol>();

                foreach (var module in assemblySymbol.Modules)
                {
                    refs.AddRange(module.GetReferencedAssemblySymbols());
                }

                for (int i = 0; i < refs.Count; i++)
                {
                    if (refs[i].IsMissing)
                    {
                        refs[i] = null; // Do not expose missing assembly symbols to ReferenceManager.Binder
                    }
                }

                return refs.ToArray();
            }

            protected override ImmutableArray<AssemblySymbol> GetNoPiaResolutionAssemblies(AssemblySymbol candidateAssembly)
            {
                if (candidateAssembly is SourceAssemblySymbol)
                {
                    // This is an optimization, if candidateAssembly links something or explicitly declares local type, 
                    // common reference binder shouldn't reuse this symbol because candidateAssembly won't be in the 
                    // set returned by GetNoPiaResolutionAssemblies(). This also makes things clearer.
                    return ImmutableArray<AssemblySymbol>.Empty;
                }

                return candidateAssembly.GetNoPiaResolutionAssemblies();
            }

            protected override bool IsLinked(AssemblySymbol candidateAssembly)
            {
                return candidateAssembly.IsLinked;
            }

            protected override AssemblySymbol GetCorLibrary(AssemblySymbol candidateAssembly)
            {
                AssemblySymbol corLibrary = candidateAssembly.CorLibrary;

                // Do not expose missing assembly symbols to ReferenceManager.Binder
                return corLibrary.IsMissing ? null : corLibrary;
            }

            public void CreateSourceAssemblyForCompilation(CSharpCompilation compilation)
            {
                using (Logger.LogBlock(FunctionId.CSharp_Compilation_CreateSourceAssembly, message: compilation.AssemblyName))
                {
                    // We are reading the Reference Manager state outside of a lock by accessing 
                    // IsBound and HasCircularReference properties.
                    // Once isBound flag is flipped the state of the manager is available and doesn't change.
                    // 
                    // If two threads are building SourceAssemblySymbol and the first just updated 
                    // set isBound flag to 1 but not yet set lazySourceAssemblySymbol,
                    // the second thread may end up reusing the Reference Manager data the first thread calculated. 
                    // That's ok since 
                    // 1) the second thread would produce the same data,
                    // 2) all results calculated by the second thread will be thrown away since the first thread 
                    //    already acquired SymbolCacheAndReferenceManagerStateGuard that is needed to publish the data.

                    // The given compilation is the first compilation that shares this manager and its symbols are requested.
                    // Perform full reference resolution and binding.
                    if (!IsBound && CreateAndSetSourceAssemblyFullBind(compilation))
                    {
                        // we have successfully bound the references for the compilation
                    }
                    else if (!HasCircularReference)
                    {
                        // Another compilation that shares the manager with the given compilation
                        // already bound its references and produced tables that we can use to construct 
                        // source assembly symbol faster. Unless we encountered a circular reference.
                        CreateAndSetSourceAssemblyReuseData(compilation);
                    }
                    else
                    {
                        // We encountered a circular reference while binding the previous compilation.
                        // This compilation can't share bound references with other compilations. Create a new manager.

                        // NOTE: The CreateSourceAssemblyFullBind is going to replace compilation's reference manager with newManager.

                        var newManager = new ReferenceManager(this.SimpleAssemblyName, this.IdentityComparer, this.ObservedMetadata);
                        var successful = newManager.CreateAndSetSourceAssemblyFullBind(compilation);

                        // The new manager isn't shared with any other compilation so there is no other 
                        // thread but the current one could have initialized it.
                        Debug.Assert(successful);

                        newManager.AssertBound();
                    }

                    AssertBound();
                    Debug.Assert((object)compilation.lazyAssemblySymbol != null);
                }
            }

            /// <summary>
            /// Creates a <see cref="PEAssemblySymbol"/> from specified metadata. 
            /// </summary>
            /// <remarks>
            /// Used by EnC to create symbols for emit baseline. The PE symbols are used by <see cref="SymbolMatcher"/>.
            /// 
            /// The assembly references listed in the metadata AssemblyRef table are matched to the resolved references 
            /// stored on this <see cref="ReferenceManager"/>. Each AssemblyRef is matched against the assembly identities
            /// using an exact equality comparison. No unification or further resolution is performed.
            /// </remarks>
            public PEAssemblySymbol CreatePEAssemblyForAssemblyMetadata(AssemblyMetadata metadata, MetadataImportOptions importOptions)
            {
                AssertBound();

                var referencedAssembliesByIdentity = new Dictionary<AssemblyIdentity, AssemblySymbol>();
                foreach (var symbol in this.ReferencedAssemblies)
                {
                    referencedAssembliesByIdentity.Add(symbol.Identity, symbol);
                }

                var assembly = metadata.Assembly;
                var peReferences = assembly.AssemblyReferences.SelectAsArray(MapAssemblyIdentityToResolvedSymbol, referencedAssembliesByIdentity);
                var assemblySymbol = new PEAssemblySymbol(assembly, DocumentationProvider.Default, isLinked: false, importOptions: importOptions);

                var unifiedAssemblies = this.UnifiedAssemblies.WhereAsArray(unified => referencedAssembliesByIdentity.ContainsKey(unified.OriginalReference));
                InitializeAssemblyReuseData(assemblySymbol, peReferences, unifiedAssemblies);

                if (assembly.ContainsNoPiaLocalTypes())
                {
                    assemblySymbol.SetNoPiaResolutionAssemblies(this.ReferencedAssemblies);
                }

                return assemblySymbol;
            }

            private static AssemblySymbol MapAssemblyIdentityToResolvedSymbol(AssemblyIdentity identity, Dictionary<AssemblyIdentity, AssemblySymbol> map)
            {
                AssemblySymbol symbol;
                if (map.TryGetValue(identity, out symbol))
                {
                    return symbol;
                }
                return new MissingAssemblySymbol(identity);
            }

            private void CreateAndSetSourceAssemblyReuseData(CSharpCompilation compilation)
            {
                AssertBound();

                // If the compilation has a reference from metadata to source assembly 
                // we can't share the referenced PE symbols.
                Debug.Assert(!HasCircularReference);

                string moduleName = compilation.MakeSourceModuleName();
                var assemblySymbol = new SourceAssemblySymbol(compilation, this.SimpleAssemblyName, moduleName, this.ReferencedModules);

                InitializeAssemblyReuseData(assemblySymbol);

                if ((object)compilation.lazyAssemblySymbol == null)
                {
                    lock (SymbolCacheAndReferenceManagerStateGuard)
                    {
                        if ((object)compilation.lazyAssemblySymbol == null)
                        {
                            compilation.lazyAssemblySymbol = assemblySymbol;
                            Debug.Assert(ReferenceEquals(compilation.referenceManager, this));
                        }
                    }
                }
            }

            private void InitializeAssemblyReuseData(AssemblySymbol assemblySymbol)
            {
                InitializeAssemblyReuseData(assemblySymbol, this.ReferencedAssemblies, this.UnifiedAssemblies);
            }

            private void InitializeAssemblyReuseData(AssemblySymbol assemblySymbol, ImmutableArray<AssemblySymbol> referencedAssemblies, ImmutableArray<UnifiedAssembly<AssemblySymbol>> unifiedAssemblies)
            {
                AssertBound();

                assemblySymbol.SetCorLibrary(this.CorLibrary);

                var sourceModuleReferences = new ModuleReferences<AssemblySymbol>(referencedAssemblies.SelectAsArray(a => a.Identity), referencedAssemblies, unifiedAssemblies);
                assemblySymbol.Modules[0].SetReferences(sourceModuleReferences);

                var assemblyModules = assemblySymbol.Modules;
                var referencedModulesReferences = this.ReferencedModulesReferences;
                Debug.Assert(assemblyModules.Length == referencedModulesReferences.Length + 1);

                for (int i = 1; i < assemblyModules.Length; i++)
                {
                    assemblyModules[i].SetReferences(referencedModulesReferences[i - 1]);
                }
            }

            // Returns false if another compilation sharing this manager finished binding earlier and we should reuse its results.
            private bool CreateAndSetSourceAssemblyFullBind(CSharpCompilation compilation)
            {
                SourceAssemblySymbol assemblySymbol;

                Dictionary<MetadataReference, ReferencedAssembly> referencedAssembliesMap;
                Dictionary<MetadataReference, int> referencedModulesMap;
                IDictionary<string, MetadataReference> boundReferenceDirectiveMap;
                ImmutableArray<MetadataReference> boundReferenceDirectives;
                bool hasCircularReference;

                // Let's process our references and separate Assembly refs from addmodule.
                ImmutableArray<AssemblyData> referencedAssemblies;
                ImmutableArray<PEModule> modules; // To make sure the modules are not collected ahead of time.
                ImmutableArray<MetadataReference> references;

                DiagnosticBag diagnostics = DiagnosticBag.GetInstance();
                bool exception = true;
                var referenceMap = default(ImmutableArray<ResolvedReference>);
                try
                {
                    referenceMap = ResolveMetadataReferences(
                        compilation,
                        out references,
                        out boundReferenceDirectiveMap,
                        out boundReferenceDirectives,
                        out referencedAssemblies,
                        out modules,
                        diagnostics);
                    exception = false;
                }
                finally
                {
                    if (exception)
                    {
                        // Prevent a leak report in the unit test output.
                        diagnostics.Free();
                    }
                }

                ImmutableArray<Diagnostic> resolutionDiagnostics = diagnostics.ToReadOnlyAndFree();

                var assemblyBeingBuiltData = new AssemblyDataForAssemblyBeingBuilt(new AssemblyIdentity(name: SimpleAssemblyName), referencedAssemblies, modules);

                var assembliesArray = new AssemblyData[referencedAssemblies.Length + 1];
                assembliesArray[0] = assemblyBeingBuiltData;
                for (int i = 1; i < assembliesArray.Length; i++)
                {
                    assembliesArray[i] = referencedAssemblies[i - 1];
                }

                ImmutableArray<AssemblyData> allAssemblies = assembliesArray.AsImmutableOrNull();

                // Let's bind all the references
                int corLibraryIndex;
                BoundInputAssembly[] bindingResult = Bind(allAssemblies,
                                                          out hasCircularReference,
                                                          out corLibraryIndex);

                Debug.Assert(bindingResult.Length == allAssemblies.Length);

                // Create AssemblySymbols for assemblies that can't use any existing symbols.
                var newSymbols = new List<int>();

                for (int i = 1; i < bindingResult.Length; i++)
                {
                    if ((object)bindingResult[i].AssemblySymbol == null)
                    {
                        // symbols hasn't been found in the cache, create a new one

                        var compilationData = allAssemblies[i] as AssemblyDataForCompilation;
                        if (compilationData != null)
                        {
                            bindingResult[i].AssemblySymbol = new Symbols.Retargeting.RetargetingAssemblySymbol(
                                                        compilationData.Compilation.SourceAssembly, compilationData.IsLinked);
                        }
                        else
                        {
                            var fileData = (AssemblyDataForFile)allAssemblies[i];

                            bindingResult[i].AssemblySymbol = new PEAssemblySymbol(fileData.Assembly, fileData.DocumentationProvider, fileData.IsLinked, fileData.EffectiveImportOptions);
                        }

                        newSymbols.Add(i);
                    }

                    Debug.Assert(allAssemblies[i].IsLinked == bindingResult[i].AssemblySymbol.IsLinked);
                }

                assemblySymbol = new SourceAssemblySymbol(compilation, SimpleAssemblyName, compilation.MakeSourceModuleName(), netModules: modules);

                AssemblySymbol corLibrary;

                if (corLibraryIndex == 0)
                {
                    corLibrary = assemblySymbol;
                }
                else if (corLibraryIndex > 0)
                {
                    corLibrary = bindingResult[corLibraryIndex].AssemblySymbol;
                }
                else
                {
                    corLibrary = MissingCorLibrarySymbol.Instance;
                }

                assemblySymbol.SetCorLibrary(corLibrary);

                // Setup bound references for newly created AssemblySymbols
                // This should be done after we created/found all AssemblySymbols 
                Dictionary<AssemblyIdentity, MissingAssemblySymbol> missingAssemblies = null;

                // Setup bound references for newly created SourceAssemblySymbol
                ImmutableArray<ModuleReferences<AssemblySymbol>> moduleReferences;
                SetupReferencesForSourceAssembly(assemblySymbol, assemblyBeingBuiltData, bindingResult, ref missingAssemblies, out moduleReferences);

                if (newSymbols.Count > 0)
                {
                    // Only if we detected that a referenced assembly refers to the assembly being built
                    // we allow the references to get ahold of the assembly being built.
                    if (hasCircularReference)
                    {
                        bindingResult[0].AssemblySymbol = assemblySymbol;
                    }

                    InitializeNewSymbols(newSymbols, assemblySymbol, allAssemblies, bindingResult, missingAssemblies);
                }

                // Setup references for the compilation (out parameters)
                referencedAssembliesMap = new Dictionary<MetadataReference, ReferencedAssembly>(referenceMap.Length);
                referencedModulesMap = new Dictionary<MetadataReference, int>(modules.Length);

                var sourceModule = assemblySymbol.SourceModule;
                var referencedAssemblySymbols = sourceModule.GetReferencedAssemblySymbols();

                for (int i = 0; i < referenceMap.Length; i++)
                {
                    if (referenceMap[i].IsSkipped)
                    {
                        continue;
                    }

                    if (referenceMap[i].Kind == MetadataImageKind.Module)
                    {
                        // add 1 for the manifest module:
                        int moduleIndex = 1 + referenceMap[i].Index;
                        referencedModulesMap.Add(references[i], moduleIndex);
                    }
                    else
                    {
                        int assemblyIndex = referenceMap[i].Index;
                        referencedAssembliesMap.Add(references[i], new ReferencedAssembly(referencedAssemblySymbols[assemblyIndex], referenceMap[i].Aliases));
                    }
                }

                if ((object)compilation.lazyAssemblySymbol == null)
                {
                    lock (SymbolCacheAndReferenceManagerStateGuard)
                    {
                        if ((object)compilation.lazyAssemblySymbol == null)
                        {
                            if (IsBound)
                            {
                                // Another thread has finished constructing AssemblySymbol for another compilation that shares this manager.
                                // Drop the results and reuse the symbols that were created for the other compilation.
                                return false;
                            }

                            UpdateSymbolCacheNoLock(newSymbols, allAssemblies, bindingResult);

                            InitializeNoLock(
                                referencedAssembliesMap,
                                referencedModulesMap,
                                boundReferenceDirectiveMap,
                                boundReferenceDirectives,
                                hasCircularReference,
                                resolutionDiagnostics,
                                corLibrary,
                                modules,
                                moduleReferences,
                                referencedAssemblySymbols,
                                sourceModule.GetUnifiedAssemblies());

                            // Make sure that the given compilation holds on this instance of reference manager.
                            Debug.Assert(ReferenceEquals(compilation.referenceManager, this) || HasCircularReference);
                            compilation.referenceManager = this;

                            // Finally, publish the source symbol after all data have been written.
                            // Once lazyAssemblySymbol is non-null other readers might start reading the data written above.
                            compilation.lazyAssemblySymbol = assemblySymbol;
                        }
                    }
                }

                return true;
            }

            private static void InitializeNewSymbols(
                List<int> newSymbols,
                SourceAssemblySymbol sourceAssembly,
                ImmutableArray<AssemblyData> assemblies,
                BoundInputAssembly[] bindingResult,
                Dictionary<AssemblyIdentity, MissingAssemblySymbol> missingAssemblies)
            {
                Debug.Assert(newSymbols.Count > 0);

                var corLibrary = sourceAssembly.CorLibrary;
                Debug.Assert((object)corLibrary != null);

                foreach (int i in newSymbols)
                {
                    var compilationData = assemblies[i] as AssemblyDataForCompilation;

                    if (compilationData != null)
                    {
                        SetupReferencesForRetargetingAssembly(bindingResult, i, ref missingAssemblies, sourceAssemblyDebugOnly: sourceAssembly);
                    }
                    else
                    {
                        var fileData = (AssemblyDataForFile)assemblies[i];
                        SetupReferencesForFileAssembly(fileData, bindingResult, i, ref missingAssemblies, sourceAssemblyDebugOnly: sourceAssembly);
                    }
                }

                // Setup CorLibrary and NoPia stuff for newly created assemblies

                List<AssemblySymbol> linkedReferencedAssemblies = new List<AssemblySymbol>();
                var noPiaResolutionAssemblies = sourceAssembly.Modules[0].GetReferencedAssemblySymbols();

                foreach (int i in newSymbols)
                {
                    if (assemblies[i].ContainsNoPiaLocalTypes)
                    {
                        bindingResult[i].AssemblySymbol.SetNoPiaResolutionAssemblies(noPiaResolutionAssemblies);
                    }

                    // Setup linked referenced assemblies.
                    linkedReferencedAssemblies.Clear();

                    if (assemblies[i].IsLinked)
                    {
                        linkedReferencedAssemblies.Add(bindingResult[i].AssemblySymbol);
                    }

                    foreach (var referenceBinding in bindingResult[i].ReferenceBinding)
                    {
                        if (referenceBinding.IsBound &&
                            assemblies[referenceBinding.DefinitionIndex].IsLinked)
                        {
                            linkedReferencedAssemblies.Add(
                                bindingResult[referenceBinding.DefinitionIndex].AssemblySymbol);
                        }
                    }

                    if (linkedReferencedAssemblies.Count > 0)
                    {
                        bindingResult[i].AssemblySymbol.SetLinkedReferencedAssemblies(
                            ImmutableArray.CreateRange<AssemblySymbol>(linkedReferencedAssemblies.Distinct()));
                    }

                    bindingResult[i].AssemblySymbol.SetCorLibrary(corLibrary);
                }

                if (missingAssemblies != null)
                {
                    foreach (var missingAssembly in missingAssemblies.Values)
                    {
                        missingAssembly.SetCorLibrary(corLibrary);
                    }
                }
            }

            private static void UpdateSymbolCacheNoLock(List<int> newSymbols, ImmutableArray<AssemblyData> assemblies, BoundInputAssembly[] bindingResult)
            {
                // Add new assembly symbols into the cache
                foreach (int i in newSymbols)
                {
                    var compilationData = assemblies[i] as AssemblyDataForCompilation;

                    if (compilationData != null)
                    {
                        compilationData.Compilation.CacheRetargetingAssemblySymbolNoLock(bindingResult[i].AssemblySymbol);
                    }
                    else
                    {
                        var fileData = (AssemblyDataForFile)assemblies[i];
                        fileData.CachedSymbols.Add((PEAssemblySymbol)bindingResult[i].AssemblySymbol);
                    }
                }
            }

            private static void SetupReferencesForRetargetingAssembly(
                BoundInputAssembly[] bindingResult,
                int bindingIndex,
                ref Dictionary<AssemblyIdentity, MissingAssemblySymbol> missingAssemblies,
                SourceAssemblySymbol sourceAssemblyDebugOnly)
            {
                var retargetingAssemblySymbol = (Symbols.Retargeting.RetargetingAssemblySymbol)bindingResult[bindingIndex].AssemblySymbol;
                ImmutableArray<ModuleSymbol> modules = retargetingAssemblySymbol.Modules;
                int moduleCount = modules.Length;
                int refsUsed = 0;

                for (int j = 0; j < moduleCount; j++)
                {
                    ImmutableArray<AssemblyIdentity> referencedAssemblies =
                        retargetingAssemblySymbol.UnderlyingAssembly.Modules[j].GetReferencedAssemblies();

                    // For source module skip underlying linked references
                    if (j == 0)
                    {
                        ImmutableArray<AssemblySymbol> underlyingReferencedAssemblySymbols =
                            retargetingAssemblySymbol.UnderlyingAssembly.Modules[0].GetReferencedAssemblySymbols();

                        int linkedUnderlyingReferences = 0;
                        foreach (AssemblySymbol asm in underlyingReferencedAssemblySymbols)
                        {
                            if (asm.IsLinked)
                            {
                                linkedUnderlyingReferences++;
                            }
                        }

                        if (linkedUnderlyingReferences > 0)
                        {
                            var filteredReferencedAssemblies = new AssemblyIdentity[referencedAssemblies.Length - linkedUnderlyingReferences];
                            int newIndex = 0;

                            for (int k = 0; k < underlyingReferencedAssemblySymbols.Length; k++)
                            {
                                if (!underlyingReferencedAssemblySymbols[k].IsLinked)
                                {
                                    filteredReferencedAssemblies[newIndex] = referencedAssemblies[k];
                                    newIndex++;
                                }
                            }

                            Debug.Assert(newIndex == filteredReferencedAssemblies.Length);
                            referencedAssemblies = filteredReferencedAssemblies.AsImmutableOrNull();
                        }
                    }

                    int refsCount = referencedAssemblies.Length;
                    AssemblySymbol[] symbols = new AssemblySymbol[refsCount];
                    ArrayBuilder<UnifiedAssembly<AssemblySymbol>> unifiedAssemblies = null;

                    for (int k = 0; k < refsCount; k++)
                    {
                        var referenceBinding = bindingResult[bindingIndex].ReferenceBinding[refsUsed + k];
                        if (referenceBinding.IsBound)
                        {
                            symbols[k] = GetAssemblyDefinitionSymbol(bindingResult, referenceBinding, ref unifiedAssemblies);
                        }
                        else
                        {
                            symbols[k] = GetOrAddMissingAssemblySymbol(referencedAssemblies[k], ref missingAssemblies);
                        }
                    }

                    var moduleReferences = new ModuleReferences<AssemblySymbol>(referencedAssemblies, symbols.AsImmutableOrNull(), unifiedAssemblies.AsImmutableOrEmpty());
                    modules[j].SetReferences(moduleReferences, sourceAssemblyDebugOnly);

                    refsUsed += refsCount;
                }
            }

            private static void SetupReferencesForFileAssembly(
                AssemblyDataForFile fileData,
                BoundInputAssembly[] bindingResult,
                int bindingIndex,
                ref Dictionary<AssemblyIdentity, MissingAssemblySymbol> missingAssemblies,
                SourceAssemblySymbol sourceAssemblyDebugOnly)
            {
                var portableExecutableAssemblySymbol = (PEAssemblySymbol)bindingResult[bindingIndex].AssemblySymbol;

                ImmutableArray<ModuleSymbol> modules = portableExecutableAssemblySymbol.Modules;
                int moduleCount = modules.Length;
                int refsUsed = 0;

                for (int j = 0; j < moduleCount; j++)
                {
                    int moduleReferenceCount = fileData.Assembly.ModuleReferenceCounts[j];
                    var identities = new AssemblyIdentity[moduleReferenceCount];
                    var symbols = new AssemblySymbol[moduleReferenceCount];

                    fileData.AssemblyReferences.CopyTo(refsUsed, identities, 0, moduleReferenceCount);

                    ArrayBuilder<UnifiedAssembly<AssemblySymbol>> unifiedAssemblies = null;
                    for (int k = 0; k < moduleReferenceCount; k++)
                    {
                        var boundReference = bindingResult[bindingIndex].ReferenceBinding[refsUsed + k];
                        if (boundReference.IsBound)
                        {
                            symbols[k] = GetAssemblyDefinitionSymbol(bindingResult, boundReference, ref unifiedAssemblies);
                        }
                        else
                        {
                            symbols[k] = GetOrAddMissingAssemblySymbol(identities[k], ref missingAssemblies);
                        }
                    }

                    var moduleReferences = new ModuleReferences<AssemblySymbol>(identities.AsImmutableOrNull(), symbols.AsImmutableOrNull(), unifiedAssemblies.AsImmutableOrEmpty());
                    modules[j].SetReferences(moduleReferences, sourceAssemblyDebugOnly);

                    refsUsed += moduleReferenceCount;
                }
            }

            private static void SetupReferencesForSourceAssembly(
                SourceAssemblySymbol sourceAssembly,
                AssemblyDataForAssemblyBeingBuilt assemblyBeingBuiltData,
                BoundInputAssembly[] bindingResult,
                ref Dictionary<AssemblyIdentity, MissingAssemblySymbol> missingAssemblies,
                out ImmutableArray<ModuleReferences<AssemblySymbol>> moduleReferences)
            {
                ImmutableArray<ModuleSymbol> modules = sourceAssembly.Modules;
                int moduleCount = modules.Length;
                int refsUsed = 0;
                ArrayBuilder<ModuleReferences<AssemblySymbol>> moduleReferencesBuilder = moduleCount > 1
                    ? ArrayBuilder<ModuleReferences<AssemblySymbol>>.GetInstance()
                    : null;

                for (int moduleIndex = 0; moduleIndex < moduleCount; moduleIndex++)
                {
                    int refsCount = assemblyBeingBuiltData.ReferencesCountForModule[moduleIndex];
                    var identities = new AssemblyIdentity[refsCount];
                    var symbols = new AssemblySymbol[refsCount];

                    assemblyBeingBuiltData.AssemblyReferences.CopyTo(refsUsed, identities, 0, refsCount);

                    ArrayBuilder<UnifiedAssembly<AssemblySymbol>> unifiedAssemblies = null;

                    for (int k = 0; k < refsCount; k++)
                    {
                        var boundReference = bindingResult[0].ReferenceBinding[refsUsed + k];
                        if (boundReference.IsBound)
                        {
                            symbols[k] = GetAssemblyDefinitionSymbol(bindingResult, boundReference, ref unifiedAssemblies);
                        }
                        else
                        {
                            symbols[k] = GetOrAddMissingAssemblySymbol(identities[k], ref missingAssemblies);
                        }
                    }

                    var references = new ModuleReferences<AssemblySymbol>(
                        identities.AsImmutableOrNull(),
                        symbols.AsImmutableOrNull(),
                        unifiedAssemblies.AsImmutableOrEmpty());

                    if (moduleIndex > 0)
                    {
                        moduleReferencesBuilder.Add(references);
                    }

                    modules[moduleIndex].SetReferences(references, sourceAssembly);

                    refsUsed += refsCount;
                }

                moduleReferences = moduleReferencesBuilder == null
                    ? ImmutableArray<ModuleReferences<AssemblySymbol>>.Empty
                    : moduleReferencesBuilder.ToImmutableAndFree();
            }

            private static AssemblySymbol GetAssemblyDefinitionSymbol(
                BoundInputAssembly[] bindingResult,
                AssemblyReferenceBinding referenceBinding,
                ref ArrayBuilder<UnifiedAssembly<AssemblySymbol>> unifiedAssemblies)
            {
                Debug.Assert(referenceBinding.IsBound);

                var assembly = bindingResult[referenceBinding.DefinitionIndex].AssemblySymbol;
                Debug.Assert((object)assembly != null);

                if (referenceBinding.VersionDifference != 0)
                {
                    if (unifiedAssemblies == null)
                    {
                        unifiedAssemblies = new ArrayBuilder<UnifiedAssembly<AssemblySymbol>>();
                    }

                    unifiedAssemblies.Add(new UnifiedAssembly<AssemblySymbol>(assembly, referenceBinding.ReferenceIdentity));
                }

                return assembly;
            }

            private static MissingAssemblySymbol GetOrAddMissingAssemblySymbol(
                AssemblyIdentity assemblyIdentity,
                ref Dictionary<AssemblyIdentity, MissingAssemblySymbol> missingAssemblies)
            {
                MissingAssemblySymbol missingAssembly;

                if (missingAssemblies == null)
                {
                    missingAssemblies = new Dictionary<AssemblyIdentity, MissingAssemblySymbol>();
                }
                else if (missingAssemblies.TryGetValue(assemblyIdentity, out missingAssembly))
                {
                    return missingAssembly;
                }

                missingAssembly = new MissingAssemblySymbol(assemblyIdentity);
                missingAssemblies.Add(assemblyIdentity, missingAssembly);

                return missingAssembly;
            }

            private abstract class AssemblyDataForMetadataOrCompilation : AssemblyData
            {
                private List<AssemblySymbol> assemblies;
                protected AssemblyIdentity assemblyIdentity;
                protected ImmutableArray<AssemblyIdentity> referencedAssemblies;
                protected readonly bool EmbedInteropTypes;

                protected AssemblyDataForMetadataOrCompilation(bool embedInteropTypes)
                {
                    this.EmbedInteropTypes = embedInteropTypes;
                }

                public override AssemblyIdentity Identity
                {
                    get
                    {
                        return assemblyIdentity;
                    }
                }

                public override IEnumerable<AssemblySymbol> AvailableSymbols
                {
                    get
                    {
                        if (assemblies == null)
                        {
                            assemblies = new List<AssemblySymbol>();

                            // This should be done lazy because while we creating
                            // instances of this type, creation of new SourceAssembly symbols
                            // might change the set of available AssemblySymbols.
                            AddAvailableSymbols(assemblies);
                        }

                        return assemblies;
                    }
                }

                protected abstract void AddAvailableSymbols(List<AssemblySymbol> assemblies);

                public override ImmutableArray<AssemblyIdentity> AssemblyReferences
                {
                    get
                    {
                        return referencedAssemblies;
                    }
                }

                public override AssemblyReferenceBinding[] BindAssemblyReferences(
                    ImmutableArray<AssemblyData> assemblies, AssemblyIdentityComparer assemblyIdentityComparer)
                {
                    return ReferenceManager.ResolveReferencedAssemblies(referencedAssemblies, assemblies, assemblyIdentityComparer, okToResolveAgainstCompilationBeingCreated: true);
                }

                public sealed override bool IsLinked
                {
                    get
                    {
                        return EmbedInteropTypes;
                    }
                }
            }

            private sealed class AssemblyDataForFile : AssemblyDataForMetadataOrCompilation
            {
                private readonly PEAssembly assembly;
                private readonly WeakList<IAssemblySymbol> cachedSymbols;
                private readonly DocumentationProvider documentationProvider;

                /// <summary>
                /// Import options of the compilation being built.
                /// </summary>
                private readonly MetadataImportOptions compilationImportOptions;

                // This is the name of the compilation that is being built. 
                // This should be the assembly name w/o the extension. It is
                // used to compute whether or not it is possible that this
                // assembly will give friend access to the compilation.
                private readonly string sourceAssemblySimpleName;

                public PEAssembly Assembly
                {
                    get
                    {
                        return this.assembly;
                    }
                }

                /// <summary>
                /// Guarded by <see cref="F:CommonReferenceManager.SymbolCacheAndReferenceManagerStateGuard"/>.
                /// </summary>
                public WeakList<IAssemblySymbol> CachedSymbols
                {
                    get
                    {
                        return this.cachedSymbols;
                    }
                }

                public DocumentationProvider DocumentationProvider
                {
                    get
                    {
                        return this.documentationProvider;
                    }
                }

                public AssemblyDataForFile(
                    PEAssembly assembly,
                    WeakList<IAssemblySymbol> cachedSymbols,
                    bool embedInteropTypes,
                    DocumentationProvider documentationProvider,
                    string sourceAssemblySimpleName,
                    MetadataImportOptions compilationImportOptions)
                    : base(embedInteropTypes)
                {
                    Debug.Assert(assembly != null);
                    Debug.Assert(documentationProvider != null);
                    Debug.Assert(cachedSymbols != null);

                    this.cachedSymbols = cachedSymbols;
                    this.assembly = assembly;
                    this.documentationProvider = documentationProvider;
                    this.compilationImportOptions = compilationImportOptions;
                    this.sourceAssemblySimpleName = sourceAssemblySimpleName;

                    assemblyIdentity = assembly.Identity;
                    referencedAssemblies = assembly.AssemblyReferences;
                }

                private bool internalsVisibleComputed = false;
                private bool internalsPotentiallyVisibleToCompilation = false;

                internal bool InternalsMayBeVisibleToCompilation
                {
                    get
                    {
                        if (!internalsVisibleComputed)
                        {
                            internalsPotentiallyVisibleToCompilation = InternalsMayBeVisibleToAssemblyBeingCompiled(sourceAssemblySimpleName, assembly);
                            internalsVisibleComputed = true;
                        }

                        return internalsPotentiallyVisibleToCompilation;
                    }
                }

                internal MetadataImportOptions EffectiveImportOptions
                {
                    get
                    {
                        // We need to import internal members if they might be visible to the compilation being compiled:
                        if (InternalsMayBeVisibleToCompilation && compilationImportOptions == MetadataImportOptions.Public)
                        {
                            return MetadataImportOptions.Internal;
                        }

                        return compilationImportOptions;
                    }
                }

                protected override void AddAvailableSymbols(List<AssemblySymbol> assemblies)
                {
                    // accessing cached symbols requires a lock
                    lock (SymbolCacheAndReferenceManagerStateGuard)
                    {
                        foreach (var assembly in cachedSymbols)
                        {
                            var peAssembly = assembly as PEAssemblySymbol;
                            if (IsMatchingAssembly(peAssembly))
                            {
                                assemblies.Add(peAssembly);
                            }
                        }
                    }
                }

                public override bool IsMatchingAssembly(AssemblySymbol candidateAssembly)
                {
                    return IsMatchingAssembly(candidateAssembly as PEAssemblySymbol);
                }

                private bool IsMatchingAssembly(PEAssemblySymbol peAssembly)
                {
                    if ((object)peAssembly == null)
                    {
                        return false;
                    }

                    if (!ReferenceEquals(peAssembly.Assembly, this.assembly))
                    {
                        return false;
                    }

                    if (EffectiveImportOptions != peAssembly.PrimaryModule.ImportOptions)
                    {
                        return false;
                    }

                    // TODO (tomat): 
                    // We shouldn't need to compare documentation providers. All symbols in the cachedSymbols list 
                    // should share the same provider - as they share the same metadata.
                    // Removing the Equals call also avoids calling user code while holding a lock.
                    if (!peAssembly.DocumentationProvider.Equals(DocumentationProvider))
                    {
                        return false;
                    }

                    return true;
                }

                public override bool ContainsNoPiaLocalTypes
                {
                    get
                    {
                        return assembly.ContainsNoPiaLocalTypes();
                    }
                }

                public override bool DeclaresTheObjectClass
                {
                    get
                    {
                        return assembly.DeclaresTheObjectClass;
                    }
                }
            }

            private sealed class AssemblyDataForCompilation : AssemblyDataForMetadataOrCompilation
            {
                private readonly CSharpCompilation compilation;
                public CSharpCompilation Compilation
                {
                    get
                    {
                        return compilation;
                    }
                }

                public AssemblyDataForCompilation(CSharpCompilation compilation, bool embedInteropTypes)
                    : base(embedInteropTypes)
                {
                    Debug.Assert(compilation != null);
                    this.compilation = compilation;

                    // Force creation of the SourceAssemblySymbol
                    AssemblySymbol assembly = compilation.Assembly;
                    assemblyIdentity = assembly.Identity;

                    // Collect information about references
                    var refs = ArrayBuilder<AssemblyIdentity>.GetInstance();

                    var modules = assembly.Modules;
                    int mCount = modules.Length;
                    int i;

                    // Filter out linked assemblies referenced by the source module.
                    var sourceReferencedAssemblies = modules[0].GetReferencedAssemblies();
                    var sourceReferencedAssemblySymbols = modules[0].GetReferencedAssemblySymbols();
                    int rCount = sourceReferencedAssemblies.Length;

                    Debug.Assert(rCount == sourceReferencedAssemblySymbols.Length);

                    for (i = 0; i < rCount; i++)
                    {
                        if (!sourceReferencedAssemblySymbols[i].IsLinked)
                        {
                            refs.Add(sourceReferencedAssemblies[i]);
                        }
                    }

                    for (i = 1; i < mCount; i++)
                    {
                        refs.AddRange(modules[i].GetReferencedAssemblies());
                    }

                    referencedAssemblies = refs.ToImmutableAndFree();
                }

                protected override void AddAvailableSymbols(List<AssemblySymbol> assemblies)
                {
                    assemblies.Add(compilation.Assembly);

                    // accessing cached symbols requires a lock
                    lock (SymbolCacheAndReferenceManagerStateGuard)
                    {
                        compilation.AddRetargetingAssemblySymbolsNoLock(assemblies);
                    }
                }

                public override bool IsMatchingAssembly(AssemblySymbol candidateAssembly)
                {
                    var retargeting = candidateAssembly as Symbols.Retargeting.RetargetingAssemblySymbol;
                    AssemblySymbol asm;

                    if ((object)retargeting != null)
                    {
                        asm = retargeting.UnderlyingAssembly;
                    }
                    else
                    {
                        asm = candidateAssembly as SourceAssemblySymbol;
                    }

                    Debug.Assert(!(asm is Symbols.Retargeting.RetargetingAssemblySymbol));

                    return ReferenceEquals(asm, compilation.Assembly);
                }

                public override bool ContainsNoPiaLocalTypes
                {
                    get
                    {
                        return compilation.MightContainNoPiaLocalTypes();
                    }
                }

                public override bool DeclaresTheObjectClass
                {
                    get
                    {
                        return compilation.DeclaresTheObjectClass;
                    }
                }
            }

            /// <summary>
            /// For testing purposes only.
            /// </summary>
            internal static bool IsSourceAssemblySymbolCreated(CSharpCompilation compilation)
            {
                return (object)compilation.lazyAssemblySymbol != null;
            }

            /// <summary>
            /// For testing purposes only.
            /// </summary>
            internal static bool IsReferenceManagerInitialized(CSharpCompilation compilation)
            {
                return compilation.referenceManager.IsBound;
            }
        }
    }
}
