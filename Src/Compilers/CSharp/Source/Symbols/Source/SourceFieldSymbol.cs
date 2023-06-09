﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.CodeAnalysis.Collections;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.Symbols
{
    internal abstract class SourceFieldSymbol : FieldSymbol, IAttributeTargetSymbol
    {
        protected SymbolCompletionState state;
        protected readonly SourceMemberContainerTypeSymbol containingType;
        private readonly string name;
        private readonly Location location;
        private readonly SyntaxReference syntaxReference;

        private string lazyDocComment;
        private CustomAttributesBag<CSharpAttributeData> lazyCustomAttributesBag;

        private ConstantValue lazyConstantEarlyDecodingValue = Microsoft.CodeAnalysis.ConstantValue.Unset;
        private ConstantValue lazyConstantValue = Microsoft.CodeAnalysis.ConstantValue.Unset;

        protected SourceFieldSymbol(SourceMemberContainerTypeSymbol containingType, string name, SyntaxReference syntax, Location location)
        {
            Debug.Assert((object)containingType != null);
            Debug.Assert(name != null);
            Debug.Assert(syntax != null);
            Debug.Assert(location != null);

            this.containingType = containingType;
            this.name = name;
            this.syntaxReference = syntax;
            this.location = location;
        }

        internal sealed override bool RequiresCompletion
        {
            get { return true; }
        }

        internal sealed override bool HasComplete(CompletionPart part)
        {
            return state.HasComplete(part);
        }

        internal override void ForceComplete(SourceLocation locationOpt, CancellationToken cancellationToken)
        {
            state.DefaultForceComplete(this);
        }

        public SyntaxTree SyntaxTree
        {
            get
            {
                return this.syntaxReference.SyntaxTree;
            }
        }

        public CSharpSyntaxNode SyntaxNode
        {
            get
            {
                return (CSharpSyntaxNode)this.syntaxReference.GetSyntax();
            }
        }

        public sealed override string Name
        {
            get
            {
                return name;
            }
        }

        public sealed override Symbol ContainingSymbol
        {
            get
            {
                return containingType;
            }
        }

        public override NamedTypeSymbol ContainingType
        {
            get
            {
                return this.containingType;
            }
        }

        internal override LexicalSortKey GetLexicalSortKey()
        {
            return new LexicalSortKey(location, this.DeclaringCompilation);
        }

        public sealed override ImmutableArray<Location> Locations
        {
            get
            {
                return ImmutableArray.Create(location);
            }
        }

        internal Location Location
        {
            get
            {
                return location;
            }
        }

        public sealed override ImmutableArray<SyntaxReference> DeclaringSyntaxReferences
        {
            get
            {
                return ImmutableArray.Create<SyntaxReference>(syntaxReference);
            }
        }

        public sealed override string GetDocumentationCommentXml(CultureInfo preferredCulture = null, bool expandIncludes = false, CancellationToken cancellationToken = default(CancellationToken))
        {
            return SourceDocumentationCommentUtils.GetAndCacheDocumentationComment(this, expandIncludes, ref lazyDocComment);
        }

        /// <summary>
        /// Gets the syntax list of custom attributes applied on the symbol.
        /// </summary>
        protected abstract SyntaxList<AttributeListSyntax> AttributeDeclarationSyntaxList { get; }

        protected virtual IAttributeTargetSymbol AttributeOwner
        {
            get { return this; }
        }

        IAttributeTargetSymbol IAttributeTargetSymbol.AttributesOwner
        {
            get { return this.AttributeOwner; }
        }

        AttributeLocation IAttributeTargetSymbol.DefaultAttributeLocation
        {
            get { return AttributeLocation.Field; }
        }

        AttributeLocation IAttributeTargetSymbol.AllowedAttributeLocations
        {
            get { return AttributeLocation.Field; }
        }

        /// <summary>
        /// Returns a bag of applied custom attributes and data decoded from well-known attributes. Returns null if there are no attributes applied on the symbol.
        /// </summary>
        /// <remarks>
        /// Forces binding and decoding of attributes.
        /// </remarks>
        private CustomAttributesBag<CSharpAttributeData> GetAttributesBag()
        {
            var bag = this.lazyCustomAttributesBag;
            if (bag != null && bag.IsSealed)
            {
                return bag;
            }

            if (LoadAndValidateAttributes(OneOrMany.Create(this.AttributeDeclarationSyntaxList), ref lazyCustomAttributesBag))
            {
                state.NotePartComplete(CompletionPart.Attributes);
            }

            Debug.Assert(lazyCustomAttributesBag.IsSealed);
            return this.lazyCustomAttributesBag;
        }

        /// <summary>
        /// Gets the attributes applied on this symbol.
        /// Returns an empty array if there are no attributes.
        /// </summary>
        /// <remarks>
        /// NOTE: This method should always be kept as a sealed override.
        /// If you want to override attribute binding logic for a sub-class, then override <see cref="GetAttributesBag"/> method.
        /// </remarks>
        public sealed override ImmutableArray<CSharpAttributeData> GetAttributes()
        {
            return this.GetAttributesBag().Attributes;
        }

        /// <summary>
        /// Returns data decoded from well-known attributes applied to the symbol or null if there are no applied attributes.
        /// </summary>
        /// <remarks>
        /// Forces binding and decoding of attributes.
        /// </remarks>
        internal CommonFieldWellKnownAttributeData GetDecodedWellKnownAttributeData()
        {
            var attributesBag = this.lazyCustomAttributesBag;
            if (attributesBag == null || !attributesBag.IsDecodedWellKnownAttributeDataComputed)
            {
                attributesBag = this.GetAttributesBag();
            }

            return (CommonFieldWellKnownAttributeData)attributesBag.DecodedWellKnownAttributeData;
        }

        /// <summary>
        /// Returns data decoded from special early bound well-known attributes applied to the symbol or null if there are no applied attributes.
        /// </summary>
        /// <remarks>
        /// Forces binding and decoding of attributes.
        /// </remarks>
        internal CommonFieldEarlyWellKnownAttributeData GetEarlyDecodedWellKnownAttributeData()
        {
            var attributesBag = this.lazyCustomAttributesBag;
            if (attributesBag == null || !attributesBag.IsEarlyDecodedWellKnownAttributeDataComputed)
            {
                attributesBag = this.GetAttributesBag();
            }

            return (CommonFieldEarlyWellKnownAttributeData)attributesBag.EarlyDecodedWellKnownAttributeData;
        }

        internal sealed override CSharpAttributeData EarlyDecodeWellKnownAttribute(ref EarlyDecodeWellKnownAttributeArguments<EarlyWellKnownAttributeBinder, NamedTypeSymbol, AttributeSyntax, AttributeLocation> arguments)
        {
            CSharpAttributeData boundAttribute;
            ObsoleteAttributeData obsoleteData;

            if (EarlyDecodeDeprecatedOrObsoleteAttribute(ref arguments, out boundAttribute, out obsoleteData))
            {
                if (obsoleteData != null)
                {
                    arguments.GetOrCreateData<CommonFieldEarlyWellKnownAttributeData>().ObsoleteAttributeData = obsoleteData;
                }

                return boundAttribute;
            }

            return base.EarlyDecodeWellKnownAttribute(ref arguments);
        }

        /// <summary>
        /// Returns data decoded from Obsolete attribute or null if there is no Obsolete attribute.
        /// This property returns ObsoleteAttributeData.Uninitialized if attribute arguments haven't been decoded yet.
        /// </summary>
        internal sealed override ObsoleteAttributeData ObsoleteAttributeData
        {
            get
            {
                if (!this.containingType.AnyMemberHasAttributes)
                {
                    return null;
                }

                var lazyCustomAttributesBag = this.lazyCustomAttributesBag;
                if (lazyCustomAttributesBag != null && lazyCustomAttributesBag.IsEarlyDecodedWellKnownAttributeDataComputed)
                {
                    var data = (CommonFieldEarlyWellKnownAttributeData)lazyCustomAttributesBag.EarlyDecodedWellKnownAttributeData;
                    return data != null ? data.ObsoleteAttributeData : null;
                }

                return ObsoleteAttributeData.Uninitialized;
            }
        }

        internal sealed override void DecodeWellKnownAttribute(ref DecodeWellKnownAttributeArguments<AttributeSyntax, CSharpAttributeData, AttributeLocation> arguments)
        {
            Debug.Assert((object)arguments.AttributeSyntaxOpt != null);

            var attribute = arguments.Attribute;
            Debug.Assert(!attribute.HasErrors);
            Debug.Assert(arguments.SymbolPart == AttributeLocation.None);

            if (attribute.IsTargetAttribute(this, AttributeDescription.SpecialNameAttribute))
            {
                arguments.GetOrCreateData<CommonFieldWellKnownAttributeData>().HasSpecialNameAttribute = true;
            }
            else if (attribute.IsTargetAttribute(this, AttributeDescription.NonSerializedAttribute))
            {
                arguments.GetOrCreateData<CommonFieldWellKnownAttributeData>().HasNonSerializedAttribute = true;
            }
            else if (attribute.IsTargetAttribute(this, AttributeDescription.FieldOffsetAttribute))
            {
                if (this.IsStatic || this.IsConst)
                {
                    // CS0637: The FieldOffset attribute is not allowed on static or const fields
                    arguments.Diagnostics.Add(ErrorCode.ERR_StructOffsetOnBadField, arguments.AttributeSyntaxOpt.Name.Location, arguments.AttributeSyntaxOpt.GetErrorDisplayName());
                }
                else
                {
                    int offset = attribute.CommonConstructorArguments[0].DecodeValue<int>(SpecialType.System_Int32);
                    if (offset < 0)
                    {
                        // Dev10 reports CS0647: "Error emitting attribute ..."
                        CSharpSyntaxNode attributeArgumentSyntax = attribute.GetAttributeArgumentSyntax(0, arguments.AttributeSyntaxOpt);
                        arguments.Diagnostics.Add(ErrorCode.ERR_InvalidAttributeArgument, attributeArgumentSyntax.Location, arguments.AttributeSyntaxOpt.GetErrorDisplayName());
                        offset = 0;
                    }

                    // Set field offset even if the attribute specifies an invalid value, so that
                    // post-validation knows that the attribute is applied and reports better errors.
                    arguments.GetOrCreateData<CommonFieldWellKnownAttributeData>().SetFieldOffset(offset);
                }
            }
            else if (attribute.IsTargetAttribute(this, AttributeDescription.MarshalAsAttribute))
            {
                MarshalAsAttributeDecoder<CommonFieldWellKnownAttributeData, AttributeSyntax, CSharpAttributeData, AttributeLocation>.Decode(ref arguments, AttributeTargets.Field, MessageProvider.Instance);
            }
            else if (attribute.IsTargetAttribute(this, AttributeDescription.FixedBufferAttribute))
            {
                // error CS1716: Do not use 'System.Runtime.CompilerServices.FixedBuffer' attribute. Use the 'fixed' field modifier instead.
                arguments.Diagnostics.Add(ErrorCode.ERR_DoNotUseFixedBufferAttr, arguments.AttributeSyntaxOpt.Name.Location);
            }
            else if (attribute.IsTargetAttribute(this, AttributeDescription.DynamicAttribute))
            {
                // DynamicAttribute should not be set explicitly.
                arguments.Diagnostics.Add(ErrorCode.ERR_ExplicitDynamicAttr, arguments.AttributeSyntaxOpt.Location);
            }
            else if (attribute.IsTargetAttribute(this, AttributeDescription.DateTimeConstantAttribute))
            {
                VerifyConstantValueMatches(attribute.DecodeDateTimeConstantValue(), ref arguments);
            }
            else if (attribute.IsTargetAttribute(this, AttributeDescription.DecimalConstantAttribute))
            {
                VerifyConstantValueMatches(attribute.DecodeDecimalConstantValue(), ref arguments);
            }
        }

        /// <summary>
        /// Verify the constant value matches the default value from any earlier attribute
        /// (DateTimeConstantAttribute or DecimalConstantAttribute).
        /// If not, report ERR_FieldHasMultipleDistinctConstantValues.
        /// </summary>
        private void VerifyConstantValueMatches(ConstantValue attrValue, ref DecodeWellKnownAttributeArguments<AttributeSyntax, CSharpAttributeData, AttributeLocation> arguments)
        {
            if (!attrValue.IsBad)
            {
                var data = arguments.GetOrCreateData<CommonFieldWellKnownAttributeData>();
                ConstantValue constValue;

                if (this.IsConst)
                {
                    if (this.Type.SpecialType == SpecialType.System_Decimal)
                    {
                        constValue = this.GetConstantValue(ConstantFieldsInProgress.Empty, earlyDecodingWellKnownAttributes: false);

                        if ((object)constValue != null && !constValue.IsBad && constValue != attrValue)
                        {
                            arguments.Diagnostics.Add(ErrorCode.ERR_FieldHasMultipleDistinctConstantValues, arguments.AttributeSyntaxOpt.Location);
                        }
                    }
                    else
                    {
                        arguments.Diagnostics.Add(ErrorCode.ERR_FieldHasMultipleDistinctConstantValues, arguments.AttributeSyntaxOpt.Location);
                    }

                    if (data.ConstValue == CodeAnalysis.ConstantValue.Unset)
                    {
                        data.ConstValue = attrValue;
                    }
                }
                else
                {
                    constValue = data.ConstValue;

                    if (constValue != CodeAnalysis.ConstantValue.Unset)
                    {
                        if (constValue != attrValue)
                        {
                            arguments.Diagnostics.Add(ErrorCode.ERR_FieldHasMultipleDistinctConstantValues, arguments.AttributeSyntaxOpt.Location);
                        }
                    }
                    else
                    {
                        data.ConstValue = attrValue;
                    }
                }
            }
        }

        internal override void PostDecodeWellKnownAttributes(ImmutableArray<CSharpAttributeData> boundAttributes, ImmutableArray<AttributeSyntax> allAttributeSyntaxNodes, DiagnosticBag diagnostics, AttributeLocation symbolPart, WellKnownAttributeData decodedData)
        {
            Debug.Assert(!boundAttributes.IsDefault);
            Debug.Assert(!allAttributeSyntaxNodes.IsDefault);
            Debug.Assert(boundAttributes.Length == allAttributeSyntaxNodes.Length);
            Debug.Assert(this.lazyCustomAttributesBag != null);
            Debug.Assert(this.lazyCustomAttributesBag.IsDecodedWellKnownAttributeDataComputed);
            Debug.Assert(symbolPart == AttributeLocation.None);

            var data = (CommonFieldWellKnownAttributeData)decodedData;
            int? fieldOffset = data != null ? data.Offset : null;

            if (fieldOffset.HasValue)
            {
                if (this.ContainingType.Layout.Kind != LayoutKind.Explicit)
                {
                    Debug.Assert(boundAttributes.Any());

                    // error CS0636: The FieldOffset attribute can only be placed on members of types marked with the StructLayout(LayoutKind.Explicit)
                    int i = boundAttributes.IndexOfAttribute(this, AttributeDescription.FieldOffsetAttribute);
                    diagnostics.Add(ErrorCode.ERR_StructOffsetOnBadStruct, allAttributeSyntaxNodes[i].Name.Location);
                }
            }
            else if (!this.IsStatic && !this.IsConst)
            {
                if (this.ContainingType.Layout.Kind == LayoutKind.Explicit)
                {
                    // error CS0625: '<field>': instance field types marked with StructLayout(LayoutKind.Explicit) must have a FieldOffset attribute
                    diagnostics.Add(ErrorCode.ERR_MissingStructOffset, this.Location, this);
                }
            }

            base.PostDecodeWellKnownAttributes(boundAttributes, allAttributeSyntaxNodes, diagnostics, symbolPart, decodedData);
        }

        internal override void AddSynthesizedAttributes(ref ArrayBuilder<SynthesizedAttributeData> attributes)
        {
            base.AddSynthesizedAttributes(ref attributes);

            if (this.Type.ContainsDynamic())
            {
                var compilation = this.DeclaringCompilation;
                AddSynthesizedAttribute(ref attributes, compilation.SynthesizeDynamicAttribute(this.Type, this.CustomModifiers.Length));
            }
        }

        internal sealed override bool HasSpecialName
        {
            get
            {
                if (this.HasRuntimeSpecialName)
                {
                    return true;
                }

                var data = GetDecodedWellKnownAttributeData();
                return data != null && data.HasSpecialNameAttribute;
            }
        }

        internal sealed override bool HasRuntimeSpecialName
        {
            get
            {
                return this.Name == WellKnownMemberNames.EnumBackingFieldName;
            }
        }

        internal sealed override bool IsNotSerialized
        {
            get
            {
                var data = GetDecodedWellKnownAttributeData();
                return data != null && data.HasNonSerializedAttribute;
            }
        }

        internal sealed override MarshalPseudoCustomAttributeData MarshallingInformation
        {
            get
            {
                var data = GetDecodedWellKnownAttributeData();
                return data != null ? data.MarshallingInformation : null;
            }
        }

        internal sealed override int? TypeLayoutOffset
        {
            get
            {
                var data = GetDecodedWellKnownAttributeData();
                return data != null ? data.Offset : null;
            }
        }

        internal sealed override ConstantValue GetConstantValue(ConstantFieldsInProgress inProgress, bool earlyDecodingWellKnownAttributes)
        {
            var value = this.GetLazyConstantValue(earlyDecodingWellKnownAttributes);
            if (value != Microsoft.CodeAnalysis.ConstantValue.Unset)
            {
                return value;
            }

            if (!inProgress.IsEmpty)
            {
                // Add this field as a dependency of the original field, and
                // return Unset. The outer GetConstantValue caller will call
                // this method again after evaluating any dependencies.
                inProgress.AddDependency(this);
                return Microsoft.CodeAnalysis.ConstantValue.Unset;
            }

            // Order dependencies.
            var order = ArrayBuilder<ConstantEvaluationHelpers.FieldInfo>.GetInstance();
            this.OrderAllDependencies(order, earlyDecodingWellKnownAttributes);

            // Evaluate fields in order.
            foreach (var info in order)
            {
                // Bind the field value regardless of whether the field represents
                // the start of a cycle. In the cycle case, there will be unevaluated
                // dependencies and the result will be ConstantValue.Bad plus cycle error.
                var field = info.Field;
                field.BindConstantValueIfNecessary(earlyDecodingWellKnownAttributes, startsCycle: info.StartsCycle);
            }

            order.Free();

            // Return the value of this field.
            return this.GetLazyConstantValue(earlyDecodingWellKnownAttributes);
        }

        /// <summary>
        /// Return the constant value dependencies. Compute the dependencies
        /// if necessary by evaluating the constant value but only persist the
        /// constant value if there were no dependencies. (If there are dependencies,
        /// the constant value will be re-evaluated after evaluating dependencies.)
        /// </summary>
        internal ImmutableHashSet<SourceFieldSymbol> GetConstantValueDependencies(bool earlyDecodingWellKnownAttributes)
        {
            var value = this.GetLazyConstantValue(earlyDecodingWellKnownAttributes);
            if (value != Microsoft.CodeAnalysis.ConstantValue.Unset)
            {
                // Constant value already determined. No need to
                // compute dependencies since the constant values
                // of all dependencies should be evaluated as well.
                return ImmutableHashSet<SourceFieldSymbol>.Empty;
            }

            ImmutableHashSet<SourceFieldSymbol> dependencies;
            var builder = PooledHashSet<SourceFieldSymbol>.GetInstance();
            var diagnostics = DiagnosticBag.GetInstance();
            value = MakeConstantValue(builder, earlyDecodingWellKnownAttributes, diagnostics);

            // Only persist if there are no dependencies and the calculation
            // completed successfully. (We could probably persist in other
            // scenarios but it's probably not worth the added complexity.)
            if ((builder.Count == 0) &&
                (value != null) &&
                !value.IsBad &&
                (value != Microsoft.CodeAnalysis.ConstantValue.Unset) &&
                diagnostics.IsEmptyWithoutResolution)
            {
                this.SetLazyConstantValue(
                    value,
                    earlyDecodingWellKnownAttributes,
                    diagnostics,
                    startsCycle: false);
                dependencies = ImmutableHashSet<SourceFieldSymbol>.Empty;
            }
            else
            {
                dependencies = ImmutableHashSet<SourceFieldSymbol>.Empty.Union(builder);
            }

            diagnostics.Free();
            builder.Free();
            return dependencies;
        }

        private void BindConstantValueIfNecessary(bool earlyDecodingWellKnownAttributes, bool startsCycle)
        {
            if (this.GetLazyConstantValue(earlyDecodingWellKnownAttributes) != Microsoft.CodeAnalysis.ConstantValue.Unset)
            {
                return;
            }

            var builder = PooledHashSet<SourceFieldSymbol>.GetInstance();
            var diagnostics = DiagnosticBag.GetInstance();
            if (startsCycle)
            {
                diagnostics.Add(ErrorCode.ERR_CircConstValue, this.location, this);
            }

            var value = MakeConstantValue(builder, earlyDecodingWellKnownAttributes, diagnostics);
            this.SetLazyConstantValue(
                value,
                earlyDecodingWellKnownAttributes,
                diagnostics,
                startsCycle);
            diagnostics.Free();
            builder.Free();
        }

        private ConstantValue GetLazyConstantValue(bool earlyDecodingWellKnownAttributes)
        {
            return earlyDecodingWellKnownAttributes ? this.lazyConstantEarlyDecodingValue : this.lazyConstantValue;
        }

        private void SetLazyConstantValue(
            ConstantValue value,
            bool earlyDecodingWellKnownAttributes,
            DiagnosticBag diagnostics,
            bool startsCycle)
        {
            Debug.Assert(value != Microsoft.CodeAnalysis.ConstantValue.Unset);
            Debug.Assert((GetLazyConstantValue(earlyDecodingWellKnownAttributes) == Microsoft.CodeAnalysis.ConstantValue.Unset) ||
                (GetLazyConstantValue(earlyDecodingWellKnownAttributes) == value));

            if (earlyDecodingWellKnownAttributes)
            {
                Interlocked.CompareExchange(ref this.lazyConstantEarlyDecodingValue, value, Microsoft.CodeAnalysis.ConstantValue.Unset);
            }
            else
            {
                if (Interlocked.CompareExchange(ref this.lazyConstantValue, value, Microsoft.CodeAnalysis.ConstantValue.Unset) == Microsoft.CodeAnalysis.ConstantValue.Unset)
                {
#if REPORT_ALL
                    Console.WriteLine("Thread {0}, Field {1}, StartsCycle {2}", Thread.CurrentThread.ManagedThreadId, this, startsCycle);
#endif
                    this.AddSemanticDiagnostics(diagnostics);
                    this.state.NotePartComplete(CompletionPart.ConstantValue);
                }
            }
        }

        protected abstract ConstantValue MakeConstantValue(HashSet<SourceFieldSymbol> dependencies, bool earlyDecodingWellKnownAttributes, DiagnosticBag diagnostics);
    }
}
