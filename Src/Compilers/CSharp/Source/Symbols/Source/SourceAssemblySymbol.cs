﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Symbols.Metadata.PE;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Roslyn.Utilities;
using CommonAssemblyWellKnownAttributeData = Microsoft.CodeAnalysis.CommonAssemblyWellKnownAttributeData<Microsoft.CodeAnalysis.CSharp.Symbols.NamedTypeSymbol>;

namespace Microsoft.CodeAnalysis.CSharp.Symbols
{
    /// <summary>
    /// Represents an assembly built by compiler.
    /// </summary>
    internal sealed class SourceAssemblySymbol : MetadataOrSourceAssemblySymbol, IAttributeTargetSymbol
    {
        /// <summary>
        /// A Compilation the assembly is created for.
        /// </summary>
        private readonly CSharpCompilation compilation;

        private SymbolCompletionState state;

        /// <summary>
        /// Assembly's identity.
        /// </summary>
        internal AssemblyIdentity lazyAssemblyIdentity;
        private readonly string assemblySimpleName;

        // Computing the identity requires computing the public key. Computing the public key 
        // can require binding attributes that contain version or strong name information. 
        // Attribute binding will check type visibility which will possibly 
        // check IVT relationships. To correctly determine the IVT relationship requires the public key. 
        // To avoid infinite recursion, this type notes, per thread, the assembly for which the thread 
        // is actively computing the public key (assemblyForWhichCurrentThreadIsComputingKeys). Should a request to determine IVT
        // relationship occur on the thread that is computing the public key, access is optimistically
        // granted provided the simple assembly names match. When such access is granted
        // the assembly to which we have been granted access is noted (optimisticallyGrantedInternalsAccess).
        // After the public key has been computed, the set of optimistic grants is reexamined 
        // to insure that full identities match. This may produce diagnostics.
        private StrongNameKeys lazyStrongNameKeys;

        /// <summary>
        /// A list of modules the assembly consists of. 
        /// The first (index=0) module is a SourceModuleSymbol, which is a primary module, the rest are net-modules.
        /// </summary>
        private readonly ImmutableArray<ModuleSymbol> modules;

        /// <summary>
        /// Bag of assembly's custom attributes and decoded well-known attribute data from source.
        /// </summary>
        private CustomAttributesBag<CSharpAttributeData> lazySourceAttributesBag;

        /// <summary>
        /// Bag of assembly's custom attributes and decoded well-known attribute data from added netmodules.
        /// </summary>
        private CustomAttributesBag<CSharpAttributeData> lazyNetModuleAttributesBag;

        private IDictionary<string, NamedTypeSymbol> lazyForwardedTypesFromSource;

        /// <summary>
        /// Indices of duplicate assembly attributes, i.e. attributes that bind to the same constructor and have identical arguments, that must not be emitted.
        /// </summary>
        /// <remarks>
        /// These indices correspond to the merged assembly attributes from source and added net modules, i.e. attributes returned by <see cref="M:GetAttributes"/> method.
        /// </remarks>
        private HashSet<int> lazyDuplicateAttributeIndices;

        private ThreeState lazyContainsExtensionMethods;

        /// <summary>
        /// Map for storing effectively private or effectively internal fields declared in this assembly but never initialized nor assigned.
        /// Each {symbol, bool} key-value pair in this map indicates the following:
        ///  (a) Key: Unassigned field symbol.
        ///  (b) Value: True if the unassigned field is effectively internal, false otherwise.
        /// </summary>
        private ConcurrentDictionary<FieldSymbol, bool> unassignedFieldsMap = new ConcurrentDictionary<FieldSymbol, bool>();

        /// <summary>
        /// private fields declared in this assembly but never read
        /// </summary>
        private ConcurrentSet<FieldSymbol> unreadFields = new ConcurrentSet<FieldSymbol>();

        /// <summary>
        /// We imitate the native compiler's policy of not warning about unused fields
        /// when the enclosing type is used by an extern method for a ref argument.
        /// Here we keep track of those types.
        /// </summary>
        internal ConcurrentSet<TypeSymbol> TypesReferencedInExternalMethods = new ConcurrentSet<TypeSymbol>();

        /// <summary>
        /// The warnings for unused fields.
        /// </summary>
        private ImmutableArray<Diagnostic> unusedFieldWarnings;

        internal SourceAssemblySymbol(
            CSharpCompilation compilation,
            string assemblySimpleName,
            string moduleName,
            ImmutableArray<PEModule> netModules)
        {
            Debug.Assert(compilation != null);
            Debug.Assert(!String.IsNullOrWhiteSpace(assemblySimpleName));
            Debug.Assert(!String.IsNullOrWhiteSpace(moduleName));
            Debug.Assert(!netModules.IsDefault);

            this.compilation = compilation;
            this.assemblySimpleName = assemblySimpleName;

            ArrayBuilder<ModuleSymbol> moduleBuilder = new ArrayBuilder<ModuleSymbol>(1 + netModules.Length);

            moduleBuilder.Add(new SourceModuleSymbol(this, compilation.Declarations, moduleName));

            var importOptions = (compilation.Options.MetadataImportOptions == MetadataImportOptions.All) ?
                MetadataImportOptions.All : MetadataImportOptions.Internal;

            foreach (PEModule netModule in netModules)
            {
                moduleBuilder.Add(new PEModuleSymbol(this, netModule, importOptions, moduleBuilder.Count));
                // SetReferences will be called later by the ReferenceManager (in CreateSourceAssemblyFullBind for 
                // a fresh manager, in CreateSourceAssemblyReuseData for a reused one).
            }

            this.modules = moduleBuilder.ToImmutableAndFree();
        }

        public override string Name
        {
            get
            {
                return assemblySimpleName;
            }
        }

        /// <remarks>
        /// This override is essential - it's a base case of the recursive definition.
        /// </remarks>
        internal sealed override CSharpCompilation DeclaringCompilation
        {
            get
            {
                return compilation;
            }
        }

        public override bool IsInteractive
        {
            get
            {
                return compilation.IsSubmission;
            }
        }

        internal bool MightContainNoPiaLocalTypes()
        {
            for (int i = 1; i < this.modules.Length; i++)
            {
                var peModuleSymbol = (Metadata.PE.PEModuleSymbol)this.modules[i];
                if (peModuleSymbol.Module.ContainsNoPiaLocalTypes())
                {
                    return true;
                }
            }

            return SourceModule.MightContainNoPiaLocalTypes();
        }

        public override AssemblyIdentity Identity
        {
            get
            {
                if (lazyAssemblyIdentity == null)
                    Interlocked.CompareExchange(ref lazyAssemblyIdentity, ComputeIdentity(), null);

                return lazyAssemblyIdentity;
            }
        }

        internal override Symbol GetSpecialTypeMember(SpecialMember member)
        {
            var makeMissingMember = compilation.Feature("MakeMissingMember");
            return makeMissingMember != null && makeMissingMember == member.ToString() ? null : base.GetSpecialTypeMember(member);
        }

        private string GetWellKnownAttributeDataStringField(Func<CommonAssemblyWellKnownAttributeData, string> fieldGetter, string missingValue = null)
        {
            string fieldValue = missingValue;

            var data = GetSourceDecodedWellKnownAttributeData();
            if (data != null)
            {
                fieldValue = fieldGetter(data);
            }

            if ((object)fieldValue == (object)missingValue)
            {
                data = GetNetModuleDecodedWellKnownAttributeData();
                if (data != null)
                {
                    fieldValue = fieldGetter(data);
                }
            }

            return fieldValue;
        }

        internal bool RuntimeCompatibilityWrapNonExceptionThrows
        {
            get
            {
                var data = GetSourceDecodedWellKnownAttributeData() ?? GetNetModuleDecodedWellKnownAttributeData();

                // By default WrapNonExceptionThrows is considered to be true.
                return (data != null) ? data.RuntimeCompatibilityWrapNonExceptionThrows : CommonAssemblyWellKnownAttributeData.WrapNonExceptionThrowsDefault;
            }
        }

        internal string FileVersion
        {
            get
            {
                return GetWellKnownAttributeDataStringField(data => data.AssemblyFileVersionAttributeSetting);
            }
        }

        internal string Title
        {
            get
            {
                return GetWellKnownAttributeDataStringField(data => data.AssemblyTitleAttributeSetting);
            }
        }

        internal string Description
        {
            get
            {
                return GetWellKnownAttributeDataStringField(data => data.AssemblyDescriptionAttributeSetting);
            }
        }

        internal string Company
        {
            get
            {
                return GetWellKnownAttributeDataStringField(data => data.AssemblyCompanyAttributeSetting);
            }
        }

        internal string Product
        {
            get
            {
                return GetWellKnownAttributeDataStringField(data => data.AssemblyProductAttributeSetting);
            }
        }

        internal string InformationalVersion
        {
            get
            {
                return GetWellKnownAttributeDataStringField(data => data.AssemblyInformationalVersionAttributeSetting);
            }
        }

        internal string Copyright
        {
            get
            {
                return GetWellKnownAttributeDataStringField(data => data.AssemblyCopyrightAttributeSetting);
            }
        }

        internal string Trademark
        {
            get
            {
                return GetWellKnownAttributeDataStringField(data => data.AssemblyTrademarkAttributeSetting);
            }
        }

        private ThreeState AssemblyDelaySignAttributeSetting
        {
            get
            {
                var defaultValue = ThreeState.Unknown;
                var fieldValue = defaultValue;

                var data = GetSourceDecodedWellKnownAttributeData();
                if (data != null)
                {
                    fieldValue = data.AssemblyDelaySignAttributeSetting;
                }

                if (fieldValue == defaultValue)
                {
                    data = GetNetModuleDecodedWellKnownAttributeData();
                    if (data != null)
                    {
                        fieldValue = data.AssemblyDelaySignAttributeSetting;
                    }
                }

                return fieldValue;
            }
        }

        private string AssemblyKeyContainerAttributeSetting
        {
            get
            {
                return GetWellKnownAttributeDataStringField(data => data.AssemblyKeyContainerAttributeSetting, WellKnownAttributeData.StringMissingValue);
            }
        }

        private string AssemblyKeyFileAttributeSetting
        {
            get
            {
                return GetWellKnownAttributeDataStringField(data => data.AssemblyKeyFileAttributeSetting, WellKnownAttributeData.StringMissingValue);
            }
        }

        private string AssemblyCultureAttributeSetting
        {
            get
            {
                return GetWellKnownAttributeDataStringField(data => data.AssemblyCultureAttributeSetting);
            }
        }

        internal string SignatureKey
        {
            get
            {
                string key = GetWellKnownAttributeDataStringField(data => data.AssemblySignatureKeyAttributeSetting);
                return key;
            }
        }

        private Version AssemblyVersionAttributeSetting
        {
            get
            {
                var defaultValue = default(Version);
                var fieldValue = defaultValue;

                var data = GetSourceDecodedWellKnownAttributeData();
                if (data != null)
                {
                    fieldValue = data.AssemblyVersionAttributeSetting;
                }

                if (fieldValue == defaultValue)
                {
                    data = GetNetModuleDecodedWellKnownAttributeData();
                    if (data != null)
                    {
                        fieldValue = data.AssemblyVersionAttributeSetting;
                    }
                }

                return fieldValue;
            }
        }

        internal AssemblyHashAlgorithm AssemblyHashAlgorithm
        {
            get
            {
                return AssemblyAlgorithmIdAttributeSetting ?? AssemblyHashAlgorithm.Sha1;
            }
        }

        internal AssemblyHashAlgorithm? AssemblyAlgorithmIdAttributeSetting
        {
            get
            {
                var fieldValue = default(AssemblyHashAlgorithm?);

                var data = GetSourceDecodedWellKnownAttributeData();
                if (data != null)
                {
                    fieldValue = data.AssemblyAlgorithmIdAttributeSetting;
                }

                if (!fieldValue.HasValue)
                {
                    data = GetNetModuleDecodedWellKnownAttributeData();
                    if (data != null)
                    {
                        fieldValue = data.AssemblyAlgorithmIdAttributeSetting;
                    }
                }

                return fieldValue;
            }
        }

        /// <summary>
        /// This represents what the user claimed in source through the AssemblyFlagsAttribute.
        /// It may be modified as emitted due to presence or absence of the public key.
        /// </summary>
        internal AssemblyNameFlags Flags
        {
            get
            {
                var defaultValue = default(AssemblyNameFlags);
                var fieldValue = defaultValue;

                var data = GetSourceDecodedWellKnownAttributeData();
                if (data != null)
                {
                    fieldValue = data.AssemblyFlagsAttributeSetting;
                }

                data = GetNetModuleDecodedWellKnownAttributeData();
                if (data != null)
                {
                    fieldValue |= data.AssemblyFlagsAttributeSetting;
                }

                return fieldValue;
            }
        }

