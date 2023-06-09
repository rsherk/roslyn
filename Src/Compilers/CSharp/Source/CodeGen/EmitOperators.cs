﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using Microsoft.CodeAnalysis.CodeGen;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.CodeGen
{
    partial class CodeGenerator
    {
        private void EmitUnaryOperatorExpression(BoundUnaryOperator expression, bool used)
        {
            var operatorKind = expression.OperatorKind;

            if (operatorKind.IsChecked())
            {
                EmitUnaryCheckedOperatorExpression(expression, used);
                return;
            }

            if (!used)
            {
                EmitExpression(expression.Operand, used: false);
                return;
            }

            if (operatorKind == UnaryOperatorKind.BoolLogicalNegation)
            {
                EmitCondExpr(expression.Operand, sense: false);
                return;
            }

            EmitExpression(expression.Operand, used: true);
            switch (operatorKind.Operator())
            {
                case UnaryOperatorKind.UnaryMinus:
                    builder.EmitOpCode(ILOpCode.Neg);
                    break;

                case UnaryOperatorKind.BitwiseComplement:
                    builder.EmitOpCode(ILOpCode.Not);
                    break;

                case UnaryOperatorKind.UnaryPlus:
                    break;

                default:
                    throw ExceptionUtilities.UnexpectedValue(operatorKind.Operator());
            }
        }

        private void EmitBinaryOperatorExpression(BoundBinaryOperator expression, bool used)
        {
            var operatorKind = expression.OperatorKind;

            if (operatorKind.EmitsAsCheckedInstruction())
            {
                EmitBinaryCheckedOperatorExpression(expression, used);
                return;
            }

            // if operator does not have sideeffects itself and is not shortcircuiting
            // we can simply emit sideefects from the first operand and then from the second one
            if (!used && !operatorKind.IsLogical() && !OperatorHasSideEffects(operatorKind))
            {
                EmitExpression(expression.Left, false);
                EmitExpression(expression.Right, false);
                return;
            }

            if (IsConditional(operatorKind))
            {
                EmitBinaryCondOperator(expression, true);
            }
            else
            {
                EmitBinaryArithOperator(expression);
            }

            EmitPopIfUnused(used);
        }

        private void EmitBinaryArithOperator(BoundBinaryOperator expression)
        {
            EmitExpression(expression.Left, true);
            EmitExpression(expression.Right, true);

            switch (expression.OperatorKind.Operator())
            {
                case BinaryOperatorKind.Multiplication:
                    builder.EmitOpCode(ILOpCode.Mul);
                    break;

                case BinaryOperatorKind.Addition:
                    builder.EmitOpCode(ILOpCode.Add);
                    break;

                case BinaryOperatorKind.Subtraction:
                    builder.EmitOpCode(ILOpCode.Sub);
                    break;

                case BinaryOperatorKind.Division:
                    if (IsUnsignedBinaryOperator(expression))
                    {
                        builder.EmitOpCode(ILOpCode.Div_un);
                    }
                    else
                    {
                        builder.EmitOpCode(ILOpCode.Div);
                    }
                    break;

                case BinaryOperatorKind.Remainder:
                    if (IsUnsignedBinaryOperator(expression))
                    {
                        builder.EmitOpCode(ILOpCode.Rem_un);
                    }
                    else
                    {
                        builder.EmitOpCode(ILOpCode.Rem);
                    }
                    break;

                case BinaryOperatorKind.LeftShift:
                    builder.EmitOpCode(ILOpCode.Shl);
                    break;

                case BinaryOperatorKind.RightShift:
                    if (IsUnsignedBinaryOperator(expression))
                    {
                        builder.EmitOpCode(ILOpCode.Shr_un);
                    }
                    else
                    {
                        builder.EmitOpCode(ILOpCode.Shr);
                    }
                    break;

                case BinaryOperatorKind.And:
                    builder.EmitOpCode(ILOpCode.And);
                    break;

                case BinaryOperatorKind.Xor:
                    builder.EmitOpCode(ILOpCode.Xor);
                    break;

                case BinaryOperatorKind.Or:
                    builder.EmitOpCode(ILOpCode.Or);
                    break;

                default:
                    throw ExceptionUtilities.UnexpectedValue(expression.OperatorKind.Operator());
            }

            EmitConversionToEnumUnderlyingType(expression, @checked: false);
        }

        private void EmitShortCircuitingOperator(BoundBinaryOperator condition, bool sense, bool stopSense, bool stopValue)
        {
            // we generate:
            //
            // gotoif (a == stopSense) fallThrough
            // b == sense
            // goto labEnd
            // fallThrough:
            // stopValue
            // labEnd:
            //                 AND       OR
            //            +-  ------    -----
            // stopSense  |   !sense    sense
            // stopValue  |     0         1

            object lazyFallThrough = null;

            EmitCondBranch(condition.Left, ref lazyFallThrough, stopSense);
            EmitCondExpr(condition.Right, sense);

            // if fallthrough was not initialized, no one is going to take that branch
            // and we are done with Right on stack
            if (lazyFallThrough == null)
            {
                return;
            }

            var labEnd = new object();
            builder.EmitBranch(ILOpCode.Br, labEnd);

            // if we get to fallThrough, we should not have Right on stack. Adjust for that.
            builder.AdjustStack(-1);

            builder.MarkLabel(lazyFallThrough);
            builder.EmitBoolConstant(stopValue);
            builder.MarkLabel(labEnd);
        }

        //NOTE: odd positions assume inverted sense
        private static readonly ILOpCode[] CompOpCodes = new ILOpCode[]
        {
            //  <            <=               >                >=
            ILOpCode.Clt,    ILOpCode.Cgt,    ILOpCode.Cgt,    ILOpCode.Clt,     // Signed
            ILOpCode.Clt_un, ILOpCode.Cgt_un, ILOpCode.Cgt_un, ILOpCode.Clt_un,  // Unsigned
            ILOpCode.Clt,    ILOpCode.Cgt_un, ILOpCode.Cgt,    ILOpCode.Clt_un,  // Float
        };

        //NOTE: The result of this should be a boolean on the stack.
        private void EmitBinaryCondOperator(BoundBinaryOperator binOp, bool sense)
        {
            bool andOrSense = sense;
            int opIdx;

            switch (binOp.OperatorKind.OperatorWithLogical())
            {
                case BinaryOperatorKind.LogicalOr:
                    Debug.Assert(binOp.Left.Type.SpecialType == SpecialType.System_Boolean);
                    Debug.Assert(binOp.Right.Type.SpecialType == SpecialType.System_Boolean);

                    // Rewrite (a || b) as ~(~a && ~b)
                    andOrSense = !andOrSense;
                    // Fall through
                    goto case BinaryOperatorKind.LogicalAnd;

                case BinaryOperatorKind.LogicalAnd:
                    Debug.Assert(binOp.Left.Type.SpecialType == SpecialType.System_Boolean);
                    Debug.Assert(binOp.Right.Type.SpecialType == SpecialType.System_Boolean);

                    // ~(a && b) is equivalent to (~a || ~b)
                    if (!andOrSense)
                    {
                        // generate (~a || ~b)
                        EmitShortCircuitingOperator(binOp, sense, sense, true);
                    }
                    else
                    {
                        // generate (a && b)
                        EmitShortCircuitingOperator(binOp, sense, !sense, false);
                    }
                    return;

                case BinaryOperatorKind.And:
                    Debug.Assert(binOp.Left.Type.SpecialType == SpecialType.System_Boolean);
                    Debug.Assert(binOp.Right.Type.SpecialType == SpecialType.System_Boolean);
                    EmitBinaryCondOperatorHelper(ILOpCode.And, binOp.Left, binOp.Right, sense);
                    return;

                case BinaryOperatorKind.Or:
                    Debug.Assert(binOp.Left.Type.SpecialType == SpecialType.System_Boolean);
                    Debug.Assert(binOp.Right.Type.SpecialType == SpecialType.System_Boolean);
                    EmitBinaryCondOperatorHelper(ILOpCode.Or, binOp.Left, binOp.Right, sense);
                    return;

                case BinaryOperatorKind.Xor:
                    Debug.Assert(binOp.Left.Type.SpecialType == SpecialType.System_Boolean);
                    Debug.Assert(binOp.Right.Type.SpecialType == SpecialType.System_Boolean);

                    // Xor is equivalent to not equal.
                    if (sense)
                        EmitBinaryCondOperatorHelper(ILOpCode.Xor, binOp.Left, binOp.Right, true);
                    else
                        EmitBinaryCondOperatorHelper(ILOpCode.Ceq, binOp.Left, binOp.Right, true);
                    return;

                case BinaryOperatorKind.NotEqual:
                    // neq  is emitted as  !eq
                    sense = !sense;
                    goto case BinaryOperatorKind.Equal;

                case BinaryOperatorKind.Equal:

                    var constant = binOp.Left.ConstantValue;
                    var comparand = binOp.Right;

                    if (constant == null)
                    {
                        constant = comparand.ConstantValue;
                        comparand = binOp.Left;
                    }

                    if (constant != null)
                    {
                        if (constant.IsDefaultValue)
                        {
                            if (!constant.IsFloating)
                            {
                                if (sense)
                                {
                                    EmitIsNullOrZero(comparand, constant);
                                }
                                else
                                {
                                    //  obj != null/0   for pointers and integral numerics is emitted as cgt.un
                                    EmitIsNotNullOrZero(comparand, constant);
                                }
                                return;
                            }
                        }
                        else if (constant.IsBoolean)
                        {
                            // treat  "x = True" ==> "x"
                            EmitExpression(comparand, true);
                            EmitIsSense(sense);
                            return;
                        }
                    }

                    EmitBinaryCondOperatorHelper(ILOpCode.Ceq, binOp.Left, binOp.Right, sense);
                    return;

                case BinaryOperatorKind.LessThan:
                    opIdx = 0;
                    break;

                case BinaryOperatorKind.LessThanOrEqual:
                    opIdx = 1;
                    sense = !sense; // lte is emitted as !gt 
                    break;

                case BinaryOperatorKind.GreaterThan:
                    opIdx = 2;
                    break;

                case BinaryOperatorKind.GreaterThanOrEqual:
                    opIdx = 3;
                    sense = !sense; // gte is emitted as !lt 
                    break;

                default:
                    throw ExceptionUtilities.UnexpectedValue(binOp.OperatorKind.OperatorWithLogical());
            }

            if (IsUnsignedBinaryOperator(binOp))
            {
                opIdx += 4;
            }
            else if (IsFloat(binOp.OperatorKind))
            {
                opIdx += 8;
            }

            EmitBinaryCondOperatorHelper(CompOpCodes[opIdx], binOp.Left, binOp.Right, sense);
            return;
        }

        private void EmitIsNotNullOrZero(BoundExpression comparand, ConstantValue nullOrZero)
        {
            EmitExpression(comparand, true);

            var comparandType = comparand.Type;
            if (comparandType.IsReferenceType && !comparandType.IsVerifierReference())
            {
                EmitBox(comparandType, comparand.Syntax);
            }

            builder.EmitConstantValue(nullOrZero);
            builder.EmitOpCode(ILOpCode.Cgt_un);
        }

        private void EmitIsNullOrZero(BoundExpression comparand, ConstantValue nullOrZero)
        {
            EmitExpression(comparand, true);

            var comparandType = comparand.Type;
            if (comparandType.IsReferenceType && !comparandType.IsVerifierReference())
            {
                EmitBox(comparandType, comparand.Syntax);
            }

            builder.EmitConstantValue(nullOrZero);
            builder.EmitOpCode(ILOpCode.Ceq);
        }

        private void EmitBinaryCondOperatorHelper(ILOpCode opCode, BoundExpression left, BoundExpression right, bool sense)
        {
            EmitExpression(left, true);
            EmitExpression(right, true);
            this.builder.EmitOpCode(opCode);
            EmitIsSense(sense);
        }

        // generate a conditional (ie, boolean) expression...
        // this will leave a value on the stack which conforms to sense, ie:(condition == sense)
        private ConstResKind EmitCondExpr(BoundExpression condition, bool sense)
        {
            while (condition.Kind == BoundKind.UnaryOperator)
            {
                var unOp = (BoundUnaryOperator)condition;
                Debug.Assert(unOp.OperatorKind == UnaryOperatorKind.BoolLogicalNegation);
                condition = unOp.Operand;
                sense = !sense;
            }

            Debug.Assert(condition.Type.SpecialType == SpecialType.System_Boolean);

            var constantValue = condition.ConstantValue;
            if (constantValue != null)
            {
                Debug.Assert(constantValue.Discriminator == ConstantValueTypeDiscriminator.Boolean);
                var constant = constantValue.BooleanValue;
                builder.EmitBoolConstant(constant == sense);
                return (constant == sense ? ConstResKind.ConstTrue : ConstResKind.ConstFalse);
            }

            if (condition.Kind == BoundKind.BinaryOperator)
            {
                var binOp = (BoundBinaryOperator)condition;
                if (IsConditional(binOp.OperatorKind))
                {
                    EmitBinaryCondOperator(binOp, sense);
                    return ConstResKind.NotAConst;
                }
            }

            EmitExpression(condition, true);
            EmitIsSense(sense);

            return ConstResKind.NotAConst;
        }

        private void EmitUnaryCheckedOperatorExpression(BoundUnaryOperator expression, bool used)
        {
            Debug.Assert(expression.OperatorKind.Operator() == UnaryOperatorKind.UnaryMinus);
            var type = expression.OperatorKind.OperandTypes();

            // Spec 7.6.2
            // Implementation of unary minus has two overloads:
            //   int operator –(int x)
            //   long operator –(long x)
            // 
            // The result is computed by subtracting x from zero. 
            // If the value of x is the smallest representable value of the operand type (−2^31 for int or −2^63 for long),
            // then the mathematical negation of x is not representable within the operand type. If this occurs within a checked context, 
            // a System.OverflowException is thrown; if it occurs within an unchecked context, 
            // the result is the value of the operand and the overflow is not reported.
            Debug.Assert(type == UnaryOperatorKind.Int || type == UnaryOperatorKind.Long);

            // ldc.i4.0
            // conv.i8  (when the operand is 64bit)
            // <epxr>
            // sub.ovf

            builder.EmitOpCode(ILOpCode.Ldc_i4_0);

            if (type == UnaryOperatorKind.Long)
            {
                builder.EmitOpCode(ILOpCode.Conv_i8);
            }

            EmitExpression(expression.Operand, used: true);
            builder.EmitOpCode(ILOpCode.Sub_ovf);

            EmitPopIfUnused(used);
        }

        private void EmitConversionToEnumUnderlyingType(BoundBinaryOperator expression, bool @checked)
        {
            // If we are doing an enum addition or subtraction and the 
            // underlying type is 8 or 16 bits then we will have done the operation in 32 
            // bits and we need to convert back down to the smaller bit size
            // to [one|zero]extend the value
            // NOTE: we do not need to do this for bitwise operations since they will always 
            //       result in a properly sign-extended result, assuming operands were sign extended
            //
            // If e is a value of enum type E and u is a value of underlying type u then:
            //
            // e + u --> (E)((U)e + u)
            // u + e --> (E)(u + (U)e)
            // e - e --> (U)((U)e - (U)e)
            // e - u --> (E)((U)e - u)
            // e & e --> (E)((U)e & (U)e)
            // e | e --> (E)((U)e | (U)e)
            // e ^ e --> (E)((U)e ^ (U)e)
            //
            // NOTE: (E) is actually emitted as (U) and in last 3 cases is not necessary.
            //
            // Due to a bug, the native compiler allows:
            //
            // u - e --> (E)(u - (U)e)
            //
            // And so Roslyn does as well.

            TypeSymbol enumType;

            switch (expression.OperatorKind.Operator() | expression.OperatorKind.OperandTypes())
            {
                case BinaryOperatorKind.EnumAndUnderlyingAddition:
                case BinaryOperatorKind.EnumSubtraction:
                case BinaryOperatorKind.EnumAndUnderlyingSubtraction:
                    enumType = expression.Left.Type;
                    break;
                case BinaryOperatorKind.EnumAnd:
                case BinaryOperatorKind.EnumOr:
                case BinaryOperatorKind.EnumXor:
                    Debug.Assert(expression.Left.Type == expression.Right.Type);
                    enumType = null;
                    break;
                case BinaryOperatorKind.UnderlyingAndEnumSubtraction:
                case BinaryOperatorKind.UnderlyingAndEnumAddition:
                    enumType = expression.Right.Type;
                    break;
                default:
                    enumType = null;
                    break;
            }

            if ((object)enumType == null)
            {
                return;
            }

            Debug.Assert(enumType.IsEnumType());

            SpecialType type = enumType.GetEnumUnderlyingType().SpecialType;
            switch (type)
            {
                case SpecialType.System_Byte:
                    builder.EmitNumericConversion(Microsoft.Cci.PrimitiveTypeCode.Int32, Microsoft.Cci.PrimitiveTypeCode.UInt8, @checked);
                    break;
                case SpecialType.System_SByte:
                    builder.EmitNumericConversion(Microsoft.Cci.PrimitiveTypeCode.Int32, Microsoft.Cci.PrimitiveTypeCode.Int8, @checked);
                    break;
                case SpecialType.System_Int16:
                    builder.EmitNumericConversion(Microsoft.Cci.PrimitiveTypeCode.Int32, Microsoft.Cci.PrimitiveTypeCode.Int16, @checked);
                    break;
                case SpecialType.System_UInt16:
                    builder.EmitNumericConversion(Microsoft.Cci.PrimitiveTypeCode.Int32, Microsoft.Cci.PrimitiveTypeCode.UInt16, @checked);
                    break;
            }
        }

        private void EmitBinaryCheckedOperatorExpression(BoundBinaryOperator expression, bool used)
        {
            EmitExpression(expression.Left, true);
            EmitExpression(expression.Right, true);

            var unsigned = IsUnsignedBinaryOperator(expression);

            switch (expression.OperatorKind.Operator())
            {
                case BinaryOperatorKind.Multiplication:
                    if (unsigned)
                    {
                        builder.EmitOpCode(ILOpCode.Mul_ovf_un);
                    }
                    else
                    {
                        builder.EmitOpCode(ILOpCode.Mul_ovf);
                    }
                    break;

                case BinaryOperatorKind.Addition:
                    if (unsigned)
                    {
                        builder.EmitOpCode(ILOpCode.Add_ovf_un);
                    }
                    else
                    {
                        builder.EmitOpCode(ILOpCode.Add_ovf);
                    }
                    break;

                case BinaryOperatorKind.Subtraction:
                    if (unsigned)
                    {
                        builder.EmitOpCode(ILOpCode.Sub_ovf_un);
                    }
                    else
                    {
                        builder.EmitOpCode(ILOpCode.Sub_ovf);
                    }
                    break;

                default:
                    throw ExceptionUtilities.UnexpectedValue(expression.OperatorKind.Operator());
            }

            EmitConversionToEnumUnderlyingType(expression, @checked: true);

            EmitPopIfUnused(used);
        }

        private static bool OperatorHasSideEffects(BinaryOperatorKind kind)
        {
            switch (kind.Operator())
            {
                case BinaryOperatorKind.Division:
                case BinaryOperatorKind.Remainder:
                    return true;
                default:
                    return kind.IsChecked();
            }
        }

        // emits IsTrue/IsFalse according to the sense
        // IsTrue actually does nothing
        private void EmitIsSense(bool sense)
        {
            if (!sense)
            {
                builder.EmitOpCode(ILOpCode.Ldc_i4_0);
                builder.EmitOpCode(ILOpCode.Ceq);
            }
        }

        private static bool IsUnsigned(SpecialType type)
        {
            switch (type)
            {
                case SpecialType.System_Byte:
                case SpecialType.System_UInt16:
                case SpecialType.System_UInt32:
                case SpecialType.System_UInt64:
                    return true;
            }
            return false;
        }

        private static bool IsUnsignedBinaryOperator(BoundBinaryOperator op)
        {
            BinaryOperatorKind opKind = op.OperatorKind;
            BinaryOperatorKind type = opKind.OperandTypes();
            switch (type)
            {
                case BinaryOperatorKind.Enum:
                case BinaryOperatorKind.EnumAndUnderlying:
                    return IsUnsigned(Binder.GetEnumPromotedType(op.Left.Type.GetEnumUnderlyingType().SpecialType));

                case BinaryOperatorKind.UnderlyingAndEnum:
                    return IsUnsigned(Binder.GetEnumPromotedType(op.Right.Type.GetEnumUnderlyingType().SpecialType));

                case BinaryOperatorKind.UInt:
                case BinaryOperatorKind.ULong:
                case BinaryOperatorKind.ULongAndPointer:
                case BinaryOperatorKind.PointerAndInt:
                case BinaryOperatorKind.PointerAndUInt:
                case BinaryOperatorKind.PointerAndLong:
                case BinaryOperatorKind.PointerAndULong:
                case BinaryOperatorKind.Pointer:
                    return true;

                // Dev10 bases signedness on the first operand (see ILGENREC::genOperatorExpr).
                case BinaryOperatorKind.IntAndPointer:
                case BinaryOperatorKind.LongAndPointer:
                // Dev10 converts the uint to a native int, so it counts as signed.
                case BinaryOperatorKind.UIntAndPointer:
                default:
                    return false;
            }
        }

        private static bool IsConditional(BinaryOperatorKind opKind)
        {
            switch (opKind.OperatorWithLogical())
            {
                case BinaryOperatorKind.LogicalAnd:
                case BinaryOperatorKind.LogicalOr:
                case BinaryOperatorKind.Equal:
                case BinaryOperatorKind.NotEqual:
                case BinaryOperatorKind.LessThan:
                case BinaryOperatorKind.LessThanOrEqual:
                case BinaryOperatorKind.GreaterThan:
                case BinaryOperatorKind.GreaterThanOrEqual:
                    return true;

                case BinaryOperatorKind.And:
                case BinaryOperatorKind.Or:
                case BinaryOperatorKind.Xor:
                    return opKind.OperandTypes() == BinaryOperatorKind.Bool;
            }

            return false;
        }

        private static bool IsFloat(BinaryOperatorKind opKind)
        {
            var type = opKind.OperandTypes();
            switch (type)
            {
                case BinaryOperatorKind.Float:
                case BinaryOperatorKind.Double:
                    return true;
                default:
                    return false;
            }
        }
    }
}
