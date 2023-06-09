﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.CodeGen;
using Microsoft.CodeAnalysis.CSharp.Emit;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Cci = Microsoft.Cci;

namespace Microsoft.CodeAnalysis.CSharp.Symbols
{
    internal abstract partial class CSharpAttributeData : Cci.ICustomAttribute
    {
        IEnumerable<Cci.IMetadataExpression> Cci.ICustomAttribute.GetArguments(Microsoft.CodeAnalysis.Emit.Context context)
        {
            foreach (var argument in this.CommonConstructorArguments)
            {
                Debug.Assert(argument.Kind != TypedConstantKind.Error);

                yield return CreateMetadataExpression(argument, context);
            }
        }

        Cci.IMethodReference Cci.ICustomAttribute.Constructor(Microsoft.CodeAnalysis.Emit.Context context)
        {
            PEModuleBuilder moduleBeingBuilt = (PEModuleBuilder)context.Module;
            return (Cci.IMethodReference)moduleBeingBuilt.Translate(this.AttributeConstructor, (CSharpSyntaxNode)context.SyntaxNodeOpt, context.Diagnostics);
        }

        IEnumerable<Cci.IMetadataNamedArgument> Cci.ICustomAttribute.GetNamedArguments(Microsoft.CodeAnalysis.Emit.Context context)
        {
            foreach (var namedArgument in this.CommonNamedArguments)
            {
                yield return CreateMetadataNamedArgument(namedArgument.Key, namedArgument.Value, context);
            }
        }

        int Cci.ICustomAttribute.ArgumentCount
        {
            get
            {
                return this.CommonConstructorArguments.Length;
            }
        }

        ushort Cci.ICustomAttribute.NamedArgumentCount
        {
            get
            {
                return (ushort)this.CommonNamedArguments.Length;
            }
        }

        Cci.ITypeReference Cci.ICustomAttribute.GetType(Microsoft.CodeAnalysis.Emit.Context context)
        {
            PEModuleBuilder moduleBeingBuilt = (PEModuleBuilder)context.Module;
            return moduleBeingBuilt.Translate(this.AttributeClass, syntaxNodeOpt: (CSharpSyntaxNode)context.SyntaxNodeOpt, diagnostics: context.Diagnostics);
        }

        bool Cci.ICustomAttribute.AllowMultiple
        {
            get { return this.AttributeClass.GetAttributeUsageInfo().AllowMultiple; }
        }

        private Cci.IMetadataExpression CreateMetadataExpression(TypedConstant argument, Microsoft.CodeAnalysis.Emit.Context context)
        {
            if (argument.IsNull)
            {
                return CreateMetadataConstant(argument.Type, null, context);
            }

            switch (argument.Kind)
            {
                case TypedConstantKind.Array:
                    return CreateMetadataArray(argument, context);

                case TypedConstantKind.Type:
                    return CreateType(argument, context);

                default:
                    return CreateMetadataConstant(argument.Type, argument.Value, context);
            }
        }

        private MetadataCreateArray CreateMetadataArray(TypedConstant argument, Microsoft.CodeAnalysis.Emit.Context context)
        {
            Debug.Assert(!argument.Values.IsDefault);
            var values = argument.Values;
            var arrayType = Emit.PEModuleBuilder.Translate((ArrayTypeSymbol)argument.Type);

            if (values.Length == 0)
            {
                return new MetadataCreateArray(arrayType,
                                               arrayType.GetElementType(context),
                                               ImmutableArray<Cci.IMetadataExpression>.Empty);
            }

            var metadataExprs = new Cci.IMetadataExpression[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                metadataExprs[i] = CreateMetadataExpression(values[i], context);
            }

            return new MetadataCreateArray(arrayType,
                                           arrayType.GetElementType(context),
                                           metadataExprs.AsImmutableOrNull());
        }

        private static MetadataTypeOf CreateType(TypedConstant argument, Microsoft.CodeAnalysis.Emit.Context context)
        {
            Debug.Assert(argument.Value != null);
            var moduleBeingBuilt = (PEModuleBuilder)context.Module;
            var syntaxNodeOpt = (CSharpSyntaxNode)context.SyntaxNodeOpt;
            var diagnostics = context.Diagnostics;
            return new MetadataTypeOf(moduleBeingBuilt.Translate((TypeSymbol)argument.Value, syntaxNodeOpt, diagnostics),
                                      moduleBeingBuilt.Translate((TypeSymbol)argument.Type, syntaxNodeOpt, diagnostics));
        }

        private static MetadataConstant CreateMetadataConstant(ITypeSymbol type, object value, Microsoft.CodeAnalysis.Emit.Context context)
        {
            PEModuleBuilder moduleBeingBuilt = (PEModuleBuilder)context.Module;
            return moduleBeingBuilt.CreateConstant((TypeSymbol)type, value, syntaxNodeOpt: (CSharpSyntaxNode)context.SyntaxNodeOpt, diagnostics: context.Diagnostics);
        }

        private Cci.IMetadataNamedArgument CreateMetadataNamedArgument(string name, TypedConstant argument, Microsoft.CodeAnalysis.Emit.Context context)
        {
            var symbol = LookupName(name);
            var value = CreateMetadataExpression(argument, context);
            TypeSymbol type;
            var fieldSymbol = symbol as FieldSymbol;
            if ((object)fieldSymbol != null)
            {
                type = fieldSymbol.Type;
            }
            else
            {
                type = ((PropertySymbol)symbol).Type;
            }

            PEModuleBuilder moduleBeingBuilt = (PEModuleBuilder)context.Module;
            return new MetadataNamedArgument(symbol, moduleBeingBuilt.Translate(type, syntaxNodeOpt: (CSharpSyntaxNode)context.SyntaxNodeOpt, diagnostics: context.Diagnostics), value);
        }

        private Symbol LookupName(string name)
        {
            var type = this.AttributeClass;
            while ((object)type != null)
            {
                foreach (var member in type.GetMembers(name))
                {
                    if (member.DeclaredAccessibility == Accessibility.Public)
                    {
                        return member;
                    }
                }
                type = type.BaseTypeNoUseSiteDiagnostics;
            }

            Debug.Assert(false, "Name does not match an attribute field or a property.  How can that be?");
            return null;
        }
    }
}