        private StrongNameKeys ComputeStrongNameKeys()
        {
            // TODO:
            // In order to allow users to escape problems that we create with our provisional granting of IVT access,
            // consider not binding the attributes if the command line options were specified, then later bind them
            // and report warnings if both were used.
            EnsureAttributesAreBound();

            // when both attributes and command-line options specified, cmd line wins.
            string keyFile = compilation.Options.CryptoKeyFile;

            if (string.IsNullOrEmpty(keyFile))
            {
                keyFile = this.AssemblyKeyFileAttributeSetting;

                if ((object)keyFile == (object)WellKnownAttributeData.StringMissingValue)
                {
                    keyFile = null;
                }
            }

            string keyContainer = compilation.Options.CryptoKeyContainer;

            if (string.IsNullOrEmpty(keyContainer))
            {
                keyContainer = this.AssemblyKeyContainerAttributeSetting;

                if ((object)keyContainer == (object)WellKnownAttributeData.StringMissingValue)
                {
                    keyContainer = null;
                }
            }

            return StrongNameKeys.Create(DeclaringCompilation.Options.StrongNameProvider, keyFile, keyContainer, MessageProvider.Instance);
        }

        // A collection of assemblies to which we were granted internals access by only checking matches for assembly name
        // and ignoring public key. This just acts as a set. The bool is ignored.
        private ConcurrentDictionary<AssemblySymbol, bool> optimisticallyGrantedInternalsAccess;

        //EDMAURER please don't use thread local storage widely. This is hoped to be a one-off usage.
        [ThreadStatic]
        private static AssemblySymbol assemblyForWhichCurrentThreadIsComputingKeys;

        internal StrongNameKeys StrongNameKeys
        {
            get
            {
                if (lazyStrongNameKeys == null)
                {
                    try
                    {
                        assemblyForWhichCurrentThreadIsComputingKeys = this;
                        Interlocked.CompareExchange(ref lazyStrongNameKeys, ComputeStrongNameKeys(), null);
                    }
                    finally
                    {
                        assemblyForWhichCurrentThreadIsComputingKeys = null;
                    }
                }

                return lazyStrongNameKeys;
            }
        }

        internal override ImmutableArray<byte> PublicKey
        {
            get { return StrongNameKeys.PublicKey; }
        }

        public override ImmutableArray<ModuleSymbol> Modules
        {
            get
            {
                return modules;
            }
        }

        //TODO: cache
        public override ImmutableArray<Location> Locations
        {
            get
            {
                return this.Modules.SelectMany(m => m.Locations).AsImmutable();
            }
        }

        private void ValidateAttributeSemantics(DiagnosticBag diagnostics)
        {
            //diagnostics that come from computing the public key.
            //If building a netmodule, strong name keys need not be validated. Dev11 didn't.
            if (StrongNameKeys.DiagnosticOpt != null && !compilation.Options.OutputKind.IsNetModule())
            {
                diagnostics.Add(StrongNameKeys.DiagnosticOpt);
            }

            ValidateIVTPublicKeys(diagnostics);
            //diagnostics that result from IVT checks performed while in the process of computing the public key.
            CheckOptimisticIVTAccessGrants(diagnostics);

            DetectAttributeAndOptionConflicts(diagnostics);

            if (IsDelaySign && !Identity.HasPublicKey)
            {
                diagnostics.Add(ErrorCode.WRN_DelaySignButNoKey, NoLocation.Singleton);
            }

            // A container cannot contain just the public key. So if we have the public
            // key, no keyPair, and no container, then the public key came from a file
            // that contained only that. In that case we cannot sign.
            if (DeclaringCompilation.Options.OutputKind != OutputKind.NetModule &&
                Identity.HasPublicKey &&
                !IsDelaySign &&
                !StrongNameKeys.CanSign &&
                StrongNameKeys.DiagnosticOpt == null)
            {
                diagnostics.Add(ErrorCode.ERR_SignButNoPrivateKey, NoLocation.Singleton, StrongNameKeys.KeyFilePath);
            }

            ReportDiagnosticsForSynthesizedAttributes(this.compilation, diagnostics);
        }

        /// <summary>
        /// We're going to synthesize some well-known attributes for this assembly symbol.  However, at synthesis time, it is
        /// too late to report diagnostics or cancel the emit.  Instead, we check for use site errors on the types and members
        /// we know we'll need at synthesis time.
        /// </summary>
        /// <remarks>
        /// As in Dev10, we won't report anything if the attribute TYPES are missing (note: missing, not erroneous) because we won't
        /// synthesize anything in that case.  We'll only report diagnostics if the attribute TYPES are present and either they or 
        /// the attribute CONSTRUCTORS have errors.
        /// </remarks>
        private static void ReportDiagnosticsForSynthesizedAttributes(CSharpCompilation compilation, DiagnosticBag diagnostics)
        {
            ReportDiagnosticsForUnsafeSynthesizedAttributes(compilation, diagnostics);

            CSharpCompilationOptions compilationOptions = compilation.Options;
            if (!compilationOptions.OutputKind.IsNetModule())
            {
                TypeSymbol compilationRelaxationsAttribute = compilation.GetWellKnownType(WellKnownType.System_Runtime_CompilerServices_CompilationRelaxationsAttribute);
                Debug.Assert((object)compilationRelaxationsAttribute != null, "GetWellKnownType unexpectedly returned null");
                if (!(compilationRelaxationsAttribute is MissingMetadataTypeSymbol))
                {
                    // As in Dev10 (see GlobalAttrBind::EmitCompilerGeneratedAttrs), we only synthesize this attribute if CompilationRelaxationsAttribute is found.
                    Binder.ReportUseSiteDiagnosticForSynthesizedAttribute(compilation,
                        WellKnownMember.System_Runtime_CompilerServices_CompilationRelaxationsAttribute__ctorInt32, diagnostics, NoLocation.Singleton);
                }

                TypeSymbol runtimeCompatibilityAttribute = compilation.GetWellKnownType(WellKnownType.System_Runtime_CompilerServices_RuntimeCompatibilityAttribute);
                Debug.Assert((object)runtimeCompatibilityAttribute != null, "GetWellKnownType unexpectedly returned null");
                if (!(runtimeCompatibilityAttribute is MissingMetadataTypeSymbol))
                {
                    // As in Dev10 (see GlobalAttrBind::EmitCompilerGeneratedAttrs), we only synthesize this attribute if RuntimeCompatibilityAttribute is found.
                    Binder.ReportUseSiteDiagnosticForSynthesizedAttribute(compilation,
                        WellKnownMember.System_Runtime_CompilerServices_RuntimeCompatibilityAttribute__ctor, diagnostics, NoLocation.Singleton);

                    Binder.ReportUseSiteDiagnosticForSynthesizedAttribute(compilation,
                        WellKnownMember.System_Runtime_CompilerServices_RuntimeCompatibilityAttribute__WrapNonExceptionThrows, diagnostics, NoLocation.Singleton);
                }
            }
        }

        /// <summary>
        /// If this compilation allows unsafe code (note: allows, not contains), then when we actually emit the assembly/module, 
        /// we're going to synthesize SecurityPermissionAttribute/UnverifiableCodeAttribute.  However, at synthesis time, it is
        /// too late to report diagnostics or cancel the emit.  Instead, we check for use site errors on the types and members
        /// we know we'll need at synthesis time.
        /// </summary>
        /// <remarks>
        /// As in Dev10, we won't report anything if the attribute TYPES are missing (note: missing, not erroneous) because we won't
        /// synthesize anything in that case.  We'll only report diagnostics if the attribute TYPES are present and either they or 
        /// the attribute CONSTRUCTORS have errors.
        /// </remarks>
        private static void ReportDiagnosticsForUnsafeSynthesizedAttributes(CSharpCompilation compilation, DiagnosticBag diagnostics)
        {
            CSharpCompilationOptions compilationOptions = compilation.Options;
            if (!compilationOptions.AllowUnsafe)
            {
                return;
            }

            TypeSymbol unverifiableCodeAttribute = compilation.GetWellKnownType(WellKnownType.System_Security_UnverifiableCodeAttribute);
            Debug.Assert((object)unverifiableCodeAttribute != null, "GetWellKnownType unexpectedly returned null");
            if (unverifiableCodeAttribute is MissingMetadataTypeSymbol)
            {
                return;
            }

            // As in Dev10 (see GlobalAttrBind::EmitCompilerGeneratedAttrs), we only synthesize this attribute if
            // UnverifiableCodeAttribute is found.
            Binder.ReportUseSiteDiagnosticForSynthesizedAttribute(compilation,
                WellKnownMember.System_Security_UnverifiableCodeAttribute__ctor, diagnostics, NoLocation.Singleton);


            TypeSymbol securityPermissionAttribute = compilation.GetWellKnownType(WellKnownType.System_Security_Permissions_SecurityPermissionAttribute);
            Debug.Assert((object)securityPermissionAttribute != null, "GetWellKnownType unexpectedly returned null");
            if (securityPermissionAttribute is MissingMetadataTypeSymbol)
            {
                return;
            }

            TypeSymbol securityAction = compilation.GetWellKnownType(WellKnownType.System_Security_Permissions_SecurityAction);
            Debug.Assert((object)securityAction != null, "GetWellKnownType unexpectedly returned null");
            if (securityAction is MissingMetadataTypeSymbol)
            {
                return;
            }

            // As in Dev10 (see GlobalAttrBind::EmitCompilerGeneratedAttrs), we only synthesize this attribute if
            // UnverifiableCodeAttribute, SecurityAction, and SecurityPermissionAttribute are found.
            Binder.ReportUseSiteDiagnosticForSynthesizedAttribute(compilation,
                WellKnownMember.System_Security_Permissions_SecurityPermissionAttribute__ctor, diagnostics, NoLocation.Singleton);

            // Not actually an attribute, but the same logic applies.
            Binder.ReportUseSiteDiagnosticForSynthesizedAttribute(compilation,
                WellKnownMember.System_Security_Permissions_SecurityPermissionAttribute__SkipVerification, diagnostics, NoLocation.Singleton);
        }

