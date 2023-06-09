﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.FindSymbols.Finders
{
    internal class EventSymbolReferenceFinder : AbstractMethodOrPropertyOrEventSymbolReferenceFinder<IEventSymbol>
    {
        protected override bool CanFind(IEventSymbol symbol)
        {
            return true;
        }

        protected override async Task<IEnumerable<ISymbol>> DetermineCascadedSymbolsAsync(IEventSymbol symbol, Solution solution, IImmutableSet<Project> projects, CancellationToken cancellationToken)
        {
            var baseSymbols = await base.DetermineCascadedSymbolsAsync(symbol, solution, projects, cancellationToken).ConfigureAwait(false);
            baseSymbols = baseSymbols ?? SpecializedCollections.EmptyEnumerable<ISymbol>();
            var backingField = symbol.ContainingType.GetMembers().OfType<IFieldSymbol>().Where(f => symbol.Equals(f.AssociatedPropertyOrEvent));
            var associatedNamedType = symbol.ContainingType.GetTypeMembers().Where(n => symbol.Equals(n.AssociatedEvent));

            return baseSymbols.Concat(backingField).Concat(associatedNamedType);
        }

        protected override Task<IEnumerable<Document>> DetermineDocumentsToSearchAsync(
            IEventSymbol symbol,
            Project project,
            IImmutableSet<Document> documents,
            CancellationToken cancellationToken)
        {
            return FindDocumentsAsync(project, documents, cancellationToken, symbol.Name);
        }

        protected override Task<IEnumerable<ReferenceLocation>> FindReferencesInDocumentAsync(
            IEventSymbol symbol,
            Document document,
            CancellationToken cancellationToken)
        {
            return FindReferencesInDocumentUsingSymbolNameAsync(symbol, document, cancellationToken);
        }
    }
}