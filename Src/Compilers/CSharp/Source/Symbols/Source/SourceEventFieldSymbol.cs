﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
namespace Microsoft.CodeAnalysis.CSharp.Symbols
{
    /// <summary>
    /// A delegate field associated with a <see cref="SourceFieldLikeEventSymbol"/>.
    /// </summary>
    /// <remarks>
    /// SourceFieldSymbol takes care of the initializer (plus "var" in the interactive case).
    /// </remarks>
    internal sealed class SourceEventFieldSymbol : SourceMemberFieldSymbol
    {
        private readonly SourceEventSymbol associatedEvent;

        internal SourceEventFieldSymbol(SourceEventSymbol associatedEvent, VariableDeclaratorSyntax declaratorSyntax, DiagnosticBag discardedDiagnostics)
            : base(associatedEvent.containingType, declaratorSyntax, associatedEvent.Modifiers, modifierErrors: true, diagnostics: discardedDiagnostics)
        {
            this.associatedEvent = associatedEvent;
        }

        public override bool IsImplicitlyDeclared
        {
            get
            {
                return true;
            }
        }

        protected override IAttributeTargetSymbol AttributeOwner
        {
            get
            {
                return this.associatedEvent;
            }
        }

        public override Symbol AssociatedPropertyOrEvent
        {
            get
            {
                return this.associatedEvent;
            }
        }

        public override Accessibility DeclaredAccessibility
        {
            get
            {
                return Accessibility.Private;
            }
        }

        internal override void AddSynthesizedAttributes(ref ArrayBuilder<SynthesizedAttributeData> attributes)
        {
            base.AddSynthesizedAttributes(ref attributes);

            var compilation = this.DeclaringCompilation;
            AddSynthesizedAttribute(ref attributes, compilation.SynthesizeAttribute(WellKnownMember.System_Runtime_CompilerServices_CompilerGeneratedAttribute__ctor));

            // Dev11 doesn't synthesize this attribute, the debugger has a knowledge 
            // of special name C# compiler uses for backing fields, which is not desirable.
            AddSynthesizedAttribute(ref attributes, compilation.SynthesizeDebuggerBrowsableNeverAttribute());
        }
    }
}
