﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
namespace Microsoft.CodeAnalysis.CSharp
{
    /// <summary>
    /// This binder keeps track of the set of constant fields that are currently being evaluated
    /// so that the set can be passed into the next call to SourceFieldSymbol.ConstantValue (and
    /// its callers).
    /// </summary>
    internal sealed class ConstantFieldsInProgressBinder : Binder
    {
        private readonly ConstantFieldsInProgress inProgress;

        internal ConstantFieldsInProgressBinder(ConstantFieldsInProgress inProgress, Binder next)
            : base(next, BinderFlags.FieldInitializer | next.Flags)
        {
            this.inProgress = inProgress;
        }

        internal override ConstantFieldsInProgress ConstantFieldsInProgress
        {
            get
            {
                return inProgress;
            }
        }
    }
}