﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp
{
    internal abstract partial class ConversionsBase
    {
        /// <remarks>
        /// NOTE: Keep this method in sync with AnalyzeImplicitUserDefinedConversionForSwitchGoverningType.
        /// </remarks>
        private UserDefinedConversionResult AnalyzeImplicitUserDefinedConversions(
            BoundExpression sourceExpression,
            TypeSymbol source,
            TypeSymbol target,
            ref HashSet<DiagnosticInfo> useSiteDiagnostics)
        {
            Debug.Assert(sourceExpression != null || (object)source != null);
            Debug.Assert((object)target != null);

            // User-defined conversions that involve generics can be quite strange. There
            // are two basic problems: first, that generic user-defined conversions can be
            // "shadowed" by built-in conversions, and second, that generic user-defined
            // conversions can make conversions that would never have been legal user-defined
            // conversions if declared non-generically. I call this latter kind of conversion
            // a "suspicious" conversion.
            //
            // The shadowed conversions are easily dealt with:
            //
            // SPEC: If a predefined implicit conversion exists from a type S to type T,
            // SPEC: all user-defined conversions, implicit or explicit, are ignored.
            // SPEC: If a predefined explicit conversion exists from a type S to type T,
            // SPEC: any user-defined explicit conversion from S to T are ignored.
            //
            // The rule above can come into play in cases like:
            //
            // sealed class C<T> { public static implicit operator T(C<T> c) { ... } }
            // C<object> c = whatever;
            // object o = c;
            //
            // The built-in implicit conversion from C<object> to object must shadow
            // the user-defined implicit conversion.
            //
            // The caller of this method checks for user-defined conversions *after*
            // predefined implicit conversions, so we already know that if we got here,
            // there was no predefined implicit conversion. 
            //
            // Note that a user-defined *implicit* conversion may win over a built-in
            // *explicit* conversion by the rule given above. That is, if we created
            // an implicit conversion from T to C<T>, then the user-defined implicit 
            // conversion from object to C<object> could be valid, even though that
            // would be "replacing" a built-in explicit conversion with a user-defined
            // implicit conversion. This is one of the "suspicious" conversions,
            // as it would not be legal to declare a user-defined conversion from
            // object in a non-generic type.
            //
            // The way the native compiler handles suspicious conversions involving
            // interfaces is neither sensible nor in line with the rules in the 
            // specification. It is not clear at this time whether we should be exactly
            // matching the native compiler, the specification, or neither, in Roslyn.

            // Spec (6.4.4 User-defined implicit conversions)
            //   A user-defined implicit conversion from an expression E to type T is processed as follows:

            // SPEC: Find the set of types D from which user-defined conversion operators...
            var d = ArrayBuilder<NamedTypeSymbol>.GetInstance();
            ComputeUserDefinedImplicitConversionTypeSet(source, target, d, ref useSiteDiagnostics);

            // SPEC: Find the set of applicable user-defined and lifted conversion operators, U...
            var ubuild = ArrayBuilder<UserDefinedConversionAnalysis>.GetInstance();
            ComputeApplicableUserDefinedImplicitConversionSet(sourceExpression, source, target, d, ubuild, ref useSiteDiagnostics);
            d.Free();
            ImmutableArray<UserDefinedConversionAnalysis> u = ubuild.ToImmutableAndFree();

            // SPEC: If U is empty, the conversion is undefined and a compile-time error occurs.
            if (u.Length == 0)
            {
                return UserDefinedConversionResult.NoApplicableOperators(u);
            }

            // SPEC: Find the most specific source type SX of the operators in U...
            TypeSymbol sx = MostSpecificSourceTypeForImplicitUserDefinedConversion(u, source, ref useSiteDiagnostics);
            if ((object)sx == null)
            {
                return UserDefinedConversionResult.NoBestSourceType(u);
            }

            // SPEC: Find the most specific target type TX of the operators in U...
            TypeSymbol tx = MostSpecificTargetTypeForImplicitUserDefinedConversion(u, target, ref useSiteDiagnostics);
            if ((object)tx == null)
            {
                return UserDefinedConversionResult.NoBestTargetType(u);
            }

            int? best = MostSpecificConversionOperator(sx, tx, u);
            if (best == null)
            {
                return UserDefinedConversionResult.Ambiguous(u);
            }

            return UserDefinedConversionResult.Valid(u, best.Value);
        }

        private static void ComputeUserDefinedImplicitConversionTypeSet(TypeSymbol s, TypeSymbol t, ArrayBuilder<NamedTypeSymbol> d, ref HashSet<DiagnosticInfo> useSiteDiagnostics)
        {
            // Spec 6.4.4: User-defined implicit conversions
            //   Find the set of types D from which user-defined conversion operators
            //   will be considered. This set consists of S0 (if S0 is a class or struct),
            //   the base classes of S0 (if S0 is a class), and T0 (if T0 is a class or struct).

            TypeSymbol s0 = GetUnderlyingEffectiveType(s, ref useSiteDiagnostics);
            TypeSymbol t0 = GetUnderlyingEffectiveType(t, ref useSiteDiagnostics);

            AddTypesParticipatingInUserDefinedConversion(d, s0, includeBaseTypes: true, useSiteDiagnostics: ref useSiteDiagnostics);
            AddTypesParticipatingInUserDefinedConversion(d, t0, includeBaseTypes: false, useSiteDiagnostics: ref useSiteDiagnostics);
        }

        /// <summary>
        /// This method find the set of applicable user-defined and lifted conversion operators, u.
        /// The set consists of the user-defined and lifted implicit conversion operators declared by
        /// the classes and structs in d that convert from a type encompassing source to a type encompassed by target.
        /// However if allowAnyTarget is true, then it considers all operators that convert from a type encompassing source
        /// to any target. This flag must be set only if we are computing user defined conversions from a given source
        /// type to any target type.
        /// </summary>
        /// <remarks>
        /// Currently allowAnyTarget flag is only set to true by AnalyzeImplicitUserDefinedConversionForSwitchGoverningType,
        /// where we must consider user defined implicit conversions from the type of the switch expression to
        /// any of the possible switch governing types.
        /// </remarks>
        private void ComputeApplicableUserDefinedImplicitConversionSet(
            BoundExpression sourceExpression,
            TypeSymbol source,
            TypeSymbol target,
            ArrayBuilder<NamedTypeSymbol> d,
            ArrayBuilder<UserDefinedConversionAnalysis> u,
            ref HashSet<DiagnosticInfo> useSiteDiagnostics,
            bool allowAnyTarget = false)
        {
            Debug.Assert(sourceExpression != null || (object)source != null);
            Debug.Assert(((object)target != null) == !allowAnyTarget);
            Debug.Assert(d != null);
            Debug.Assert(u != null);

            // SPEC: Find the set of applicable user-defined and lifted conversion operators, U.
            // SPEC: The set consists of the user-defined and lifted implicit conversion operators
            // SPEC: declared by the classes and structs in D that convert from a type encompassing
            // SPEC: E to a type encompassed by T. If U is empty, the conversion is undefined and
            // SPEC: a compile-time error occurs.

            // SPEC: Give a user-defined conversion operator that converts from a non-nullable
            // SPEC: value type S to a non-nullable value type T, a lifted conversion operator
            // SPEC: exists that converts from S? to T?.

            // DELIBERATE SPEC VIOLATION:
            //
            // The spec here essentially says that we add an applicable "regular" conversion and 
            // an applicable lifted conversion, if there is one, to the candidate set, and then
            // let them duke it out to determine which one is "best".
            //
            // This is not at all what the native compiler does, and attempting to implement
            // the specification, or slight variations on it, produces too many backwards-compatibility
            // breaking changes.
            //
            // The native compiler deviates from the specification in two major ways here.
            // First, it does not add *both* the regular and lifted forms to the candidate set.
            // Second, the way it characterizes a "lifted" form is very, very different from
            // how the specification characterizes a lifted form. 
            //
            // An operation, in this case, X-->Y, is properly said to be "lifted" to X?-->Y? via
            // the rule that X?-->Y? matches the behaviour of X-->Y for non-null X, and converts
            // null X to null Y otherwise.
            //
            // The native compiler, by contrast, takes the existing operator and "lifts" either
            // the operator's parameter type or the operator's return type to nullable. For
            // example, a conversion from X?-->Y would be "lifted" to X?-->Y? by making the
            // conversion from X? to Y, and then from Y to Y?.  No "lifting" semantics
            // are imposed; we do not check to see if the X? is null. This operator is not
            // actually "lifted" at all; rather, an implicit conversion is applied to the 
            // output. **The native compiler considers the result type Y? of that standard implicit
            // conversion to be the result type of the "lifted" conversion**, rather than
            // properly considering Y to be the result type of the conversion for the purposes 
            // of computing the best output type.
            //
            // MOREOVER: the native compiler actually *does* implement nullable lifting semantics
            // in the case where the input type of the user-defined conversion is a non-nullable
            // value type and the output type is a nullable value type **or pointer type, or 
            // reference type**. This is an enormous departure from the specification; the
            // native compiler will take a user-defined conversion from X-->Y? or X-->C and "lift"
            // it to a conversion from X?-->Y? or X?-->C that has nullable semantics.
            // 
            // This is quite confusing. In this code we will classify the conversion as either
            // "normal" or "lifted" on the basis of *whether or not special lifting semantics
            // are to be applied*. That is, whether or not a later rewriting pass is going to
            // need to insert a check to see if the source expression is null, and decide
            // whether or not to call the underlying unlifted conversion or produce a null
            // value without calling the unlifted conversion.

            // DELIBERATE SPEC VIOLATION (See bug 17021)
            // The specification defines a type U as "encompassing" a type V
            // if there is a standard implicit conversion from U to V, and
            // neither are interface types.
            //
            // The intention of this language is to ensure that we do not allow user-defined
            // conversions that involve interfaces. We have a reasonable expectation that a
            // conversion that involves an interface is one that preserves referential identity,
            // and user-defined conversions usually do not.
            //
            // Now, suppose we have a standard conversion from Alpha to Beta, a user-defined
            // conversion from Beta to Gamma, and a standard conversion from Gamma to Delta.
            // The specification allows the implicit conversion from Alpha to Delta only if 
            // Beta encompasses Alpha and Delta encompasses Gamma.  And therefore, none of them
            // can be interface types, de jure.
            //
            // However, the dev10 compiler only checks Alpha and Delta to see if they are interfaces,
            // and allows Beta and Gamma to be interfaces. 
            //
            // So what's the big deal there? It's not legal to define a user-defined conversion where
            // the input or output types are interfaces, right?
            //
            // It is not legal to define such a conversion, no, but it is legal to create one via generic
            // construction. If we have a conversion from T to C<T>, then C<I> has a conversion from I to C<I>.
            //
            // The dev10 compiler fails to check for this situation. This means that, 
            // you can convert from int to C<IComparable> because int implements IComparable, but cannot
            // convert from IComparable to C<IComparable>!
            //
            // Unfortunately, we know of several real programs that rely upon this bug, so we are going
            // to reproduce it here.

            if ((object)source != null && source.IsInterfaceType() || (object)target != null && target.IsInterfaceType())
            {
                return;
            }

            foreach (NamedTypeSymbol declaringType in d)
            {
                foreach (MethodSymbol op in declaringType.GetOperators(WellKnownMemberNames.ImplicitConversionName))
                {
                    // We might have a bad operator and be in an error recovery situation. Ignore it.
                    if (op.ReturnsVoid || op.ParameterCount != 1)
                    {
                        continue;
                    }

                    TypeSymbol convertsFrom = op.ParameterTypes[0];
                    TypeSymbol convertsTo = op.ReturnType;
                    Conversion fromConversion = EncompassingImplicitConversion(sourceExpression, source, convertsFrom, ref useSiteDiagnostics);
                    Conversion toConversion = allowAnyTarget ? Conversion.Identity :
                        EncompassingImplicitConversion(null, convertsTo, target, ref useSiteDiagnostics);

                    if (fromConversion.Exists && toConversion.Exists)
                    {
                        // There is an additional spec violation in the native compiler. Suppose
                        // we have a conversion from X-->Y and are asked to do "Y? y = new X();"  Clearly
                        // the intention is to convert from X-->Y via the implicit conversion, and then
                        // stick a standard implicit conversion from Y-->Y? on the back end. **In this 
                        // situation, the native compiler treats the conversion as though it were
                        // actually X-->Y? in source for the purposes of determining the best target
                        // type of an operator.
                        //
                        // We perpetuate this fiction here.

                        if ((object)target != null && target.IsNullableType() && convertsTo.IsNonNullableValueType())
                        {
                            convertsTo = MakeNullableType(convertsTo);
                            toConversion = allowAnyTarget ? Conversion.Identity :
                                EncompassingImplicitConversion(null, convertsTo, target, ref useSiteDiagnostics);
                        }

                        u.Add(UserDefinedConversionAnalysis.Normal(op, fromConversion, toConversion, convertsFrom, convertsTo));
                    }
                    else if ((object)source != null && (object)target != null && source.IsNullableType() && convertsFrom.IsNonNullableValueType() && target.CanBeAssignedNull())
                    {
                        // As mentioned above, here we diverge from the specification, in two ways.
                        // First, we only check for the lifted form if the normal form was inapplicable.
                        // Second, we are supposed to apply lifting semantics only if the conversion 
                        // parameter and return types are *both* non-nullable value types.
                        //
                        // In fact the native compiler determines whether to check for a lifted form on
                        // the basis of:
                        //
                        // * Is the type we are ultimately converting from a nullable value type?
                        // * Is the parameter type of the conversion a non-nullable value type?
                        // * Is the type we are ultimately converting to a nullable value type, 
                        //   pointer type, or reference type?
                        //
                        // If the answer to all those questions is "yes" then we lift to nullable
                        // and see if the resulting operator is applicable.
                        TypeSymbol nullableFrom = MakeNullableType(convertsFrom);
                        TypeSymbol nullableTo = convertsTo.IsNonNullableValueType() ? MakeNullableType(convertsTo) : convertsTo;
                        Conversion liftedFromConversion = EncompassingImplicitConversion(sourceExpression, source, nullableFrom, ref useSiteDiagnostics);
                        Conversion liftedToConversion = !allowAnyTarget ?
                            EncompassingImplicitConversion(null, nullableTo, target, ref useSiteDiagnostics) :
                            Conversion.Identity;
                        if (liftedFromConversion.Exists && liftedToConversion.Exists)
                        {
                            u.Add(UserDefinedConversionAnalysis.Lifted(op, liftedFromConversion, liftedToConversion, nullableFrom, nullableTo));
                        }
                    }
                }
            }
        }

        private TypeSymbol MostSpecificSourceTypeForImplicitUserDefinedConversion(ImmutableArray<UserDefinedConversionAnalysis> u, TypeSymbol source, ref HashSet<DiagnosticInfo> useSiteDiagnostics)
        {
            // SPEC: If any of the operators in U convert from S then SX is S.
            if ((object)source != null)
            {
                if (u.Any(conv => conv.FromType == source))
                {
                    return source;
                }
            }

            // SPEC: Otherwise, SX is the most encompassed type in the set of
            // SPEC: source types of the operators in U.
            return MostEncompassedType(u, conv => conv.FromType, ref useSiteDiagnostics);
        }

        private TypeSymbol MostSpecificTargetTypeForImplicitUserDefinedConversion(ImmutableArray<UserDefinedConversionAnalysis> u, TypeSymbol target, ref HashSet<DiagnosticInfo> useSiteDiagnostics)
        {
            // SPEC: If any of the operators in U convert to T then TX is T.
            // SPEC: Otherwise, TX is the most encompassing type in the set of
            // SPEC: target types of the operators in U. 

            // DELIBERATE SPEC VIOLATION:
            // The native compiler deviates from the specification in the way it 
            // determines what the "converts to" type is. The specification is pretty
            // clear that the "converts to" type is the actual return type of the 
            // conversion operator, or, in the case of a lifted operator, the lifted-to-
            // nullable type. That is, if we have X-->Y then the converts-to type of
            // the operator in its normal form is Y, and the converts-to type of the 
            // operator in its lifted form is Y?. 
            //
            // The native compiler does not do this. Suppose we have a user-defined
            // conversion X-->Y, and the assignment Y? y = new X(); -- the native 
            // compiler will consider the converts-to type of X-->Y to be Y?, surprisingly
            // enough. 
            //
            // We have previously written the appropriate "ToType" into the conversion analysis
            // to perpetuate this fiction.

            if (u.Any(conv => conv.ToType == target))
            {
                return target;
            }

            return MostEncompassingType(u, conv => conv.ToType, ref useSiteDiagnostics);
        }

        private static int LiftingCount(UserDefinedConversionAnalysis conv)
        {
            int count = 0;
            if (conv.FromType != conv.Operator.ParameterTypes[0])
            {
                count += 1;
            }

            if (conv.ToType != conv.Operator.ReturnType)
            {
                count += 1;
            }

            return count;
        }

        private static int? MostSpecificConversionOperator(TypeSymbol sx, TypeSymbol tx, ImmutableArray<UserDefinedConversionAnalysis> u)
        {
            Debug.Assert((object)sx != null);
            Debug.Assert((object)tx != null);

            // SPEC: If U contains exactly one user-defined conversion operator from SX to TX 
            // SPEC: then that is the most-specific conversion operator;
            //
            // SPEC: Otherise, if U contains exactly one lifted conversion operator that converts from
            // SPEC: SX to TX then this is the most specific operator.
            //
            // SPEC: Otherise, the conversion is ambiguous and a compile-time error occurs.
            //
            // SPEC ERROR:
            //
            // Clearly the text above cannot be correct because it gives undesirable results.
            // Suppose we have structs E and F with an implicit user defined conversion from 
            // F to E. We have an assignment from F to E?. Clearly what should happen is
            // we should convert F to E, then convert E to E?.  But the spec says that this
            // should be an error. Why? Because both F-->E and F?-->E? are added to the candidate
            // set. What is SX? Clearly F, because there is a candidate that takes an F.  
            // What is TX? Clearly E? because there is a candidate that returns an E?.  
            // And now the overload resolution problem is ambiguous because neither operator
            // takes SX and returns TX. 
            //
            // DELIBERATE SPEC VIOLATION:
            //
            // The native compiler takes a rather different approach than the approach described
            // in the specification. Rather than adding both the lifted and unlifted forms of
            // each operator to the candidate set, using those operators to determine the best
            // source and target types, and then choosing the unique operator from that source type
            // to that target type, it instead *transforms in place* the "from" and "to" types
            // of each operator so that their nullability matches those of the source and target
            // types. This can then lead to ambiguities; consider for example a type that
            // has user defined conversions X-->Y and X-->Y?.  If we have a conversion from X to
            // Y?, the spec would say that the operators X-->Y, its lifted form X?-->Y?, and
            // X-->Y? are applicable candidates and that the best of them is X-->Y?.  
            //
            // The native compiler arrives at the same conclusion but by different logic; it says
            // that X-->Y has a "half lifted" form X-->Y?, and that it is "worse" than X-->Y?
            // because it is half lifted.

            // Therefore we match this behaviour by first checking to see if there is a unique
            // best operator that converts from the source type to the target type with liftings
            // on neither side.

            BestIndex bestUnlifted = u.UniqueIndex(
                conv =>
                conv.FromType == sx &&
                conv.ToType == tx &&
                LiftingCount(conv) == 0);

            if (bestUnlifted.Kind == BestIndexKind.Best)
            {
                return bestUnlifted.Best;
            }
            else if (bestUnlifted.Kind == BestIndexKind.Ambiguous)
            {
                // If we got an ambiguity, don't continue. We need to bail immediately.

                // UNDONE: We can do better error reporting if we return the ambiguity and
                // use that in the error message.
                return null;
            }

            // There was no fully-unlifted operator. Check to see if there was any *half-lifted* operator. 
            //
            // For example, suppose we had a conversion from X-->Y?, and lifted it to X?-->Y?. (The spec
            // says not to do such a lifting because Y? is not a non-nullable value type, but the native
            // compiler does so and we are being compatible with it.) That would be a half-lifted operator.
            //
            // For example, suppose we had a conversion from X-->Y, and the assignment Y? y = new X(); --
            // this would also be a "half lifted" conversion even though there is no "lifting" going on
            // (in the sense that we are not checking the source to see if it is null.)
            // 

            BestIndex bestHalfLifted = u.UniqueIndex(
                conv =>
                conv.FromType == sx &&
                conv.ToType == tx &&
                LiftingCount(conv) == 1);

            if (bestHalfLifted.Kind == BestIndexKind.Best)
            {
                return bestHalfLifted.Best;
            }
            else if (bestHalfLifted.Kind == BestIndexKind.Ambiguous)
            {
                // UNDONE: We can do better error reporting if we return the ambiguity and
                // use that in the error message.
                return null;
            }

            // Finally, see if there is a unique best *fully lifted* operator.

            BestIndex bestFullyLifted = u.UniqueIndex(
                conv =>
                conv.FromType == sx &&
                conv.ToType == tx &&
                LiftingCount(conv) == 2);

            if (bestFullyLifted.Kind == BestIndexKind.Best)
            {
                return bestFullyLifted.Best;
            }
            else if (bestFullyLifted.Kind == BestIndexKind.Ambiguous)
            {
                // UNDONE: We can do better error reporting if we return the ambiguity and
                // use that in the error message.
                return null;
            }

            return null;
        }

        // Is A encompassed by B?
        private bool IsEncompassedBy(BoundExpression aExpr, TypeSymbol a, TypeSymbol b, ref HashSet<DiagnosticInfo> useSiteDiagnostics)
        {
            Debug.Assert((object)a != null);
            Debug.Assert((object)b != null);

            // SPEC: If a standard implicit conversion exists from a type A to a type B
            // SPEC: and if neither A nor B is an interface type then A is said to be
            // SPEC: encompassed by B, and B is said to encompass A.

            return EncompassingImplicitConversion(aExpr, a, b, ref useSiteDiagnostics).Exists;
        }

        private Conversion EncompassingImplicitConversion(BoundExpression aExpr, TypeSymbol a, TypeSymbol b, ref HashSet<DiagnosticInfo> useSiteDiagnostics)
        {
            Debug.Assert(aExpr != null || (object)a != null);
            Debug.Assert((object)b != null);

            // DELIBERATE SPEC VIOLATION: 
            // We ought to be saying that an encompassing conversion never exists when one of
            // the types is an interface type, but due to a desire to be compatible with a 
            // dev10 bug, we allow it. See the comment regarding bug 17021 above for more details.

            var result = ClassifyStandardImplicitConversion(aExpr, a, b, ref useSiteDiagnostics);
            return IsEncompassingImplicitConversionKind(result.Kind) ? result : Conversion.NoConversion;
        }

        private static bool IsEncompassingImplicitConversionKind(ConversionKind kind)
        {
            switch (kind)
            {
                // Doesn't even exist.
                case ConversionKind.NoConversion:

                // Specifically disallowed because there would be subtle
                // consequences for the overload betterness rules.
                case ConversionKind.MethodGroup:
                case ConversionKind.AnonymousFunction:
                case ConversionKind.ImplicitDynamic:

                // DELIBERATE SPEC VIOLATION: 
                // We do not support an encompassing implicit conversion from a zero constant
                // to an enum type, because the native compiler did not.  It would be a breaking
                // change.
                case ConversionKind.ImplicitEnumeration:

                // Not built in.
                case ConversionKind.ImplicitUserDefined:
                case ConversionKind.ExplicitUserDefined:

                // Not implicit.
                case ConversionKind.ExplicitNumeric:
                case ConversionKind.ExplicitEnumeration:
                case ConversionKind.ExplicitNullable:
                case ConversionKind.ExplicitReference:
                case ConversionKind.Unboxing:
                case ConversionKind.ExplicitDynamic:
                case ConversionKind.PointerToPointer:
                case ConversionKind.PointerToInteger:
                case ConversionKind.IntegerToPointer:
                case ConversionKind.IntPtr:
                    return false;

                // Spec'd in C# 4.
                case ConversionKind.Identity:
                case ConversionKind.ImplicitNumeric:
                case ConversionKind.ImplicitNullable:
                case ConversionKind.ImplicitReference:
                case ConversionKind.Boxing:
                case ConversionKind.ImplicitConstant:
                case ConversionKind.PointerToVoid:

                // Added to spec in Roslyn timeframe.
                case ConversionKind.NullLiteral:
                case ConversionKind.NullToPointer:
                    return true;

                default:
                    throw ExceptionUtilities.Unreachable;
            }
        }

        private TypeSymbol MostEncompassedType<T>(
            ImmutableArray<T> items,
            Func<T, TypeSymbol> extract,
            ref HashSet<DiagnosticInfo> useSiteDiagnostics)
        {
            return MostEncompassedType<T>(items, x => true, extract, ref useSiteDiagnostics);
        }

        private TypeSymbol MostEncompassedType<T>(
            ImmutableArray<T> items,
            Func<T, bool> valid,
            Func<T, TypeSymbol> extract,
            ref HashSet<DiagnosticInfo> useSiteDiagnostics)
        {

            // SPEC: The most encompassed type is the one type in the set that 
            // SPEC: is encompassed by all the other types.

            // We have a bit of a graph theory problem here. Suppose hypothetically
            // speaking we have three types in the set such that:
            //
            // X is encompassed by Y
            // X is encompassed by Z
            // Y is encompassed by X
            //
            // In that situation, X is the unique type in the set that is encompassed
            // by all the other types, despite the fact that it appears to be neither
            // better nor worse than Y! 
            //
            // But in practice this situation never arises because implicit convertibility
            // is transitive; if Y is implicitly convertible to X and X is implicitly convertible
            // to Z, then Y is implicitly convertible to Z.
            //
            // Because we have this transitivity, we can rephrase the problem as follows:
            //
            // Find the unique best type in the set, where the best type is the type that is 
            // better than every other type. By "X is better than Y" we mean "X is encompassed 
            // by Y but Y is not encompassed by X".

            HashSet<DiagnosticInfo> _useSiteDiagnostics = useSiteDiagnostics;
            int? best = items.UniqueBestValidIndex(valid,
                (left, right) =>
                {
                    TypeSymbol leftType = extract(left);
                    TypeSymbol rightType = extract(right);
                    if (leftType == rightType)
                    {
                        return BetterResult.Equal;
                    }

                    bool leftWins = IsEncompassedBy(null, leftType, rightType, ref _useSiteDiagnostics);
                    bool rightWins = IsEncompassedBy(null, rightType, leftType, ref _useSiteDiagnostics);
                    if (leftWins == rightWins)
                    {
                        return BetterResult.Neither;
                    }
                    return leftWins ? BetterResult.Left : BetterResult.Right;
                });

            useSiteDiagnostics = _useSiteDiagnostics;
            return best == null ? null : extract(items[best.Value]);
        }

        private TypeSymbol MostEncompassingType<T>(
            ImmutableArray<T> items,
            Func<T, TypeSymbol> extract,
            ref HashSet<DiagnosticInfo> useSiteDiagnostics)
        {
            return MostEncompassingType<T>(items, x => true, extract, ref useSiteDiagnostics);
        }


        private TypeSymbol MostEncompassingType<T>(
            ImmutableArray<T> items,
            Func<T, bool> valid,
            Func<T, TypeSymbol> extract,
            ref HashSet<DiagnosticInfo> useSiteDiagnostics)
        {

            // See comments above.
            HashSet<DiagnosticInfo> _useSiteDiagnostics = useSiteDiagnostics;
            int? best = items.UniqueBestValidIndex(valid,
                (left, right) =>
                {
                    TypeSymbol leftType = extract(left);
                    TypeSymbol rightType = extract(right);
                    if (leftType == rightType)
                    {
                        return BetterResult.Equal;
                    }

                    bool leftWins = IsEncompassedBy(null, rightType, leftType, ref _useSiteDiagnostics);
                    bool rightWins = IsEncompassedBy(null, leftType, rightType, ref _useSiteDiagnostics);
                    if (leftWins == rightWins)
                    {
                        return BetterResult.Neither;
                    }
                    return leftWins ? BetterResult.Left : BetterResult.Right;
                });

            useSiteDiagnostics = _useSiteDiagnostics;
            return best == null ? null : extract(items[best.Value]);
        }

        private NamedTypeSymbol MakeNullableType(TypeSymbol type)
        {
            var nullable = this.corLibrary.GetDeclaredSpecialType(SpecialType.System_Nullable_T);
            return nullable.Construct(type);
        }

        /// <remarks>
        /// NOTE: Keep this method in sync with AnalyzeImplicitUserDefinedConversion.
        /// </remarks>
        protected UserDefinedConversionResult AnalyzeImplicitUserDefinedConversionForSwitchGoverningType(TypeSymbol source, ref HashSet<DiagnosticInfo> useSiteDiagnostics)
        {
            // SPEC:    The governing type of a switch statement is established by the switch expression.
            // SPEC:    1) If the type of the switch expression is sbyte, byte, short, ushort, int, uint,
            // SPEC:       long, ulong, bool, char, string, or an enum-type, or if it is the nullable type
            // SPEC:       corresponding to one of these types, then that is the governing type of the switch statement. 
            // SPEC:    2) Otherwise, exactly one user-defined implicit conversion (§6.4) must exist from the
            // SPEC:       type of the switch expression to one of the following possible governing types:
            // SPEC:       sbyte, byte, short, ushort, int, uint, long, ulong, char, string, or, a nullable type
            // SPEC:       corresponding to one of those types

            // NOTE:    This method implements part (2) above, it should be called only if (1) is false for source type.
            Debug.Assert((object)source != null);
            Debug.Assert(!source.IsValidSwitchGoverningType());

            // NOTE:    There are multiple possible approaches for implementing (2):
            // NOTE:    1)  AnalyzeImplicitUserDefinedConversion from source type to each of the possible governing types
            // NOTE:        mentioned in (2): Though this approach exactly matches the specification, it is highly inefficient.
            // NOTE:        We need to consider 20 possible target types (ten primitive non-nullable types
            // NOTE:        and ten primitive nullable types) to determine if there is exactly one valid user defined conversion to
            // NOTE:        any one of these types, requiring 20 calls to AnalyzeImplicitUserDefinedConversion.
            // NOTE:    2)  Native compiler's approach: Native compiler implements this by walking through all the implicit user defined operators
            // NOTE:        from the source type to a valid switch governing type, as per (2), and determining if there is a unique best.
            // NOTE:        This part is that piece of code doesn't call into the code for analying user defined implicit conversion, but does the
            // NOTE:        analysis of applicable lifted/normal forms itself. This makes it very difficult to maintain and is bug prone.
            // NOTE:        See the SPEC VIOLATION comment later in this method for one of the cases where it gets the analysis wrong and violates the
            // NOTE:        language specification.
            // NOTE:    3)  Use an approach similar to native compiler's approach, but call into the common code for analying user defined implicit conversion.


            // NOTE:    We choose approach (3) and implement it as a slight variation of AnalyzeImplicitUserDefinedConversion as follows:

            // NOTE:    (a) Compute the set of types D from which user-defined conversion operators should be considered by considering only the source type.
            // NOTE:    (b) Instead of computing applicable user defined implicit conversions U from the source type to a specific target type,
            // NOTE:        we compute these from the source type to ANY target type.
            // NOTE:    (c) If U is empty, the conversion is undefined and a compile-time error occurs.
            // NOTE:    (d) Find the most specific source type SX of the operators in U...
            // NOTE:    (e) Instead of finding the most specific target type TX of the operators in U, we consider all unique target types TX for all operators in U.
            // NOTE:        Let this set of unique target types be called Y.
            // NOTE:    (f) Check if there is exactly one target type TX in Y such that it satisfied both conditions below:
            // NOTE:        (i)  TX is a valid switch governing type as per condition (2) of the SPEC for establishing the switch governing type and 
            // NOTE:        (ii) There is a valid most specific user defined implicit conversion operator from SX to TX.
            // NOTE:        If there exists such unique TX in Y, then that operator is the resultant user defined conversion and TX is the resultant switch governing type.
            // NOTE:        Otherwise we either have ambiguity or no applicable operators.

            // (a) Compute the set of types D from which user-defined conversion operators should be considered by considering only the source type.
            var d = ArrayBuilder<NamedTypeSymbol>.GetInstance();
            ComputeUserDefinedImplicitConversionTypeSet(source, t: null, d: d, useSiteDiagnostics: ref useSiteDiagnostics);

            // (b) Instead of computing applicable user defined implicit conversions U from the source type to a specific target type,
            //     we compute these from the source type to ANY target type.
            var ubuild = ArrayBuilder<UserDefinedConversionAnalysis>.GetInstance();
            ComputeApplicableUserDefinedImplicitConversionSet(null, source, target: null, d: d, u: ubuild, useSiteDiagnostics: ref useSiteDiagnostics, allowAnyTarget: true);
            d.Free();
            ImmutableArray<UserDefinedConversionAnalysis> u = ubuild.ToImmutableAndFree();

            // (c) If U is empty, the conversion is undefined and a compile-time error occurs.
            if (u.Length == 0)
            {
                return UserDefinedConversionResult.NoApplicableOperators(u);
            }

            // (d) Find the most specific source type SX of the operators in U...
            TypeSymbol sx = MostSpecificSourceTypeForImplicitUserDefinedConversion(u, source, ref useSiteDiagnostics);
            if ((object)sx == null)
            {
                return UserDefinedConversionResult.NoBestSourceType(u);
            }

            return MostSpecificConversionOperatorForSwitchGoverningType(sx, u);
        }

        private static UserDefinedConversionResult MostSpecificConversionOperatorForSwitchGoverningType(TypeSymbol sx, ImmutableArray<UserDefinedConversionAnalysis> u)
        {
            // This method finds the most specific user-defined implicit conversion operator from the best source type SX to a valid switch governing type.
            // It implements steps (e) and (f) for AnalyzeImplicitUserDefinedConversionForSwitchGoverningType, see comments in that method for details.

            // (e) Instead of finding the most specific target type TX of the operators in U, we consider all unique target types TX for all operators in U.
            //     Let this set of unique target types be called Y.
            var y = new HashSet<TypeSymbol>();
            UserDefinedConversionResult? exactConversionResult = null;

            // (f) Check if there is exactly one target type TX in Y such that it satisfied both conditions below:
            //     (i)  TX is a valid switch governing type as per condition (2) of the SPEC for establishing the switch governing type and 
            //     (ii) There is a valid most specific user defined implicit conversion operator from SX to TX.

            foreach (UserDefinedConversionAnalysis analysis in u)
            {
                TypeSymbol tx = analysis.ToType;

                if (y.Add(tx) && tx.IsValidSwitchGoverningType(isTargetTypeOfUserDefinedOp: true))
                {
                    if (!exactConversionResult.HasValue)
                    {
                        // NOTE:    As mentioned in the comments at the start of this function, native compiler doesn't call into
                        // NOTE:    the code for analying user defined implicit conversion, i.e. MostSpecificConversionOperator,
                        // NOTE:    but does the analysis of applicable lifted/normal forms itself.
                        // NOTE:    This introduces a SPEC VIOLATION for the following test in the native compiler:

                        // NOTE:    // See test SwitchTests.CS0166_AggregateTypeWithMultipleImplicitConversions_07
                        // NOTE:    struct Conv
                        // NOTE:    {
                        // NOTE:        public static implicit operator int (Conv C) { return 1; }
                        // NOTE:        public static implicit operator int (Conv? C2) { return 0; }
                        // NOTE:        public static int Main()
                        // NOTE:        {
                        // NOTE:            Conv? D = new Conv();
                        // NOTE:            switch(D)
                        // NOTE:            {   ...

                        // SPEC VIOLATION: Native compiler allows the above code to compile
                        // SPEC VIOLATION: even though there are two user-defined implicit conversions:
                        // SPEC VIOLATION: 1) To int type (applicable in normal form): public static implicit operator int (Conv? C2)
                        // SPEC VIOLATION: 2) To int? type (applicable in lifted form): public static implicit operator int (Conv C)

                        // SPEC VIOLATION: We maintain compability with the native compiler.

                        int? best = MostSpecificConversionOperator(sx, tx, u);
                        if (best != null)
                        {
                            exactConversionResult = UserDefinedConversionResult.Valid(u, best.Value);
                            continue;
                        }
                    }

                    return UserDefinedConversionResult.Ambiguous(u);
                }
            }

            // If there exists such unique TX in Y, then that operator is the resultant user defined conversion and TX is the resultant switch governing type.
            // Otherwise we either have ambiguity or no applicable operators.

            return exactConversionResult.HasValue ?
                exactConversionResult.Value :
                UserDefinedConversionResult.NoApplicableOperators(u);
        }
    }
}