        private void ValidateIVTPublicKeys(DiagnosticBag diagnostics)
        {
            EnsureAttributesAreBound();

            if (!this.Identity.IsStrongName)
                return;

            if (lazyInternalsVisibleToMap != null)
            {
                foreach (var keys in lazyInternalsVisibleToMap.Values)
                {
                    foreach (var oneKey in keys)
                    {
                        if (oneKey.Key.IsDefaultOrEmpty)
                        {
                            diagnostics.Add(ErrorCode.ERR_FriendAssemblySNReq, oneKey.Value.Item1, oneKey.Value.Item2);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// True if internals are exposed at all.
        /// </summary>
        /// <remarks>
        /// Forces binding and decoding of attributes.
        /// This property shouldn't be accessed during binding as it can lead to attribute binding cycle.
        /// </remarks>
        private bool InternalsAreVisible
        {
            get
            {
                EnsureAttributesAreBound();
                return lazyInternalsVisibleToMap != null;
            }
        }

        private void DetectAttributeAndOptionConflicts(DiagnosticBag diagnostics)
        {
            EnsureAttributesAreBound();

            ThreeState assemblyDelaySignAttributeSetting = this.AssemblyDelaySignAttributeSetting;
            if (compilation.Options.DelaySign.HasValue && (assemblyDelaySignAttributeSetting != ThreeState.Unknown) &&
                (DeclaringCompilation.Options.DelaySign.Value != (assemblyDelaySignAttributeSetting == ThreeState.True)))
            {
                diagnostics.Add(ErrorCode.WRN_CmdOptionConflictsSource, NoLocation.Singleton, "DelaySign", AttributeDescription.AssemblyDelaySignAttribute.FullName);
            }

            if (!String.IsNullOrEmpty(compilation.Options.CryptoKeyContainer))
            {
                string assemblyKeyContainerAttributeSetting = this.AssemblyKeyContainerAttributeSetting;

                if ((object)assemblyKeyContainerAttributeSetting == (object)CommonAssemblyWellKnownAttributeData.StringMissingValue)
                {
                    if (compilation.Options.OutputKind == OutputKind.NetModule)
                    {
                        // We need to synthesize this attribute for .NET module,
                        // touch the constructor in order to generate proper use-site diagnostics
                        Binder.ReportUseSiteDiagnosticForSynthesizedAttribute(compilation,
                            WellKnownMember.System_Reflection_AssemblyKeyNameAttribute__ctor,
                            diagnostics,
                            NoLocation.Singleton);
                    }
                }
                else if (String.Compare(compilation.Options.CryptoKeyContainer, assemblyKeyContainerAttributeSetting, StringComparison.OrdinalIgnoreCase) != 0)
                {
                    // Native compiler reports a warning in this case, notifying the user that attribute value from source is ignored,
                    // but it doesn't drop the attribute during emit. That might be fine if we produce an assembly because we actually sign it with correct
                    // key (the one from compilation options) without relying on the emitted attribute.
                    // If we are building a .NET module, things get more complicated. In particular, we don't sign the module, we emit an attribute with the key 
                    // information, which will be used to sign an assembly once the module is linked into it. If there is already an attribute like that in source,
                    // native compiler emits both of them, synthetic attribute is emitted after the one from source. Incidentally, ALink picks the last attribute
                    // for signing and things seem to work out. However, relying on the order of attributes feels fragile, especially given that Roslyn emits
                    // synthetic attributes before attributes from source. The behavior we settled on for .NET modules is that, if the attribute in source has the
                    // same value as the one in compilation options, we won't emit the senthetic attribute. If the value doesn't match, we report an error, which 
                    // is a breaking change. Bottom line, we will never produce a module or an assembly with two attributes, regardless whether values are the same
                    // or not.
                    if (compilation.Options.OutputKind == OutputKind.NetModule)
                    {
                        diagnostics.Add(ErrorCode.ERR_CmdOptionConflictsSource, NoLocation.Singleton, AttributeDescription.AssemblyKeyNameAttribute.FullName, "CryptoKeyContainer");
                    }
                    else
                    {
                        diagnostics.Add(ErrorCode.WRN_CmdOptionConflictsSource, NoLocation.Singleton, "CryptoKeyContainer", AttributeDescription.AssemblyKeyNameAttribute.FullName);
                    }
                }

            }

            if (!String.IsNullOrEmpty(compilation.Options.CryptoKeyFile))
            {
                string assemblyKeyFileAttributeSetting = this.AssemblyKeyFileAttributeSetting;

                if ((object)assemblyKeyFileAttributeSetting == (object)CommonAssemblyWellKnownAttributeData.StringMissingValue)
                {
                    if (compilation.Options.OutputKind == OutputKind.NetModule)
                    {
                        // We need to synthesize this attribute for .NET module,
                        // touch the constructor in order to generate proper use-site diagnostics
                        Binder.ReportUseSiteDiagnosticForSynthesizedAttribute(compilation,
                            WellKnownMember.System_Reflection_AssemblyKeyFileAttribute__ctor,
                            diagnostics,
                            NoLocation.Singleton);
                    }
                }
                else if (String.Compare(compilation.Options.CryptoKeyFile, assemblyKeyFileAttributeSetting, StringComparison.OrdinalIgnoreCase) != 0)
                {
                    // Comment in similar section for CryptoKeyContainer is applicable here as well.
                    if (compilation.Options.OutputKind == OutputKind.NetModule)
                    {
                        diagnostics.Add(ErrorCode.ERR_CmdOptionConflictsSource, NoLocation.Singleton, AttributeDescription.AssemblyKeyFileAttribute.FullName, "CryptoKeyFile");
                    }
                    else
                    {
                        diagnostics.Add(ErrorCode.WRN_CmdOptionConflictsSource, NoLocation.Singleton, "CryptoKeyFile", AttributeDescription.AssemblyKeyFileAttribute.FullName);
                    }
                }
            }
        }

        internal bool IsDelaySign
        {
            get
            {
                //commandline setting trumps attribute value. Warning assumed to be given elsewhere
                if (compilation.Options.DelaySign.HasValue)
                {
                    return compilation.Options.DelaySign.Value;
                }

                return (this.AssemblyDelaySignAttributeSetting == ThreeState.True);
            }
        }

        internal SourceModuleSymbol SourceModule
        {
            get { return (SourceModuleSymbol)this.Modules[0]; }
        }

        internal override bool RequiresCompletion
        {
            get { return true; }
        }

        internal override bool HasComplete(CompletionPart part)
        {
            return state.HasComplete(part);
        }

        internal override void ForceComplete(SourceLocation locationOpt, CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var incompletePart = state.NextIncompletePart;
                switch (incompletePart)
                {
                    case CompletionPart.Attributes:
                        EnsureAttributesAreBound();
                        break;
                    case CompletionPart.StartAttributeChecks:
                    case CompletionPart.FinishAttributeChecks:
                        if (state.NotePartComplete(CompletionPart.StartAttributeChecks))
                        {
                            var diagnostics = DiagnosticBag.GetInstance();
                            ValidateAttributeSemantics(diagnostics);
                            AddSemanticDiagnostics(diagnostics);
                            var thisThreadCompleted = state.NotePartComplete(CompletionPart.FinishAttributeChecks);
                            Debug.Assert(thisThreadCompleted);
                            diagnostics.Free();
                        }
                        break;
                    case CompletionPart.Module:
                        SourceModule.ForceComplete(locationOpt, cancellationToken);
                        if (SourceModule.HasComplete(CompletionPart.MembersCompleted))
                        {
                            state.NotePartComplete(CompletionPart.Module);
                            break;
                        }
                        else
                        {
                            Debug.Assert(locationOpt != null, "If no location was specified, then the module members should be completed");
                            // this is the last completion part we can handle if there is a location.
                            return;
                        }

                    case CompletionPart.StartValidatingAddedModules:
                    case CompletionPart.FinishValidatingAddedModules:
                        if (state.NotePartComplete(CompletionPart.StartValidatingAddedModules))
                        {
                            ReportDiagnosticsForAddedModules();
                            var thisThreadCompleted = state.NotePartComplete(CompletionPart.FinishValidatingAddedModules);
                            Debug.Assert(thisThreadCompleted);
                        }
                        break;

                    case CompletionPart.None:
                        return;

                    default:
                        // any other values are completion parts intended for other kinds of symbols
                        state.NotePartComplete(CompletionPart.All & ~CompletionPart.AssemblySymbolAll);
                        break;
                }

                state.SpinWaitComplete(incompletePart, cancellationToken);
            }
        }

        private void ReportDiagnosticsForAddedModules()
        {
            var diagnostics = DiagnosticBag.GetInstance();

            foreach (var pair in compilation.GetBoundReferenceManager().ReferencedModuleIndexMap)
            {
                var fileRef = pair.Key as PortableExecutableReference;

                if ((object)fileRef != null && (object)fileRef.FullPath != null)
                {
                    string fileName = PathUtilities.GetFileName(fileRef.FullPath);
                    string moduleName = modules[pair.Value].Name;

                    if (!string.Equals(fileName, moduleName, StringComparison.OrdinalIgnoreCase))
                    {
                        // Used to be ERR_ALinkFailed
                        diagnostics.Add(ErrorCode.ERR_NetModuleNameMismatch, NoLocation.Singleton, moduleName, fileName);
                    }
                }
            }

            // Alink performed these checks only when emitting an assembly.
            if (modules.Length > 1 && !compilation.Options.OutputKind.IsNetModule())
            {
                var assemblyMachine = this.Machine;
                bool isPlatformAgnostic = (assemblyMachine == System.Reflection.PortableExecutable.Machine.I386 && !this.Bit32Required);
                var knownModuleNames = new HashSet<String>(StringComparer.OrdinalIgnoreCase);

                for (int i = 1; i < modules.Length; i++)
                {
                    ModuleSymbol m = modules[i];
                    if (!knownModuleNames.Add(m.Name))
                    {
                        diagnostics.Add(ErrorCode.ERR_NetModuleNameMustBeUnique, NoLocation.Singleton, m.Name);
                    }

                    if (!((PEModuleSymbol)m).Module.IsCOFFOnly)
                    {
                        var moduleMachine = m.Machine;

                        if (moduleMachine == System.Reflection.PortableExecutable.Machine.I386 && !m.Bit32Required)
                        {
                            // Other module is agnostic, this is always safe
                            ;
                        }
                        else if (isPlatformAgnostic)
                        {
                            diagnostics.Add(ErrorCode.ERR_AgnosticToMachineModule, NoLocation.Singleton, m);
                        }
                        else if (assemblyMachine != moduleMachine)
                        {
                            // Different machine types, and neither is agnostic
                            // So it is a conflict
                            diagnostics.Add(ErrorCode.ERR_ConflictingMachineModule, NoLocation.Singleton, m);
                        }
                    }
                }

                // Assembly main module must explicitly reference all the modules referenced by other assembly 
                // modules, i.e. all modules from transitive closure must be referenced explicitly here
                for (int i = 1; i < modules.Length; i++)
                {
                    var m = (PEModuleSymbol)modules[i];

                    try
                    {
                        foreach (var referencedModuleName in m.Module.GetReferencedManagedModulesOrThrow())
                        {
                            // Do not report error for this module twice
                            if (knownModuleNames.Add(referencedModuleName))
                            {
                                diagnostics.Add(ErrorCode.ERR_MissingNetModuleReference, NoLocation.Singleton, referencedModuleName);
                            }
                        }
                    }
                    catch (BadImageFormatException)
                    {
                        diagnostics.Add(new CSDiagnosticInfo(ErrorCode.ERR_BindToBogus, m), NoLocation.Singleton);
                    }
                }
            }

            ReportNameCollisionDiagnosticsForAddedModules(this.GlobalNamespace, diagnostics);

            compilation.SemanticDiagnostics.AddRange(diagnostics);
            diagnostics.Free();
        }

        private void ReportNameCollisionDiagnosticsForAddedModules(NamespaceSymbol ns, DiagnosticBag diagnostics)
        {
            var mergedNs = ns as MergedNamespaceSymbol;

            if ((object)mergedNs == null)
            {
                return;
            }

            ImmutableArray<NamespaceSymbol> constituent = mergedNs.ConstituentNamespaces;

            if (constituent.Length > 2 || (constituent.Length == 2 && constituent[0].ContainingModule.Ordinal != 0 && constituent[1].ContainingModule.Ordinal != 0))
            {
                var topLevelTypesFromModules = ArrayBuilder<NamedTypeSymbol>.GetInstance();

                foreach (var moduleNs in constituent)
                {
                    Debug.Assert(moduleNs.Extent.Kind == NamespaceKind.Module);

                    if (moduleNs.ContainingModule.Ordinal != 0)
                    {
                        topLevelTypesFromModules.AddRange(moduleNs.GetTypeMembers());
                    }
                }

                topLevelTypesFromModules.Sort(NameCollisionForAddedModulesTypeComparer.Singleton);

                bool reportedAnError = false;

                for (int i = 0; i < topLevelTypesFromModules.Count - 1; i++)
                {
                    NamedTypeSymbol x = topLevelTypesFromModules[i];
                    NamedTypeSymbol y = topLevelTypesFromModules[i + 1];

                    if (x.Arity == y.Arity && x.Name == y.Name)
                    {
                        if (!reportedAnError)
                        {
                            // Skip synthetic <Module> type which every .NET module has.
                            if (x.Arity != 0 || !x.ContainingNamespace.IsGlobalNamespace || x.Name != "<Module>")
                            {
                                diagnostics.Add(ErrorCode.ERR_DuplicateNameInNS, y.Locations[0],
                                                y.ToDisplayString(SymbolDisplayFormat.ShortFormat),
                                                y.ContainingNamespace);
                            }

                            reportedAnError = true;
                        }
                    }
                    else
                    {
                        reportedAnError = false;
                    }
                }

                topLevelTypesFromModules.Free();

                // Descent into child namespaces.
                foreach (Symbol member in mergedNs.GetMembers())
                {
                    if (member.Kind == SymbolKind.Namespace)
                    {
                        ReportNameCollisionDiagnosticsForAddedModules((NamespaceSymbol)member, diagnostics);
                    }
                }
            }
        }

        private class NameCollisionForAddedModulesTypeComparer : IComparer<NamedTypeSymbol>
        {
            public static readonly NameCollisionForAddedModulesTypeComparer Singleton = new NameCollisionForAddedModulesTypeComparer();

            private NameCollisionForAddedModulesTypeComparer() { }

            public int Compare(NamedTypeSymbol x, NamedTypeSymbol y)
            {
                int result = String.CompareOrdinal(x.Name, y.Name);

                if (result == 0)
                {
                    result = x.Arity - y.Arity;

                    if (result == 0)
                    {
                        result = x.ContainingModule.Ordinal - y.ContainingModule.Ordinal;
                    }
                }

                return result;
            }
        }

        private bool IsKnownAssemblyAttribute(CSharpAttributeData attribute)
        {
            // TODO: This list used to include AssemblyOperatingSystemAttribute and AssemblyProcessorAttribute,
            //       but it doesn't look like they are defined, cannot find them on MSDN.
            if (attribute.IsTargetAttribute(this, AttributeDescription.AssemblyTitleAttribute) ||
                attribute.IsTargetAttribute(this, AttributeDescription.AssemblyDescriptionAttribute) ||
                attribute.IsTargetAttribute(this, AttributeDescription.AssemblyConfigurationAttribute) ||
                attribute.IsTargetAttribute(this, AttributeDescription.AssemblyCultureAttribute) ||
                attribute.IsTargetAttribute(this, AttributeDescription.AssemblyVersionAttribute) ||
                attribute.IsTargetAttribute(this, AttributeDescription.AssemblyCompanyAttribute) ||
                attribute.IsTargetAttribute(this, AttributeDescription.AssemblyProductAttribute) ||
                attribute.IsTargetAttribute(this, AttributeDescription.AssemblyInformationalVersionAttribute) ||
                attribute.IsTargetAttribute(this, AttributeDescription.AssemblyCopyrightAttribute) ||
                attribute.IsTargetAttribute(this, AttributeDescription.AssemblyTrademarkAttribute) ||
                attribute.IsTargetAttribute(this, AttributeDescription.AssemblyKeyFileAttribute) ||
                attribute.IsTargetAttribute(this, AttributeDescription.AssemblyKeyNameAttribute) ||
                attribute.IsTargetAttribute(this, AttributeDescription.AssemblyAlgorithmIdAttribute) ||
                attribute.IsTargetAttribute(this, AttributeDescription.AssemblyFlagsAttribute) ||
                attribute.IsTargetAttribute(this, AttributeDescription.AssemblyDelaySignAttribute) ||
                attribute.IsTargetAttribute(this, AttributeDescription.AssemblyFileVersionAttribute) ||
                attribute.IsTargetAttribute(this, AttributeDescription.SatelliteContractVersionAttribute) ||
                attribute.IsTargetAttribute(this, AttributeDescription.AssemblySignatureKeyAttribute))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets unique source assembly attributes that should be emitted,
        /// i.e. filters out attributes with errors and duplicate attributes.
        /// </summary>
        private HashSet<CSharpAttributeData> GetUniqueSourceAssemblyAttributes(out HashSet<int> attributeIndicesToSkip)
        {
            ImmutableArray<CSharpAttributeData> appliedSourceAttributes = this.GetSourceAttributesBag().Attributes;

            HashSet<CSharpAttributeData> uniqueAttributes = null;
            attributeIndicesToSkip = null;

            for (int i = 0; i < appliedSourceAttributes.Length; i++)
            {
                CSharpAttributeData attribute = appliedSourceAttributes[i];
                if (!attribute.HasErrors)
                {
                    if (!AddUniqueAssemblyAttribute(attribute, ref uniqueAttributes))
                    {
                        if (attributeIndicesToSkip == null)
                        {
                            attributeIndicesToSkip = new HashSet<int>();
                        }

                        attributeIndicesToSkip.Add(i);
                    }
                }
            }

            return uniqueAttributes;
        }

        private static bool AddUniqueAssemblyAttribute(CSharpAttributeData attribute, ref HashSet<CSharpAttributeData> uniqueAttributes)
        {
            Debug.Assert(!attribute.HasErrors);

            if (uniqueAttributes == null)
            {
                uniqueAttributes = new HashSet<CSharpAttributeData>(comparer: CommonAttributeDataComparer.Instance);
            }

            return uniqueAttributes.Add(attribute);
        }

        private bool ValidateAttributeUsageForNetModuleAttribute(CSharpAttributeData attribute, string netModuleName, DiagnosticBag diagnostics, ref HashSet<CSharpAttributeData> uniqueAttributes)
        {
            Debug.Assert(!attribute.HasErrors);

            var attributeClass = attribute.AttributeClass;

            if (attributeClass.GetAttributeUsageInfo().AllowMultiple)
            {
                // Duplicate attributes are allowed, but native compiler doesn't emit duplicate attributes, i.e. attributes with same constructor and arguments.
                return AddUniqueAssemblyAttribute(attribute, ref uniqueAttributes);
            }
            else
            {
                // Duplicate attributes with same attribute type are not allowed.
                // Check if there is an existing assembly attribute with same attribute type.
                if (uniqueAttributes == null || !uniqueAttributes.Contains((a) => a.AttributeClass == attributeClass))
                {
                    // Attribute with unique attribute type, not a duplicate.
                    bool success = AddUniqueAssemblyAttribute(attribute, ref uniqueAttributes);
                    Debug.Assert(success);
                    return true;
                }
                else
                {
                    // Duplicate attribute with same attribute type, we should report an error.

                    // Native compiler suppresses the error for
                    // (a) Duplicate well-known assembly attributes and
                    // (b) Identical duplicates, i.e. attributes with same constructor and arguments.

                    // For (a), native compiler picks the last of these duplicate well-known netmodule attributes, but these can vary based on the ordering of referenced netmodules.

                    if (IsKnownAssemblyAttribute(attribute))
                    {
                        if (!uniqueAttributes.Contains(attribute))
                        {
                            // This attribute application will be ignored.
                            diagnostics.Add(ErrorCode.WRN_AssemblyAttributeFromModuleIsOverridden, NoLocation.Singleton, attribute.AttributeClass, netModuleName);
                        }
                    }
                    else if (AddUniqueAssemblyAttribute(attribute, ref uniqueAttributes))
                    {
                        // Error
                        diagnostics.Add(ErrorCode.ERR_DuplicateAttributeInNetModule, NoLocation.Singleton, attribute.AttributeClass.Name, netModuleName);
                    }

                    return false;
                }
            }

            // CONSIDER Handling badly targetted assembly attributes from netmodules
            //if (!badDuplicateAttribute && ((attributeUsageInfo.ValidTargets & AttributeTargets.Assembly) == 0))
            //{
            //    // Error and skip
            //    diagnostics.Add(ErrorCode.ERR_AttributeOnBadSymbolTypeInNetModule, NoLocation.Singleton, attribute.AttributeClass.Name, netModuleName, attributeUsageInfo.GetValidTargetsString());
            //    return false;
            //}
        }

        private ImmutableArray<CSharpAttributeData> GetNetModuleAttributes(out ImmutableArray<string> netModuleNames)
        {
            ArrayBuilder<CSharpAttributeData> moduleAssemblyAttributesBuilder = null;
            ArrayBuilder<string> netModuleNameBuilder = null;

            for (int i = 1; i < this.modules.Length; i++)
            {
                var peModuleSymbol = (Metadata.PE.PEModuleSymbol)this.modules[i];
                string netModuleName = peModuleSymbol.Name;
                foreach (var attributeData in peModuleSymbol.GetAssemblyAttributes())
                {
                    if (netModuleNameBuilder == null)
                    {
                        netModuleNameBuilder = ArrayBuilder<string>.GetInstance();
                        moduleAssemblyAttributesBuilder = ArrayBuilder<CSharpAttributeData>.GetInstance();
                    }

                    netModuleNameBuilder.Add(netModuleName);
                    moduleAssemblyAttributesBuilder.Add(attributeData);
                }
            }

            if (netModuleNameBuilder == null)
            {
                netModuleNames = ImmutableArray<string>.Empty;
                return ImmutableArray<CSharpAttributeData>.Empty;
            }

            netModuleNames = netModuleNameBuilder.ToImmutableAndFree();
            return moduleAssemblyAttributesBuilder.ToImmutableAndFree();
        }

        private WellKnownAttributeData ValidateAttributeUsageAndDecodeWellKnownAttributes(
            ImmutableArray<CSharpAttributeData> attributesFromNetModules,
            ImmutableArray<string> netModuleNames,
            DiagnosticBag diagnostics,
            out HashSet<int> attributeIndicesToSkip)
        {
            Debug.Assert(attributesFromNetModules.Any());
            Debug.Assert(netModuleNames.Any());
            Debug.Assert(attributesFromNetModules.Length == netModuleNames.Length);

            var tree = CSharpSyntaxTree.Dummy;

            int netModuleAttributesCount = attributesFromNetModules.Length;
            int sourceAttributesCount = this.GetSourceAttributesBag().Attributes.Length;

            // Get unique source assembly attributes.
            HashSet<CSharpAttributeData> uniqueAttributes = GetUniqueSourceAssemblyAttributes(out attributeIndicesToSkip);

            var arguments = new DecodeWellKnownAttributeArguments<AttributeSyntax, CSharpAttributeData, AttributeLocation>();
            arguments.AttributesCount = netModuleAttributesCount;
            arguments.Diagnostics = diagnostics;
            arguments.SymbolPart = AttributeLocation.None;

            // Attributes from the second added module should override attributes from the first added module, etc. 
            // Attributes from source should override attributes from added modules.
            // That is why we are iterating attributes backwards.
            for (int i = netModuleAttributesCount - 1; i >= 0; i--)
            {
                CSharpAttributeData attribute = attributesFromNetModules[i];
                if (!attribute.HasErrors && ValidateAttributeUsageForNetModuleAttribute(attribute, netModuleNames[i], diagnostics, ref uniqueAttributes))
                {
                    arguments.Attribute = attribute;
                    arguments.Index = i;

                    // CONSIDER: Provide usable AttributeSyntax node for dignostics of malformed netmodule assembly attributes
                    arguments.AttributeSyntaxOpt = null;

                    this.DecodeWellKnownAttribute(ref arguments, isFromNetModule: true);
                }
                else
                {
                    if (attributeIndicesToSkip == null)
                    {
                        attributeIndicesToSkip = new HashSet<int>();
                    }

                    attributeIndicesToSkip.Add(i + sourceAttributesCount);
                }
            }

            return arguments.HasDecodedData ? arguments.DecodedData : null;
        }

        private void LoadAndValidateNetModuleAttributes(ref CustomAttributesBag<CSharpAttributeData> lazyNetModuleAttributesBag)
        {
            // Indices of duplicate assembly attributes, i.e. attributes that bind to the same constructor and have identical arguments, that must not be emitted.
            HashSet<int> attributeIndicesToSkip;

            if (compilation.Options.OutputKind.IsNetModule())
            {
                // Compute duplicate source assembly attributes, i.e. attributes with same constructor and arguments, that must not be emitted.
                var unused = GetUniqueSourceAssemblyAttributes(out attributeIndicesToSkip);
                Interlocked.CompareExchange(ref lazyNetModuleAttributesBag, CustomAttributesBag<CSharpAttributeData>.Empty, null);
            }
            else
            {
                var diagnostics = DiagnosticBag.GetInstance();

                ImmutableArray<string> netModuleNames;
                ImmutableArray<CSharpAttributeData> attributesFromNetModules = GetNetModuleAttributes(out netModuleNames);

                WellKnownAttributeData wellKnownData = null;

                if (attributesFromNetModules.Any())
                {
                    wellKnownData = ValidateAttributeUsageAndDecodeWellKnownAttributes(attributesFromNetModules, netModuleNames, diagnostics, out attributeIndicesToSkip);
                }
                else
                {
                    // Compute duplicate source assembly attributes, i.e. attributes with same constructor and arguments, that must not be emitted.
                    var unused = GetUniqueSourceAssemblyAttributes(out attributeIndicesToSkip);
                }

                // Load type forwarders from modules
                HashSet<NamedTypeSymbol> forwardedTypes = null;

                // Similar to attributes, type forwarders from the second added module should override type forwarders from the first added module, etc. 
                // This affects only diagnostics.
                for (int i = this.modules.Length - 1; i > 0; i--)
                {
                    var peModuleSymbol = (Metadata.PE.PEModuleSymbol)this.modules[i];

                    foreach (NamedTypeSymbol forwarded in peModuleSymbol.GetForwardedTypes())
                    {
                        if (forwardedTypes == null)
                        {
                            if (wellKnownData == null)
                            {
                                wellKnownData = new CommonAssemblyWellKnownAttributeData();
                            }

                            forwardedTypes = ((CommonAssemblyWellKnownAttributeData)wellKnownData).ForwardedTypes;
                            if (forwardedTypes == null)
                            {
                                forwardedTypes = new HashSet<NamedTypeSymbol>();
                                ((CommonAssemblyWellKnownAttributeData)wellKnownData).ForwardedTypes = forwardedTypes;
                            }
                        }

                        if (forwardedTypes.Add(forwarded))
                        {
                            if (forwarded.IsErrorType())
                            {
                                DiagnosticInfo info = forwarded.GetUseSiteDiagnostic() ?? ((ErrorTypeSymbol)forwarded).ErrorInfo;

                                if ((object)info != null)
                                {
                                    diagnostics.Add(info, NoLocation.Singleton);
                                }
                            }
                        }
                    }
                }

                CustomAttributesBag<CSharpAttributeData> netModuleAttributesBag;

                if (wellKnownData != null || attributesFromNetModules.Any())
                {
                    netModuleAttributesBag = new CustomAttributesBag<CSharpAttributeData>();

                    netModuleAttributesBag.SetEarlyDecodedWellKnownAttributeData(null);
                    netModuleAttributesBag.SetDecodedWellKnownAttributeData(wellKnownData);
                    netModuleAttributesBag.SetAttributes(attributesFromNetModules);
                    if (netModuleAttributesBag.IsEmpty) netModuleAttributesBag = CustomAttributesBag<CSharpAttributeData>.Empty;
                }
                else
                {
                    netModuleAttributesBag = CustomAttributesBag<CSharpAttributeData>.Empty;
                }

                // Check if we have any duplicate assembly attribute that must not be emitted,
                // unless we are emitting a net module.
                if (attributeIndicesToSkip != null)
                {
                    Debug.Assert(attributeIndicesToSkip.Any());
                    Interlocked.CompareExchange(ref lazyDuplicateAttributeIndices, attributeIndicesToSkip, null);
                }

                if (Interlocked.CompareExchange(ref lazyNetModuleAttributesBag, netModuleAttributesBag, null) == null)
                {
                    this.AddSemanticDiagnostics(diagnostics);
                }

                diagnostics.Free();
                Debug.Assert(attributeIndicesToSkip == null ||
                             !attributeIndicesToSkip.Any((index) => index < 0 || index >= this.GetAttributes().Length));
            }

            Debug.Assert(lazyNetModuleAttributesBag.IsSealed);
        }

        private void EnsureNetModuleAttributesAreBound()
        {
            if (lazyNetModuleAttributesBag == null)
            {
                LoadAndValidateNetModuleAttributes(ref lazyNetModuleAttributesBag);
            }
        }

        private CustomAttributesBag<CSharpAttributeData> GetNetModuleAttributesBag()
        {
            EnsureNetModuleAttributesAreBound();
            return lazyNetModuleAttributesBag;
        }

        internal CommonAssemblyWellKnownAttributeData GetNetModuleDecodedWellKnownAttributeData()
        {
            var attributesBag = this.GetNetModuleAttributesBag();
            Debug.Assert(attributesBag.IsSealed);
            return (CommonAssemblyWellKnownAttributeData)attributesBag.DecodedWellKnownAttributeData;
        }

        internal ImmutableArray<SyntaxList<AttributeListSyntax>> GetAttributeDeclarations()
        {
            var mergedAttributesBuilder = ArrayBuilder<SyntaxList<AttributeListSyntax>>.GetInstance();

            foreach (var rootNs in DeclaringCompilation.Declarations.AllRootNamespacesUnordered())
            {
                if (rootNs.HasAssemblyAttributes)
                {
                    var attrList = ((CompilationUnitSyntax)rootNs.Location.SourceTree.GetRoot()).AttributeLists;
                    mergedAttributesBuilder.Add(attrList);
                }
            }

            return mergedAttributesBuilder.ToImmutableAndFree();
        }

        private void EnsureAttributesAreBound()
        {
            if ((lazySourceAttributesBag == null || !lazySourceAttributesBag.IsSealed) &&
                LoadAndValidateAttributes(OneOrMany.Create(GetAttributeDeclarations()), ref lazySourceAttributesBag))
            {
                state.NotePartComplete(CompletionPart.Attributes);
            }
        }

        /// <summary>
        /// Returns a bag of applied custom attributes and data decoded from well-known attributes. Returns null if there are no attributes applied on the symbol.
        /// </summary>
        /// <remarks>
        /// Forces binding and decoding of attributes.
        /// </remarks>
        private CustomAttributesBag<CSharpAttributeData> GetSourceAttributesBag()
        {
            EnsureAttributesAreBound();
            return lazySourceAttributesBag;
        }

        /// <summary>
        /// Gets the attributes applied on this symbol.
        /// Returns an empty array if there are no attributes.
        /// </summary>
        /// <remarks>
        /// NOTE: This method should always be kept as a sealed override.
        /// If you want to override attribute binding logic for a sub-class, then override <see cref="GetSourceAttributesBag"/> method.
        /// </remarks>
        public sealed override ImmutableArray<CSharpAttributeData> GetAttributes()
        {
            var attributes = this.GetSourceAttributesBag().Attributes;
            var netmoduleAttributes = this.GetNetModuleAttributesBag().Attributes;
            Debug.Assert(!attributes.IsDefault);
            Debug.Assert(!netmoduleAttributes.IsDefault);

            if (attributes.Length > 0)
            {
                if (netmoduleAttributes.Length > 0)
                {
                    attributes = attributes.Concat(netmoduleAttributes);
                }
            }
            else
            {
                attributes = netmoduleAttributes;
            }

            Debug.Assert(!attributes.IsDefault);
            return attributes;
        }

        /// <summary>
        /// Returns true if the assembly attribute at the given index is a duplicate assembly attribute that must not be emitted.
        /// Duplicate assembly attributes are attributes that bind to the same constructor and have identical arguments.
        /// </summary>
        /// <remarks>
        /// This method must be invoked only after all the assembly attributes have been bound.
        /// </remarks>
        internal bool IsIndexOfDuplicateAssemblyAttribute(int index)
        {
            Debug.Assert(this.lazySourceAttributesBag.IsSealed);
            Debug.Assert(this.lazyNetModuleAttributesBag.IsSealed);
            Debug.Assert(index >= 0);
            Debug.Assert(index < this.GetAttributes().Length);
            Debug.Assert(this.lazyDuplicateAttributeIndices == null ||
                !this.DeclaringCompilation.Options.OutputKind.IsNetModule());

            return this.lazyDuplicateAttributeIndices != null && this.lazyDuplicateAttributeIndices.Contains(index);
        }

        /// <summary>
        /// Returns data decoded from source assembly attributes or null if there are none.
        /// </summary>
        /// <remarks>
        /// Forces binding and decoding of attributes.
        /// TODO: We should replace methods GetSourceDecodedWellKnownAttributeData and GetNetModuleDecodedWellKnownAttributeData with
        /// a single method GetDecodedWellKnownAttributeData, which merges DecodedWellKnownAttributeData from source and netmodule attributes.
        /// </remarks>
        internal CommonAssemblyWellKnownAttributeData GetSourceDecodedWellKnownAttributeData()
        {
            var attributesBag = this.lazySourceAttributesBag;
            if (attributesBag == null || !attributesBag.IsDecodedWellKnownAttributeDataComputed)
            {
                attributesBag = this.GetSourceAttributesBag();
            }

            return (CommonAssemblyWellKnownAttributeData)attributesBag.DecodedWellKnownAttributeData;
        }

        private static IEnumerable<Microsoft.Cci.SecurityAttribute> GetSecurityAttributes(CustomAttributesBag<CSharpAttributeData> attributesBag)
        {
            Debug.Assert(attributesBag.IsSealed);

            var wellKnownAttributeData = (CommonAssemblyWellKnownAttributeData)attributesBag.DecodedWellKnownAttributeData;
            if (wellKnownAttributeData != null)
            {
                SecurityWellKnownAttributeData securityData = wellKnownAttributeData.SecurityInformation;
                if (securityData != null)
                {
                    foreach (var securityAttribute in securityData.GetSecurityAttributes<CSharpAttributeData>(attributesBag.Attributes))
                    {
                        yield return securityAttribute;
                    }
                }
            }
        }

        internal IEnumerable<Microsoft.Cci.SecurityAttribute> GetSecurityAttributes()
        {
            // user defined security attributes:

            foreach (var securityAttribute in GetSecurityAttributes(this.GetSourceAttributesBag()))
            {
                yield return securityAttribute;
            }

            // Net module assembly security attributes:

            foreach (var securityAttribute in GetSecurityAttributes(this.GetNetModuleAttributesBag()))
            {
                yield return securityAttribute;
            }

            // synthesized security attributes:

            if (this.compilation.Options.AllowUnsafe)
            {
                // NOTE: GlobalAttrBind::EmitCompilerGeneratedAttrs skips attribute if the well-known types aren't available.
                if (!(this.compilation.GetWellKnownType(WellKnownType.System_Security_UnverifiableCodeAttribute) is MissingMetadataTypeSymbol) &&
                    !(this.compilation.GetWellKnownType(WellKnownType.System_Security_Permissions_SecurityPermissionAttribute) is MissingMetadataTypeSymbol))
                {
                    var securityActionType = this.compilation.GetWellKnownType(WellKnownType.System_Security_Permissions_SecurityAction);
                    if (!(securityActionType is MissingMetadataTypeSymbol))
                    {
                        var fieldRequestMinimum = (FieldSymbol)this.compilation.GetWellKnownTypeMember(WellKnownMember.System_Security_Permissions_SecurityAction__RequestMinimum);

                        // NOTE: Dev10 handles missing enum value.
                        object constantValue = (object)fieldRequestMinimum == null || fieldRequestMinimum.HasUseSiteError ? 0 : fieldRequestMinimum.ConstantValue;
                        var typedConstantRequestMinimum = new TypedConstant(securityActionType, TypedConstantKind.Enum, constantValue);

                        var boolType = this.compilation.GetSpecialType(SpecialType.System_Boolean);
                        Debug.Assert(!boolType.HasUseSiteError,
                            "Use site errors should have been checked ahead of time (type bool).");

                        var typedConstantTrue = new TypedConstant(boolType, TypedConstantKind.Primitive, value: true);

                        var attribute = compilation.SynthesizeAttribute(
                            WellKnownMember.System_Security_Permissions_SecurityPermissionAttribute__ctor,
                            ImmutableArray.Create(typedConstantRequestMinimum),
                            ImmutableArray.Create(new KeyValuePair<string, TypedConstant>("SkipVerification", typedConstantTrue)));

                        if (attribute != null)
                        {
                            yield return new Microsoft.Cci.SecurityAttribute((Cci.SecurityAction)constantValue, attribute);
                        }
                    }
                }
            }
        }

        internal override ImmutableArray<AssemblySymbol> GetNoPiaResolutionAssemblies()
        {
            return this.modules[0].GetReferencedAssemblySymbols();
        }

        internal override void SetNoPiaResolutionAssemblies(ImmutableArray<AssemblySymbol> assemblies)
        {
            throw ExceptionUtilities.Unreachable;
        }

        internal override ImmutableArray<AssemblySymbol> GetLinkedReferencedAssemblies()
        {
            // SourceAssemblySymbol is never used directly as a reference
            // when it is or any of its references is linked.
            return default(ImmutableArray<AssemblySymbol>);
        }

        internal override void SetLinkedReferencedAssemblies(ImmutableArray<AssemblySymbol> assemblies)
        {
            // SourceAssemblySymbol is never used directly as a reference
            // when it is or any of its references is linked.
            throw ExceptionUtilities.Unreachable;
        }

        internal override bool IsLinked
        {
            get
            {
                return false;
            }
        }

        internal bool DeclaresTheObjectClass
        {
            get
            {
                if ((object)this.CorLibrary != (object)this)
                {
                    return false;
                }

                var obj = GetSpecialType(SpecialType.System_Object);

                return !obj.IsErrorType() && obj.DeclaredAccessibility == Accessibility.Public;
            }
        }

        public override bool MightContainExtensionMethods
        {
            get
            {
                // Note this method returns true until all ContainsExtensionMethods is
                // called, after which the correct value will be returned. In other words,
                // the return value may change from true to false on subsequent calls.
                if (this.lazyContainsExtensionMethods.HasValue())
                {
                    return this.lazyContainsExtensionMethods.Value();
                }
                return true;
            }
        }

        private bool HasDebuggableAttribute
        {
            get
            {
                CommonAssemblyWellKnownAttributeData assemblyData = this.GetSourceDecodedWellKnownAttributeData();
                return assemblyData != null && assemblyData.HasDebuggableAttribute;
            }
        }

        internal override void AddSynthesizedAttributes(ref ArrayBuilder<SynthesizedAttributeData> attributes)
        {
            base.AddSynthesizedAttributes(ref attributes);

            bool containsExtensionMethods = this.ContainsExtensionMethods();
            CSharpCompilationOptions options = this.compilation.Options;

            // Synthesize CompilationRelaxationsAttribute only if all the following requirements are met:
            // (a) We are not building a netmodule.
            // (b) There is no applied CompilationRelaxationsAttribute assembly attribute in source.
            // (c) There is no applied CompilationRelaxationsAttribute assembly attribute for any of the added PE modules.

            // Above requirements also hold for synthesizing RuntimeCompatibilityAttribute attribute.

            bool isBuildingNetModule = options.OutputKind.IsNetModule();
            bool emitCompilationRelaxationsAttribute = !isBuildingNetModule && !this.Modules.Any(m => m.HasAssemblyCompilationRelaxationsAttribute);
            bool emitRuntimeCompatibilityAttribute = !isBuildingNetModule && !this.Modules.Any(m => m.HasAssemblyRuntimeCompatibilityAttribute);

            // Synthesize DebuggableAttribute only if all the following requirements are met:
            // (a) We are not building a netmodule.
            // (b) We are emitting debug information (full or pdbonly).
            // (c) There is no applied DebuggableAttribute assembly attribute in source.

            // CONSIDER: Native VB compiler and Roslyn VB compiler also have an additional requirement: There is no applied DebuggableAttribute *module* attribute in source.
            // CONSIDER: Should we check for module DebuggableAttribute?

            bool emitDebuggableAttribute = !isBuildingNetModule &&
                options.DebugInformationKind.IsValid() && options.DebugInformationKind != DebugInformationKind.None &&
                !this.HasDebuggableAttribute;

            if (containsExtensionMethods)
            {
                // No need to check if [Extension] attribute was explicitly set since
                // we'll issue CS1112 error in those cases and won't generate IL.
                AddSynthesizedAttribute(ref attributes, compilation.SynthesizeAttribute(
                    WellKnownMember.System_Runtime_CompilerServices_ExtensionAttribute__ctor));
            }

            if (emitCompilationRelaxationsAttribute)
            {
                // Synthesize attribute: [CompilationRelaxationsAttribute(CompilationRelaxations.NoStringInterning)]

                // NOTE: GlobalAttrBind::EmitCompilerGeneratedAttrs skips attribute if the well-known types aren't available.
                if (!(this.compilation.GetWellKnownType(WellKnownType.System_Runtime_CompilerServices_CompilationRelaxationsAttribute) is MissingMetadataTypeSymbol))
                {
                    var int32Type = this.compilation.GetSpecialType(SpecialType.System_Int32);
                    Debug.Assert(!int32Type.HasUseSiteError,
                        "Use site errors should have been checked ahead of time (type int).");

                    var typedConstantNoStringInterning = new TypedConstant(int32Type, TypedConstantKind.Primitive, Cci.Constants.CompilationRelaxations_NoStringInterning);

                    AddSynthesizedAttribute(ref attributes, compilation.SynthesizeAttribute(
                        WellKnownMember.System_Runtime_CompilerServices_CompilationRelaxationsAttribute__ctorInt32,
                        ImmutableArray.Create(typedConstantNoStringInterning)));
                }
            }

            if (emitRuntimeCompatibilityAttribute)
            {
                // Synthesize attribute: [RuntimeCompatibilityAttribute(WrapNonExceptionThrows = true)]

                // NOTE: GlobalAttrBind::EmitCompilerGeneratedAttrs skips attribute if the well-known types aren't available.
                if (!(this.compilation.GetWellKnownType(WellKnownType.System_Runtime_CompilerServices_RuntimeCompatibilityAttribute) is MissingMetadataTypeSymbol))
                {
                    var boolType = this.compilation.GetSpecialType(SpecialType.System_Boolean);
                    Debug.Assert(!boolType.HasUseSiteError, "Use site errors should have been checked ahead of time (type bool).");

                    var typedConstantTrue = new TypedConstant(boolType, TypedConstantKind.Primitive, value: true);

                    AddSynthesizedAttribute(ref attributes, compilation.SynthesizeAttribute(
                        WellKnownMember.System_Runtime_CompilerServices_RuntimeCompatibilityAttribute__ctor,
                        ImmutableArray<TypedConstant>.Empty,
                        ImmutableArray.Create(new KeyValuePair<string, TypedConstant>("WrapNonExceptionThrows", typedConstantTrue))));
                }
            }

            if (emitDebuggableAttribute)
            {
                AddSynthesizedAttribute(ref attributes, compilation.SynthesizeDebuggableAttribute());
            }

            if (compilation.Options.OutputKind == OutputKind.NetModule)
            {
                // If the attribute is applied in source, do not add synthetic one.
                // If its value is different from the supplied through options, an error should have been reported by now.

                if (!String.IsNullOrEmpty(compilation.Options.CryptoKeyContainer) &&
                    (object)AssemblyKeyContainerAttributeSetting == (object)CommonAssemblyWellKnownAttributeData.StringMissingValue)
                {
                    var stringType = this.compilation.GetSpecialType(SpecialType.System_String);
                    Debug.Assert(!stringType.HasUseSiteError, "Use site errors should have been checked ahead of time (type string).");

                    var typedConstant = new TypedConstant(stringType, TypedConstantKind.Primitive, compilation.Options.CryptoKeyContainer);
                    AddSynthesizedAttribute(ref attributes, compilation.SynthesizeAttribute(WellKnownMember.System_Reflection_AssemblyKeyNameAttribute__ctor, ImmutableArray.Create(typedConstant)));
                }

                if (!String.IsNullOrEmpty(compilation.Options.CryptoKeyFile) &&
                    (object)AssemblyKeyFileAttributeSetting == (object)CommonAssemblyWellKnownAttributeData.StringMissingValue)
                {
                    var stringType = this.compilation.GetSpecialType(SpecialType.System_String);
                    Debug.Assert(!stringType.HasUseSiteError, "Use site errors should have been checked ahead of time (type string).");

                    var typedConstant = new TypedConstant(stringType, TypedConstantKind.Primitive, compilation.Options.CryptoKeyFile);
                    AddSynthesizedAttribute(ref attributes, compilation.SynthesizeAttribute(WellKnownMember.System_Reflection_AssemblyKeyFileAttribute__ctor, ImmutableArray.Create(typedConstant)));
                }
            }
        }

        /// <summary>
        /// Returns true if and only if at least one type within the assembly contains
        /// extension methods. Note, this method is expensive since it potentially
        /// inspects all types within the assembly. The expectation is that this method is
        /// only called at emit time, when all types have been or will be traversed anyway.
        /// </summary>
        private bool ContainsExtensionMethods()
        {
            if (!this.lazyContainsExtensionMethods.HasValue())
            {
                this.lazyContainsExtensionMethods = ContainsExtensionMethods(this.modules).ToThreeState();
            }

            return this.lazyContainsExtensionMethods.Value();
        }

        private static bool ContainsExtensionMethods(ImmutableArray<ModuleSymbol> modules)
        {
            foreach (var module in modules)
            {
                if (ContainsExtensionMethods(module.GlobalNamespace))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ContainsExtensionMethods(NamespaceSymbol ns)
        {
            foreach (var member in ns.GetMembersUnordered())
            {
                switch (member.Kind)
                {
                    case SymbolKind.Namespace:
                        if (ContainsExtensionMethods((NamespaceSymbol)member))
                        {
                            return true;
                        }
                        break;
                    case SymbolKind.NamedType:
                        if (((NamedTypeSymbol)member).MightContainExtensionMethods)
                        {
                            return true;
                        }
                        break;
                    default:
                        Debug.Assert(false, "Unexpected member kind: " + member.Kind);
                        break;
                }
            }
            return false;
        }

        //Once the computation of the AssemblyIdentity is complete, check whether
        //any of the IVT access grants that were optimistically made during AssemblyIdentity computation
        //are in fact invalid now that the full identity is known.
        private void CheckOptimisticIVTAccessGrants(DiagnosticBag bag)
        {
            ConcurrentDictionary<AssemblySymbol, bool> haveGrantedAssemblies = optimisticallyGrantedInternalsAccess;

            if (haveGrantedAssemblies != null)
            {
                foreach (var otherAssembly in haveGrantedAssemblies.Keys)
                {
                    IVTConclusion conclusion = MakeFinalIVTDetermination(otherAssembly);

                    Debug.Assert(conclusion != IVTConclusion.NoRelationshipClaimed);

                    if (conclusion == IVTConclusion.PublicKeyDoesntMatch)
                        bag.Add(ErrorCode.ERR_FriendRefNotEqualToThis, NoLocation.Singleton,
                                                                      otherAssembly.Identity);
                    else if (conclusion == IVTConclusion.OneSignedOneNot)
                        bag.Add(ErrorCode.ERR_FriendRefSigningMismatch, NoLocation.Singleton,
                                                                      otherAssembly.Identity);
                }
            }
        }

        internal override IEnumerable<ImmutableArray<byte>> GetInternalsVisibleToPublicKeys(string simpleName)
        {
            //EDMAURER assume that if EnsureAttributesAreBound() returns, then the internals visible to map has been populated.
            //Do not optimize by checking if m_lazyInternalsVisibleToMap is Nothing. It may be non-null yet still
            //incomplete because another thread is in the process of building it.

            EnsureAttributesAreBound();

            if (lazyInternalsVisibleToMap == null)
                return Enumerable.Empty<ImmutableArray<byte>>();

            ConcurrentDictionary<ImmutableArray<byte>, Tuple<Location, string>> result = null;

            lazyInternalsVisibleToMap.TryGetValue(simpleName, out result);

            return (result != null) ? result.Keys : Enumerable.Empty<ImmutableArray<byte>>();
        }

        internal override bool AreInternalsVisibleToThisAssembly(AssemblySymbol potentialGiverOfAccess)
        {
            // Ensure that optimistic IVT access is only granted to requests that originated on the thread
            //that is trying to compute the assembly identity. This gives us deterministic behavior when
            //two threads are checking IVT access but only one of them is in the process of computing identity.

            //as an optimization confirm that the identity has not yet been computed to avoid testing TLS
            if (lazyStrongNameKeys == null)
            {
                var assemblyWhoseKeysAreBeingComputed = assemblyForWhichCurrentThreadIsComputingKeys;
                if ((object)assemblyWhoseKeysAreBeingComputed != null)
                {
                    //ThrowIfFalse(assemblyWhoseKeysAreBeingComputed Is Me);
                    if (!potentialGiverOfAccess.GetInternalsVisibleToPublicKeys(this.Name).IsEmpty())
                    {
                        if (optimisticallyGrantedInternalsAccess == null)
                            Interlocked.CompareExchange(ref optimisticallyGrantedInternalsAccess, new ConcurrentDictionary<AssemblySymbol, bool>(), null);

                        optimisticallyGrantedInternalsAccess.TryAdd(potentialGiverOfAccess, true);
                        return true;
                    }
                    else
                        return false;
                }
            }

            IVTConclusion conclusion = MakeFinalIVTDetermination(potentialGiverOfAccess);

            return conclusion == IVTConclusion.Match || conclusion == IVTConclusion.OneSignedOneNot;
        }

        private AssemblyIdentity ComputeIdentity()
        {
            return new AssemblyIdentity(
                assemblySimpleName,
                this.AssemblyVersionAttributeSetting,
                cultureName: this.AssemblyCultureAttributeSetting,
                publicKeyOrToken: StrongNameKeys.PublicKey,
                hasPublicKey: !StrongNameKeys.PublicKey.IsDefault);
        }

        //This maps from assembly name to a set of public keys. It uses concurrent dictionaries because it is built,
        //one attribute at a time, in the callback that validates an attribute's application to a symbol. It is assumed
        //to be complete after a call to GetAttributes(). The second dictionary is acting like a set. The value element is
        //only used when the key is empty in which case it stores the location and value of the attribute string which
        //may be used to construct a diagnostic if the assembly being compiled is found to be strong named.
        private ConcurrentDictionary<string, ConcurrentDictionary<ImmutableArray<byte>, Tuple<Location, string>>> lazyInternalsVisibleToMap;

        private static Location GetAssemblyAttributeLocationForDiagnostic(AttributeSyntax attributeSyntaxOpt)
        {
            return (object)attributeSyntaxOpt != null ? attributeSyntaxOpt.Location : NoLocation.Singleton;
        }

        private void DecodeTypeForwardedToAttribute(ref DecodeWellKnownAttributeArguments<AttributeSyntax, CSharpAttributeData, AttributeLocation> arguments)
        {
            // this code won't be called unless we bound a well-formed, semantically correct ctor call.
            Debug.Assert(!arguments.Attribute.HasErrors);

            TypeSymbol forwardedType = (TypeSymbol)arguments.Attribute.CommonConstructorArguments[0].Value;

            // This can happen if the argument is the null literal.
            if ((object)forwardedType == null)
            {
                arguments.Diagnostics.Add(ErrorCode.ERR_InvalidFwdType, GetAssemblyAttributeLocationForDiagnostic(arguments.AttributeSyntaxOpt));
                return;
            }

            DiagnosticInfo useSiteDiagnostic = forwardedType.GetUseSiteDiagnostic();
            if (useSiteDiagnostic != null &&
                useSiteDiagnostic.Code != (int)ErrorCode.ERR_UnexpectedUnboundGenericName &&
                Symbol.ReportUseSiteDiagnostic(useSiteDiagnostic, arguments.Diagnostics, GetAssemblyAttributeLocationForDiagnostic(arguments.AttributeSyntaxOpt)))
            {
                return;
            }

            Debug.Assert(forwardedType.TypeKind != TypeKind.Error);

            if (forwardedType.ContainingAssembly == this)
            {
                arguments.Diagnostics.Add(ErrorCode.ERR_ForwardedTypeInThisAssembly, GetAssemblyAttributeLocationForDiagnostic(arguments.AttributeSyntaxOpt), forwardedType);
                return;
            }

            if ((object)forwardedType.ContainingType != null)
            {
                arguments.Diagnostics.Add(ErrorCode.ERR_ForwardedTypeIsNested, GetAssemblyAttributeLocationForDiagnostic(arguments.AttributeSyntaxOpt), forwardedType, forwardedType.ContainingType);
                return;
            }

            if (forwardedType.Kind != SymbolKind.NamedType)
            {
                // NOTE: Dev10 actually tests whether the forwarded type is an aggregate.  This would seem to
                // exclude nullable and void, but that shouldn't be an issue because they have to be defined in
                // corlib (since they are special types) and corlib can't refer to other assemblies (by definition).

                arguments.Diagnostics.Add(ErrorCode.ERR_InvalidFwdType, GetAssemblyAttributeLocationForDiagnostic(arguments.AttributeSyntaxOpt));
                return;
            }

            // NOTE: There is no danger of the type being forwarded back to this assembly, because the type
            // won't even bind successfully unless we have a reference to an assembly that actually contains
            // the type.

            var assemblyData = arguments.GetOrCreateData<CommonAssemblyWellKnownAttributeData>();
            HashSet<NamedTypeSymbol> forwardedTypes = assemblyData.ForwardedTypes;
            if (forwardedTypes == null)
            {
                forwardedTypes = new HashSet<NamedTypeSymbol>() { (NamedTypeSymbol)forwardedType };
                assemblyData.ForwardedTypes = forwardedTypes;
            }
            else if (!forwardedTypes.Add((NamedTypeSymbol)forwardedType))
            {
                // NOTE: For the purposes of reporting this error, Dev10 considers C<int> and C<char>
                // different types.  However, it will actually emit a single forwarder for C`1 (i.e.
                // we'll have to de-dup again at emit time).
                arguments.Diagnostics.Add(ErrorCode.ERR_DuplicateTypeForwarder, GetAssemblyAttributeLocationForDiagnostic(arguments.AttributeSyntaxOpt), forwardedType);
            }
        }

        private static void DecodeOneInternalsVisibleToAttribute(
            AttributeSyntax nodeOpt,
            CSharpAttributeData attrData,
            DiagnosticBag diagnostics,
            ref ConcurrentDictionary<string, ConcurrentDictionary<ImmutableArray<byte>, Tuple<Location, string>>> lazyInternalsVisibleToMap)
        {
            // this code won't be called unless we bound a well-formed, semantically correct ctor call.
            Debug.Assert(!attrData.HasErrors);

            string displayName = (string)attrData.CommonConstructorArguments[0].Value;

            if (displayName == null)
            {
                diagnostics.Add(ErrorCode.ERR_CannotPassNullForFriendAssembly, GetAssemblyAttributeLocationForDiagnostic(nodeOpt));
                return;
            }

            AssemblyIdentity identity;
            AssemblyIdentityParts parts;
            if (!AssemblyIdentity.TryParseDisplayName(displayName, out identity, out parts))
            {
                diagnostics.Add(ErrorCode.WRN_InvalidAssemblyName, GetAssemblyAttributeLocationForDiagnostic(nodeOpt), displayName);
                return;
            }

            const AssemblyIdentityParts allowedParts = AssemblyIdentityParts.Name | AssemblyIdentityParts.PublicKey;

            if ((parts & ~allowedParts) != 0)
            {
                diagnostics.Add(ErrorCode.ERR_FriendAssemblyBadArgs, GetAssemblyAttributeLocationForDiagnostic(nodeOpt), displayName);
                return;
            }

            if (lazyInternalsVisibleToMap == null)
            {
                Interlocked.CompareExchange(ref lazyInternalsVisibleToMap,
                                            new ConcurrentDictionary<string, ConcurrentDictionary<ImmutableArray<byte>, Tuple<Location, String>>>(), null);
            }

            //later, once the identity is established we confirm that if the assembly being 
            //compiled is signed all of the IVT attributes specify a key. Stash the location for that
            //in the event that a diagnostic needs to be produced.

            Tuple<Location, string> locationAndValue = null;

            // only need to store anything when there is no public key. The only reason to store
            // this stuff is for production of errors when the assembly is signed but the IVT attrib
            // doesn't contain a public key.
            if (identity.PublicKey.IsEmpty)
            {
                locationAndValue = new Tuple<Location, string>(GetAssemblyAttributeLocationForDiagnostic(nodeOpt), displayName);
            }

            //when two threads are attempting to update the internalsVisibleToMap one of these TryAdd()
            //calls can fail. We assume that the 'other' thread in that case will successfully add the same
            //contents eventually.
            ConcurrentDictionary<ImmutableArray<byte>, Tuple<Location, string>> keys = null;
            if (lazyInternalsVisibleToMap.TryGetValue(identity.Name, out keys))
            {
                keys.TryAdd(identity.PublicKey, locationAndValue);
            }
            else
            {
                keys = new ConcurrentDictionary<ImmutableArray<byte>, Tuple<Location, String>>();
                keys.TryAdd(identity.PublicKey, locationAndValue);
                lazyInternalsVisibleToMap.TryAdd(identity.Name, keys);
            }
        }

        IAttributeTargetSymbol IAttributeTargetSymbol.AttributesOwner
        {
            get { return this; }
        }

        AttributeLocation IAttributeTargetSymbol.DefaultAttributeLocation
        {
            get { return AttributeLocation.Assembly; }
        }

        AttributeLocation IAttributeTargetSymbol.AllowedAttributeLocations
        {
            get
            {
                return IsInteractive ? AttributeLocation.None : AttributeLocation.Assembly | AttributeLocation.Module;
            }
        }

        internal override void DecodeWellKnownAttribute(ref DecodeWellKnownAttributeArguments<AttributeSyntax, CSharpAttributeData, AttributeLocation> arguments)
        {
            DecodeWellKnownAttribute(ref arguments, isFromNetModule: false);
        }

        private void DecodeWellKnownAttribute(ref DecodeWellKnownAttributeArguments<AttributeSyntax, CSharpAttributeData, AttributeLocation> arguments, bool isFromNetModule)
        {
            var attribute = arguments.Attribute;
            Debug.Assert(!attribute.HasErrors);
            Debug.Assert(arguments.SymbolPart == AttributeLocation.None);
            int signature;

            if (attribute.IsTargetAttribute(this, AttributeDescription.InternalsVisibleToAttribute))
            {
                DecodeOneInternalsVisibleToAttribute(arguments.AttributeSyntaxOpt, attribute, arguments.Diagnostics, ref lazyInternalsVisibleToMap);
            }
            else if (attribute.IsTargetAttribute(this, AttributeDescription.AssemblySignatureKeyAttribute))
            {
                var signatureKey = (string)attribute.CommonConstructorArguments[0].Value;
                arguments.GetOrCreateData<CommonAssemblyWellKnownAttributeData>().AssemblySignatureKeyAttributeSetting = signatureKey;

                if (!StrongNameKeys.IsValidPublicKeyString(signatureKey))
                {
                    arguments.Diagnostics.Add(ErrorCode.ERR_InvalidSignaturePublicKey, attribute.GetAttributeArgumentSyntaxLocation(0, arguments.AttributeSyntaxOpt));
                }
            }
            else if (attribute.IsTargetAttribute(this, AttributeDescription.AssemblyKeyFileAttribute))
            {
                arguments.GetOrCreateData<CommonAssemblyWellKnownAttributeData>().AssemblyKeyFileAttributeSetting = (string)attribute.CommonConstructorArguments[0].Value;
            }
            else if (attribute.IsTargetAttribute(this, AttributeDescription.AssemblyKeyNameAttribute))
            {
                arguments.GetOrCreateData<CommonAssemblyWellKnownAttributeData>().AssemblyKeyContainerAttributeSetting = (string)attribute.CommonConstructorArguments[0].Value;
            }
            else if (attribute.IsTargetAttribute(this, AttributeDescription.AssemblyDelaySignAttribute))
            {
                arguments.GetOrCreateData<CommonAssemblyWellKnownAttributeData>().AssemblyDelaySignAttributeSetting = (bool)attribute.CommonConstructorArguments[0].Value ? ThreeState.True : ThreeState.False;
            }
            else if (attribute.IsTargetAttribute(this, AttributeDescription.AssemblyVersionAttribute))
            {
                string verString = (string)attribute.CommonConstructorArguments[0].Value;
                Version version;
                if (!VersionHelper.TryParseWithWildcards(verString, out version))
                {
                    Location attributeArgumentSyntaxLocation = attribute.GetAttributeArgumentSyntaxLocation(0, arguments.AttributeSyntaxOpt);
                    arguments.Diagnostics.Add(ErrorCode.ERR_InvalidVersionFormat, attributeArgumentSyntaxLocation);
                }

                arguments.GetOrCreateData<CommonAssemblyWellKnownAttributeData>().AssemblyVersionAttributeSetting = version;
            }
            else if (attribute.IsTargetAttribute(this, AttributeDescription.AssemblyFileVersionAttribute))
            {
                Version dummy;
                string verString = (string)attribute.CommonConstructorArguments[0].Value;
                //Validation here includes checking that the values are < 65535
                if (!VersionHelper.TryParse(verString, out dummy) || !VersionHelper.Validate(ref dummy))
                {
                    Location attributeArgumentSyntaxLocation = attribute.GetAttributeArgumentSyntaxLocation(0, arguments.AttributeSyntaxOpt);
                    arguments.Diagnostics.Add(ErrorCode.WRN_InvalidVersionFormat, attributeArgumentSyntaxLocation);
                }

                arguments.GetOrCreateData<CommonAssemblyWellKnownAttributeData>().AssemblyFileVersionAttributeSetting = verString;
            }
            else if (attribute.IsTargetAttribute(this, AttributeDescription.AssemblyTitleAttribute))
            {
                arguments.GetOrCreateData<CommonAssemblyWellKnownAttributeData>().AssemblyTitleAttributeSetting = (string)attribute.CommonConstructorArguments[0].Value;
            }
            else if (attribute.IsTargetAttribute(this, AttributeDescription.AssemblyDescriptionAttribute))
            {
                arguments.GetOrCreateData<CommonAssemblyWellKnownAttributeData>().AssemblyDescriptionAttributeSetting = (string)attribute.CommonConstructorArguments[0].Value;
            }
            else if (attribute.IsTargetAttribute(this, AttributeDescription.AssemblyCultureAttribute))
            {
                var cultureString = (string)attribute.CommonConstructorArguments[0].Value;
                if (this.compilation.Options.OutputKind.IsApplication() && !String.IsNullOrEmpty(cultureString))
                {
                    Location attributeArgumentSyntaxLocation = attribute.GetAttributeArgumentSyntaxLocation(0, arguments.AttributeSyntaxOpt);
                    arguments.Diagnostics.Add(ErrorCode.ERR_InvalidAssemblyCultureForExe, attributeArgumentSyntaxLocation);
                }

                arguments.GetOrCreateData<CommonAssemblyWellKnownAttributeData>().AssemblyCultureAttributeSetting = cultureString;
            }
            else if (attribute.IsTargetAttribute(this, AttributeDescription.AssemblyCompanyAttribute))
            {
                arguments.GetOrCreateData<CommonAssemblyWellKnownAttributeData>().AssemblyCompanyAttributeSetting = (string)attribute.CommonConstructorArguments[0].Value;
            }
            else if (attribute.IsTargetAttribute(this, AttributeDescription.AssemblyProductAttribute))
            {
                arguments.GetOrCreateData<CommonAssemblyWellKnownAttributeData>().AssemblyProductAttributeSetting = (string)attribute.CommonConstructorArguments[0].Value;
            }
            else if (attribute.IsTargetAttribute(this, AttributeDescription.AssemblyInformationalVersionAttribute))
            {
                arguments.GetOrCreateData<CommonAssemblyWellKnownAttributeData>().AssemblyInformationalVersionAttributeSetting = (string)attribute.CommonConstructorArguments[0].Value;
            }
            else if (attribute.IsTargetAttribute(this, AttributeDescription.SatelliteContractVersionAttribute))
            {
                //just check the format of this one, don't do anything else with it.
                Version dummy;
                string verString = (string)attribute.CommonConstructorArguments[0].Value;

                //Validation here includes checking that the values are < 65535
                if (!VersionHelper.TryParse(verString, out dummy) || !VersionHelper.Validate(ref dummy))
                {
                    Location attributeArgumentSyntaxLocation = attribute.GetAttributeArgumentSyntaxLocation(0, arguments.AttributeSyntaxOpt);
                    arguments.Diagnostics.Add(ErrorCode.ERR_InvalidVersionFormat2, attributeArgumentSyntaxLocation);
                }
            }
            else if (attribute.IsTargetAttribute(this, AttributeDescription.AssemblyCopyrightAttribute))
            {
                arguments.GetOrCreateData<CommonAssemblyWellKnownAttributeData>().AssemblyCopyrightAttributeSetting = (string)attribute.CommonConstructorArguments[0].Value;
            }
            else if (attribute.IsTargetAttribute(this, AttributeDescription.AssemblyTrademarkAttribute))
            {
                arguments.GetOrCreateData<CommonAssemblyWellKnownAttributeData>().AssemblyTrademarkAttributeSetting = (string)attribute.CommonConstructorArguments[0].Value;
            }
            else if ((signature = attribute.GetTargetAttributeSignatureIndex(this, AttributeDescription.AssemblyFlagsAttribute)) != -1)
            {
                object value = attribute.CommonConstructorArguments[0].Value;
                System.Reflection.AssemblyNameFlags nameFlags;

                if (signature == 0 || signature == 1)
                {
                    nameFlags = (System.Reflection.AssemblyNameFlags)value;
                }
                else
                {
                    nameFlags = (System.Reflection.AssemblyNameFlags)(uint)value;
                }

                arguments.GetOrCreateData<CommonAssemblyWellKnownAttributeData>().AssemblyFlagsAttributeSetting = nameFlags;
            }
            else if (attribute.IsSecurityAttribute(compilation))
            {
                attribute.DecodeSecurityAttribute<CommonAssemblyWellKnownAttributeData>(this, compilation, ref arguments);
            }
            else if (attribute.IsTargetAttribute(this, AttributeDescription.ClassInterfaceAttribute))
            {
                attribute.DecodeClassInterfaceAttribute(arguments.AttributeSyntaxOpt, arguments.Diagnostics);
            }
            else if (attribute.IsTargetAttribute(this, AttributeDescription.TypeLibVersionAttribute))
            {
                ValidateIntegralAttributeNonNegativeArguments(attribute, arguments.AttributeSyntaxOpt, arguments.Diagnostics);
            }
            else if (attribute.IsTargetAttribute(this, AttributeDescription.ComCompatibleVersionAttribute))
            {
                ValidateIntegralAttributeNonNegativeArguments(attribute, arguments.AttributeSyntaxOpt, arguments.Diagnostics);
            }
            else if (attribute.IsTargetAttribute(this, AttributeDescription.GuidAttribute))
            {
                attribute.DecodeGuidAttribute(arguments.AttributeSyntaxOpt, arguments.Diagnostics);
            }
            else if (attribute.IsTargetAttribute(this, AttributeDescription.CompilationRelaxationsAttribute))
            {
                arguments.GetOrCreateData<CommonAssemblyWellKnownAttributeData>().HasCompilationRelaxationsAttribute = true;
            }
            else if (attribute.IsTargetAttribute(this, AttributeDescription.RuntimeCompatibilityAttribute))
            {
                bool wrapNonExceptionThrows = true;

                foreach (var namedArg in attribute.CommonNamedArguments)
                {
                    switch (namedArg.Key)
                    {
                        case "WrapNonExceptionThrows":
                            wrapNonExceptionThrows = namedArg.Value.DecodeValue<bool>(SpecialType.System_Boolean);
                            break;
                    }
                }

                arguments.GetOrCreateData<CommonAssemblyWellKnownAttributeData>().RuntimeCompatibilityWrapNonExceptionThrows = wrapNonExceptionThrows;
            }
            else if (attribute.IsTargetAttribute(this, AttributeDescription.DebuggableAttribute))
            {
                arguments.GetOrCreateData<CommonAssemblyWellKnownAttributeData>().HasDebuggableAttribute = true;
            }
            else if (!isFromNetModule && attribute.IsTargetAttribute(this, AttributeDescription.TypeForwardedToAttribute))
            {
                DecodeTypeForwardedToAttribute(ref arguments);
            }
            else if (attribute.IsTargetAttribute(this, AttributeDescription.CaseSensitiveExtensionAttribute))
            {
                if ((object)arguments.AttributeSyntaxOpt != null)
                {
                    // [Extension] attribute should not be set explicitly.
                    arguments.Diagnostics.Add(ErrorCode.ERR_ExplicitExtension, arguments.AttributeSyntaxOpt.Location);
                }
            }
            else if ((signature = attribute.GetTargetAttributeSignatureIndex(this, AttributeDescription.AssemblyAlgorithmIdAttribute)) != -1)
            {
                object value = attribute.CommonConstructorArguments[0].Value;
                AssemblyHashAlgorithm algorithmId;

                if (signature == 0)
                {
                    algorithmId = (AssemblyHashAlgorithm)value;
                }
                else
                {
                    algorithmId = (AssemblyHashAlgorithm)(uint)value;
                }

                arguments.GetOrCreateData<CommonAssemblyWellKnownAttributeData>().AssemblyAlgorithmIdAttributeSetting = algorithmId;
            }
        }

        // Checks that the integral arguments for the given well-known attribute are non-negative.
        private static void ValidateIntegralAttributeNonNegativeArguments(CSharpAttributeData attribute, AttributeSyntax nodeOpt, DiagnosticBag diagnostics)
        {
            Debug.Assert(!attribute.HasErrors);

            int argCount = attribute.CommonConstructorArguments.Length;
            for (int i = 0; i < argCount; i++)
            {
                int arg = attribute.GetConstructorArgument<int>(i, SpecialType.System_Int32);
                if (arg < 0)
                {
                    // CS0591: Invalid value for argument to '{0}' attribute
                    Location attributeArgumentSyntaxLocation = attribute.GetAttributeArgumentSyntaxLocation(i, nodeOpt);
                    diagnostics.Add(ErrorCode.ERR_InvalidAttributeArgument, attributeArgumentSyntaxLocation, (object)nodeOpt != null ? nodeOpt.GetErrorDisplayName() : "");
                }
            }
        }

        internal void NoteFieldAccess(FieldSymbol field, bool read, bool write)
        {
            var container = field.ContainingType as SourceMemberContainerTypeSymbol;
            if ((object)container == null)
            {
                // field is not in source.
                return;
            }

            container.EnsureFieldDefinitionsNoted();

            if (this.unusedFieldWarnings.IsDefault)
            {
                if (read)
                {
                    this.unreadFields.Remove(field);
                }

                if (write)
                {
                    bool _;
                    this.unassignedFieldsMap.TryRemove(field, out _);
                }
            }
            else
            {
                // It's acceptable to run flow analysis again after the diagnostics have been computed - just
                // make sure that the nothing is different than the first time.

                Debug.Assert(
                     !(read && this.unreadFields.Remove(field)),
                     "we are already reporting unused field warnings, there could be no more changes");

                Debug.Assert(
                    !(write && this.unassignedFieldsMap.ContainsKey(field)),
                    "we are already reporting unused field warnings, there could be no more changes");
            }
        }

        internal void NoteFieldDefinition(FieldSymbol field, bool isInternal, bool isUnread)
        {
            Debug.Assert(this.unusedFieldWarnings.IsDefault, "We shouldn't have computed the diagnostics if we're still noting definitions.");

            this.unassignedFieldsMap.TryAdd(field, isInternal);
            if (isUnread)
            {
                this.unreadFields.Add(field);
            }
        }

        /// <summary>
        /// Get the warnings for unused fields.  This should only be fetched when all method bodies have been compiled.
        /// </summary>
        internal ImmutableArray<Diagnostic> GetUnusedFieldWarnings(CancellationToken cancellationToken)
        {
            if (this.unusedFieldWarnings.IsDefault)
            {
                //Our maps of unread and unassigned fields won't be done until the assembly is complete.
                this.ForceComplete(locationOpt: null, cancellationToken: cancellationToken);

                Debug.Assert(this.HasComplete(CompletionPart.Module),
                    "Don't consume unused field information if there are still types to be processed.");

                // Build this up in a local before we assign it to this.unusedFieldWarnings (so other threads
                // can see that it's not done).
                DiagnosticBag diagnostics = DiagnosticBag.GetInstance();

                // NOTE: two threads can come in here at the same time.  If they do, then they will
                // share the diagnostic bag.  That's alright, as long as each one processes only
                // the fields that it successfully removes from the shared map/set.  Furthermore,
                // there should be no problem with re-calling this method on the same assembly,
                // since there will be nothing left in the map/set the second time.
                bool internalsAreVisible =
                    this.InternalsAreVisible ||
                    this.DeclaringCompilation.Options.OutputKind.IsNetModule();

                HashSet<FieldSymbol> handledUnreadFields = null;

                foreach (FieldSymbol field in this.unassignedFieldsMap.Keys) // Not mutating, so no snapshot required.
                {
                    bool isInternalAccessiblity;
                    bool success = this.unassignedFieldsMap.TryGetValue(field, out isInternalAccessiblity);
                    Debug.Assert(success, "Once CompletionPart.Module is set, no one should be modifying the map.");

                    if (isInternalAccessiblity && internalsAreVisible)
                    {
                        continue;
                    }

                    if (!field.CanBeReferencedByName)
                    {
                        continue;
                    }

                    var containingType = field.ContainingType as SourceNamedTypeSymbol;
                    if ((object)containingType == null)
                    {
                        continue;
                    }

                    bool unread = this.unreadFields.Contains(field);
                    if (unread)
                    {
                        if (handledUnreadFields == null)
                        {
                            handledUnreadFields = new HashSet<FieldSymbol>();
                        }
                        handledUnreadFields.Add(field);
                    }

                    if (containingType.HasStructLayoutAttribute)
                    {
                        continue;
                    }

                    Symbol associatedPropertyOrEvent = field.AssociatedPropertyOrEvent;
                    if ((object)associatedPropertyOrEvent != null && associatedPropertyOrEvent.Kind == SymbolKind.Event)
                    {
                        if (unread)
                        {
                            diagnostics.Add(ErrorCode.WRN_UnreferencedEvent, associatedPropertyOrEvent.Locations[0], associatedPropertyOrEvent);
                        }
                    }
                    else if (unread)
                    {
                        diagnostics.Add(ErrorCode.WRN_UnreferencedField, field.Locations[0], field);
                    }
                    else
                    {
                        diagnostics.Add(ErrorCode.WRN_UnassignedInternalField, field.Locations[0], field, DefaultValue(field.Type));
                    }
                }

                foreach (FieldSymbol field in this.unreadFields) // Not mutating, so no snapshot required.
                {
                    if (handledUnreadFields != null && handledUnreadFields.Contains(field))
                    {
                        // Handled in the first foreach loop.
                        continue;
                    }

                    if (!field.CanBeReferencedByName)
                    {
                        continue;
                    }

                    var containingType = field.ContainingType as SourceNamedTypeSymbol;
                    if ((object)containingType != null && !containingType.HasStructLayoutAttribute)
                    {
                        diagnostics.Add(ErrorCode.WRN_UnreferencedFieldAssg, field.Locations[0], field);
                    }
                }

                ImmutableInterlocked.InterlockedInitialize(ref this.unusedFieldWarnings, diagnostics.ToReadOnlyAndFree());
            }

            Debug.Assert(!this.unusedFieldWarnings.IsDefault);
            return this.unusedFieldWarnings;
        }

        private static string DefaultValue(TypeSymbol type)
        {
            // TODO: localize these strings
            if (type.IsReferenceType) return "null";
            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean:
                    return "false";
                case SpecialType.System_Byte:
                case SpecialType.System_Decimal:
                case SpecialType.System_Double:
                case SpecialType.System_Int16:
                case SpecialType.System_Int32:
                case SpecialType.System_Int64:
                case SpecialType.System_SByte:
                case SpecialType.System_Single:
                case SpecialType.System_UInt16:
                case SpecialType.System_UInt32:
                case SpecialType.System_UInt64:
                    return "0";
                default:
                    return "";
            }
        }

        internal override NamedTypeSymbol TryLookupForwardedMetadataTypeWithCycleDetection(ref MetadataTypeName emittedName, ConsList<AssemblySymbol> visitedAssemblies)
        {
            int forcedArity = emittedName.ForcedArity;

            if (emittedName.UseCLSCompliantNameArityEncoding)
            {
                if (forcedArity == -1)
                {
                    forcedArity = emittedName.InferredArity;
                }
                else if (forcedArity != emittedName.InferredArity)
                {
                    return null;
                }

                Debug.Assert(forcedArity == emittedName.InferredArity);
            }

            if (lazyForwardedTypesFromSource == null)
            {
                IDictionary<string, NamedTypeSymbol> forwardedTypesFromSource;
                CommonAssemblyWellKnownAttributeData<NamedTypeSymbol> wellKnownAttributeData = GetSourceDecodedWellKnownAttributeData();

                if (wellKnownAttributeData != null && wellKnownAttributeData.ForwardedTypes != null)
                {
                    forwardedTypesFromSource = new Dictionary<string, NamedTypeSymbol>();

                    foreach (NamedTypeSymbol forwardedType in wellKnownAttributeData.ForwardedTypes)
                    {
                        NamedTypeSymbol originalDefinition = forwardedType.OriginalDefinition;
                        Debug.Assert((object)originalDefinition.ContainingType == null, "How did a nested type get forwarded?");

                        string fullEmittedName = MetadataHelpers.BuildQualifiedName(originalDefinition.ContainingSymbol.ToDisplayString(SymbolDisplayFormat.QualifiedNameOnlyFormat),
                                                                                    originalDefinition.MetadataName);
                        // Since we need to allow multiple constructions of the same generic type at the source
                        // level, we need to de-dup the original definitions.
                        forwardedTypesFromSource[fullEmittedName] = originalDefinition;
                    }
                }
                else
                {
                    forwardedTypesFromSource = SpecializedCollections.EmptyDictionary<string, NamedTypeSymbol>();
                }

                lazyForwardedTypesFromSource = forwardedTypesFromSource;
            }

            NamedTypeSymbol result;

            if (lazyForwardedTypesFromSource.TryGetValue(emittedName.FullName, out result))
            {
                if ((forcedArity == -1 || result.Arity == forcedArity) &&
                    (!emittedName.UseCLSCompliantNameArityEncoding || result.Arity == 0 || result.MangleName))
                {
                    return result;
                }
            }
            else if (!compilation.Options.OutputKind.IsNetModule())
            {
                // See if any of added modules forward the type.

                // Similar to attributes, type forwarders from the second added module should override type forwarders from the first added module, etc. 
                for (int i = this.modules.Length - 1; i > 0; i--)
                {
                    var peModuleSymbol = (Metadata.PE.PEModuleSymbol)this.modules[i];

                    var forwardedToAssembly = peModuleSymbol.GetAssemblyForForwardedType(ref emittedName);
                    if ((object)forwardedToAssembly != null)
                    {
                        // Don't bother to check the forwarded-to assembly if we've already seen it.
                        if (visitedAssemblies != null && visitedAssemblies.Contains(forwardedToAssembly))
                        {
                            return CreateCycleInTypeForwarderErrorTypeSymbol(ref emittedName);
                        }
                        else
                        {
                            visitedAssemblies = new ConsList<AssemblySymbol>(this, visitedAssemblies ?? ConsList<AssemblySymbol>.Empty);
                            return forwardedToAssembly.LookupTopLevelMetadataTypeWithCycleDetection(ref emittedName, visitedAssemblies, digThroughForwardedTypes: true);
                        }
                    }
                }
            }

            return null;
        }
    }
}
