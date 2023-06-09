﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp
{
    /// <summary>
    /// This portion of the binder converts a AnonymousObjectCreationExpressionSyntax into 
    /// a bound anonymous object creation node
    /// </summary>
    internal abstract partial class Binder
    {
        private BoundExpression BindAnonymousObjectCreation(AnonymousObjectCreationExpressionSyntax node, DiagnosticBag diagnostics)
        {
            //  prepare
            var initializers = node.Initializers;
            int fieldCount = initializers.Count;
            bool hasError = false;

            //  bind field initializers
            BoundExpression[] boundExpressions = new BoundExpression[fieldCount];
            AnonymousTypeField[] fields = new AnonymousTypeField[fieldCount];
            CSharpSyntaxNode[] fieldSyntaxNodes = new CSharpSyntaxNode[fieldCount];

            // WARNING: Note that SemanticModel.GetDeclaredSymbol for field initializer node relies on 
            //          the fact that the order of properties in anonymous type template corresponds 
            //          1-to-1 to the appropriate filed initializer syntax nodes; This means such 
            //          correspondence must be preserved all the time including erroneos scenarios

            // set of names already used
            HashSet<string> uniqueFieldNames = new HashSet<string>();

            for (int i = 0; i < fieldCount; i++)
            {
                AnonymousObjectMemberDeclaratorSyntax fieldInitializer = initializers[i];
                NameEqualsSyntax nameEquals = fieldInitializer.NameEquals;
                ExpressionSyntax expression = fieldInitializer.Expression;

                SyntaxToken nameToken = default(SyntaxToken);
                if (nameEquals != null)
                {
                    nameToken = nameEquals.Name.Identifier;
                }
                else
                {
                    nameToken = expression.ExtractAnonymousTypeMemberName();
                }

                hasError = hasError || expression.HasErrors;
                boundExpressions[i] = this.BindValue(expression, diagnostics, BindValueKind.RValue);

                //  check the name to be unique
                string fieldName = null;
                if (nameToken.CSharpKind() == SyntaxKind.IdentifierToken)
                {
                    fieldName = nameToken.ValueText;
                    if (uniqueFieldNames.Contains(fieldName))
                    {
                        //  name duplication
                        Error(diagnostics, ErrorCode.ERR_AnonymousTypeDuplicatePropertyName, fieldInitializer);
                        hasError = true;
                        fieldName = null;
                    }
                    else
                    {
                        uniqueFieldNames.Add(fieldName);
                    }
                }
                else
                {
                    // there is something wrong with field's name
                    hasError = true;
                }

                //  calculate the expression's type and report errors if needed
                TypeSymbol fieldType = GetAnonymousTypeFieldType(boundExpressions[i], fieldInitializer, diagnostics, ref hasError);

                // build anonymous type field descriptor
                fieldSyntaxNodes[i] = (nameToken.CSharpKind() == SyntaxKind.IdentifierToken) ? (CSharpSyntaxNode)nameToken.Parent : fieldInitializer;
                fields[i] = new AnonymousTypeField(fieldName == null ? '$' + i.ToString() : fieldName, fieldSyntaxNodes[i].Location, fieldType);

                //  NOTE: ERR_InvalidAnonymousTypeMemberDeclarator (CS0746) would be generated by parser if needed
            }

            //  Create anonymous type 
            AnonymousTypeManager manager = this.Compilation.AnonymousTypeManager;
            AnonymousTypeDescriptor descriptor = new AnonymousTypeDescriptor(fields.AsImmutableOrNull(), node.NewKeyword.GetLocation());
            NamedTypeSymbol anonymousType = manager.ConstructAnonymousTypeSymbol(descriptor);

            // declarators - bound nodes created for providing semantic info 
            // on anonymous type fields having explicitly specified name
            ArrayBuilder<BoundAnonymousPropertyDeclaration> declarators =
                ArrayBuilder<BoundAnonymousPropertyDeclaration>.GetInstance();
            for (int i = 0; i < fieldCount; i++)
            {
                NameEqualsSyntax explicitName = initializers[i].NameEquals;
                if (explicitName != null)
                {
                    AnonymousTypeField field = fields[i];
                    if (field.Name != null)
                    {
                        //  get property symbol and create a bound property declaration node
                        foreach (var symbol in anonymousType.GetMembers(field.Name))
                        {
                            if (symbol.Kind == SymbolKind.Property)
                            {
                                declarators.Add(new BoundAnonymousPropertyDeclaration(fieldSyntaxNodes[i], (PropertySymbol)symbol, field.Type));
                                break;
                            }
                        }
                    }
                }
            }

            // check if anonymous object creation is allowed in this context
            if (!this.IsAnonymousTypesAllowed())
            {
                Error(diagnostics, ErrorCode.ERR_AnonymousTypeNotAvailable, node.NewKeyword);
                hasError = true;
            }

            //  Finally create a bound node
            return new BoundAnonymousObjectCreationExpression(
                node,
                anonymousType.InstanceConstructors[0],
                boundExpressions.AsImmutableOrNull(),
                declarators.ToImmutableAndFree(),
                anonymousType,
                hasError);
        }

        /// <summary>
        /// Actually, defines if an error ERR_AnonymousTypeNotAvailable is to be generated; 
        /// 
        /// Dev10 rules (which are based on BindingContext::InMethod()) are difficult to 
        /// reproduce, so this implementation checks both current symbol as well as syntax nodes.
        /// </summary>
        private bool IsAnonymousTypesAllowed()
        {
            if ((object)this.ContainingMemberOrLambda == null)
            {
                return false;
            }

            switch (this.ContainingMemberOrLambda.Kind)
            {
                case SymbolKind.Method:
                    return true;

                case SymbolKind.Field:
                    return !((FieldSymbol)this.ContainingMemberOrLambda).IsConst;

                case SymbolKind.NamedType:
                    //  allow usage of anonymous types in script classes
                    return ((NamedTypeSymbol)this.ContainingMemberOrLambda).IsScriptClass;
            }

            return false;
        }

        /// <summary>
        /// Returns the type to be used as a field type; generates errors in case the type is not
        /// supported for anonymous type fields.
        /// </summary>
        private TypeSymbol GetAnonymousTypeFieldType(BoundExpression expression, CSharpSyntaxNode errorSyntax, DiagnosticBag diagnostics, ref bool hasError)
        {
            object errorArg = null;
            TypeSymbol expressionType = expression.Type;

            if (!expression.HasAnyErrors)
            {
                if (expression.HasExpressionType())
                {
                    if (expressionType.SpecialType == SpecialType.System_Void)
                    {
                        errorArg = expressionType;
                        expressionType = CreateErrorType(SyntaxFacts.GetText(SyntaxKind.VoidKeyword));
                    }
                    else if (expressionType.IsUnsafe())
                    {
                        errorArg = expressionType;
                        // CONSIDER: we could use an explicit error type instead of the unsafe type.
                    }
                    else if (expressionType.IsRestrictedType())
                    {
                        errorArg = expressionType;
                    }
                }
                else
                {
                    if (expression.Kind == BoundKind.UnboundLambda)
                    {
                        errorArg = ((UnboundLambda)expression).MessageID.Localize();
                    }
                    else if (expression.Kind == BoundKind.MethodGroup)
                    {
                        errorArg = MessageID.IDS_MethodGroup.Localize();
                    }
                    else
                    {
                        Debug.Assert(expression.IsLiteralNull(), "How did we successfully bind an expression without a type?");
                        errorArg = MessageID.IDS_NULL.Localize();
                    }
                }
            }

            if ((object)expressionType == null)
            {
                expressionType = CreateErrorType("error");
            }

            if (errorArg != null)
            {
                hasError = true;
                Error(diagnostics, ErrorCode.ERR_AnonymousTypePropertyAssignedBadValue, errorSyntax, errorArg);
                // NOTE: ERR_QueryRangeVariableAssignedBadValue is being generated 
                //       by query binding code and never reach this point
            }

            return expressionType;
        }
    }
}
