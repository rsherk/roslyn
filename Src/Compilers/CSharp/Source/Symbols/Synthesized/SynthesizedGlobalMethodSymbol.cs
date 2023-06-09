﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis.CodeGen;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.Symbols
{
    /// <summary>
    /// Represents a compiler generated synthesized method symbol
    /// that must be emitted in the compiler generated
    /// PrivateImplementationDetails class
    /// </summary>
    internal abstract class SynthesizedGlobalMethodSymbol : MethodSymbol
    {
        private readonly ModuleSymbol containingModule;
        private readonly PrivateImplementationDetails privateImplType;
        private readonly TypeSymbol returnType;
        private ImmutableArray<ParameterSymbol> parameters;
        private readonly string name;

        internal SynthesizedGlobalMethodSymbol(ModuleSymbol containingModule, PrivateImplementationDetails privateImplType, TypeSymbol returnType, string name)
        {
            Debug.Assert((object)containingModule != null);
            Debug.Assert(privateImplType != null);
            Debug.Assert((object)returnType != null);
            Debug.Assert(name != null);

            this.containingModule = containingModule;
            this.privateImplType = privateImplType;
            this.returnType = returnType;
            this.name = name;
        }

        protected void SetParameters(ImmutableArray<ParameterSymbol> parameters)
        {
            ImmutableInterlocked.InterlockedExchange(ref this.parameters, parameters);
        }

        public sealed override bool IsImplicitlyDeclared
        {
            get { return true; }
        }

        internal sealed override bool GenerateDebugInfo
        {
            get { return false; }
        }

        internal sealed override ModuleSymbol ContainingModule
        {
            get
            {
                return containingModule;
            }
        }

        public sealed override AssemblySymbol ContainingAssembly
        {
            get
            {
                return containingModule.ContainingAssembly;
            }
        }

        /// <summary>
        /// Synthesized methods that must be emitted in the compiler generated
        /// PrivateImplementationDetails class have null containing type symbol.
        /// </summary>
        public sealed override Symbol ContainingSymbol
        {
            get { return null; }
        }

        public sealed override NamedTypeSymbol ContainingType
        {
            get
            {
                return null;
            }
        }

        internal PrivateImplementationDetails ContainingPrivateImplementationDetailsType
        {
            get { return this.privateImplType; }
        }

        public override string Name
        {
            get { return name; }
        }

        internal override bool HasSpecialName
        {
            get { return false; }
        }

        internal override System.Reflection.MethodImplAttributes ImplementationAttributes
        {
            get { return default(System.Reflection.MethodImplAttributes); }
        }

        internal override bool RequiresSecurityObject
        {
            get { return false; }
        }

        public override DllImportData GetDllImportData()
        {
            return null;
        }

        internal override MarshalPseudoCustomAttributeData ReturnValueMarshallingInformation
        {
            get { return null; }
        }

        internal override bool HasDeclarativeSecurity
        {
            get { return false; }
        }

        internal override IEnumerable<Microsoft.Cci.SecurityAttribute> GetSecurityInformation()
        {
            throw ExceptionUtilities.Unreachable;
        }

        internal sealed override ObsoleteAttributeData ObsoleteAttributeData
        {
            get { return null; }
        }

        internal sealed override ImmutableArray<string> GetAppliedConditionalSymbols()
        {
            return ImmutableArray<string>.Empty;
        }

        public override bool IsVararg
        {
            get { return false; }
        }

        public override ImmutableArray<TypeParameterSymbol> TypeParameters
        {
            get { return ImmutableArray<TypeParameterSymbol>.Empty; }
        }

        public override ImmutableArray<ParameterSymbol> Parameters
        {
            get
            {
                if (parameters.IsEmpty)
                {
                    return ImmutableArray<ParameterSymbol>.Empty;
                }

                return parameters;
            }
        }

        public override Accessibility DeclaredAccessibility
        {
            get { return Accessibility.Internal; }
        }

        public override ImmutableArray<Location> Locations
        {
            get { return ImmutableArray<Location>.Empty; }
        }

        public override ImmutableArray<SyntaxReference> DeclaringSyntaxReferences
        {
            get
            {
                return ImmutableArray<SyntaxReference>.Empty;
            }
        }

        public override TypeSymbol ReturnType
        {
            get { return returnType; }
        }

        public override ImmutableArray<CustomModifier> ReturnTypeCustomModifiers
        {
            get { return ImmutableArray<CustomModifier>.Empty; }
        }

        public override ImmutableArray<TypeSymbol> TypeArguments
        {
            get { return ImmutableArray<TypeSymbol>.Empty; }
        }

        public override Symbol AssociatedPropertyOrEvent
        {
            get { return null; }
        }

        public override int Arity
        {
            get { return 0; }
        }

        public override bool ReturnsVoid
        {
            get { return this.ReturnType.SpecialType == SpecialType.System_Void; }
        }

        public override MethodKind MethodKind
        {
            get { return MethodKind.Ordinary; }
        }

        public override bool IsExtern
        {
            get { return false; }
        }

        public override bool IsSealed
        {
            get { return false; }
        }

        public override bool IsAbstract
        {
            get { return false; }
        }

        public override bool IsOverride
        {
            get { return false; }
        }

        public override bool IsVirtual
        {
            get { return false; }
        }

        public override bool IsStatic
        {
            get { return true; }
        }

        public override bool IsAsync
        {
            get { return false; }
        }

        public override bool HidesBaseMethodsByName
        {
            get { return false; }
        }

        internal sealed override bool IsMetadataNewSlot(bool ignoreInterfaceImplementationChanges = false)
        {
            return false;
        }

        internal sealed override bool IsMetadataVirtual(bool ignoreInterfaceImplementationChanges = false)
        {
            return false;
        }

        internal override bool IsMetadataFinal()
        {
            return false;
        }

        public override bool IsExtensionMethod
        {
            get { return false; }
        }

        internal override Microsoft.Cci.CallingConvention CallingConvention
        {
            get { return 0; }
        }

        internal override bool IsExplicitInterfaceImplementation
        {
            get { return false; }
        }

        public override ImmutableArray<MethodSymbol> ExplicitInterfaceImplementations
        {
            get { return ImmutableArray<MethodSymbol>.Empty; }
        }

        internal override bool SynthesizesLoweredBoundBody
        {
            get { return true; }
        }

        internal abstract override void GenerateMethodBody(TypeCompilationState compilationState, DiagnosticBag diagnostics);
    }
}
