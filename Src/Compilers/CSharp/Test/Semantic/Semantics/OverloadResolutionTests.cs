﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Roslyn.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests
{
    public class OverloadResolutionTests : OverloadResolutionTestBase
    {
        [Fact]
        public void TestBug12439()
        {
            // The spec has an omission; I believe we intended it to say that there is no
            // conversion from any old-style anonymous method expression to any expression tree
            // type. This is the rule the native compiler enforces; Roslyn should as well, and 
            // we should clarify the specification.

            string source =
 @"
using System;
using System.Linq.Expressions;
class Program
{
    static void Main()
    {
        Foo(delegate { }); // No error; chooses the non-expression version.
    }
    static void Foo(Action a) { }
    static void Foo(Expression<Action> a) { }
}";
            var comp = CreateCompilationWithMscorlibAndSystemCore(source);
            comp.VerifyDiagnostics();
        }

        [Fact]
        public void TestBug11961()
        {
            // This test verifies a deliberate spec violation that we have implemented
            // to ensure backwards compatibility.
            //
            // When doing overload resolution, we must find the best applicable method.
            //
            // When tiebreaking between two applicable candidate methods, we examine each conversion from the
            // argument to the corresponding parameter type to detemine which method is better *in that parameter*.
            // If a method is *not worse* in every parameter and better in at least one, then that method
            // wins. Under what circumstances is one conversion better than another?
            //
            // * A conversion from the argument to a more specific parameter type is better than a conversion to a less
            //   specific parameter type. If we have M(null) and candidates M(string) and M(object) then the conversion to 
            //   string is better because string is more specific.
            //
            // * If the argument is a lambda and the parameter types are delegate types, then the conversion to the
            //   delegate type with the more specific return type wins. If we have M(()=>null) and we are choosing between
            //   M(ObjectReturningDelegate) and M(StringReturningDelegate), the latter wins.
            //
            // In C# 3, these rules were never in conflict because no delegate type was ever more or less specific
            // than another. But in C# 4 we added delegate covariance and contravariance, and now there are delegate
            // types that are more specific than others. We did not correctly update the C# 4 compiler to handle this 
            // situation.
            // 
            // Unfortunately, real-world code exists that depends on this bad behaviour, so we are going to preserve it.
            //
            // The essence of the bug is: the correct behaviour is to do the first tiebreaker first, and the second tiebreaker
            // if necessary. The native compiler, and now Roslyn, does this wrong. It says "is the argument a lambda?" If so,
            // then it applies the second tiebreaker and ignores the first. Otherwise, it applies the first tiebreaker and 
            // ignores the second.
            //
            // Let's take a look at some examples of where it does and does not make a difference:
            //
            // On the first call, the native compiler and Roslyn agree that overload 2 is better. (Remember, Action<T> is 
            // contravariant, so Action<object> is more specific than Action<string>. Every action that takes an object 
            // is also an action that takes a string, so an action that takes an object is more specific.) This is the correct
            // behaviour. The compiler uses the first tiebreaker.

            // On the second call, the native compiler incorrectly believes that overload 3 is better, because it 
            // does not correctly determine that Action<object> is more specific than Action<string> when the argument is 
            // a lambda. The correct behaviour according to the spec would be to produce an ambiguity error. (Why? 
            // because overload 3 is more specific in its first parameter type, and less specific in its second parameter type.
            // And vice-versa for overload 4. No overload is not-worse in all parameters.)

            string source1 = @"
using System;
class P
{
  static void M(Action<string> a) { Console.Write(1); }
  static void M(Action<object> a) { Console.Write(2); }
  static void M(string x, Action<string> a) { Console.Write(3); }
  static void M(object x, Action<object> a) { Console.Write(4); }
  static void M1(string x, Func<object> a) { Console.Write(5); }
  static void M1(object x, Func<ValueType> a) { Console.Write(6); }
  static void M2(Func<object> a, string x) { Console.Write(7); }
  static void M2(Func<ValueType> a, object x) { Console.Write(8); }
  static void M3(Func<object> a, Action<object> b, string x) { Console.Write(9); }
  static void M3(Func<ValueType> a, Action<string> b,  object x) { Console.Write('A'); }
  static void M5(Action<object> b, string x, Func<object> a) { Console.Write('D'); }
  static void M5(Action<string> b, object x, Func<ValueType> a) { Console.Write('E'); }
  static void Main()
  {
    M(null);
    M((string)null, q=>{});
    M(q=>{});
    M1((string)null, ()=>{ throw new NotImplementedException();});
    M2(()=>{ throw new NotImplementedException();}, (string)null);
    M3(()=>{ throw new NotImplementedException();}, q=> {}, (string)null);
    M5(q=> {}, (string)null, ()=>{ throw new NotImplementedException();});
  }
}";

            CompileAndVerify(source1, expectedOutput: @"232579D");

            // Now let's look at some ambiguity errors:
            //
            // On the first call, the native compiler incorrectly produces an ambiguity error. The correct behaviour according
            // to the specification is to choose overload 2, because it is more specific. Because the argument is a lambda
            // we incorrectly skip the first tiebreaker entirely and go straight to the second tiebreaker. We are now in a situation
            // where we have two delegate types that are both void returning, and so by the second tiebreaker, neither is better.

            // On the second call, the native compiler correctly produces an ambiguity error. Overload 3 is better that the 
            // overload 4 in its first parameter and worse in its second parameter, and similarly for overload 4. Since
            // neither overload is not-worse in all parameters, neither is the best choice.

            string source2 = @"
using System;
class P
{
  static void M(Action<string> a) { }
  static void M(Action<object> a) { }
  static void M(string x, Action<string> a) { }
  static void M(object x, Action<object> a) { }
  static void M1(string x, Func<object> a) { Console.Write(5); }
  static void M1(object x, Func<ValueType> a) { Console.Write(6); }
  static void M4(Func<object> a, Action<object> b, Action<string> x) { Console.Write('B'); }
  static void M4(Func<ValueType> a, Action<string> b, Action<object> x) { Console.Write('C'); }
  static void M6(Action<object> b, string x, object a) { Console.Write('F'); }
  static void M6(Action<string> b, object x, string a) { Console.Write('G'); }
  static void Main()
  {

    M((string)null, null);
    M1((string)null, ()=>{ return 5;});
    M4(()=>{ throw new NotImplementedException();}, q=> {}, q=> {});
    M6(q=> {},(string)null, (string)null);
  }
}";

            CreateCompilationWithMscorlib(source2).VerifyDiagnostics(
// (18,5): error CS0121: The call is ambiguous between the following methods or properties: 'P.M(string, System.Action<string>)' and 'P.M(object, System.Action<object>)'
//     M((string)null, null);
Diagnostic(ErrorCode.ERR_AmbigCall, "M").WithArguments("P.M(string, System.Action<string>)", "P.M(object, System.Action<object>)"),
// (19,5): error CS0121: The call is ambiguous between the following methods or properties: 'P.M1(string, System.Func<object>)' and 'P.M1(object, System.Func<System.ValueType>)'
//     M1((string)null, ()=>{ return 5;});
Diagnostic(ErrorCode.ERR_AmbigCall, "M1").WithArguments("P.M1(string, System.Func<object>)", "P.M1(object, System.Func<System.ValueType>)"),
// (20,5): error CS0121: The call is ambiguous between the following methods or properties: 'P.M4(System.Func<object>, System.Action<object>, System.Action<string>)' and 'P.M4(System.Func<System.ValueType>, System.Action<string>, System.Action<object>)'
//     M4(()=>{ throw new NotImplementedException();}, q=> {}, q=> {});
Diagnostic(ErrorCode.ERR_AmbigCall, "M4").WithArguments("P.M4(System.Func<object>, System.Action<object>, System.Action<string>)", "P.M4(System.Func<System.ValueType>, System.Action<string>, System.Action<object>)"),
// (21,5): error CS0121: The call is ambiguous between the following methods or properties: 'P.M6(System.Action<object>, string, object)' and 'P.M6(System.Action<string>, object, string)'
//     M6(q=> {},(string)null, (string)null);
Diagnostic(ErrorCode.ERR_AmbigCall, "M6").WithArguments("P.M6(System.Action<object>, string, object)", "P.M6(System.Action<string>, object, string)")
                );

            // By comparing these two programs, it becomes clear how unfortunate this is. M(q=>null) is ambiguious,
            // M(null) is unambiguous. But M((string)null, q=>{}) is unambiguous, M((string)null, null) is ambiguous!

            string source3 = @"
using System;
using System.Collections.Generic;

class SyntaxNode {}

class ExpressionSyntax : SyntaxNode {}

class IdentifierNameSyntax : ExpressionSyntax {}

class SyntaxAnnotation {}

static class P
{
    public static TRoot ReplaceNodes1<TRoot>(this TRoot root, IEnumerable<SyntaxNode> nodes, Func<SyntaxNode, SyntaxNode, SyntaxNode> computeReplacementNode)
        where TRoot : SyntaxNode
    {
        Console.Write('A');
        return null;
    }

    public static TRoot ReplaceNodes1<TRoot, TNode>(this TRoot root, IEnumerable<TNode> nodes, Func<TNode, TNode, SyntaxNode> computeReplacementNode)
        where TRoot : SyntaxNode
        where TNode : SyntaxNode
    {
        Console.Write('B');
        return null;
    }

    public static TNode WithAdditionalAnnotations<TNode>(this TNode node, params SyntaxAnnotation[] annotations) 
        where TNode : SyntaxNode
    {
        return null;
    }

    public static TRoot ReplaceNodes2<TRoot, TNode>(this TRoot root, IEnumerable<TNode> nodes, Func<TNode, TNode, SyntaxNode> computeReplacementNode)
        where TRoot : SyntaxNode
        where TNode : SyntaxNode
    {
        Console.Write('B');
        return null;
    }

    public static TRoot ReplaceNodes2<TRoot>(this TRoot root, IEnumerable<SyntaxNode> nodes, Func<SyntaxNode, SyntaxNode, SyntaxNode> computeReplacementNode)
        where TRoot : SyntaxNode
    {
        Console.Write('A');
        return null;
    }

    static void Main()
    {
        ExpressionSyntax expr = null;
        var identifierNodes = new List<IdentifierNameSyntax>();
        var myAnnotation = new SyntaxAnnotation();
        expr.ReplaceNodes1(identifierNodes, (e, e2) => e2.WithAdditionalAnnotations(myAnnotation));
        expr.ReplaceNodes2(identifierNodes, (e, e2) => e2.WithAdditionalAnnotations(myAnnotation));
    }
}";

            CompileAndVerify(source3, additionalRefs: new[] { SystemCoreRef }, expectedOutput: @"BB");

        }

        [Fact]
        public void DeviationFromSpec()
        {

            string source1 = @"
using System;
class P
{
  static void M1(int a) { Console.Write(1); }
  static void M1(uint? a) { Console.Write(2); }
  static void M2(int? a) { Console.Write(3); }
  static void M2(uint a) { Console.Write(4); }
  static void Main()
  {
    int i = 0;
    int? ni = 0;
    uint u = 0;
    uint? nu = 0;
    short s = 0;
    short? ns = 0;
    ushort us = 0;
    ushort? nus = 0;
    
    M1(null);
    M1(i);
    Console.Write("" "");//M1(ni);
    M1(u);
    M1(nu);
    M1(s);
    Console.Write("" "");//M1(ns);
    M1(us);
    M1(nus);

    M2(null);
    M2(i);
    M2(ni);
    M2(u);
    Console.Write("" "");//M2(nu);
    M2(s);
    M2(ns);
    M2(us);
    M2(nus);
  }
}";

            CompileAndVerify(source1, expectedOutput: @"21 221 123334 3333");

            string source2 = @"
using System;
class P
{
  static void M1(int a) { Console.Write(1); }
  static void M1(uint? a) { Console.Write(2); }
  static void M2(int? a) { Console.Write(3); }
  static void M2(uint a) { Console.Write(4); }
  static void Main()
  {
    int? ni = 0;
    uint? nu = 0;
    short? ns = 0;
    //ushort us = 0;
    
    M1(ni);
    M1(ns);
    M2(nu);
  }
}";

            CreateCompilationWithMscorlib(source2).VerifyDiagnostics(
// (16,8): error CS1503: Argument 1: cannot convert from 'int?' to 'int'
//     M1(ni);
Diagnostic(ErrorCode.ERR_BadArgType, "ni").WithArguments("1", "int?", "int"),
// (17,8): error CS1503: Argument 1: cannot convert from 'short?' to 'int'
//     M1(ns);
Diagnostic(ErrorCode.ERR_BadArgType, "ns").WithArguments("1", "short?", "int"),
// (19,8): error CS1503: Argument 1: cannot convert from 'uint?' to 'int?'
//     M2(nu);
Diagnostic(ErrorCode.ERR_BadArgType, "nu").WithArguments("1", "uint?", "int?")
                );
        }


        [Fact]
        public void ParametersExactlyMatchExpression()
        {
            string source2 = @"
using System;
class P
{
  delegate int DA();    
  delegate int DB();    

  static void M1(DA a) { Console.Write(1); }
  static void M1(DB a) { Console.Write(2); }

  static void Main()
  {
    int x = 1;
    M1(() => x);
  }
}";

            CreateCompilationWithMscorlib(source2).VerifyDiagnostics(
// (14,5): error CS0121: The call is ambiguous between the following methods or properties: 'P.M1(P.DA)' and 'P.M1(P.DB)'
//     M1(() => x);
Diagnostic(ErrorCode.ERR_AmbigCall, "M1").WithArguments("P.M1(P.DA)", "P.M1(P.DB)")
                );
        }

        [Fact]
        public void ExactlyMatchingNestedLambda()
        {

            string source1 = @"
using System;
class P
{
  delegate Func<int> DA();    
  delegate Func<object> DB();    

  static void M1(DA a) { Console.Write(1); }
  static void M1(DB a) { Console.Write(2); }

  static void Main()
  {
    int i = 0;
    
    M1(() => () => i);
  }
}";

            CompileAndVerify(source1, expectedOutput: @"1");

            string source2 = @"
using System;
class P
{
  delegate Func<int> DA();    
  delegate Func<object> DB();    

  static void M1(DA a, object b) { Console.Write(1); }
  static void M1(DB a, int b) { Console.Write(2); }

  static void Main()
  {
    int i = 0;
    
    M1(() => () => i, i);
  }
}";

            CompileAndVerify(source2, expectedOutput: @"2");
        }

        [Fact]
        public void ParametersImplicitlyConvertableToEachOther()
        {

            string source1 = @"
using System;

class CA
{
    public static implicit operator CA(int x)
    {
        return null;
    }
    public static implicit operator CA(CB x)
    {
        return null;
    }
}

class CB
{
    public static implicit operator CB(int x)
    {
        return null;
    }
    public static implicit operator CB(CA x)
    {
        return null;
    }
}

class P
{
  static void M1(CA a) { Console.Write(1); }
  static void M1(CB a) { Console.Write(2); }

  static void Main()
  {
    int i = 0;
    M1(i);
  }
}";

            CreateCompilationWithMscorlib(source1).VerifyDiagnostics(
// (36,5): error CS0121: The call is ambiguous between the following methods or properties: 'P.M1(CA)' and 'P.M1(CB)'
//     M1(i);
Diagnostic(ErrorCode.ERR_AmbigCall, "M1").WithArguments("P.M1(CA)", "P.M1(CB)")
                );
        }

        [Fact]
        public void BetterTaskType()
        {

            string source1 = @"
using System;
using System.Threading.Tasks;
class P
{
  static void M1(Task<int> a) { Console.Write(1); }
  static void M1(Task<uint> a) { Console.Write(2); }

  static void Main()
  {
    M1(null);
  }
}";

            CompileAndVerify(source1, expectedOutput: @"1");

            string source2 = @"
using System;
using System.Threading.Tasks;
class P
{
  static void M1(Task<int> a, uint b) { Console.Write(1); }
  static void M1(Task<uint> a, int b) { Console.Write(2); }

  static void Main()
  {
    M1(null,0);
  }
}";

            CreateCompilationWithMscorlib(source2).VerifyDiagnostics(
// (11,5): error CS0121: The call is ambiguous between the following methods or properties: 'P.M1(System.Threading.Tasks.Task<int>, uint)' and 'P.M1(System.Threading.Tasks.Task<uint>, int)'
//     M1(null,0);
Diagnostic(ErrorCode.ERR_AmbigCall, "M1").WithArguments("P.M1(System.Threading.Tasks.Task<int>, uint)", "P.M1(System.Threading.Tasks.Task<uint>, int)")
                );
        }

        [Fact]
        public void BetterDelegateType()
        {

            string source1 = @"
using System;

class P
{
  static void M1(Func<int> a) { Console.Write(1); }
  static void M1(Func<uint> a) { Console.Write(2); }
  static void M2(Func<int> a) { Console.Write(3); }
  static void M2(Action a) { Console.Write(4); }

  static void Main()
  {
    M1(null);
    M2(null);
  }
}";

            CompileAndVerify(source1, expectedOutput: @"13");

            string source2 = @"
using System;

class P
{
  static void M1(Func<int> a, uint b) { Console.Write(1); }
  static void M1(Func<uint> a, int b) { Console.Write(2); }
  static void M2(Func<int> a, uint b) { Console.Write(3); }
  static void M2(Action a, int b) { Console.Write(4); }

  static void Main()
  {
    M1(null,0);
    M2(null,0);
  }
}";

            CreateCompilationWithMscorlib(source2).VerifyDiagnostics(
// (13,5): error CS0121: The call is ambiguous between the following methods or properties: 'P.M1(System.Func<int>, uint)' and 'P.M1(System.Func<uint>, int)'
//     M1(null,0);
Diagnostic(ErrorCode.ERR_AmbigCall, "M1").WithArguments("P.M1(System.Func<int>, uint)", "P.M1(System.Func<uint>, int)"),
// (14,5): error CS0121: The call is ambiguous between the following methods or properties: 'P.M2(System.Func<int>, uint)' and 'P.M2(System.Action, int)'
//     M2(null,0);
Diagnostic(ErrorCode.ERR_AmbigCall, "M2").WithArguments("P.M2(System.Func<int>, uint)", "P.M2(System.Action, int)")
                );
        }

        [Fact]
        public void TestBug9851()
        {
            // We should ensure that we do not report "no method M takes n parameters" if in fact
            // there is any method M that could take n parameters.
            TestAllErrors(
@"
class C
{
    static void J<T>(T t1, T t2) {}
    static void J(int x) {}
    public static void M()
    {
        J(123.0, 456.0m);
    }
}",
"'J' error CS0411: The type arguments for method 'C.J<T>(T, T)' cannot be inferred from the usage. Try specifying the type arguments explicitly."

);
        }

        [Fact]
        public void TestLambdaErrorReporting()
        {

            TestAllErrors(
@"
using System;
class C
{
    static void J(Action<int> action) {}
    static void J(Action<string> action) {}

    static void K(Action<decimal> action) {}
    static void K(Action<double> action) {}
    static void K(Action<string> action) {}

    public static void M()
    {
        // If there are multiple possible bindings for a lambda and both of them produce
        // 'the same' errors then we should report those errors to the exclusion of any
        // errors produced on only some of the bindings.
        //
        // For instance, here the binding of x as int produces two errors: 
        // * int does not have ToStrign
        // * int does not have Length
        // the binding of x as string produces two errors:
        // * string does not have ToStrign
        // * cannot multiply strings
        // We should only report the common error.

        J(x=>{ Console.WriteLine(x.ToStrign(), x.Length, x * 2); });

        // If there is no common error then we should report both errors:

        J(y=>{ Console.WriteLine(y == string.Empty, y / 4.5); });

        // If there is an error that is in common to two of three bindings,
        // then we should report it but only report it once.
        // For instance, here the binding of x as decimal produces:
        // * no decimal == string
        // * no decimal - double
        // The binding as double produces:
        // * no double == string
        // The binding as string produces:
        // * no string - double
        //
        // There is no error common to all three bindings. However, of the 
        // four errors we should only report two of them.

        K(z=>{ Console.WriteLine(z == string.Empty, z - 4.5); });
    }
}",
          "'ToStrign' error CS1061: 'string' does not contain a definition for 'ToStrign' and no extension method 'ToStrign' accepting a first argument of type 'string' could be found (are you missing a using directive or an assembly reference?)",
          "'y / 4.5' error CS0019: Operator '/' cannot be applied to operands of type 'string' and 'double'",
          "'y == string.Empty' error CS0019: Operator '==' cannot be applied to operands of type 'int' and 'string'",
          "'z - 4.5' error CS0019: Operator '-' cannot be applied to operands of type 'string' and 'double'",
          "'z == string.Empty' error CS0019: Operator '==' cannot be applied to operands of type 'double' and 'string'"

);
        }


        [Fact]
        public void TestOverloadResolutionTiebreaker()
        {
            // The native compiler gets this one wrong, giving an ambiguity error. 
            // The correct analysis is that there are four candidates:
            // 0: X(params string[]) in normal form
            // 1: X(params string[]) in expanded form
            // 2: X<string>(string)
            // 3: X(string, object) with default value for second parameter
            //
            // Candidate 0 is obviously inapplicable. The other three have identical formal parameter types
            // corresponding to the given arguments, so we must go to tiebreaker round. The tiebreakers are:
            // * Nongeneric beats generic, and then
            // * applicable in normal form beats applicable only in expanded form, then
            // * more parameters declared beats fewer parameters declared
            // By these rules: 1 beats 2, 3 beats 1, 3 beats 2. 3 is unbeaten and beats at least one
            // other candidate. No other candidate has this property, so candidate 3 should win.

            string source = @"
class C 
{
    static void X(params string[] s) {}
    static void X<T>(T t){}
    static void X(string s, object o = null) {}
    public void M()
    {
        X((string)null); //-C.X(string, object)
    }
}";

            TestOverloadResolutionWithDiff(source);
        }


        [Fact]
        void TestConstraintViolationApplicabilityErrors()
        {
            // The rules for constraint satisfaction during overload resolution are a bit odd. If a constraint
            // *on a formal parameter type* is not met then the candidate is not applicable. But if a constraint
            // is violated *on the method type parameter itself* then the method can be chosen as the best
            // applicable candidate, and then rejected during "final validation".
            //
            // Furthermore: most of the time a constraint violation on a formal type parameter will also 
            // be a constraint violation on the method type parameter. The latter seems like the better 
            // error to report. We only report the violation on the formal parameter if the constraint
            // is not violated on the method type parameter.

            TestAllErrors(
@"
class C
{
    static string MakeString() { return null; }
    
    struct L<S> where S : struct {}
    class N<T> where T : struct {}
    public static void M()
    {
        string s = MakeString();

        // We violate the constraint on both T and U. 
        // The method is not applicable.
        // Overload resolution fails and reports the violation on U,
        // even though technically it was the violation on U that caused
        // the method to be inapplicable.
        Test1<string>(s, null);

        // Type inference successfully infers that V is string; 
        // we now should do exactly the same as the previous case.
        Test2(s, null);

        // In the previous two tests it is not clear whether the compiler is
        // allowing overload resolution to succeed and then final validation 
        // fails, or if the candidate set really is empty. We must verify
        // that the generic version is actually an inapplicable candidate.
        //
        // Even though its arguments under construction are better,
        // the generic version is inapplicable because the constraint
        // on T is violated. Therefore there is no error; the object
        // version wins:
        Test3(s, null);

        // By contrast, here overload resolution infers that X<string> is the
        // best possible match, and then final validation fails:
        Test4(s);

        // When a method is inapplicable because of a constraint violation we
        // prefer to state the violation on the method type parameter constraint.
        // In an error recovery scenario we might not be able to do that.
        // Here there are two errors: first, the declaration of Test5 is bad because 
        // Y does not meet the constraint on T. Second, the method call is bad
        // because string does not meet the constraint on T. We cannot say that
        // string does not meet the constraint on Y because, erroneously, there is
        // no such constraint.

        Test5(s, null);

        // Here we have another error recovery scenario. L<string> is clearly
        // illegal, but what if we try to do overload resolution anyway?
        // The constraint is not violated on Z because L<string> is a struct.
        // Overload resolution fails because the constraint on L is violated in
        // N<L<string>>. Thus that is the overload resolution error we report.
        // We therefore end up reporting this error twice, unfortunately; we 
        // should consider putting some gear in place to suppress the cascading
        // error.

        Test6<L<string>>(null);
    }
    
    static void Test1<U>(U u, N<U> nu) where U : struct { }
    static void Test2<V>(V v, N<V> nv) where V : struct { }
    static void Test3<W>(W w, N<W> nw) where W : struct { }
    static void Test3(object o1, object o2) {}
    static void Test4<X>(X x) where X : struct { }
    static void Test4(object x) {}
    static void Test5<Y>(Y y, N<Y> ny) { }
    static void Test6<Z>(N<Z> nz) where Z : struct {}
}",
"'ny' error CS0453: The type 'Y' must be a non-nullable value type in order to use it as parameter 'T' in the generic type or method 'C.N<T>'",
"'Test1<string>' error CS0453: The type 'string' must be a non-nullable value type in order to use it as parameter 'U' in the generic type or method 'C.Test1<U>(U, C.N<U>)'",
"'Test2' error CS0453: The type 'string' must be a non-nullable value type in order to use it as parameter 'V' in the generic type or method 'C.Test2<V>(V, C.N<V>)'",
"'Test4' error CS0453: The type 'string' must be a non-nullable value type in order to use it as parameter 'X' in the generic type or method 'C.Test4<X>(X)'",
"'Test5' error CS0453: The type 'string' must be a non-nullable value type in order to use it as parameter 'T' in the generic type or method 'C.N<T>'",
"'string' error CS0453: The type 'string' must be a non-nullable value type in order to use it as parameter 'S' in the generic type or method 'C.L<S>'",
"'Test6<L<string>>' error CS0453: The type 'string' must be a non-nullable value type in order to use it as parameter 'S' in the generic type or method 'C.L<S>'");
        }

        [Fact]
        void TestBug9583()
        {
            TestErrors(
@"
class C
{
    public static void M()
    {
        Foo();
    }
    static void Foo<T>(params T[] x) { }
}",

"'Foo' error CS0411: The type arguments for method 'C.Foo<T>(params T[])' cannot be inferred from the usage. Try specifying the type arguments explicitly.");

        }

        [Fact]
        public void TestMoreOverloadResolutionErrors()
        {
            TestErrors(@"
class C 
{ 
    static void VoidReturning() {}
    static void M() 
    {
        byte b = new byte(1);
        System.Console.WriteLine(VoidReturning());
    }
}",
"'byte' error CS1729: 'byte' does not contain a constructor that takes 1 arguments",
//"'System.Console.WriteLine' error CS1502: The best overloaded method match for 'System.Console.WriteLine(bool)' has some invalid arguments",  //specifically omitted by roslyn
"'VoidReturning()' error CS1503: Argument 1: cannot convert from 'void' to 'bool'"
               );
        }

        [Fact]
        void TestBug6156()
        {
            TestOverloadResolutionWithDiff(
@"
class C
{
    public static void M()
    {
        int y = 123;
        Out2 o2 = new Out2();
        Ref2 r2 = o2;
        Out1 o1 = o2;
        Ref1 r1 = o2;

        r1.M(ref y); //-Ref1.M(ref int)
        o1.M(ref y); //-Ref1.M(ref int)
        o1.M(out y); //-Out1.M(out int)
        r2.M(ref y); //-Ref2.M(ref int)
        r2.M(out y); //-Out1.M(out int)
        o2.M(ref y); //-Ref2.M(ref int)
        o2.M(out y); //-Out2.M(out int)
    }
}
class Ref1
{
    public virtual void M(ref int x) { x = 1; } // SLOT1
}
class Out1 : Ref1
{
    public virtual void M(out int x) { x = 2; } // SLOT2
}
class Ref2 : Out1
{
    public override void M(ref int x) { x = 3; } 
    // CLR says this overrides SLOT2, even though there is a ref/out mismatch with Out1.
    // C# says this overrides SLOT1
}
class Out2 : Ref2
{
    public override void M(out int x) { x = 4; } 
    // CLR says this overrides SLOT2, even though there is a ref/out mismatch with Ref2.M.
    // C# says this overrides SLOT2
}");
        }



        [Fact]
        void TestGenericMethods()
        {
            TestOverloadResolutionWithDiff(
@"
class C 
{ 
    class D<T> 
    { 
        public static void N<U>(U u){} 
        public class E<V>  
        {
            public static void O<W>(W w){}
        }
    }
    void M()
    {
        D<int>.N<byte>(1); //-C.D<int>.N<byte>(byte)
        D<int>.E<double>.O<short>(1); //-C.D<int>.E<double>.O<short>(short)
    }
}");
        }


        [Fact]
        void TestDelegateBetterness()
        {
            TestOverloadResolutionWithDiff(
@"
delegate void Action();
delegate void Action<in A>(A a);
delegate R Func<out R>();
delegate R Func<in A, out R>(A a);
delegate R Func2<in A, out R>(A a);

class Animal {}
class Mammal : Animal {}
class Tiger : Mammal {}

class C 
{ 
    static void N1(Func<object> f){}
    static void N1(Func<string> f){}

    static void N2(Func<int, object> f){}
    static void N2(Action<int> f){}

    static void N3(Func<int, Tiger> f){}
    static void N3(Func2<int, Mammal> f){}

    static void N4(Func<int, Animal> f){}
    static void N4(Func2<int, Mammal> f){}


    void M() 
    { 
        // If we have a lambda argument and two delegates, the rules are:

        // First, if one delegate is convertible to the other but not vice-versa
        // then the more specific delegate wins. A Func<string> is convertible to Func<object>
        // but not vice-versa, so Func<string> must be more specific:

        // This test is disabled; see the comments to TestBug11961 above.
        // N1(()=>null); 

        // Second, if the delegates have identical parameters, then the non-void one wins.
        // This lambda could be both an action and a func; we don't know if the construction
        // is being done for its value or for its side effects. 

        N2(x=>new System.Object()); //-C.N2(Func<int, object>)

        // Third, if the delegates have identical parameters and both have a return type
        // and the lambda has an inferred return type, then the better delegate is the
        // one where the delegate return type exactly matches the lambda return type.

        N3(x=>new Tiger()); //-C.N3(Func<int, Tiger>)

        // Fourth, if the delegate return type does not exactly match the lambda return
        // type then the most specific delegate return type wins.

        N4(x=>new Tiger()); //-C.N4(Func2<int, Mammal>)
    }
}
");
        }

        [Fact]
        void TestTieBreakers()
        {
            TestOverloadResolutionWithDiff(
@"

class C 
{ 
    class D<TD>{}
 
    void N1<T1>(T1 p1) {}
    void N1(int p1) {}

    void N2(int p1) {}
    void N2(params int[] p1) {}

    void N3(int p1, int p2, params int[] p3) {}
    void N3(int p1, params int[] p3) {}

    void N4(int p1, int p2 = 0) {}
    void N4(int p1) {}

    void N51<T51>(T51 p1, double p2 = 0) {}
    void N51<T51>(int p1, string p2 = null ) {}

    void N52<T52>(D<T52> p1, double p2 = 0) {}
    void N52<T52>(D<int> p1, string p2 = null ) {}

    void N53<T53>(T53[] p1, double p2 = 0) {}
    void N53<T53>(int[] p1, string p2 = null ) {}

    
    void M() 
    { 
        // If due to construction or dynamicness all the effective parameters of two methods are identical 
        // then we do a series of tiebreaking rules.

        // 1: A generic method is worse than a non-generic method.
        N1(123); //-C.N1(int)

        // 2: A method applicable in normal form is better than one applicable only in expanded form.
        N2(123); //-C.N2(int)

        // 3: If both methods are applicable in expanded form then the one with more 'real' parameters wins.
         
        N3(1, 2, 3, 4, 5, 6); //-C.N3(int, int, params int[])

        // 4: If one method has no default arguments substituted and the other has one or more, the one
        // with no defaults wins.
        
        N4(1); //-C.N4(int)

        // 5: The more specific method wins. One method's declared parameter types list is more specific than
        // anothers if, for each position in the list, the type of one is not less specific than that
        // of the other, and, for at least one position, the type is more specific than that of the other.

        // 5.1: A type parameter is less specific than any other type.
        // 5.2: A constructed type C<X, Y> is less specific than C<A, B> if the type list <X, Y> is less specific than <A, B>
        // 5.3: An array type X[] is less specific than Y[] if X is less specific than Y.
        // 5.4: NOT TESTED: A pointer type X* is less specific than Y* if X is less specific than Y*.

//EDMAURER removed the next three tests that fail due to the fact that omitted optional parameters are not supported.
        // N51<int>(123); //C.N51<int>(int, string)
        // N52<int>(null); //C.N52<int>(C.D<int>, string)
        // N53<int>(null); //C.N53<int>(int[], string)
        
        // 6: NOT TESTED: A non-lifted operator is better than a lifted operator.

    }
}
");
        }

        [WorkItem(540153)]
        [Fact]
        public void TestOverridingMismatchedParamsErrorCase_Source()
        {
            // Tests:
            // Replace params with non-params in signature of overridden member (and vice-versa)

            var source = @"
abstract class Base
{
    public abstract void Method1(Derived c1, Derived c2, params Derived[] c3);
    public abstract void Method2(Derived c1, Derived c2, Derived[] c3);
}
class Derived : Base
{
    public override void Method1(Derived C1, Derived C2, Derived[] C3) { } //removes 'params'
    public override void Method2(Derived C1, Derived C2, params Derived[] C3) { } //adds 'params'
}
class Test2
{
    public static void Main2()
    {
        Derived d = new Derived();
        Base b = d;
        b.Method1(d, d, d, d, d); // Fine
        d.Method1(d, d, d, d, d); // Fine
        b.Method2(d, d, d, d, d); // Should report error - No overload for Method2 takes 5 arguments
        d.Method2(d, d, d, d, d); // Should report error - No overload for Method2 takes 5 arguments
    }
}";
            CreateCompilationWithMscorlib(source).VerifyDiagnostics(
                Diagnostic(ErrorCode.ERR_BadArgCount, "b.Method2").WithArguments("Method2", "5"),
                Diagnostic(ErrorCode.ERR_BadArgCount, "d.Method2").WithArguments("Method2", "5"));
        }

        [Fact]
        public void TestImplicitImplMismatchedParamsErrorCase_Source()
        {
            // Tests:
            // Replace params with non-params in signature of implemented member (and vice-versa)

            var source = @"
interface Base
{
    void Method1(Derived c1, Derived c2, params Derived[] c3);
    void Method2(Derived c1, Derived c2, Derived[] c3);
}
class Derived : Base
{
    public void Method1(Derived C1, Derived C2, Derived[] C3) { } //removes 'params'
    public void Method2(Derived C1, Derived C2, params Derived[] C3) { } //adds 'params'
}
class Test2
{
    public static void Main2()
    {
        Derived d = new Derived();
        Base b = d;
        b.Method1(d, d, d, d, d); // Fine
        d.Method1(d, d, d, d, d); // Should report error - No overload for Method1 takes 5 arguments
        b.Method2(d, d, d, d, d); // Should report error - No overload for Method2 takes 5 arguments
        d.Method2(d, d, d, d, d); // Fine
    }
}";
            CreateCompilationWithMscorlib(source).VerifyDiagnostics(
                Diagnostic(ErrorCode.ERR_BadArgCount, "d.Method1").WithArguments("Method1", "5"),
                Diagnostic(ErrorCode.ERR_BadArgCount, "b.Method2").WithArguments("Method2", "5"));
        }

        [Fact]
        public void TestExplicitImplMismatchedParamsErrorCase_Source()
        {
            // Tests:
            // Replace params with non-params in signature of implemented member (and vice-versa)

            var source = @"
interface Base
{
    void Method1(Derived c1, Derived c2, params Derived[] c3);
    void Method2(Derived c1, Derived c2, Derived[] c3);
}
class Derived : Base
{
    void Base.Method1(Derived C1, Derived C2, Derived[] C3) { } //removes 'params'
    void Base.Method2(Derived C1, Derived C2, params Derived[] C3) { } //adds 'params' - CS0466
}
class Test2
{
    public static void Main2()
    {
        Derived d = new Derived();
        Base b = d;
        b.Method1(d, d, d, d, d); // Fine
        b.Method2(d, d, d, d, d); // Should report error - No overload for Method2 takes 5 arguments
    }
}";
            CreateCompilationWithMscorlib(source).VerifyDiagnostics(
                // (10,15): error CS0466: 'Derived.Base.Method2(Derived, Derived, params Derived[])' should not have a params parameter since 'Base.Method2(Derived, Derived, Derived[])' does not
                Diagnostic(ErrorCode.ERR_ExplicitImplParams, "Method2").WithArguments("Derived.Base.Method2(Derived, Derived, params Derived[])", "Base.Method2(Derived, Derived, Derived[])"),
                // (19,9): error CS1501: No overload for method 'Method2' takes 5 arguments
                Diagnostic(ErrorCode.ERR_BadArgCount, "b.Method2").WithArguments("Method2", "5"));
        }

        [WorkItem(540153)]
        [WorkItem(540406)]
        [Fact]
        public void TestOverridingMismatchedParamsErrorCase_Metadata()
        {
            // Tests:
            // Replace params with non-params in signature of overridden member (and vice-versa)

            var ilSource = @"
.class public abstract auto ansi beforefieldinit Base
       extends [mscorlib]System.Object
{
  .method public hidebysig newslot abstract virtual 
          instance void  Method1(class Derived c1,
                                 class Derived c2,
                                 class Derived[] c3) cil managed
  {
    .param [3]
    .custom instance void [mscorlib]System.ParamArrayAttribute::.ctor() = {}
  } // end of method Base::Method1

  .method public hidebysig newslot abstract virtual 
          instance void  Method2(class Derived c1,
                                 class Derived c2,
                                 class Derived[] c3) cil managed
  {
  } // end of method Base::Method2

  .method family hidebysig specialname rtspecialname 
          instance void  .ctor() cil managed
  {
    // Code size       7 (0x7)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  call       instance void [mscorlib]System.Object::.ctor()
    IL_0006:  ret
  } // end of method Base::.ctor

} // end of class Base

.class public auto ansi beforefieldinit Derived
       extends Base
{
  .method public hidebysig virtual instance void 
          Method1(class Derived C1,
                  class Derived C2,
                  class Derived[] C3) cil managed
  {
    ret
  } // end of method Derived::Method1

  //// Adds 'params' ////
  .method public hidebysig virtual instance void 
          Method2(class Derived C1,
                  class Derived C2,
                  class Derived[] C3) cil managed
  {
    .param [3]
    .custom instance void [mscorlib]System.ParamArrayAttribute::.ctor() = {}
	ret
  } // end of method Derived::Method2

  //// Removes 'params' ////
  .method public hidebysig specialname rtspecialname 
          instance void  .ctor() cil managed
  {
    // Code size       7 (0x7)
    .maxstack  8
    IL_0000:  ldarg.0
    IL_0001:  call       instance void Base::.ctor()
    IL_0006:  ret
  } // end of method Derived::.ctor

} // end of class Derived
";
            var csharpSource = @"
class Test2
{
    public static void Main2()
    {
        Derived d = new Derived();
        Base b = d;
        b.Method1(d, d, d, d, d); // Fine
        d.Method1(d, d, d, d, d); // Fine
        b.Method2(d, d, d, d, d); // Should report error - No overload for Method2 takes 5 arguments
        d.Method2(d, d, d, d, d); // Should report error - No overload for Method2 takes 5 arguments
    }
}";
            // Same errors as in source case
            var comp = CreateCompilationWithCustomILSource(csharpSource, ilSource);
            comp.VerifyDiagnostics(
                Diagnostic(ErrorCode.ERR_BadArgCount, "b.Method2").WithArguments("Method2", "5"),
                Diagnostic(ErrorCode.ERR_BadArgCount, "d.Method2").WithArguments("Method2", "5"));
        }

        [WorkItem(6353, "DevDiv_Projects/Roslyn")]
        [Fact()]
        void TestBaseAccessForAbstractMembers()
        {
            // Tests:
            // Override virtual member with abstract member – override this abstract member in further derived class
            // Test that call to abstract member fails when calling through "base."

            var source = @"
abstract class Base<T, U>
{
    T f = default(T);
    public abstract void Method(T i, U j);
    public virtual T Property
    {
        get { return f; }
        set { }
    }
}
class Base2<A, B> : Base<A, B>
{
    public override void Method(A a, B b)
    {
        base.Method(a, b); // Error - Cannot call abstract base member
    }
    public override A Property { set { } }
}
abstract class Base3<T, U> : Base2<T, U>
{
    public override abstract void Method(T x, U y);
    public override abstract T Property { set; }
}
class Base4<U, V> : Base3<U, V>
{
    U f;
    public override void Method(U x, V y)
    {
        base.Method(x, y); // Error - Cannot call abstract base member
    }
    public override U Property
    {
        set
        {
            f = base.Property; // No error - Only setter is abstract in base class
            base.Property = f; // Error - Cannot call abstract base member
        }
    }
}";

            CreateCompilationWithMscorlib(source).VerifyDiagnostics(
                Diagnostic(ErrorCode.ERR_AbstractBaseCall, "base.Method(a, b)").WithArguments("Base<A, B>.Method(A, B)"),
                Diagnostic(ErrorCode.ERR_AbstractBaseCall, "base.Method(x, y)").WithArguments("Base3<U, V>.Method(U, V)"),
                Diagnostic(ErrorCode.ERR_AbstractBaseCall, "base.Property").WithArguments("Base3<U, V>.Property"));
        }

        [WorkItem(6353, "DevDiv_Projects/Roslyn")]
        [Fact()]
        void TestBaseAccessForAbstractMembers1()
        {
            // Tests:
            // Override virtual member with abstract member – override this abstract member in further derived class
            // Test that assigning an abstract member referenced through "base." to a delegate fails

            var source = @"
using System;

abstract class Base<T, U>
{
    public abstract void Method(T i, U j);
}
class Base2<A, B> : Base<A, B>
{
    public override void Method(A a, B b)
    {
        Action<A, B> m = base.Method; // Error - Cannot call abstract base member
    }
}";

            CreateCompilationWithMscorlib(source).VerifyDiagnostics(
                Diagnostic(ErrorCode.ERR_AbstractBaseCall, "base.Method").WithArguments("Base<A, B>.Method(A, B)"));
        }

        [WorkItem(6353, "DevDiv_Projects/Roslyn")]
        [Fact()]
        void TestBaseAccessForAbstractMembers2()
        {
            var source = @"
namespace A
{
    abstract class Base<T>
    {
        public abstract T Method(int x);
    }
    abstract class Base2<A> : Base<A>
    {
        A f = default(A);
        public override A Method(int x) { return f; }
        public abstract A Method(A x);
    }
    class Derived : Base2<long>
    {
        // Surprisingly in Dev10 base.Method seems to bind to the second overload above and reports error (can't call abstract method)
        public override long Method(int x) { base.Method(x); return 1; }
        public override long Method(long x) { return 2; }
    }
}
namespace B
{
    abstract class Base2<A>
    {
        A f = default(A);
        public virtual A Method(int x) { return f; }
        public abstract A Method(A x);
    }
    class Derived : Base2<long>
    {
        // But the same call seems to work in this case in Dev10 i.e. base.Method correctly binds to the first overload
        public override long Method(int x) { base.Method(x); return 1; }
        public override long Method(long x) { return 2; }
    }
}
";

            CreateCompilationWithMscorlib(source).VerifyDiagnostics(
                Diagnostic(ErrorCode.ERR_AbstractBaseCall, "base.Method(x)").WithArguments("A.Base2<long>.Method(long)"));
        }

        [Fact]
        public void Bug8766_ConstructorOverloadResolution_PrivateCtor()
        {
            var source =
@"using System;
 
public class A
{
    const int C = 1;
 
    private A(int x) { Console.WriteLine(""int""); }
 
    public A(long x) { Console.WriteLine(""long""); }
 
    public void Foo() { A a = new A(C); }
    
    static void Main()
    {
        A a = new A(C);
        a.Foo();
        B.Foo();
    }
}

public class B
{
    const int C = 1;
    public static void Foo() { A a = new A(C);}
}
";

            CompileAndVerify(source, expectedOutput: @"int
int
long
");

        }

        [Fact]
        public void Bug8766_ConstructorOverloadResolution_ProtectedCtor()
        {
            var source =
@"using System;
 
public class A
{
    const int C = 1;
 
    protected A(int x) { Console.WriteLine(""int""); }
 
    public A(long x) { Console.WriteLine(""long""); }
 
    public void Foo() { A a = new A(C); }
    
    static void Main()
    {
        A a = new A(C);
        a.Foo();
        B.Foo();
    }
}

public class B
{
    const int C = 1;
    public static void Foo() { A a = new A(C);}
}
";

            CompileAndVerify(source, expectedOutput: @"int
int
long
");

        }

        [Fact, WorkItem(546694)]
        public void Bug16581_ConstructorOverloadResolution_BaseClass()
        {
            var source = @"
using System;

class A
{
    private A(int x) { }
    public A(long x)
    {
        Console.WriteLine(""PASS"");
    }
 
    private void M(int x) { }
    public void M(long x)
    {
        Console.WriteLine(""PASS"");
    }
}
 
class B: A
{
    public B(): base(123)
    {
        base.M(123);
    }

    public static void Main()
    {
        var unused = new B();
    }
}
";
            CompileAndVerify(source, expectedOutput: @"PASS
PASS");

        }

        [Fact, WorkItem(529847)]
        public void Bug14585_ConstructorOverloadResolution_BaseClass()
        {
            var source = @"
public class Base
{
    protected Base()
    {
    }
 
    public Base(int i)
    {
    }
}
 
class Test
{
    static void Main(string[] args)
    {
        var a = new Base();
    }
}
";
            CreateCompilationWithMscorlib(source).VerifyDiagnostics(
                // (17,21): error CS0122: 'Base.Base()' is inaccessible due to its protection level
                //         var a = new Base();
                Diagnostic(ErrorCode.ERR_BadAccess, "Base").WithArguments("Base.Base()"));

        }

        [Fact]
        public void Bug8766_MethodOverloadResolution()
        {
            var source =
@"using System;

public class A
{
    const int C = 1;
 
    void AA(int x) { Console.WriteLine(""int""); }
 
    public void AA(long x) { Console.Write(""long""); }
 
    public void Foo() { A a = new A(); a.AA(C); }

    static void Main()
    {
        A a = new A();
        a.Foo();
        B.Foo();
    }
}

public class B
{
    const int C = 1;
    public static void Foo() { A a = new A(); a.AA(C);}
}

";
            CompileAndVerify(source, expectedOutput: @"int
long
");

        }

        [Fact]
        public void RegressionTestForIEnumerableOfDynamic()
        {
            TestOverloadResolutionWithDiff(
@"using System;
using System.Collections.Generic;

class C
{
    class DynamicWrapper
    {
        public IEnumerable<dynamic> Value { get; set; }
    }

    static void M()
    {
        DynamicWrapper[] array = null;
        Foo(array, x => x.Value, (x, y) => string.Empty); //-C.Foo(System.Collections.Generic.IEnumerable<C.DynamicWrapper>, System.Func<C.DynamicWrapper, System.Collections.Generic.IEnumerable<dynamic>>, System.Func<C.DynamicWrapper, dynamic, string>)
    }

    static IEnumerable<dynamic> Foo(
        object source,
        Func<dynamic, IEnumerable<dynamic>> collectionSelector,
        Func<dynamic, dynamic, dynamic> resultSelector)
    {
        return null;
    }

    static IEnumerable<string> Foo(
        IEnumerable<DynamicWrapper> source,
        Func<DynamicWrapper, IEnumerable<dynamic>> collectionSelector,
        Func<DynamicWrapper, dynamic, string> resultSelector)
    {
        return null;
    }
}");
        }

        [Fact]
        public void MissingBaseTypeAndParamsCtor()
        {
            var cCommon = CreateCompilationWithMscorlib(@"
public class TCommon {}
", assemblyName: "cCommon");
            Assert.Empty(cCommon.GetDiagnostics());

            var cCS = CreateCompilationWithMscorlib(@"
public class MProvider : TCommon {}
", new MetadataReference[] { new CSharpCompilationReference(cCommon) }, assemblyName: "cCS");

            Assert.Empty(cCS.GetDiagnostics());

            var cFinal = CreateCompilationWithMscorlib(@"
public class T : MProvider {}

class PArray
{
  public PArray(TCommon t, params object[] p)
  {
  }
}

class Foo
{
  void M()
  {
    T t = new T();
    var x = new PArray(t, 1, 2);
  }
}
",
 //note that the reference to the 'cCS' compilation is missing.
 new MetadataReference[] { new CSharpCompilationReference(cCommon) });

            Assert.DoesNotThrow(() => cFinal.GetDiagnostics());
        }

        [Fact]
        public void RefOmittedComCall_Basic()
        {
            var source =
@"using System;
using System.Runtime.InteropServices;

[ComImport]
[Guid(""A88A175D-2448-447A-B786-64682CBEF156"")]
public interface IRef1
{
    int M(ref int x, int y);
}

public class Ref1Impl : IRef1
{
    public int M(ref int x, int y) { return x + y; }
}

class Test
{
   public static int Main()
   {
       IRef1 ref1 = new Ref1Impl();
       int ret = ref1.M(10, 10);
       Console.WriteLine(ret);
       return ret;
   }
}
";
            CompileAndVerify(source, expectedOutput: @"20");
        }

        [WorkItem(546733)]
        [Fact]
        public void RefOmittedComCall_Iterator()
        {
            var source =
@"using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

[ComImport]
[Guid(""A88A175D-2448-447A-B786-64682CBEF156"")]
public interface IRef1
{
    int M(ref int x, int y);
}

public class Ref1Impl : IRef1
{
    public int M(ref int x, int y) { return x + y; }
}

class Test
{
    static IEnumerable<int> M()
    {
        IRef1 ref1 = new Ref1Impl();
        int ret = ref1.M(10, 2);
        yield return ret;
    }
    public static void Main()
    {
        Console.WriteLine(M().First());
    }
}";
            CompileAndVerify(source, additionalRefs: new[] { LinqAssemblyRef }, expectedOutput: @"12");
        }

        [Fact]
        public void RefOmittedComCall_ArgumentNotAddressTaken_01()
        {
            var source =
@"using System;
using System.Runtime.InteropServices;

[ComImport]
[Guid(""A88A175D-2448-447A-B786-64682CBEF156"")]
public interface IRef1
{
    int M(ref int x, ref int y);
}

public class Ref1Impl : IRef1
{
    public int M(ref int x, ref int y)
    { 
        int ret = x + y;
        x = -1;
        y = 0;
        return ret;
    }
}

class Test
{
   public static int Main()
   {
       IRef1 ref1 = new Ref1Impl();
       int x = 10, y = 10;
       int ret = ref1.M(x, ref y);
       Console.WriteLine(x);
       Console.WriteLine(y);
       Console.WriteLine(ret);
       return ret;
   }
}
";
            CompileAndVerify(source, expectedOutput: @"10
0
20");
        }

        [Fact]
        public void RefOmittedComCall_ArgumentNotAddressTaken_02()
        {
            var source =
@"using System;
using System.Runtime.InteropServices;

[ComImport]
[Guid(""A88A175D-2448-447A-B786-64682CBEF156"")]
public interface IRef1
{
    int M(ref int x, int y);
}

public class Ref1Impl : IRef1
{
    public int M(ref int x, int y)
    { 
        int ret = x + y;
        x = 0;
        return ret;
    }
}

class Test
{
   public static int Main()
   {
       IRef1 ref1 = new Ref1Impl();
       int x = 10, y = 10;
       int ret = ref1.M(x, y);
       Console.WriteLine(x);
       Console.WriteLine(y);
       Console.WriteLine(ret);
       return ret;
   }
}
";
            CompileAndVerify(source, expectedOutput: @"10
10
20");
        }

        [Fact]
        public void RefOmittedComCall_NamedArguments()
        {
            var source =
@"using System;
using System.Runtime.InteropServices;

[ComImport]
[Guid(""A88A175D-2448-447A-B786-64682CBEF156"")]
public interface IRef1
{
    int M(ref int x, ref int y);
}

public class Ref1Impl : IRef1
{
    public int M(ref int x, ref int y)
    { 
        int ret = x + y;
        x = -1;
        y = 0;
        return ret;
    }
}

class Test
{
   public static int Main()
   {
       IRef1 ref1 = new Ref1Impl();
       int y = 10;
       int ret = ref1.M(y: ref y, x: y);
       Console.WriteLine(y);
       Console.WriteLine(ret);
       return ret;
   }
}
";
            CompileAndVerify(source, expectedOutput: @"0
20");
        }

        [Fact]
        public void RefOmittedComCall_MethodCallArgument()
        {
            var source =
@"using System;
using System.Runtime.InteropServices;

[ComImport]
[Guid(""A88A175D-2448-447A-B786-64682CBEF156"")]
public interface IRef1
{
    int M(ref int x, ref int y, ref int z);
}

public class Ref1Impl : IRef1
{
    public int M(ref int x, ref int y, ref int z)
    { 
        int ret = x + y + z;
        x = -1;
        y = -2;
        z = -3;
        return ret;
    }
}

class Test
{
    public static int Foo(ref int x)
    {
        Console.WriteLine(x);
        x++;
        return x;
    }

    public static int Main()
    {
        IRef1 ref1 = new Ref1Impl();
        int a = 10;
        int ret = ref1.M(
            z: Foo(ref a),                          // Print 10
            y: ref1.M(z: ref a, y: a, x: ref a),
            x: Foo(ref a));                         // Print -3
        Console.WriteLine(a);                       // Print -2
        Console.WriteLine(ret);                     // Print 42

        int b = 1, c = 2;
        ret = ref1.M(
            z: Foo(ref c),                          // Print 2
            y: ref1.M(z: ref b, y: b + c, x: b),
            x: Foo(ref b));                         // Print -3
        Console.WriteLine(b);                       // Print -2
        Console.WriteLine(c);                       // Print 3
        Console.WriteLine(ret);                     // Print 7
        return ret;
    }
}
";
            CompileAndVerify(source, expectedOutput: @"10
-3
-2
42
2
-3
-2
3
7");
        }

        [Fact]
        public void RefOmittedComCall_AssignToRefParam()
        {
            var source =
@"using System;
using System.Runtime.InteropServices;

[ComImport]
[Guid(""A88A175D-2448-447A-B786-64682CBEF156"")]
public interface IRef1
{
    int M(ref int x, ref int y);
}

public class Ref1Impl : IRef1
{
    public int M(ref int x, ref int y)
    { 
        x = 1;
        y = 2;
        return x + y;
    }
}

class Test
{
    public static int Foo(ref int x)
    {
        Console.WriteLine(x);
        x++;
        return x;
    }

    public static int Main()
    {
        IRef1 ref1 = new Ref1Impl();
        int a = 10;
        int ret = ref1.M(a, a) + ref1.M(10, 10);
        Console.WriteLine(ret);
        return ret;
    }
}
";
            CompileAndVerify(source, expectedOutput: @"6");
        }

        [Fact]
        public void RefOmittedComCall_ExternMethod()
        {
            var source =
@"using System;
using System.Runtime.InteropServices;

[ComImport]
[Guid(""A88A175D-2448-447A-B786-64682CBEF156"")]
public class Ref1Impl
{
    public extern int M(ref int x, int y);
}

class Test
{
   public static int Main()
   {
       var ref1 = new Ref1Impl();
       int ret = ref1.M(10, 10);
       Console.WriteLine(ret);
       return ret;
   }
}
";
            CreateCompilationWithMscorlib(source).VerifyDiagnostics();
        }

        [Fact, WorkItem(530747)]
        public void RefOmittedComCall_Unsafe()
        {
            // Native compiler generates invalid IL for ref omitted argument of pointer type, while Roslyn generates correct IL.
            // See Won't Fixed Devdiv bug #16837 for details.

            var source =
@"using System;
using System.Runtime.InteropServices;

[ComImport]
[Guid(""A88A175D-2448-447A-B786-64682CBEF156"")]
unsafe public interface IRef1
{
    void M(ref int* x);
    void M2(ref int* x, ref int* y);
    void M3(ref int* x, ref int* y);
}

unsafe public class Ref1Impl : IRef1
{
    public void M(ref int* x)
    {
        *x = *x + 1;
        x = null;
    }

    public void M2(ref int* x, ref int* y)
    { 
        *y = *y + 1;
    }

    public void M3(ref int* x, ref int* y)
    {
        x = null;
        *y = *y + 1;
    }
}

unsafe class Test
{
    public static int Main()
    {
        IRef1 ref1 = new Ref1Impl();
        int a = 1;
        int *p = &a;
        
        ref1.M(ref p);
        Console.WriteLine(a);
        Console.WriteLine(p == null);

        p = &a;
        ref1.M2(&a, ref p);
        Console.WriteLine(a);
        Console.WriteLine(*p);

        ref1.M3(p, ref p);
        Console.WriteLine(a);
        Console.WriteLine(*p);
        return 0;
    }
}
";
            CompileAndVerify(source, options: TestOptions.UnsafeExe, expectedOutput: @"2
True
3
3
4
4");
        }

        [Fact()]
        public void RefOmittedComCall_ERR_ComImportWithImpl()
        {
            var source =
@"using System;
using System.Runtime.InteropServices;

public interface IRef1
{
    int M(ref int x, int y);
}

[ComImport]
[Guid(""A88A175D-2448-447A-B786-64682CBEF156"")]
public class Ref1Impl : IRef1
{
    public int M(ref int x, int y) { return x + y; }
}

class Test
{
   public static int Main()
   {
       var ref1 = new Ref1Impl();
       int ret = ref1.M(10, 10);
       Console.WriteLine(ret);
       return ret;
   }
}
";
            CreateCompilationWithMscorlib(source).VerifyDiagnostics(
                // (13,16): error CS0423: Since 'Ref1Impl' has the ComImport attribute, 'Ref1Impl.M(ref int, int)' must be extern or abstract
                //     public int M(ref int x, int y) { return x + y; }
                Diagnostic(ErrorCode.ERR_ComImportWithImpl, "M").WithArguments("Ref1Impl.M(ref int, int)", "Ref1Impl").WithLocation(13, 16));
        }

        [Fact]
        public void RefOmittedComCall_Error_NonComImportType()
        {
            var source =
@"using System;
using System.Runtime.InteropServices;

public interface IRef1
{
    int M(ref int x, int y);
}

[ComImport]
[Guid(""A88A175D-2448-447A-B786-64682CBEF156"")]
public class Ref1Impl: IRef1
{
    public extern int M(ref int x, int y);
}

class Test
{
   public static int Main()
   {
       IRef1 ref1 = new Ref1Impl();
       int ret = ref1.M(10, 10);
       Console.WriteLine(ret);
       return ret;
   }
}
";
            CreateCompilationWithMscorlib(source).VerifyDiagnostics(
                // (21,25): error CS1620: Argument 1 must be passed with the 'ref' keyword
                //        int ret = ref1.M(10, 10);
                Diagnostic(ErrorCode.ERR_BadArgRef, "10").WithArguments("1", "ref").WithLocation(21, 25));
        }

        [Fact]
        public void RefOmittedComCall_Error_OutParam()
        {
            var source =
@"using System;
using System.Runtime.InteropServices;

[ComImport]
[Guid(""A88A175D-2448-447A-B786-64682CBEF156"")]
public interface IRef1
{
    int M(out int x, int y);
}

public class Ref1Impl : IRef1
{
    public int M(out int x, int y) { x = 1; return y; }
}

class Test
{
   public static int Main()
   {
       IRef1 ref1 = new Ref1Impl();
       int x;
       int ret = ref1.M(x, 10);
       Console.WriteLine(ret);
       return ret;
   }
}";
            CreateCompilationWithMscorlib(source).VerifyDiagnostics(
                // (22,25): error CS1620: Argument 1 must be passed with the 'out' keyword
                //        int ret = ref1.M(x, 10);
                Diagnostic(ErrorCode.ERR_BadArgRef, "x").WithArguments("1", "out").WithLocation(22, 25),
                // (22,25): error CS0165: Use of unassigned local variable 'x'
                //        int ret = ref1.M(x, 10);
                Diagnostic(ErrorCode.ERR_UseDefViolation, "x").WithArguments("x").WithLocation(22, 25));
        }

        [Fact]
        public void RefOmittedComCall_Error_WithinAttributeContext()
        {
            var source =
@"
using System;
using System.Runtime.InteropServices;

[AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
class Attr: Attribute
{
    public Attr(int x) {}
}

[ComImport]
[Guid(""A88A175D-2448-447A-B786-64682CBEF156"")]
class Attr2: Attribute
{
    public Attr2(ref int x) {}
}

[ComImport]
[Guid(""A88A175D-2448-447A-B786-64682CBEF156"")]
[Attr(new Foo().M1(1, 1))]
[Attr(Foo.M2(1, 1))]
[Attr2(1)]
public class Foo
{
    public extern int M1(ref int x, int y);
    public static extern int M2(ref int x, int y);
}";
            CreateCompilationWithMscorlib(source).VerifyDiagnostics(
                // (13,7): error CS0424: 'Attr2': a class with the ComImport attribute cannot specify a base class
                // class Attr2: Attribute
                Diagnostic(ErrorCode.ERR_ComImportWithBase, "Attr2").WithArguments("Attr2").WithLocation(13, 7),
                // (15,12): error CS0669: A class with the ComImport attribute cannot have a user-defined constructor
                //     public Attr2(ref int x) {}
                Diagnostic(ErrorCode.ERR_ComImportWithUserCtor, "Attr2").WithLocation(15, 12),
                // (20,20): error CS1620: Argument 1 must be passed with the 'ref' keyword
                // [Attr(new Foo().M1(1, 1))]
                Diagnostic(ErrorCode.ERR_BadArgRef, "1").WithArguments("1", "ref").WithLocation(20, 20),
                // (21,14): error CS1620: Argument 1 must be passed with the 'ref' keyword
                // [Attr(Foo.M2(1, 1))]
                Diagnostic(ErrorCode.ERR_BadArgRef, "1").WithArguments("1", "ref").WithLocation(21, 14),
                // (22,8): error CS1620: Argument 1 must be passed with the 'ref' keyword
                // [Attr2(1)]
                Diagnostic(ErrorCode.ERR_BadArgRef, "1").WithArguments("1", "ref").WithLocation(22, 8));
        }

        [Fact]
        public void RefOmittedComCall_CtorWithRefArgument()
        {
            var ilSource = @"
.class public auto ansi import beforefieldinit Ref1
       extends [mscorlib]System.Object
{
  .custom instance void [mscorlib]System.Runtime.InteropServices.GuidAttribute::.ctor(string) = ( 01 00 24 41 38 38 41 31 37 35 44 2D 32 34 34 38   // ..$A88A175D-2448
                                                                                                  2D 34 34 37 41 2D 42 37 38 36 2D 36 34 36 38 32   // -447A-B786-64682
                                                                                                  43 42 45 46 31 35 36 00 00 )                      // CBEF156..
  .method public hidebysig specialname rtspecialname 
          instance void  .ctor(int32& x) runtime managed internalcall
  {
  } // end of method Ref1::.ctor

} // end of class Ref1
";
            var source = @"
public class MainClass
{
    public static int Main ()
    {
        int x = 0;
        var r = new Ref1(x);
        return 0;
    }
}";

            var compilation = CreateCompilationWithCustomILSource(source, ilSource);

            compilation.VerifyDiagnostics(
                // (7,26): error CS1620: Argument 1 must be passed with the 'ref' keyword
                //         var r = new Ref1(x);
                Diagnostic(ErrorCode.ERR_BadArgRef, "x").WithArguments("1", "ref").WithLocation(7, 26));
        }

        [Fact, WorkItem(546122)]
        public void TestComImportOverloadResolutionCantOmitRef()
        {
            string source = @"
using System;
using System.Runtime.InteropServices;

[ComImport, Guid(""1234C65D-1234-447A-B786-64682CBEF136"")]
abstract class C
{
    extern public void M(ref short p);
    extern public void M(sbyte p);
}
class D : C
{
    public static void Foo()
    {
        short x = 123;
        sbyte s = 123;
        
        new D().M(x);
        
        C c = new D();
        c.M(x);

        new D().M(s);
    }
}
";
            CreateCompilationWithMscorlib(source).VerifyDiagnostics(
                // (18,19): error CS1620: Argument 1 must be passed with the 'ref' keyword
                //         new D().M(x);
                Diagnostic(ErrorCode.ERR_BadArgRef, "x").WithArguments("1", "ref"));
        }

        [Fact, WorkItem(546122)]
        public void RefOmittedComCall_BaseTypeComImport()
        {
            string source = @"
using System;
using System.Runtime.InteropServices;

[ComImport, Guid(""1234C65D-1234-447A-B786-64682CBEF136"")]
abstract class E
{
    extern public void M(ref short p);
}
class F : E
{
    [DllImport(""foo"")]
    extern public void M(sbyte p);

    public static void Foo()
    {
        short x = 123;
        sbyte s = 123;
        
        new F().M(x);
        
        E e = new F();
        e.M(x);

        new F().M(s);
    }
}

[ComImport, Guid(""1234C65D-1234-447A-B786-64682CBEF136"")]
abstract class G
{
    extern public void M(sbyte p);
}
class H : G
{
    extern public void M(ref short p);

    public static void Foo()
    {
        short x = 123;
        sbyte s = 123;
        
        new H().M(x);
        
        G g = new H();
        g.M(x);

        new H().M(s);
    }
}

[ComImport, Guid(""1234C65D-1234-447A-B786-64682CBEF136"")]
abstract class I
{
}
class J : I
{
    extern public void M(sbyte p);
    extern public void M(ref short p);

    public static void Foo()
    {
        short x = 123;
        sbyte s = 123;
        
        new J().M(x);

        I i = new J();
        i.M(x);

        new J().M(s);
    }
}
";
            CreateCompilationWithMscorlib(source).VerifyDiagnostics(
                // (12,6): error CS0601: The DllImport attribute must be specified on a method marked 'static' and 'extern'
                //     [DllImport("foo")]
                Diagnostic(ErrorCode.ERR_DllImportOnInvalidMethod, "DllImport"),
                // (20,19): error CS1503: Argument 1: cannot convert from 'short' to 'sbyte'
                //         new F().M(x);
                Diagnostic(ErrorCode.ERR_BadArgType, "x").WithArguments("1", "short", "sbyte"),
                // (36,24): warning CS0626: Method, operator, or accessor 'H.M(ref short)' is marked external and has no attributes on it. Consider adding a DllImport attribute to specify the external implementation.
                //     extern public void M(ref short p);
                Diagnostic(ErrorCode.WRN_ExternMethodNoImplementation, "M").WithArguments("H.M(ref short)"),
                // (43,19): error CS1620: Argument 1 must be passed with the 'ref' keyword
                //         new H().M(x);
                Diagnostic(ErrorCode.ERR_BadArgRef, "x").WithArguments("1", "ref"),
                // (46,13): error CS1503: Argument 1: cannot convert from 'short' to 'sbyte'
                //         g.M(x);
                Diagnostic(ErrorCode.ERR_BadArgType, "x").WithArguments("1", "short", "sbyte"),
                // (58,24): warning CS0626: Method, operator, or accessor 'J.M(sbyte)' is marked external and has no attributes on it. Consider adding a DllImport attribute to specify the external implementation.
                //     extern public void M(sbyte p);
                Diagnostic(ErrorCode.WRN_ExternMethodNoImplementation, "M").WithArguments("J.M(sbyte)"),
                // (59,24): warning CS0626: Method, operator, or accessor 'J.M(ref short)' is marked external and has no attributes on it. Consider adding a DllImport attribute to specify the external implementation.
                //     extern public void M(ref short p);
                Diagnostic(ErrorCode.WRN_ExternMethodNoImplementation, "M").WithArguments("J.M(ref short)"),
                // (66,19): error CS1503: Argument 1: cannot convert from 'short' to 'sbyte'
                //         new J().M(x);
                Diagnostic(ErrorCode.ERR_BadArgType, "x").WithArguments("1", "short", "sbyte"),
                // (69,11): error CS1061: 'I' does not contain a definition for 'M' and no extension method 'M' accepting a first argument of type 'I' could be found (are you missing a using directive or an assembly reference?)
                //         i.M(x);
                Diagnostic(ErrorCode.ERR_NoSuchMemberOrExtension, "M").WithArguments("I", "M"));
        }

        [Fact, WorkItem(546122)]
        public void RefOmittedComCall_DerivedComImport()
        {
            string source = @"
using System;
using System.Runtime.InteropServices;

interface A
{
    void M(ref short p);
    void M(sbyte p);
}

[ComImport, Guid(""1234C65D-1234-447A-B786-64682CBEF136"")]
interface B: A
{
}

class C: B
{
    public void M(ref short p) {}
    public void M(sbyte p) {}

    public static void Foo()
    {
        short x = 123;
        sbyte s = 123;

        A a = new C();
        B b = new C();
        C c = new C();

        a.M(x);
        b.M(x);
        c.M(x);

        a.M(s);
        b.M(s);
        c.M(s);
    }
}
";
            CreateCompilationWithMscorlib(source).VerifyDiagnostics(
                // (30,13): error CS1620: Argument 1 must be passed with the 'ref' keyword
                //         a.M(x);
                Diagnostic(ErrorCode.ERR_BadArgRef, "x").WithArguments("1", "ref"),
                // (32,13): error CS1620: Argument 1 must be passed with the 'ref' keyword
                //         c.M(x);
                Diagnostic(ErrorCode.ERR_BadArgRef, "x").WithArguments("1", "ref"));
        }

        [Fact, WorkItem(546122)]
        public void RefOmittedComCall_TypeParameterConstrainedToComImportType()
        {
            string source = @"
using System;
using System.Runtime.InteropServices;

[ComImport, Guid(""1234C65D-1234-447A-B786-64682CBEF136"")]
class K
{
    public extern void M(ref short p);
    public extern void M(sbyte p);
}

class H<T> where T: K, new()
{
    public static void Foo()
    {
        short x = 123;
        T t = new T();
        t.M(x);
    }
}
";
            CreateCompilationWithMscorlib(source).VerifyDiagnostics(
                // (18,13): error CS1620: Argument 1 must be passed with the 'ref' keyword
                //         t.M(x);
                Diagnostic(ErrorCode.ERR_BadArgRef, "x").WithArguments("1", "ref"));
        }

        [Fact, WorkItem(546122)]
        public void RefOmittedComCall_StaticMethod1()
        {
            string source = @"
using System;
using System.Runtime.InteropServices;

[ComImport, Guid(""1234C65D-1234-447A-B786-64682CBEF136"")]
static class E
{
    public extern static void M(ref short p);
    public extern static void M(sbyte p);
}

class Y
{
    public static void Foo()
    {
        short x = 123;
        E.M(x); // Dev11 reports CS1620 (missing 'ref')
    }
}
";
            // BREAK: Dev11 does not allow this, but it's probably an accident.
            // That is, it inspects the receiver type of the invocation and it
            // finds no receiver for a static method invocation.
            // MITIGATION: Candidates with 'ref' omitted lose tie-breakers, so
            // it should not be possible for this to cause overload resolution
            // to succeed in a different way.
            CreateCompilationWithMscorlib(source).VerifyDiagnostics();
        }

        [Fact, WorkItem(546122)]
        public void RefOmittedComCall_StaticMethod2()
        {
            string source = @"
using System;
using System.Runtime.InteropServices;

[ComImport, Guid(""1234C65D-1234-447A-B786-64682CBEF136"")]
class E
{
    public extern static void M(ref short p);
}

[ComImport, Guid(""1234C65D-1234-447A-B786-64682CBEF137"")]
class F
{
    public extern static void M(ref short p);
}

class Y
{
    public static void Main()
    {
        short x = 123;
        E E = null;
        E.M(x); // Allowed in dev11.
        F.M(x); // CS1620 (missing 'ref') in dev11.
    }
}
";
            // BREAK: Dev11 produces an error.  It doesn't make sense that the introduction
            // of a color-color local would eliminate an error, since it does not affect the
            // outcome of overload resolution.
            CreateCompilationWithMscorlib(source).VerifyDiagnostics(
                // (22,11): warning CS0219: The variable 'E' is assigned but its value is never used
                //         E E = null;
                Diagnostic(ErrorCode.WRN_UnreferencedVarAssg, "E").WithArguments("E").WithLocation(22, 11));
        }

        [Fact, WorkItem(546122), WorkItem(842476)]
        public void RefOmittedComCall_ExtensionMethod()
        {
            string source = @"
using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

[ComImport, Guid(""1234C65D-1234-447A-B786-64682CBEF136"")]
class C
{
}

static class CExtensions
{
    public static void M(this C c, ref short p) {}
    public static void M(this C c, sbyte p) {}

    public static void I(this C c, ref int p) {}
}

class X
{
    public static void Foo()
    {
        short x = 123;
        C c = new C();
        c.M(x);
        c.I(123);
    }
}
";
            CompileAndVerify(source, additionalRefs: new[] { SystemCoreRef });
        }

        [Fact]
        public void RefOmittedComCall_OverloadResolution_SingleArgument()
        {
            var source =
@"
using System;
using System.Runtime.InteropServices;

[ComImport]
[Guid(""A88A175D-2448-447A-B786-64682CBEF156"")]
public interface IRef1
{
    void M1(int x);
    void M1(ref int x);

    void M2(long x);
    void M2(ref int x);

    void M3(char x);
    void M3(ref int x);

    void M4(out uint x);
    void M4(ref int x);

    void M5(ref int x);
    void M5(ref long x);

    void M6(ref char x);
    void M6(ref long x);

    void M7(ref long x);
    void M7(int x);

    void M8(ref long x);
    void M8(char x);

    void M9(ref char x);
    void M9(long x);
}

public class Ref1Impl : IRef1
{
    public void M1(int x) { Console.WriteLine(1); }
    public void M1(ref int x) { Console.WriteLine(2); }

    public void M2(long x) { Console.WriteLine(3); }
    public void M2(ref int x) { Console.WriteLine(4); }

    public void M3(char x) { Console.WriteLine(5); }
    public void M3(ref int x) { Console.WriteLine(6); }

    public void M4(out uint x) { x = 0; Console.WriteLine(7); }
    public void M4(ref int x) { Console.WriteLine(8); }

    public void M5(ref int x) { Console.WriteLine(9); }
    public void M5(ref long x) { Console.WriteLine(10); }

    public void M6(ref char x) { Console.WriteLine(11); }
    public void M6(ref long x) { Console.WriteLine(12); }

    public void M7(ref long x) { Console.WriteLine(13); }
    public void M7(int x) { Console.WriteLine(14); }

    public void M8(ref long x) { Console.WriteLine(15); }
    public void M8(char x) { Console.WriteLine(16); }

    public void M9(ref char x) { Console.WriteLine(17); }
    public void M9(long x) { Console.WriteLine(18); }
}

class Test
{
   public static void Main()
   {
       IRef1 ref1 = new Ref1Impl();
       int i = 1;
       long l = 1;
       char c = 'c';

//     void M1(int x);
//     void M1(ref int x);

       ref1.M1(10);
       //ref1.M1(10L);      CS1503
       ref1.M1('c');
       ref1.M1(i);
       //ref1.M1(l);        CS1503
       ref1.M1(c);
       ref1.M1(ref i);
       //ref1.M1(ref l);    CS1615
       //ref1.M1(ref c);    CS1615


//     void M2(long x);
//     void M2(ref int x);

       Console.WriteLine();
       ref1.M2(10);
       ref1.M2(10L);
       ref1.M2('c');
       ref1.M2(i);
       ref1.M2(l);
       ref1.M2(c);
       ref1.M2(ref i);
       //ref1.M2(ref l);    CS1615
       //ref1.M2(ref c);    CS1615


//     void M3(char x);
//     void M3(ref int x);

       Console.WriteLine();
       ref1.M3(10);
       //ref1.M3(10L);      CS1503
       ref1.M3('c');
       ref1.M3(i);
       //ref1.M3(l);        CS1503
       ref1.M3(c);
       ref1.M3(ref i);
       //ref1.M3(ref l);    CS1615
       //ref1.M3(ref c);    CS1615


//     void M4(out uint x);
//     void M4(ref int x);

       Console.WriteLine();
       ref1.M4(10);
       //ref1.M4(10L);      CS1620
       ref1.M4('c');
       ref1.M4(i);
       //ref1.M4(l);        CS1620
       ref1.M4(c);
       ref1.M4(ref i);
       //ref1.M4(ref l);    CS1620
       //ref1.M4(ref c);    CS1620


//     void M5(ref int x);
//     void M5(ref long x);

       Console.WriteLine();
       //ref1.M5(10);       CS0121
       ref1.M5(10L);
       //ref1.M5('c');      CS0121
       //ref1.M5(i);        CS0121
       ref1.M5(l);
       //ref1.M5(c);        CS0121
       ref1.M5(ref i);
       ref1.M5(ref l);
       //ref1.M5(ref c);    CS1503


//     void M6(ref char x);
//     void M6(ref long x);

       Console.WriteLine();
       ref1.M6(10);
       ref1.M6(10L);
       //ref1.M6('c');      CS0121
       ref1.M6(i);
       ref1.M6(l);
       //ref1.M6(c);        CS0121
       //ref1.M6(ref i);    CS1503   
       ref1.M6(ref l);
       ref1.M6(ref c);


//     void M7(ref long x);
//     void M7(int x);

       Console.WriteLine();
       ref1.M7(10);
       ref1.M7(10L);
       ref1.M7('c');
       ref1.M7(i);
       ref1.M7(l);
       ref1.M7(c);
       //ref1.M7(ref i);    CS1503
       ref1.M7(ref l);
       //ref1.M7(ref c);    CS1503


//     void M8(ref long x);
//     void M8(char x);

       Console.WriteLine();
       ref1.M8(10);
       ref1.M8(10L);
       ref1.M8('c');
       ref1.M8(i);
       ref1.M8(l);
       ref1.M8(c);
       //ref1.M8(ref i);    CS1503
       ref1.M8(ref l);
       //ref1.M8(ref c);    CS1503


//     void M9(ref char x);
//     void M9(long x);

       Console.WriteLine();
       ref1.M9(10);
       ref1.M9(10L);
       ref1.M9('c');
       ref1.M9(i);
       ref1.M9(l);
       ref1.M9(c);
       //ref1.M9(ref i);    CS1503
       //ref1.M9(ref l);    CS1503
       ref1.M9(ref c);
   }
}
";
            CompileAndVerify(source, expectedOutput: @"1
1
1
1
2

3
3
3
3
3
3
4

6
5
6
5
6

8
8
8
8
8

10
10
9
10

12
12
12
12
12
11

14
13
14
14
13
14
13

15
15
16
15
15
16
15

18
18
18
18
18
18
17");
        }

        [Fact]
        public void RefOmittedComCall_OverloadResolution_SingleArgument_ErrorCases()
        {
            var source =
@"
using System;
using System.Runtime.InteropServices;

[ComImport]
[Guid(""A88A175D-2448-447A-B786-64682CBEF156"")]
public interface IRef1
{
    void M1(int x);
    void M1(ref int x);

    void M2(long x);
    void M2(ref int x);

    void M3(char x);
    void M3(ref int x);

    void M4(out uint x);
    void M4(ref int x);

    void M5(ref int x);
    void M5(ref long x);

    void M6(ref char x);
    void M6(ref long x);

    void M7(ref long x);
    void M7(int x);

    void M8(ref long x);
    void M8(char x);

    void M9(ref char x);
    void M9(long x);
}

public class Ref1Impl : IRef1
{
    public void M1(int x) { Console.WriteLine(1); }
    public void M1(ref int x) { Console.WriteLine(2); }

    public void M2(long x) { Console.WriteLine(3); }
    public void M2(ref int x) { Console.WriteLine(4); }

    public void M3(char x) { Console.WriteLine(5); }
    public void M3(ref int x) { Console.WriteLine(6); }

    public void M4(out uint x) { x = 0; Console.WriteLine(7); }
    public void M4(ref int x) { Console.WriteLine(8); }

    public void M5(ref int x) { Console.WriteLine(9); }
    public void M5(ref long x) { Console.WriteLine(10); }

    public void M6(ref char x) { Console.WriteLine(11); }
    public void M6(ref long x) { Console.WriteLine(12); }

    public void M7(ref long x) { Console.WriteLine(13); }
    public void M7(int x) { Console.WriteLine(14); }

    public void M8(ref long x) { Console.WriteLine(15); }
    public void M8(char x) { Console.WriteLine(16); }

    public void M9(ref char x) { Console.WriteLine(17); }
    public void M9(long x) { Console.WriteLine(18); }
}

class Test
{
   public static void Main()
   {
       IRef1 ref1 = new Ref1Impl();
       int i = 1;
       long l = 1;
       char c = 'c';

//     void M1(int x);
//     void M1(ref int x);

       ref1.M1(10L);      // CS1503
       ref1.M1(l);        // CS1503
       ref1.M1(ref l);    // CS1615
       ref1.M1(ref c);    // CS1615


//     void M2(long x);
//     void M2(ref int x);

       Console.WriteLine();
       ref1.M2(ref l);    // CS1615
       ref1.M2(ref c);    // CS1615


//     void M3(char x);
//     void M3(ref int x);

       Console.WriteLine();
       ref1.M3(10L);      // CS1503
       ref1.M3(l);        // CS1503
       ref1.M3(ref l);    // CS1615
       ref1.M3(ref c);    // CS1615


//     void M4(out uint x);
//     void M4(ref int x);

       Console.WriteLine();
       ref1.M4(10L);      // CS1620
       ref1.M4(l);        // CS1620
       ref1.M4(ref l);    // CS1620
       ref1.M4(ref c);    // CS1620


//     void M5(ref int x);
//     void M5(ref long x);

       Console.WriteLine();
       ref1.M5(10);       // CS0121
       ref1.M5('c');      // CS0121
       ref1.M5(i);        // CS0121
       ref1.M5(c);        // CS0121
       ref1.M5(ref c);    // CS1503


//     void M6(ref char x);
//     void M6(ref long x);

       Console.WriteLine();
       ref1.M6('c');      // CS0121
       ref1.M6(c);        // CS0121
       ref1.M6(ref i);    // CS1503   


//     void M7(ref long x);
//     void M7(int x);

       Console.WriteLine();
       ref1.M7(ref i);    // CS1503
       ref1.M7(ref c);    // CS1503


//     void M8(ref long x);
//     void M8(char x);

       Console.WriteLine();
       ref1.M8(ref i);    // CS1503
       ref1.M8(ref c);    // CS1503


//     void M9(ref char x);
//     void M9(long x);

       Console.WriteLine();
       ref1.M9(ref i);    // CS1503
       ref1.M9(ref l);    // CS1503
   }
}
";
            CreateCompilationWithMscorlib(source).VerifyDiagnostics(
                // (79,16): error CS1503: Argument 1: cannot convert from 'long' to 'int'
                //        ref1.M1(10L);      // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "10L").WithArguments("1", "long", "int"),
                // (80,16): error CS1503: Argument 1: cannot convert from 'long' to 'int'
                //        ref1.M1(l);        // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("1", "long", "int"),
                // (81,20): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        ref1.M1(ref l);    // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "l").WithArguments("1", "ref"),
                // (82,20): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        ref1.M1(ref c);    // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "c").WithArguments("1", "ref"),
                // (89,20): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        ref1.M2(ref l);    // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "l").WithArguments("1", "ref"),
                // (90,20): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        ref1.M2(ref c);    // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "c").WithArguments("1", "ref"),
                // (97,16): error CS1503: Argument 1: cannot convert from 'long' to 'char'
                //        ref1.M3(10L);      // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "10L").WithArguments("1", "long", "char"),
                // (98,16): error CS1503: Argument 1: cannot convert from 'long' to 'char'
                //        ref1.M3(l);        // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("1", "long", "char"),
                // (99,20): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        ref1.M3(ref l);    // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "l").WithArguments("1", "ref"),
                // (100,20): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        ref1.M3(ref c);    // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "c").WithArguments("1", "ref"),
                // (107,16): error CS1620: Argument 1 must be passed with the 'out' keyword
                //        ref1.M4(10L);      // CS1620
                Diagnostic(ErrorCode.ERR_BadArgRef, "10L").WithArguments("1", "out"),
                // (108,16): error CS1620: Argument 1 must be passed with the 'out' keyword
                //        ref1.M4(l);        // CS1620
                Diagnostic(ErrorCode.ERR_BadArgRef, "l").WithArguments("1", "out"),
                // (109,20): error CS1620: Argument 1 must be passed with the 'out' keyword
                //        ref1.M4(ref l);    // CS1620
                Diagnostic(ErrorCode.ERR_BadArgRef, "l").WithArguments("1", "out"),
                // (110,20): error CS1620: Argument 1 must be passed with the 'out' keyword
                //        ref1.M4(ref c);    // CS1620
                Diagnostic(ErrorCode.ERR_BadArgRef, "c").WithArguments("1", "out"),
                // (117,8): error CS0121: The call is ambiguous between the following methods or properties: 'IRef1.M5(ref int)' and 'IRef1.M5(ref long)'
                //        ref1.M5(10);       // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "ref1.M5").WithArguments("IRef1.M5(ref int)", "IRef1.M5(ref long)"),
                // (118,8): error CS0121: The call is ambiguous between the following methods or properties: 'IRef1.M5(ref int)' and 'IRef1.M5(ref long)'
                //        ref1.M5('c');      // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "ref1.M5").WithArguments("IRef1.M5(ref int)", "IRef1.M5(ref long)"),
                // (119,8): error CS0121: The call is ambiguous between the following methods or properties: 'IRef1.M5(ref int)' and 'IRef1.M5(ref long)'
                //        ref1.M5(i);        // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "ref1.M5").WithArguments("IRef1.M5(ref int)", "IRef1.M5(ref long)"),
                // (120,8): error CS0121: The call is ambiguous between the following methods or properties: 'IRef1.M5(ref int)' and 'IRef1.M5(ref long)'
                //        ref1.M5(c);        // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "ref1.M5").WithArguments("IRef1.M5(ref int)", "IRef1.M5(ref long)"),
                // (121,20): error CS1503: Argument 1: cannot convert from 'ref char' to 'ref int'
                //        ref1.M5(ref c);    // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "c").WithArguments("1", "ref char", "ref int"),
                // (128,8): error CS0121: The call is ambiguous between the following methods or properties: 'IRef1.M6(ref char)' and 'IRef1.M6(ref long)'
                //        ref1.M6('c');      // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "ref1.M6").WithArguments("IRef1.M6(ref char)", "IRef1.M6(ref long)"),
                // (129,8): error CS0121: The call is ambiguous between the following methods or properties: 'IRef1.M6(ref char)' and 'IRef1.M6(ref long)'
                //        ref1.M6(c);        // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "ref1.M6").WithArguments("IRef1.M6(ref char)", "IRef1.M6(ref long)"),
                // (130,20): error CS1503: Argument 1: cannot convert from 'ref int' to 'ref char'
                //        ref1.M6(ref i);    // CS1503   
                Diagnostic(ErrorCode.ERR_BadArgType, "i").WithArguments("1", "ref int", "ref char"),
                // (137,20): error CS1503: Argument 1: cannot convert from 'ref int' to 'ref long'
                //        ref1.M7(ref i);    // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "i").WithArguments("1", "ref int", "ref long"),
                // (138,20): error CS1503: Argument 1: cannot convert from 'ref char' to 'ref long'
                //        ref1.M7(ref c);    // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "c").WithArguments("1", "ref char", "ref long"),
                // (145,20): error CS1503: Argument 1: cannot convert from 'ref int' to 'ref long'
                //        ref1.M8(ref i);    // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "i").WithArguments("1", "ref int", "ref long"),
                // (146,20): error CS1503: Argument 1: cannot convert from 'ref char' to 'ref long'
                //        ref1.M8(ref c);    // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "c").WithArguments("1", "ref char", "ref long"),
                // (153,20): error CS1503: Argument 1: cannot convert from 'ref int' to 'ref char'
                //        ref1.M9(ref i);    // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "i").WithArguments("1", "ref int", "ref char"),
                // (154,20): error CS1503: Argument 1: cannot convert from 'ref long' to 'ref char'
                //        ref1.M9(ref l);    // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("1", "ref long", "ref char"));
        }

        [Fact, WorkItem(546176)]
        public void RefOmittedComCall_OverloadResolution_SingleArgument_IndexedProperties()
        {
            var source1 =
@"
.class interface public abstract import IA
{
  .custom instance void [mscorlib]System.Runtime.InteropServices.CoClassAttribute::.ctor(class [mscorlib]System.Type) = ( 01 00 01 41 00 00 )
  .custom instance void [mscorlib]System.Runtime.InteropServices.GuidAttribute::.ctor(string) = ( 01 00 24 31 36 35 46 37 35 32 44 2D 45 39 43 34 2D 34 46 37 45 2D 42 30 44 30 2D 43 44 46 44 37 41 33 36 45 32 31 31 00 00 )

  .property instance int32 P1(int32)
  {
    .get instance int32 IA::get_P1(int32)
    .set instance void IA::set_P1(int32, int32)
  }
  .property instance int32 P1(int32&)
  {
    .get instance int32 IA::get_P1(int32&)
    .set instance void IA::set_P1(int32&, int32)
  }
  .method public abstract virtual instance int32 get_P1(int32 i) { }
  .method public abstract virtual instance void set_P1(int32 i, int32 v) { }
  .method public abstract virtual instance int32 get_P1(int32& i) { }
  .method public abstract virtual instance void set_P1(int32& i, int32 v) { }


  .property instance int32 P2(int64)
  {
    .get instance int32 IA::get_P2(int64)
    .set instance void IA::set_P2(int64, int32)
  }
  .property instance int32 P2(int32&)
  {
    .get instance int32 IA::get_P2(int32&)
    .set instance void IA::set_P2(int32&, int32)
  }
  .method public abstract virtual instance int32 get_P2(int64 i) { }
  .method public abstract virtual instance void set_P2(int64 i, int32 v) { }
  .method public abstract virtual instance int32 get_P2(int32& i) { }
  .method public abstract virtual instance void set_P2(int32& i, int32 v) { }


  .property instance int32 P3(char)
  {
    .get instance int32 IA::get_P3(char)
    .set instance void IA::set_P3(char, int32)
  }
  .property instance int32 P3(int32&)
  {
    .get instance int32 IA::get_P3(int32&)
    .set instance void IA::set_P3(int32&, int32)
  }
  .method public abstract virtual instance int32 get_P3(char i) { }
  .method public abstract virtual instance void set_P3(char i, int32 v) { }
  .method public abstract virtual instance int32 get_P3(int32& i) { }
  .method public abstract virtual instance void set_P3(int32& i, int32 v) { }


  .property instance int32 P4(int64&)
  {
    .get instance int32 IA::get_P4(int64&)
    .set instance void IA::set_P4(int64&, int32)
  }
  .property instance int32 P4(int32&)
  {
    .get instance int32 IA::get_P4(int32&)
    .set instance void IA::set_P4(int32&, int32)
  }
  .method public abstract virtual instance int32 get_P4(int64& i) { }
  .method public abstract virtual instance void set_P4(int64& i, int32 v) { }
  .method public abstract virtual instance int32 get_P4(int32& i) { }
  .method public abstract virtual instance void set_P4(int32& i, int32 v) { }


  .property instance int32 P5(int64&)
  {
    .get instance int32 IA::get_P5(int64&)
    .set instance void IA::set_P5(int64&, int32)
  }
  .property instance int32 P5(char&)
  {
    .get instance int32 IA::get_P5(char&)
    .set instance void IA::set_P5(char&, int32)
  }
  .method public abstract virtual instance int32 get_P5(int64& i) { }
  .method public abstract virtual instance void set_P5(int64& i, int32 v) { }
  .method public abstract virtual instance int32 get_P5(char& i) { }
  .method public abstract virtual instance void set_P5(char& i, int32 v) { }


  .property instance int32 P6(int64&)
  {
    .get instance int32 IA::get_P6(int64&)
    .set instance void IA::set_P6(int64&, int32)
  }
  .property instance int32 P6(int32)
  {
    .get instance int32 IA::get_P6(int32)
    .set instance void IA::set_P6(int32, int32)
  }
  .method public abstract virtual instance int32 get_P6(int64& i) { }
  .method public abstract virtual instance void set_P6(int64& i, int32 v) { }
  .method public abstract virtual instance int32 get_P6(int32 i) { }
  .method public abstract virtual instance void set_P6(int32 i, int32 v) { }


  .property instance int32 P7(int64&)
  {
    .get instance int32 IA::get_P7(int64&)
    .set instance void IA::set_P7(int64&, int32)
  }
  .property instance int32 P7(char)
  {
    .get instance int32 IA::get_P7(char)
    .set instance void IA::set_P7(char, int32)
  }
  .method public abstract virtual instance int32 get_P7(int64& i) { }
  .method public abstract virtual instance void set_P7(int64& i, int32 v) { }
  .method public abstract virtual instance int32 get_P7(char i) { }
  .method public abstract virtual instance void set_P7(char i, int32 v) { }


  .property instance int32 P8(int64)
  {
    .get instance int32 IA::get_P8(int64)
    .set instance void IA::set_P8(int64, int32)
  }
  .property instance int32 P8(char&)
  {
    .get instance int32 IA::get_P8(char&)
    .set instance void IA::set_P8(char&, int32)
  }
  .method public abstract virtual instance int32 get_P8(int64 i) { }
  .method public abstract virtual instance void set_P8(int64 i, int32 v) { }
  .method public abstract virtual instance int32 get_P8(char& i) { }
  .method public abstract virtual instance void set_P8(char& i, int32 v) { }

}


.class public A implements IA
{
  .method public hidebysig specialname rtspecialname instance void .ctor()
  {
    ret
  }

  .property instance int32 P1(int32)
  {
    .get instance int32 A::get_P1(int32)
    .set instance void A::set_P1(int32, int32)
  }
  .property instance int32 P1(int32&)
  {
    .get instance int32 A::get_P1(int32&)
    .set instance void A::set_P1(int32&, int32)
  }
  .method public virtual instance int32 get_P1(int32 i)
  {
    ldc.i4.1
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance void set_P1(int32 i, int32 v)
  {
    ldc.i4.2
    call       void [mscorlib]System.Console::WriteLine(int32)
    ret
  }
  .method public virtual instance int32 get_P1(int32& i)
  {
    ldc.i4.3
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance void set_P1(int32& i, int32 v)
  {
    ldc.i4.4
    call       void [mscorlib]System.Console::WriteLine(int32)
    ret
  }


  .property instance int32 P2(int64)
  {
    .get instance int32 A::get_P2(int64)
    .set instance void A::set_P2(int64, int32)
  }
  .property instance int32 P2(int32&)
  {
    .get instance int32 A::get_P2(int32&)
    .set instance void A::set_P2(int32&, int32)
  }
  .method public virtual instance int32 get_P2(int64 i)
  {
    ldc.i4.5
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance void set_P2(int64 i, int32 v)
  {
    ldc.i4.6
    call       void [mscorlib]System.Console::WriteLine(int32)
    ret
  }
  .method public virtual instance int32 get_P2(int32& i)
  {
    ldc.i4.7
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance void set_P2(int32& i, int32 v)
  {
    ldc.i4.8
    call       void [mscorlib]System.Console::WriteLine(int32)
    ret
  }


  .property instance int32 P3(char)
  {
    .get instance int32 A::get_P3(char)
    .set instance void A::set_P3(char, int32)
  }
  .property instance int32 P3(int32&)
  {
    .get instance int32 A::get_P3(int32&)
    .set instance void A::set_P3(int32&, int32)
  }
  .method public virtual instance int32 get_P3(char i)
  {
    ldc.i4.s 9
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance void set_P3(char i, int32 v)
  {
    ldc.i4.s 10
    call       void [mscorlib]System.Console::WriteLine(int32)
    ret
  }
  .method public virtual instance int32 get_P3(int32& i)
  {
    ldc.i4.s 11
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance void set_P3(int32& i, int32 v)
  {
    ldc.i4.s 12
    call       void [mscorlib]System.Console::WriteLine(int32)
    ret
  }


  .property instance int32 P4(int64&)
  {
    .get instance int32 A::get_P4(int64&)
    .set instance void A::set_P4(int64&, int32)
  }
  .property instance int32 P4(int32&)
  {
    .get instance int32 A::get_P4(int32&)
    .set instance void A::set_P4(int32&, int32)
  }
  .method public virtual instance int32 get_P4(int64& i)
  {
    ldc.i4.s 13
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance void set_P4(int64& i, int32 v)
  {
    ldc.i4.s 14
    call       void [mscorlib]System.Console::WriteLine(int32)
    ret
  }
  .method public virtual instance int32 get_P4(int32& i)
  {
    ldc.i4.s 15
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance void set_P4(int32& i, int32 v)
  {
    ldc.i4.s 16
    call       void [mscorlib]System.Console::WriteLine(int32)
    ret
  }


  .property instance int32 P5(int64&)
  {
    .get instance int32 A::get_P5(int64&)
    .set instance void A::set_P5(int64&, int32)
  }
  .property instance int32 P5(char&)
  {
    .get instance int32 A::get_P5(char&)
    .set instance void A::set_P5(char&, int32)
  }
  .method public virtual instance int32 get_P5(int64& i)
  {
    ldc.i4.s 17
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance void set_P5(int64& i, int32 v)
  {
    ldc.i4.s 18
    call       void [mscorlib]System.Console::WriteLine(int32)
    ret
  }
  .method public virtual instance int32 get_P5(char& i)
  {
    ldc.i4.s 19
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance void set_P5(char& i, int32 v)
  {
    ldc.i4.s 20
    call       void [mscorlib]System.Console::WriteLine(int32)
    ret
  }


  .property instance int32 P6(int64&)
  {
    .get instance int32 A::get_P6(int64&)
    .set instance void A::set_P6(int64&, int32)
  }
  .property instance int32 P6(int32)
  {
    .get instance int32 A::get_P6(int32)
    .set instance void A::set_P6(int32, int32)
  }
  .method public virtual instance int32 get_P6(int64& i)
  {
    ldc.i4.s 21
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance void set_P6(int64& i, int32 v)
  {
    ldc.i4.s 22
    call       void [mscorlib]System.Console::WriteLine(int32)
    ret
  }
  .method public virtual instance int32 get_P6(int32 i)
  {
    ldc.i4.s 23
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance void set_P6(int32 i, int32 v)
  {
    ldc.i4.s 24
    call       void [mscorlib]System.Console::WriteLine(int32)
    ret
  }


  .property instance int32 P7(int64&)
  {
    .get instance int32 A::get_P7(int64&)
    .set instance void A::set_P7(int64&, int32)
  }
  .property instance int32 P7(char)
  {
    .get instance int32 A::get_P7(char)
    .set instance void A::set_P7(char, int32)
  }
  .method public virtual instance int32 get_P7(int64& i)
  {
    ldc.i4.s 25
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance void set_P7(int64& i, int32 v)
  {
    ldc.i4.s 26
    call       void [mscorlib]System.Console::WriteLine(int32)
    ret
  }
  .method public virtual instance int32 get_P7(char i)
  {
    ldc.i4.s 27
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance void set_P7(char i, int32 v)
  {
    ldc.i4.s 28
    call       void [mscorlib]System.Console::WriteLine(int32)
    ret
  }


  .property instance int32 P8(int64)
  {
    .get instance int32 A::get_P8(int64)
    .set instance void A::set_P8(int64, int32)
  }
  .property instance int32 P8(char&)
  {
    .get instance int32 A::get_P8(char&)
    .set instance void A::set_P8(char&, int32)
  }
  .method public virtual instance int32 get_P8(int64 i)
  {
    ldc.i4.s 29
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance void set_P8(int64 i, int32 v)
  {
    ldc.i4.s 30
    call       void [mscorlib]System.Console::WriteLine(int32)
    ret
  }
  .method public virtual instance int32 get_P8(char& i)
  {
    ldc.i4.s 31
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance void set_P8(char& i, int32 v)
  {
    ldc.i4.s 32
    call       void [mscorlib]System.Console::WriteLine(int32)
    ret
  }
}
";

            var source2 =
@"
using System;
using System.Runtime.InteropServices;

class Test
{
   public static void Main()
   {
       IA a = new A();
       int i = 1;
       long l = 1;
       char c = 'c';
       int value;

//     int P1(int) { get; set; }
//     int P1(ref int) { get; set; }

       value = a.P1[10];
       a.P1[10] = value;
       //value = a.P1[10L];         CS1503
       //a.P1[10L] = value;         CS1503
       value = a.P1['c'];
       a.P1['c'] = value;
       value = a.P1[i];
       a.P1[i] = value;
       //value = a.P1[l];           CS1503
       //a.P1[l] = value;           CS1503
       value = a.P1[c];
       a.P1[c] = value;
       value = a.P1[ref i];
       a.P1[ref i] = value;
       //value = a.P1[ref l];       CS1615
       //a.P1[ref l] = value;       CS1615
       //value = a.P1[ref c];       CS1615
       //a.P1[ref c] = value;       CS1615

//     int P2(long) { get; set; }
//     int P2(ref int) { get; set; }

       Console.WriteLine();
       value = a.P2[10];
       a.P2[10] = value;
       value = a.P2[10L];
       a.P2[10L] = value;
       value = a.P2['c'];
       a.P2['c'] = value;
       value = a.P2[i];
       a.P2[i] = value;
       value = a.P2[l];
       a.P2[l] = value;
       value = a.P2[c];
       a.P2[c] = value;
       value = a.P2[ref i];
       a.P2[ref i] = value;
       //value = a.P2[ref l];       CS1615
       //a.P2[ref l] = value;       CS1615
       //value = a.P2[ref c];       CS1615
       //a.P2[ref c] = value;       CS1615

//     int P3(char) { get; set; }
//     int P3(ref int) { get; set; }

       Console.WriteLine();
       value = a.P3[10];
       a.P3[10] = value;
       //value = a.P3[10L];         CS1503
       //a.P3[10L] = value;         CS1503
       value = a.P3['c'];
       a.P3['c'] = value;
       value = a.P3[i];
       a.P3[i] = value;
       //value = a.P3[l];           CS1503
       //a.P3[l] = value;           CS1503
       value = a.P3[c];
       a.P3[c] = value;
       value = a.P3[ref i];
       a.P3[ref i] = value;
       //value = a.P3[ref l];       CS1615
       //a.P3[ref l] = value;       CS1615
       //value = a.P3[ref c];       CS1615
       //a.P3[ref c] = value;       CS1615

//     int P4(ref int) { get; set; }
//     int P4(ref long) { get; set; }

       Console.WriteLine();
       //value = a.P4[10];          CS0121
       //a.P4[10] = value;          CS0121
       value = a.P4[10L];
       a.P4[10L] = value;
       //value = a.P4['c'];         CS0121
       //a.P4['c'] = value;         CS0121
       //value = a.P4[i];           CS0121
       //a.P4[i] = value;           CS0121
       value = a.P4[l];
       a.P4[l] = value;
       //value = a.P4[c];           CS0121
       //a.P4[c] = value;           CS0121
       value = a.P4[ref i];
       a.P4[ref i] = value;
       value = a.P4[ref l];
       a.P4[ref l] = value;
       //value = a.P4[ref c];       CS1503
       //a.P4[ref c] = value;       CS1503

//     int P5(ref char) { get; set; }
//     int P5(ref long) { get; set; }

       Console.WriteLine();
       value = a.P5[10];
       a.P5[10] = value;
       value = a.P5[10L];
       a.P5[10L] = value;
       //value = a.P5['c'];         CS0121
       //a.P5['c'] = value;         CS0121
       value = a.P5[i];
       a.P5[i] = value;
       value = a.P5[l];
       a.P5[l] = value;
       //value = a.P5[c];           CS0121
       //a.P5[c] = value;           CS0121
       //value = a.P5[ref i];       CS1503
       //a.P5[ref i] = value;       CS1503
       value = a.P5[ref l];
       a.P5[ref l] = value;
       value = a.P5[ref c];
       a.P5[ref c] = value;

//     int P6(ref long) { get; set; }
//     int P6(int) { get; set; }

       Console.WriteLine();
       value = a.P6[10];
       a.P6[10] = value;
       value = a.P6[10L];
       a.P6[10L] = value;
       value = a.P6['c'];
       a.P6['c'] = value;
       value = a.P6[i];
       a.P6[i] = value;
       value = a.P6[l];
       a.P6[l] = value;
       value = a.P6[c];
       a.P6[c] = value;
       //value = a.P6[ref i];       CS1503
       //a.P6[ref i] = value;       CS1503
       value = a.P6[ref l];
       a.P6[ref l] = value;
       //value = a.P6[ref c];       CS1503
       //a.P6[ref c] = value;       CS1503

//     int P7(ref long) { get; set; }
//     int P7(char) { get; set; }

       Console.WriteLine();
       value = a.P7[10];
       a.P7[10] = value;
       value = a.P7[10L];
       a.P7[10L] = value;
       value = a.P7['c'];
       a.P7['c'] = value;
       value = a.P7[i];
       a.P7[i] = value;
       value = a.P7[l];
       a.P7[l] = value;
       value = a.P7[c];
       a.P7[c] = value;
       //value = a.P7[ref i];       CS1503
       //a.P7[ref i] = value;       CS1503
       value = a.P7[ref l];
       a.P7[ref l] = value;
       //value = a.P7[ref c];       CS1503
       //a.P7[ref c] = value;       CS1503

//     int P8(ref char) { get; set; }
//     int P8(long) { get; set; }

       Console.WriteLine();
       value = a.P8[10];
       a.P8[10] = value;
       value = a.P8[10L];
       a.P8[10L] = value;
       value = a.P8['c'];
       a.P8['c'] = value;
       value = a.P8[i];
       a.P8[i] = value;
       value = a.P8[l];
       a.P8[l] = value;
       value = a.P8[c];
       a.P8[c] = value;
       //value = a.P8[ref i];       CS1615
       //a.P8[ref i] = value;       CS1615
       //value = a.P8[ref l];       CS1615
       //a.P8[ref l] = value;       CS1615
       value = a.P8[ref c];
       a.P8[ref c] = value;
   }
}
";
            var expectedOutput = @"1
2
1
2
1
2
1
2
3
4

5
6
5
6
5
6
5
6
5
6
5
6
7
8

11
12
9
10
11
12
9
10
11
12

13
14
13
14
15
16
13
14

17
18
17
18
17
18
17
18
17
18
19
20

23
24
21
22
23
24
23
24
21
22
23
24
21
22

25
26
25
26
27
28
25
26
25
26
27
28
25
26

29
30
29
30
29
30
29
30
29
30
29
30
31
32";
            var compilation = CreateCompilationWithCustomILSource(source2, source1, compOptions: TestOptions.Exe);
            CompileAndVerify(compilation, expectedOutput: expectedOutput);
        }

        [Fact, WorkItem(546176)]
        public void RefOmittedComCall_OverloadResolution_SingleArgument_IndexedProperties_ErrorCases()
        {
            var source1 =
@"
.class interface public abstract import IA
{
  .custom instance void [mscorlib]System.Runtime.InteropServices.CoClassAttribute::.ctor(class [mscorlib]System.Type) = ( 01 00 01 41 00 00 )
  .custom instance void [mscorlib]System.Runtime.InteropServices.GuidAttribute::.ctor(string) = ( 01 00 24 31 36 35 46 37 35 32 44 2D 45 39 43 34 2D 34 46 37 45 2D 42 30 44 30 2D 43 44 46 44 37 41 33 36 45 32 31 31 00 00 )

  .property instance int32 P1(int32)
  {
    .get instance int32 IA::get_P1(int32)
    .set instance void IA::set_P1(int32, int32)
  }
  .property instance int32 P1(int32&)
  {
    .get instance int32 IA::get_P1(int32&)
    .set instance void IA::set_P1(int32&, int32)
  }
  .method public abstract virtual instance int32 get_P1(int32 i) { }
  .method public abstract virtual instance void set_P1(int32 i, int32 v) { }
  .method public abstract virtual instance int32 get_P1(int32& i) { }
  .method public abstract virtual instance void set_P1(int32& i, int32 v) { }


  .property instance int32 P2(int64)
  {
    .get instance int32 IA::get_P2(int64)
    .set instance void IA::set_P2(int64, int32)
  }
  .property instance int32 P2(int32&)
  {
    .get instance int32 IA::get_P2(int32&)
    .set instance void IA::set_P2(int32&, int32)
  }
  .method public abstract virtual instance int32 get_P2(int64 i) { }
  .method public abstract virtual instance void set_P2(int64 i, int32 v) { }
  .method public abstract virtual instance int32 get_P2(int32& i) { }
  .method public abstract virtual instance void set_P2(int32& i, int32 v) { }


  .property instance int32 P3(char)
  {
    .get instance int32 IA::get_P3(char)
    .set instance void IA::set_P3(char, int32)
  }
  .property instance int32 P3(int32&)
  {
    .get instance int32 IA::get_P3(int32&)
    .set instance void IA::set_P3(int32&, int32)
  }
  .method public abstract virtual instance int32 get_P3(char i) { }
  .method public abstract virtual instance void set_P3(char i, int32 v) { }
  .method public abstract virtual instance int32 get_P3(int32& i) { }
  .method public abstract virtual instance void set_P3(int32& i, int32 v) { }


  .property instance int32 P4(int64&)
  {
    .get instance int32 IA::get_P4(int64&)
    .set instance void IA::set_P4(int64&, int32)
  }
  .property instance int32 P4(int32&)
  {
    .get instance int32 IA::get_P4(int32&)
    .set instance void IA::set_P4(int32&, int32)
  }
  .method public abstract virtual instance int32 get_P4(int64& i) { }
  .method public abstract virtual instance void set_P4(int64& i, int32 v) { }
  .method public abstract virtual instance int32 get_P4(int32& i) { }
  .method public abstract virtual instance void set_P4(int32& i, int32 v) { }


  .property instance int32 P5(int64&)
  {
    .get instance int32 IA::get_P5(int64&)
    .set instance void IA::set_P5(int64&, int32)
  }
  .property instance int32 P5(char&)
  {
    .get instance int32 IA::get_P5(char&)
    .set instance void IA::set_P5(char&, int32)
  }
  .method public abstract virtual instance int32 get_P5(int64& i) { }
  .method public abstract virtual instance void set_P5(int64& i, int32 v) { }
  .method public abstract virtual instance int32 get_P5(char& i) { }
  .method public abstract virtual instance void set_P5(char& i, int32 v) { }


  .property instance int32 P6(int64&)
  {
    .get instance int32 IA::get_P6(int64&)
    .set instance void IA::set_P6(int64&, int32)
  }
  .property instance int32 P6(int32)
  {
    .get instance int32 IA::get_P6(int32)
    .set instance void IA::set_P6(int32, int32)
  }
  .method public abstract virtual instance int32 get_P6(int64& i) { }
  .method public abstract virtual instance void set_P6(int64& i, int32 v) { }
  .method public abstract virtual instance int32 get_P6(int32 i) { }
  .method public abstract virtual instance void set_P6(int32 i, int32 v) { }


  .property instance int32 P7(int64&)
  {
    .get instance int32 IA::get_P7(int64&)
    .set instance void IA::set_P7(int64&, int32)
  }
  .property instance int32 P7(char)
  {
    .get instance int32 IA::get_P7(char)
    .set instance void IA::set_P7(char, int32)
  }
  .method public abstract virtual instance int32 get_P7(int64& i) { }
  .method public abstract virtual instance void set_P7(int64& i, int32 v) { }
  .method public abstract virtual instance int32 get_P7(char i) { }
  .method public abstract virtual instance void set_P7(char i, int32 v) { }


  .property instance int32 P8(int64)
  {
    .get instance int32 IA::get_P8(int64)
    .set instance void IA::set_P8(int64, int32)
  }
  .property instance int32 P8(char&)
  {
    .get instance int32 IA::get_P8(char&)
    .set instance void IA::set_P8(char&, int32)
  }
  .method public abstract virtual instance int32 get_P8(int64 i) { }
  .method public abstract virtual instance void set_P8(int64 i, int32 v) { }
  .method public abstract virtual instance int32 get_P8(char& i) { }
  .method public abstract virtual instance void set_P8(char& i, int32 v) { }

}


.class public A implements IA
{
  .method public hidebysig specialname rtspecialname instance void .ctor()
  {
    ret
  }

  .property instance int32 P1(int32)
  {
    .get instance int32 A::get_P1(int32)
    .set instance void A::set_P1(int32, int32)
  }
  .property instance int32 P1(int32&)
  {
    .get instance int32 A::get_P1(int32&)
    .set instance void A::set_P1(int32&, int32)
  }
  .method public virtual instance int32 get_P1(int32 i)
  {
    ldc.i4.1
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance void set_P1(int32 i, int32 v)
  {
    ldc.i4.2
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance int32 get_P1(int32& i)
  {
    ldc.i4.3
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance void set_P1(int32& i, int32 v)
  {
    ldc.i4.4
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }


  .property instance int32 P2(int64)
  {
    .get instance int32 A::get_P2(int64)
    .set instance void A::set_P2(int64, int32)
  }
  .property instance int32 P2(int32&)
  {
    .get instance int32 A::get_P2(int32&)
    .set instance void A::set_P2(int32&, int32)
  }
  .method public virtual instance int32 get_P2(int64 i)
  {
    ldc.i4.5
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance void set_P2(int64 i, int32 v)
  {
    ldc.i4.6
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance int32 get_P2(int32& i)
  {
    ldc.i4.7
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance void set_P2(int32& i, int32 v)
  {
    ldc.i4.8
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }


  .property instance int32 P3(char)
  {
    .get instance int32 A::get_P3(char)
    .set instance void A::set_P3(char, int32)
  }
  .property instance int32 P3(int32&)
  {
    .get instance int32 A::get_P3(int32&)
    .set instance void A::set_P3(int32&, int32)
  }
  .method public virtual instance int32 get_P3(char i)
  {
    ldc.i4.s 9
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance void set_P3(char i, int32 v)
  {
    ldc.i4.s 10
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance int32 get_P3(int32& i)
  {
    ldc.i4.s 11
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance void set_P3(int32& i, int32 v)
  {
    ldc.i4.s 12
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }


  .property instance int32 P4(int64&)
  {
    .get instance int32 A::get_P4(int64&)
    .set instance void A::set_P4(int64&, int32)
  }
  .property instance int32 P4(int32&)
  {
    .get instance int32 A::get_P4(int32&)
    .set instance void A::set_P4(int32&, int32)
  }
  .method public virtual instance int32 get_P4(int64& i)
  {
    ldc.i4.s 13
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance void set_P4(int64& i, int32 v)
  {
    ldc.i4.s 14
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance int32 get_P4(int32& i)
  {
    ldc.i4.s 15
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance void set_P4(int32& i, int32 v)
  {
    ldc.i4.s 16
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }


  .property instance int32 P5(int64&)
  {
    .get instance int32 A::get_P5(int64&)
    .set instance void A::set_P5(int64&, int32)
  }
  .property instance int32 P5(char&)
  {
    .get instance int32 A::get_P5(char&)
    .set instance void A::set_P5(char&, int32)
  }
  .method public virtual instance int32 get_P5(int64& i)
  {
    ldc.i4.s 17
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance void set_P5(int64& i, int32 v)
  {
    ldc.i4.s 18
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance int32 get_P5(char& i)
  {
    ldc.i4.s 19
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance void set_P5(char& i, int32 v)
  {
    ldc.i4.s 20
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }


  .property instance int32 P6(int64&)
  {
    .get instance int32 A::get_P6(int64&)
    .set instance void A::set_P6(int64&, int32)
  }
  .property instance int32 P6(int32)
  {
    .get instance int32 A::get_P6(int32)
    .set instance void A::set_P6(int32, int32)
  }
  .method public virtual instance int32 get_P6(int64& i)
  {
    ldc.i4.s 21
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance void set_P6(int64& i, int32 v)
  {
    ldc.i4.s 22
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance int32 get_P6(int32 i)
  {
    ldc.i4.s 23
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance void set_P6(int32 i, int32 v)
  {
    ldc.i4.s 24
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }


  .property instance int32 P7(int64&)
  {
    .get instance int32 A::get_P7(int64&)
    .set instance void A::set_P7(int64&, int32)
  }
  .property instance int32 P7(char)
  {
    .get instance int32 A::get_P7(char)
    .set instance void A::set_P7(char, int32)
  }
  .method public virtual instance int32 get_P7(int64& i)
  {
    ldc.i4.s 25
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance void set_P7(int64& i, int32 v)
  {
    ldc.i4.s 26
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance int32 get_P7(char i)
  {
    ldc.i4.s 27
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance void set_P7(char i, int32 v)
  {
    ldc.i4.s 28
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }


  .property instance int32 P8(int64)
  {
    .get instance int32 A::get_P8(int64)
    .set instance void A::set_P8(int64, int32)
  }
  .property instance int32 P8(char&)
  {
    .get instance int32 A::get_P8(char&)
    .set instance void A::set_P8(char&, int32)
  }
  .method public virtual instance int32 get_P8(int64 i)
  {
    ldc.i4.s 29
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance void set_P8(int64 i, int32 v)
  {
    ldc.i4.s 30
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance int32 get_P8(char& i)
  {
    ldc.i4.s 31
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
  .method public virtual instance void set_P8(char& i, int32 v)
  {
    ldc.i4.s 32
    call       void [mscorlib]System.Console::WriteLine(int32)
    ldc.i4.0
    ret
  }
}
";

            var source2 =
@"
class Test
{
   public static void Main()
   {
       IA a = new A();
       int i = 1;
       long l = 1;
       char c = 'c';
       int value;

//     int P1(int) { get; set; }
//     int P1(ref int) { get; set; }

       value = a.P1[10L];         // CS1503
       a.P1[10L] = value;         // CS1503
       value = a.P1[l];           // CS1503
       a.P1[l] = value;           // CS1503
       value = a.P1[ref l];       // CS1615
       a.P1[ref l] = value;       // CS1615
       value = a.P1[ref c];       // CS1615
       a.P1[ref c] = value;       // CS1615

//     int P2(long) { get; set; }
//     int P2(ref int) { get; set; }

       value = a.P2[ref l];       // CS1615
       a.P2[ref l] = value;       // CS1615
       value = a.P2[ref c];       // CS1615
       a.P2[ref c] = value;       // CS1615

//     int P3(char) { get; set; }
//     int P3(ref int) { get; set; }

       value = a.P3[10L];         // CS1503
       a.P3[10L] = value;         // CS1503
       value = a.P3[l];           // CS1503
       a.P3[l] = value;           // CS1503
       value = a.P3[ref l];       // CS1615
       a.P3[ref l] = value;       // CS1615
       value = a.P3[ref c];       // CS1615
       a.P3[ref c] = value;       // CS1615

//     int P4(ref int) { get; set; }
//     int P4(ref long) { get; set; }

       value = a.P4[10];          // CS0121
       a.P4[10] = value;          // CS0121
       value = a.P4['c'];         // CS0121
       a.P4['c'] = value;         // CS0121
       value = a.P4[i];           // CS0121
       a.P4[i] = value;           // CS0121
       value = a.P4[c];           // CS0121
       a.P4[c] = value;           // CS0121
       value = a.P4[ref c];       // CS1503
       a.P4[ref c] = value;       // CS1503

//     int P5(ref char) { get; set; }
//     int P5(ref long) { get; set; }

       value = a.P5['c'];         // CS0121
       a.P5['c'] = value;         // CS0121
       value = a.P5[c];           // CS0121
       a.P5[c] = value;           // CS0121
       value = a.P5[ref i];       // CS1503
       a.P5[ref i] = value;       // CS1503

//     int P6(ref long) { get; set; }
//     int P6(int) { get; set; }

       value = a.P6[ref i];       // CS1503
       a.P6[ref i] = value;       // CS1503
       value = a.P6[ref c];       // CS1503
       a.P6[ref c] = value;       // CS1503

//     int P7(ref long) { get; set; }
//     int P7(char) { get; set; }

       value = a.P7[ref i];       // CS1503
       a.P7[ref i] = value;       // CS1503
       value = a.P7[ref c];       // CS1503
       a.P7[ref c] = value;       // CS1503

//     int P8(ref char) { get; set; }
//     int P8(long) { get; set; }

       value = a.P8[ref i];       // CS1615
       a.P8[ref i] = value;       // CS1615
       value = a.P8[ref l];       // CS1615
       a.P8[ref l] = value;       // CS1615
   }
}
";
            CreateCompilationWithCustomILSource(source2, source1).VerifyDiagnostics(
                // (15,21): error CS1503: Argument 1: cannot convert from 'long' to 'int'
                //        value = a.P1[10L];         // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "10L").WithArguments("1", "long", "int"),
                // (16,13): error CS1503: Argument 1: cannot convert from 'long' to 'int'
                //        a.P1[10L] = value;         // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "10L").WithArguments("1", "long", "int"),
                // (17,21): error CS1503: Argument 1: cannot convert from 'long' to 'int'
                //        value = a.P1[l];           // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("1", "long", "int"),
                // (18,13): error CS1503: Argument 1: cannot convert from 'long' to 'int'
                //        a.P1[l] = value;           // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("1", "long", "int"),
                // (19,25): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        value = a.P1[ref l];       // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "l").WithArguments("1", "ref"),
                // (20,17): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        a.P1[ref l] = value;       // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "l").WithArguments("1", "ref"),
                // (21,25): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        value = a.P1[ref c];       // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "c").WithArguments("1", "ref"),
                // (22,17): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        a.P1[ref c] = value;       // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "c").WithArguments("1", "ref"),
                // (27,25): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        value = a.P2[ref l];       // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "l").WithArguments("1", "ref"),
                // (28,17): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        a.P2[ref l] = value;       // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "l").WithArguments("1", "ref"),
                // (29,25): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        value = a.P2[ref c];       // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "c").WithArguments("1", "ref"),
                // (30,17): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        a.P2[ref c] = value;       // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "c").WithArguments("1", "ref"),
                // (35,21): error CS1503: Argument 1: cannot convert from 'long' to 'char'
                //        value = a.P3[10L];         // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "10L").WithArguments("1", "long", "char"),
                // (36,13): error CS1503: Argument 1: cannot convert from 'long' to 'char'
                //        a.P3[10L] = value;         // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "10L").WithArguments("1", "long", "char"),
                // (37,21): error CS1503: Argument 1: cannot convert from 'long' to 'char'
                //        value = a.P3[l];           // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("1", "long", "char"),
                // (38,13): error CS1503: Argument 1: cannot convert from 'long' to 'char'
                //        a.P3[l] = value;           // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("1", "long", "char"),
                // (39,25): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        value = a.P3[ref l];       // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "l").WithArguments("1", "ref"),
                // (40,17): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        a.P3[ref l] = value;       // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "l").WithArguments("1", "ref"),
                // (41,25): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        value = a.P3[ref c];       // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "c").WithArguments("1", "ref"),
                // (42,17): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        a.P3[ref c] = value;       // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "c").WithArguments("1", "ref"),
                // (47,16): error CS0121: The call is ambiguous between the following methods or properties: 'IA.P4[ref long]' and 'IA.P4[ref int]'
                //        value = a.P4[10];          // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "a.P4[10]").WithArguments("IA.P4[ref long]", "IA.P4[ref int]"),
                // (58,8): error CS0121: The call is ambiguous between the following methods or properties: 'IA.P4[ref long]' and 'IA.P4[ref int]'
                //        a.P4[10] = value;          // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "a.P4[10]").WithArguments("IA.P4[ref long]", "IA.P4[ref int]"),
                // (49,16): error CS0121: The call is ambiguous between the following methods or properties: 'IA.P4[ref long]' and 'IA.P4[ref int]'
                //        value = a.P4['c'];         // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "a.P4['c']").WithArguments("IA.P4[ref long]", "IA.P4[ref int]"),
                // (50,8): error CS0121: The call is ambiguous between the following methods or properties: 'IA.P4[ref long]' and 'IA.P4[ref int]'
                //        a.P4['c'] = value;         // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "a.P4['c']").WithArguments("IA.P4[ref long]", "IA.P4[ref int]"),
                // (51,16): error CS0121: The call is ambiguous between the following methods or properties: 'IA.P4[ref long]' and 'IA.P4[ref int]'
                //        value = a.P4[i];           // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "a.P4[i]").WithArguments("IA.P4[ref long]", "IA.P4[ref int]"),
                // (52,8): error CS0121: The call is ambiguous between the following methods or properties: 'IA.P4[ref long]' and 'IA.P4[ref int]'
                //        a.P4[i] = value;           // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "a.P4[i]").WithArguments("IA.P4[ref long]", "IA.P4[ref int]"),
                // (53,16): error CS0121: The call is ambiguous between the following methods or properties: 'IA.P4[ref long]' and 'IA.P4[ref int]'
                //        value = a.P4[c];           // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "a.P4[c]").WithArguments("IA.P4[ref long]", "IA.P4[ref int]"),
                // (54,8): error CS0121: The call is ambiguous between the following methods or properties: 'IA.P4[ref long]' and 'IA.P4[ref int]'
                //        a.P4[c] = value;           // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "a.P4[c]").WithArguments("IA.P4[ref long]", "IA.P4[ref int]"),
                // (55,25): error CS1503: Argument 1: cannot convert from 'ref char' to 'ref long'
                //        value = a.P4[ref c];       // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "c").WithArguments("1", "ref char", "ref long"),
                // (56,17): error CS1503: Argument 1: cannot convert from 'ref char' to 'ref long'
                //        a.P4[ref c] = value;       // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "c").WithArguments("1", "ref char", "ref long"),
                // (61,16): error CS0121: The call is ambiguous between the following methods or properties: 'IA.P5[ref long]' and 'IA.P5[ref char]'
                //        value = a.P5['c'];         // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "a.P5['c']").WithArguments("IA.P5[ref long]", "IA.P5[ref char]"),
                // (62,8): error CS0121: The call is ambiguous between the following methods or properties: 'IA.P5[ref long]' and 'IA.P5[ref char]'
                //        a.P5['c'] = value;         // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "a.P5['c']").WithArguments("IA.P5[ref long]", "IA.P5[ref char]"),
                // (63,16): error CS0121: The call is ambiguous between the following methods or properties: 'IA.P5[ref long]' and 'IA.P5[ref char]'
                //        value = a.P5[c];           // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "a.P5[c]").WithArguments("IA.P5[ref long]", "IA.P5[ref char]"),
                // (64,8): error CS0121: The call is ambiguous between the following methods or properties: 'IA.P5[ref long]' and 'IA.P5[ref char]'
                //        a.P5[c] = value;           // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "a.P5[c]").WithArguments("IA.P5[ref long]", "IA.P5[ref char]"),
                // (65,25): error CS1503: Argument 1: cannot convert from 'ref int' to 'ref long'
                //        value = a.P5[ref i];       // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "i").WithArguments("1", "ref int", "ref long"),
                // (66,17): error CS1503: Argument 1: cannot convert from 'ref int' to 'ref long'
                //        a.P5[ref i] = value;       // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "i").WithArguments("1", "ref int", "ref long"),
                // (71,25): error CS1503: Argument 1: cannot convert from 'ref int' to 'ref long'
                //        value = a.P6[ref i];       // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "i").WithArguments("1", "ref int", "ref long"),
                // (72,17): error CS1503: Argument 1: cannot convert from 'ref int' to 'ref long'
                //        a.P6[ref i] = value;       // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "i").WithArguments("1", "ref int", "ref long"),
                // (73,25): error CS1503: Argument 1: cannot convert from 'ref char' to 'ref long'
                //        value = a.P6[ref c];       // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "c").WithArguments("1", "ref char", "ref long"),
                // (74,17): error CS1503: Argument 1: cannot convert from 'ref char' to 'ref long'
                //        a.P6[ref c] = value;       // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "c").WithArguments("1", "ref char", "ref long"),
                // (79,25): error CS1503: Argument 1: cannot convert from 'ref int' to 'ref long'
                //        value = a.P7[ref i];       // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "i").WithArguments("1", "ref int", "ref long"),
                // (80,17): error CS1503: Argument 1: cannot convert from 'ref int' to 'ref long'
                //        a.P7[ref i] = value;       // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "i").WithArguments("1", "ref int", "ref long"),
                // (81,25): error CS1503: Argument 1: cannot convert from 'ref char' to 'ref long'
                //        value = a.P7[ref c];       // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "c").WithArguments("1", "ref char", "ref long"),
                // (82,17): error CS1503: Argument 1: cannot convert from 'ref char' to 'ref long'
                //        a.P7[ref c] = value;       // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "c").WithArguments("1", "ref char", "ref long"),
                // (87,25): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        value = a.P8[ref i];       // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "i").WithArguments("1", "ref"),
                // (88,17): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        a.P8[ref i] = value;       // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "i").WithArguments("1", "ref"),
                // (89,25): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        value = a.P8[ref l];       // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "l").WithArguments("1", "ref"),
                // (90,17): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        a.P8[ref l] = value;       // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "l").WithArguments("1", "ref"));
        }

        [Fact]
        public void RefOmittedComCall_OverloadResolution_MultipleArguments()
        {
            var source =
@"
using System;
using System.Runtime.InteropServices;

[ComImport]
[Guid(""A88A175D-2448-447A-B786-64682CBEF156"")]
public interface IRef1
{
    void M1(int x, long y);
    void M1(ref int x, int y);

    void M2(int x, char y);
    void M2(ref int x, int y);

    void M3(ref int x, char y);
    void M3(ref long x, int y);

    void M4(ref int x, long y);
    void M4(ref int x, ref int y);

    void M5(ref int x, char y);
    void M5(ref long x, int y);

    void M6(ref int x, int y);
    void M6(ref long x, int y);

    void M7(ref int x, long y);
    void M7(ref long x, ref int y);

    void M8(long x, ref int y);
    void M8(ref long x, int y);

    void M9(ref long x, ref int y);
    void M9(ref int x, ref long y);
}

public class Ref1Impl : IRef1
{
    public void M1(int x, long y) { Console.WriteLine(1); }
    public void M1(ref int x, int y) { Console.WriteLine(2); }

    public void M2(int x, char y) { Console.WriteLine(3); }
    public void M2(ref int x, int y) { Console.WriteLine(4); }

    public void M3(ref int x, char y) { Console.WriteLine(5); }
    public void M3(ref long x, int y) { Console.WriteLine(6); }

    public void M4(ref int x, long y) { Console.WriteLine(7); }
    public void M4(ref int x, ref int y) { Console.WriteLine(8); }

    public void M5(ref int x, char y) { Console.WriteLine(9); }
    public void M5(ref long x, int y) { Console.WriteLine(10); }

    public void M6(ref int x, int y) { Console.WriteLine(11); }
    public void M6(ref long x, int y) { Console.WriteLine(12); }

    public void M7(ref int x, long y) { Console.WriteLine(13); }
    public void M7(ref long x, ref int y) { Console.WriteLine(14); }

    public void M8(long x, ref int y) { Console.WriteLine(15); }
    public void M8(ref long x, int y) { Console.WriteLine(16); }

    public void M9(ref long x, ref int y) { Console.WriteLine(17); }
    public void M9(ref int x, ref long y) { Console.WriteLine(18); }
}

class Test
{
   public static void Main()
   {
       IRef1 ref1 = new Ref1Impl();
       int i = 1;
       long l = 1;
       char c = 'c';


//     void M1(int x, long y);
//     void M1(ref int x, int y);

       ref1.M1(i, i);
       ref1.M1(i, l);
       ref1.M1(i, c);
       //ref1.M1(l, i);      CS1503
       //ref1.M1(l, l);      CS1503
       //ref1.M1(l, c);      CS1503
       ref1.M1(c, i);
       ref1.M1(c, l);
       ref1.M1(c, c);

       ref1.M1(ref i, i);
       //ref1.M1(ref i, l);  CS1615
       ref1.M1(ref i, c);
       //ref1.M1(ref l, i);  CS1615
       //ref1.M1(ref l, l);  CS1615
       //ref1.M1(ref l, c);  CS1615
       //ref1.M1(ref c, i);  CS1615
       //ref1.M1(ref c, l);  CS1615
       //ref1.M1(ref c, c);  CS1615


//     void M2(int x, char y);
//     void M2(ref int x, int y);

       Console.WriteLine();
       ref1.M2(i, i);
       //ref1.M2(i, l);      CS1503
       ref1.M2(i, c);
       //ref1.M2(l, i);      CS1503, CS1503
       //ref1.M2(l, l);      CS1503, CS1503
       //ref1.M2(l, c);      CS1503
       ref1.M2(c, i);
       //ref1.M2(c, l);      CS1503
       ref1.M2(c, c);

       ref1.M2(ref i, i);
       //ref1.M2(ref i, l);  CS1615, CS1503
       ref1.M2(ref i, c);
       //ref1.M2(ref l, i);  CS1615, CS1503
       //ref1.M2(ref l, l);  CS1615, CS1503
       //ref1.M2(ref l, c);  CS1615
       //ref1.M2(ref c, i);  CS1615, CS1503
       //ref1.M2(ref c, l);  CS1615, CS1503
       //ref1.M2(ref c, c);  CS1615


//     void M3(ref int x, char y);
//     void M3(ref long x, int y);

       Console.WriteLine();
       ref1.M3(i, i);
       //ref1.M3(i, l);      CS1503
       ref1.M3(i, c);
       ref1.M3(l, i);
       //ref1.M3(l, l);      CS1620, CS1503
       ref1.M3(l, c);
       ref1.M3(c, i);
       //ref1.M3(c, l);      CS1503
       ref1.M3(c, c);

       //ref1.M3(ref i, i);  CS1503
       //ref1.M3(ref i, l);  CS1503
       ref1.M3(ref i, c);
       ref1.M3(ref l, i);
       //ref1.M3(ref l, l);  CS1503, CS1503
       ref1.M3(ref l, c);
       //ref1.M3(ref c, i);  CS1503, CS1503
       //ref1.M3(ref c, l);  CS1503, CS1503
       //ref1.M3(ref c, c);  CS1503


//     void M4(ref int x, long y);
//     void M4(ref int x, ref int y);

       Console.WriteLine();
       //ref1.M4(i, i);      CS0121
       ref1.M4(i, l);
       //ref1.M4(i, c);      CS0121
       //ref1.M4(l, i);      CS1620
       //ref1.M4(l, l);      CS1620
       //ref1.M4(l, c);      CS1620
       //ref1.M4(c, i);      CS0121
       ref1.M4(c, l);      
       //ref1.M4(c, c);      CS0121

       ref1.M4(i, ref i);
       //ref1.M4(l, ref i);  CS1620, CS1615
       ref1.M4(c, ref i);
       //ref1.M4(i, ref l);  CS1615
       //ref1.M4(l, ref l);  CS1620
       //ref1.M4(c, ref l);  CS1615

       ref1.M4(ref i, i);
       ref1.M4(ref i, l);
       ref1.M4(ref i, c);

       ref1.M4(ref i, ref i);


//     void M5(ref int x, char y);
//     void M5(ref long x, int y);

       Console.WriteLine();
       ref1.M5(i, i);
       //ref1.M5(i, l);    CS1503
       ref1.M5(i, c);
       ref1.M5(l, i);
       //ref1.M5(l, l);    CS1620, CS1503
       ref1.M5(l, c);
       ref1.M5(c, i);
       //ref1.M5(c, l);    CS1503
       ref1.M5(c, c);

       //ref1.M5(ref i, i);  CS1503
       //ref1.M5(ref i, l);  CS1503
       ref1.M5(ref i, c);
       ref1.M5(ref l, i);
       //ref1.M5(ref l, l);  CS1503
       ref1.M5(ref l, c);


//     void M6(ref int x, int y);
//     void M6(ref long x, int y);

       Console.WriteLine();
       //ref1.M6(i, i);    CS0121
       //ref1.M6(i, l);    CS1503
       //ref1.M6(i, c);    CS0121
       ref1.M6(l, i);
       //ref1.M6(l, l);    CS1620, CS1503
       ref1.M6(l, c);
       //ref1.M6(c, i);    CS0121
       //ref1.M6(c, l);    CS1503
       //ref1.M6(c, c);    CS0121

       ref1.M6(ref i, i);
       //ref1.M6(ref i, l);  CS1503
       ref1.M6(ref i, c);
       ref1.M6(ref l, i);
       //ref1.M6(ref l, l);  CS1503, CS1503
       ref1.M6(ref l, c);


//     void M7(ref int x, long y);
//     void M7(ref long x, ref int y);

       Console.WriteLine();
       //ref1.M7(i, i);    CS0121
       ref1.M7(i, l);
       //ref1.M7(i, c);    CS0121
       ref1.M7(l, i);
       //ref1.M7(l, l);    CS1620
       ref1.M7(l, c);
       //ref1.M7(c, i);    CS0121
       ref1.M7(c, l);
       //ref1.M7(c, c);    CS0121

       ref1.M7(i, ref i);
       ref1.M7(l, ref i);
       ref1.M7(c, ref i);
       //ref1.M7(i, ref l);  CS1615
       //ref1.M7(l, ref l);  CS1620, CS1615
       //ref1.M7(c, ref l);  CS1615

       ref1.M7(ref i, i);
       ref1.M7(ref i, l);
       ref1.M7(ref i, c);

       //ref1.M7(ref i, ref i);  CS1615


//     void M8(long x, ref int y);
//     void M8(ref long x, int y);

       Console.WriteLine();
       ref1.M8(i, i);
       //ref1.M8(i, l);    CS1620
       //ref1.M8(i, c);    CS0121
       //ref1.M8(l, i);    CS0121
       //ref1.M8(l, l);    CS1620
       ref1.M8(l, c);
       ref1.M8(c, i);
       //ref1.M8(c, l);    CS1620
       //ref1.M8(c, c);    CS0121

       ref1.M8(i, ref i);
       ref1.M8(l, ref i);
       ref1.M8(c, ref i);
       //ref1.M8(i, ref l);   CS1503
       //ref1.M8(l, ref l);   CS1503
       //ref1.M8(c, ref l);   CS1503

       //ref1.M8(ref i, i);   CS1615
       //ref1.M8(ref i, l);   CS1615, CS1620
       //ref1.M8(ref i, c);   CS1615
       ref1.M8(ref l, i);
       //ref1.M8(ref l, l);   CS1615, CS1620
       ref1.M8(ref l, c);

       //ref1.M8(ref i, ref i);   CS1615
       //ref1.M8(ref i, ref l);   CS1615, CS1503
       //ref1.M8(ref l, ref i);   CS1615
       //ref1.M8(ref l, ref l);   CS1615, CS1503


//     void M9(ref long x, ref int y);
//     void M9(ref int x, ref long y);

       Console.WriteLine();
       //ref1.M9(i, i);    CS0121
       ref1.M9(i, l);
       //ref1.M9(i, c);    CS0121
       ref1.M9(l, i);
       //ref1.M9(l, l);    CS1620
       ref1.M9(l, c);
       //ref1.M9(c, i);    CS0121
       ref1.M9(c, l);
       //ref1.M9(c, c);    CS0121

       ref1.M9(i, ref i);
       ref1.M9(l, ref i);
       ref1.M9(c, ref i);
       ref1.M9(i, ref l);
       //ref1.M9(l, ref l);   CS1503
       ref1.M9(c, ref l);

       ref1.M9(ref i, i);
       ref1.M9(ref i, l);
       ref1.M9(ref i, c);
       ref1.M9(ref l, i);
       //ref1.M9(ref l, l);   CS1620
       ref1.M9(ref l, c);

       //ref1.M9(ref i, ref i);   CS1503
       ref1.M9(ref i, ref l);
       ref1.M9(ref l, ref i);
       //ref1.M9(ref l, ref l);   CS1503
   }
}
";
            CompileAndVerify(source, expectedOutput: @"1
1
1
1
1
1
2
2

4
3
4
3
4
4

6
5
6
6
6
5
5
6
6

7
7
8
8
7
7
7
8

10
9
10
10
10
9
9
10
10

12
12
11
11
12
12

13
14
14
13
14
14
14
13
13
13

16
15
16
15
15
15
16
16

18
17
17
18
17
17
17
18
18
18
18
18
17
17
18
17");
        }

        [Fact]
        public void RefOmittedComCall_OverloadResolution_MultipleArguments_ErrorCases()
        {
            var source =
@"
using System;
using System.Runtime.InteropServices;

[ComImport]
[Guid(""A88A175D-2448-447A-B786-64682CBEF156"")]
public interface IRef1
{
    void M1(int x, long y);
    void M1(ref int x, int y);

    void M2(int x, char y);
    void M2(ref int x, int y);

    void M3(ref int x, char y);
    void M3(ref long x, int y);

    void M4(ref int x, long y);
    void M4(ref int x, ref int y);

    void M5(ref int x, char y);
    void M5(ref long x, int y);

    void M6(ref int x, int y);
    void M6(ref long x, int y);

    void M7(ref int x, long y);
    void M7(ref long x, ref int y);

    void M8(long x, ref int y);
    void M8(ref long x, int y);

    void M9(ref long x, ref int y);
    void M9(ref int x, ref long y);
}

public class Ref1Impl : IRef1
{
    public void M1(int x, long y) { Console.WriteLine(1); }
    public void M1(ref int x, int y) { Console.WriteLine(2); }

    public void M2(int x, char y) { Console.WriteLine(3); }
    public void M2(ref int x, int y) { Console.WriteLine(4); }

    public void M3(ref int x, char y) { Console.WriteLine(5); }
    public void M3(ref long x, int y) { Console.WriteLine(6); }

    public void M4(ref int x, long y) { Console.WriteLine(7); }
    public void M4(ref int x, ref int y) { Console.WriteLine(8); }

    public void M5(ref int x, char y) { Console.WriteLine(9); }
    public void M5(ref long x, int y) { Console.WriteLine(10); }

    public void M6(ref int x, int y) { Console.WriteLine(11); }
    public void M6(ref long x, int y) { Console.WriteLine(12); }

    public void M7(ref int x, long y) { Console.WriteLine(13); }
    public void M7(ref long x, ref int y) { Console.WriteLine(14); }

    public void M8(long x, ref int y) { Console.WriteLine(15); }
    public void M8(ref long x, int y) { Console.WriteLine(16); }

    public void M9(ref long x, ref int y) { Console.WriteLine(17); }
    public void M9(ref int x, ref long y) { Console.WriteLine(18); }
}

class Test
{
   public static void Main()
   {
       IRef1 ref1 = new Ref1Impl();
       int i = 1;
       long l = 1;
       char c = 'c';


//     void M1(int x, long y);
//     void M1(ref int x, int y);

       ref1.M1(l, i);      // CS1503
       ref1.M1(l, l);      // CS1503
       ref1.M1(l, c);      // CS1503
       ref1.M1(ref i, l);  // CS1615
       ref1.M1(ref l, i);  // CS1615
       ref1.M1(ref l, l);  // CS1615
       ref1.M1(ref l, c);  // CS1615
       ref1.M1(ref c, i);  // CS1615
       ref1.M1(ref c, l);  // CS1615
       ref1.M1(ref c, c);  // CS1615


//     void M2(int x, char y);
//     void M2(ref int x, int y);

       Console.WriteLine();
       ref1.M2(i, l);      // CS1503
       ref1.M2(l, i);      // CS1503, CS1503
       ref1.M2(l, l);      // CS1503, CS1503
       ref1.M2(l, c);      // CS1503
       ref1.M2(c, l);      // CS1503
       ref1.M2(ref i, l);  // CS1615, CS1503
       ref1.M2(ref l, i);  // CS1615, CS1503
       ref1.M2(ref l, l);  // CS1615, CS1503
       ref1.M2(ref l, c);  // CS1615
       ref1.M2(ref c, i);  // CS1615, CS1503
       ref1.M2(ref c, l);  // CS1615, CS1503
       ref1.M2(ref c, c);  // CS1615


//     void M3(ref int x, char y);
//     void M3(ref long x, int y);

       Console.WriteLine();
       ref1.M3(i, l);      // CS1503
       ref1.M3(l, l);      // CS1620, CS1503
       ref1.M3(c, l);      // CS1503
       ref1.M3(ref i, i);  // CS1503
       ref1.M3(ref i, l);  // CS1503
       ref1.M3(ref l, l);  // CS1503, CS1503
       ref1.M3(ref c, i);  // CS1503, CS1503
       ref1.M3(ref c, l);  // CS1503, CS1503
       ref1.M3(ref c, c);  // CS1503


//     void M4(ref int x, long y);
//     void M4(ref int x, ref int y);

       Console.WriteLine();
       ref1.M4(i, i);      // CS0121
       ref1.M4(i, c);      // CS0121
       ref1.M4(l, i);      // CS1620
       ref1.M4(l, l);      // CS1620
       ref1.M4(l, c);      // CS1620
       ref1.M4(c, i);      // CS0121
       ref1.M4(c, c);      // CS0121
       ref1.M4(l, ref i);  // CS1620, CS1615
       ref1.M4(i, ref l);  // CS1615
       ref1.M4(l, ref l);  // CS1620
       ref1.M4(c, ref l);  // CS1615


//     void M5(ref int x, char y);
//     void M5(ref long x, int y);

       Console.WriteLine();
       ref1.M5(i, l);    // CS1503
       ref1.M5(l, l);    // CS1620, CS1503
       ref1.M5(c, l);    // CS1503
       ref1.M5(ref i, i);  // CS1503
       ref1.M5(ref i, l);  // CS1503
       ref1.M5(ref l, l);  // CS1503


//     void M6(ref int x, int y);
//     void M6(ref long x, int y);

       Console.WriteLine();
       ref1.M6(i, i);    // CS0121
       ref1.M6(i, l);    // CS1503
       ref1.M6(i, c);    // CS0121
       ref1.M6(l, l);    // CS1620, CS1503
       ref1.M6(c, i);    // CS0121
       ref1.M6(c, l);    // CS1503
       ref1.M6(c, c);    // CS0121
       ref1.M6(ref i, l);  // CS1503
       ref1.M6(ref l, l);  // CS1503, CS1503


//     void M7(ref int x, long y);
//     void M7(ref long x, ref int y);

       Console.WriteLine();
       ref1.M7(i, i);    // CS0121
       ref1.M7(i, c);    // CS0121
       ref1.M7(l, l);    // CS1620
       ref1.M7(c, i);    // CS0121
       ref1.M7(c, c);    // CS0121
       ref1.M7(i, ref l);  // CS1615
       ref1.M7(l, ref l);  // CS1620, CS1615
       ref1.M7(c, ref l);  // CS1615
       ref1.M7(ref i, ref i);  // CS1615


//     void M8(long x, ref int y);
//     void M8(ref long x, int y);

       Console.WriteLine();
       ref1.M8(i, l);    // CS1620
       ref1.M8(i, c);    // CS0121
       ref1.M8(l, i);    // CS0121
       ref1.M8(l, l);    // CS1620
       ref1.M8(c, l);    // CS1620
       ref1.M8(c, c);    // CS0121
       ref1.M8(i, ref l);   // CS1503
       ref1.M8(l, ref l);   // CS1503
       ref1.M8(c, ref l);   // CS1503
       ref1.M8(ref i, i);   // CS1615
       ref1.M8(ref i, l);   // CS1615, CS1620
       ref1.M8(ref i, c);   // CS1615
       ref1.M8(ref l, l);   // CS1615, CS1620
       ref1.M8(ref i, ref i);   // CS1615
       ref1.M8(ref i, ref l);   // CS1615, CS1503
       ref1.M8(ref l, ref i);   // CS1615
       ref1.M8(ref l, ref l);   // CS1615, CS1503


//     void M9(ref long x, ref int y);
//     void M9(ref int x, ref long y);

       Console.WriteLine();
       ref1.M9(i, i);    // CS0121
       ref1.M9(i, c);    // CS0121
       ref1.M9(l, l);    // CS1620
       ref1.M9(c, i);    // CS0121
       ref1.M9(c, c);    // CS0121
       ref1.M9(l, ref l);   // CS1503
       ref1.M9(ref l, l);   // CS1620
       ref1.M9(ref i, ref i);   // CS1503
       ref1.M9(ref l, ref l);   // CS1503
   }
}
";
            CreateCompilationWithMscorlib(source).VerifyDiagnostics(
                // (80,16): error CS1503: Argument 1: cannot convert from 'long' to 'int'
                //        ref1.M1(l, i);      // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("1", "long", "int"),
                // (81,16): error CS1503: Argument 1: cannot convert from 'long' to 'int'
                //        ref1.M1(l, l);      // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("1", "long", "int"),
                // (82,16): error CS1503: Argument 1: cannot convert from 'long' to 'int'
                //        ref1.M1(l, c);      // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("1", "long", "int"),
                // (83,20): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        ref1.M1(ref i, l);  // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "i").WithArguments("1", "ref"),
                // (84,20): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        ref1.M1(ref l, i);  // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "l").WithArguments("1", "ref"),
                // (85,20): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        ref1.M1(ref l, l);  // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "l").WithArguments("1", "ref"),
                // (86,20): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        ref1.M1(ref l, c);  // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "l").WithArguments("1", "ref"),
                // (87,20): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        ref1.M1(ref c, i);  // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "c").WithArguments("1", "ref"),
                // (88,20): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        ref1.M1(ref c, l);  // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "c").WithArguments("1", "ref"),
                // (89,20): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        ref1.M1(ref c, c);  // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "c").WithArguments("1", "ref"),
                // (96,19): error CS1503: Argument 2: cannot convert from 'long' to 'char'
                //        ref1.M2(i, l);      // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("2", "long", "char"),
                // (97,16): error CS1503: Argument 1: cannot convert from 'long' to 'int'
                //        ref1.M2(l, i);      // CS1503, CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("1", "long", "int"),
                // (97,19): error CS1503: Argument 2: cannot convert from 'int' to 'char'
                //        ref1.M2(l, i);      // CS1503, CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "i").WithArguments("2", "int", "char"),
                // (98,16): error CS1503: Argument 1: cannot convert from 'long' to 'int'
                //        ref1.M2(l, l);      // CS1503, CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("1", "long", "int"),
                // (98,19): error CS1503: Argument 2: cannot convert from 'long' to 'char'
                //        ref1.M2(l, l);      // CS1503, CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("2", "long", "char"),
                // (99,16): error CS1503: Argument 1: cannot convert from 'long' to 'int'
                //        ref1.M2(l, c);      // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("1", "long", "int"),
                // (100,19): error CS1503: Argument 2: cannot convert from 'long' to 'char'
                //        ref1.M2(c, l);      // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("2", "long", "char"),
                // (101,20): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        ref1.M2(ref i, l);  // CS1615, CS1503
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "i").WithArguments("1", "ref"),
                // (101,23): error CS1503: Argument 2: cannot convert from 'long' to 'char'
                //        ref1.M2(ref i, l);  // CS1615, CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("2", "long", "char"),
                // (102,20): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        ref1.M2(ref l, i);  // CS1615, CS1503
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "l").WithArguments("1", "ref"),
                // (102,23): error CS1503: Argument 2: cannot convert from 'int' to 'char'
                //        ref1.M2(ref l, i);  // CS1615, CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "i").WithArguments("2", "int", "char"),
                // (103,20): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        ref1.M2(ref l, l);  // CS1615, CS1503
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "l").WithArguments("1", "ref"),
                // (103,23): error CS1503: Argument 2: cannot convert from 'long' to 'char'
                //        ref1.M2(ref l, l);  // CS1615, CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("2", "long", "char"),
                // (104,20): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        ref1.M2(ref l, c);  // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "l").WithArguments("1", "ref"),
                // (105,20): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        ref1.M2(ref c, i);  // CS1615, CS1503
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "c").WithArguments("1", "ref"),
                // (105,23): error CS1503: Argument 2: cannot convert from 'int' to 'char'
                //        ref1.M2(ref c, i);  // CS1615, CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "i").WithArguments("2", "int", "char"),
                // (106,20): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        ref1.M2(ref c, l);  // CS1615, CS1503
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "c").WithArguments("1", "ref"),
                // (106,23): error CS1503: Argument 2: cannot convert from 'long' to 'char'
                //        ref1.M2(ref c, l);  // CS1615, CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("2", "long", "char"),
                // (107,20): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        ref1.M2(ref c, c);  // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "c").WithArguments("1", "ref"),
                // (114,19): error CS1503: Argument 2: cannot convert from 'long' to 'char'
                //        ref1.M3(i, l);      // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("2", "long", "char"),
                // (115,16): error CS1620: Argument 1 must be passed with the 'ref' keyword
                //        ref1.M3(l, l);      // CS1620, CS1503
                Diagnostic(ErrorCode.ERR_BadArgRef, "l").WithArguments("1", "ref"),
                // (115,19): error CS1503: Argument 2: cannot convert from 'long' to 'char'
                //        ref1.M3(l, l);      // CS1620, CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("2", "long", "char"),
                // (116,19): error CS1503: Argument 2: cannot convert from 'long' to 'char'
                //        ref1.M3(c, l);      // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("2", "long", "char"),
                // (117,23): error CS1503: Argument 2: cannot convert from 'int' to 'char'
                //        ref1.M3(ref i, i);  // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "i").WithArguments("2", "int", "char"),
                // (118,23): error CS1503: Argument 2: cannot convert from 'long' to 'char'
                //        ref1.M3(ref i, l);  // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("2", "long", "char"),
                // (119,20): error CS1503: Argument 1: cannot convert from 'ref long' to 'ref int'
                //        ref1.M3(ref l, l);  // CS1503, CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("1", "ref long", "ref int"),
                // (119,23): error CS1503: Argument 2: cannot convert from 'long' to 'char'
                //        ref1.M3(ref l, l);  // CS1503, CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("2", "long", "char"),
                // (120,20): error CS1503: Argument 1: cannot convert from 'ref char' to 'ref int'
                //        ref1.M3(ref c, i);  // CS1503, CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "c").WithArguments("1", "ref char", "ref int"),
                // (120,23): error CS1503: Argument 2: cannot convert from 'int' to 'char'
                //        ref1.M3(ref c, i);  // CS1503, CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "i").WithArguments("2", "int", "char"),
                // (121,20): error CS1503: Argument 1: cannot convert from 'ref char' to 'ref int'
                //        ref1.M3(ref c, l);  // CS1503, CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "c").WithArguments("1", "ref char", "ref int"),
                // (121,23): error CS1503: Argument 2: cannot convert from 'long' to 'char'
                //        ref1.M3(ref c, l);  // CS1503, CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("2", "long", "char"),
                // (122,20): error CS1503: Argument 1: cannot convert from 'ref char' to 'ref int'
                //        ref1.M3(ref c, c);  // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "c").WithArguments("1", "ref char", "ref int"),
                // (129,8): error CS0121: The call is ambiguous between the following methods or properties: 'IRef1.M4(ref int, long)' and 'IRef1.M4(ref int, ref int)'
                //        ref1.M4(i, i);      // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "ref1.M4").WithArguments("IRef1.M4(ref int, long)", "IRef1.M4(ref int, ref int)"),
                // (130,8): error CS0121: The call is ambiguous between the following methods or properties: 'IRef1.M4(ref int, long)' and 'IRef1.M4(ref int, ref int)'
                //        ref1.M4(i, c);      // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "ref1.M4").WithArguments("IRef1.M4(ref int, long)", "IRef1.M4(ref int, ref int)"),
                // (131,16): error CS1620: Argument 1 must be passed with the 'ref' keyword
                //        ref1.M4(l, i);      // CS1620
                Diagnostic(ErrorCode.ERR_BadArgRef, "l").WithArguments("1", "ref"),
                // (132,16): error CS1620: Argument 1 must be passed with the 'ref' keyword
                //        ref1.M4(l, l);      // CS1620
                Diagnostic(ErrorCode.ERR_BadArgRef, "l").WithArguments("1", "ref"),
                // (133,16): error CS1620: Argument 1 must be passed with the 'ref' keyword
                //        ref1.M4(l, c);      // CS1620
                Diagnostic(ErrorCode.ERR_BadArgRef, "l").WithArguments("1", "ref"),
                // (134,8): error CS0121: The call is ambiguous between the following methods or properties: 'IRef1.M4(ref int, long)' and 'IRef1.M4(ref int, ref int)'
                //        ref1.M4(c, i);      // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "ref1.M4").WithArguments("IRef1.M4(ref int, long)", "IRef1.M4(ref int, ref int)"),
                // (135,8): error CS0121: The call is ambiguous between the following methods or properties: 'IRef1.M4(ref int, long)' and 'IRef1.M4(ref int, ref int)'
                //        ref1.M4(c, c);      // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "ref1.M4").WithArguments("IRef1.M4(ref int, long)", "IRef1.M4(ref int, ref int)"),
                // (136,16): error CS1620: Argument 1 must be passed with the 'ref' keyword
                //        ref1.M4(l, ref i);  // CS1620, CS1615
                Diagnostic(ErrorCode.ERR_BadArgRef, "l").WithArguments("1", "ref"),
                // (136,23): error CS1615: Argument 2 should not be passed with the 'ref' keyword
                //        ref1.M4(l, ref i);  // CS1620, CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "i").WithArguments("2", "ref"),
                // (137,23): error CS1615: Argument 2 should not be passed with the 'ref' keyword
                //        ref1.M4(i, ref l);  // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "l").WithArguments("2", "ref"),
                // (138,16): error CS1620: Argument 1 must be passed with the 'ref' keyword
                //        ref1.M4(l, ref l);  // CS1620
                Diagnostic(ErrorCode.ERR_BadArgRef, "l").WithArguments("1", "ref"),
                // (138,23): error CS1615: Argument 2 should not be passed with the 'ref' keyword
                //        ref1.M4(l, ref l);  // CS1620
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "l").WithArguments("2", "ref"),
                // (139,23): error CS1615: Argument 2 should not be passed with the 'ref' keyword
                //        ref1.M4(c, ref l);  // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "l").WithArguments("2", "ref"),
                // (146,19): error CS1503: Argument 2: cannot convert from 'long' to 'char'
                //        ref1.M5(i, l);    // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("2", "long", "char"),
                // (147,16): error CS1620: Argument 1 must be passed with the 'ref' keyword
                //        ref1.M5(l, l);    // CS1620, CS1503
                Diagnostic(ErrorCode.ERR_BadArgRef, "l").WithArguments("1", "ref"),
                // (147,19): error CS1503: Argument 2: cannot convert from 'long' to 'char'
                //        ref1.M5(l, l);    // CS1620, CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("2", "long", "char"),
                // (148,19): error CS1503: Argument 2: cannot convert from 'long' to 'char'
                //        ref1.M5(c, l);    // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("2", "long", "char"),
                // (149,23): error CS1503: Argument 2: cannot convert from 'int' to 'char'
                //        ref1.M5(ref i, i);  // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "i").WithArguments("2", "int", "char"),
                // (150,23): error CS1503: Argument 2: cannot convert from 'long' to 'char'
                //        ref1.M5(ref i, l);  // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("2", "long", "char"),
                // (151,20): error CS1503: Argument 1: cannot convert from 'ref long' to 'ref int'
                //        ref1.M5(ref l, l);  // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("1", "ref long", "ref int"),
                // (151,23): error CS1503: Argument 2: cannot convert from 'long' to 'char'
                //        ref1.M5(ref l, l);  // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("2", "long", "char"),
                // (158,8): error CS0121: The call is ambiguous between the following methods or properties: 'IRef1.M6(ref int, int)' and 'IRef1.M6(ref long, int)'
                //        ref1.M6(i, i);    // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "ref1.M6").WithArguments("IRef1.M6(ref int, int)", "IRef1.M6(ref long, int)"),
                // (159,19): error CS1503: Argument 2: cannot convert from 'long' to 'int'
                //        ref1.M6(i, l);    // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("2", "long", "int"),
                // (160,8): error CS0121: The call is ambiguous between the following methods or properties: 'IRef1.M6(ref int, int)' and 'IRef1.M6(ref long, int)'
                //        ref1.M6(i, c);    // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "ref1.M6").WithArguments("IRef1.M6(ref int, int)", "IRef1.M6(ref long, int)"),
                // (161,16): error CS1620: Argument 1 must be passed with the 'ref' keyword
                //        ref1.M6(l, l);    // CS1620, CS1503
                Diagnostic(ErrorCode.ERR_BadArgRef, "l").WithArguments("1", "ref"),
                // (161,19): error CS1503: Argument 2: cannot convert from 'long' to 'int'
                //        ref1.M6(l, l);    // CS1620, CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("2", "long", "int"),
                // (162,8): error CS0121: The call is ambiguous between the following methods or properties: 'IRef1.M6(ref int, int)' and 'IRef1.M6(ref long, int)'
                //        ref1.M6(c, i);    // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "ref1.M6").WithArguments("IRef1.M6(ref int, int)", "IRef1.M6(ref long, int)"),
                // (163,19): error CS1503: Argument 2: cannot convert from 'long' to 'int'
                //        ref1.M6(c, l);    // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("2", "long", "int"),
                // (164,8): error CS0121: The call is ambiguous between the following methods or properties: 'IRef1.M6(ref int, int)' and 'IRef1.M6(ref long, int)'
                //        ref1.M6(c, c);    // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "ref1.M6").WithArguments("IRef1.M6(ref int, int)", "IRef1.M6(ref long, int)"),
                // (165,23): error CS1503: Argument 2: cannot convert from 'long' to 'int'
                //        ref1.M6(ref i, l);  // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("2", "long", "int"),
                // (166,20): error CS1503: Argument 1: cannot convert from 'ref long' to 'ref int'
                //        ref1.M6(ref l, l);  // CS1503, CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("1", "ref long", "ref int"),
                // (166,23): error CS1503: Argument 2: cannot convert from 'long' to 'int'
                //        ref1.M6(ref l, l);  // CS1503, CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("2", "long", "int"),
                // (173,8): error CS0121: The call is ambiguous between the following methods or properties: 'IRef1.M7(ref int, long)' and 'IRef1.M7(ref long, ref int)'
                //        ref1.M7(i, i);    // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "ref1.M7").WithArguments("IRef1.M7(ref int, long)", "IRef1.M7(ref long, ref int)"),
                // (174,8): error CS0121: The call is ambiguous between the following methods or properties: 'IRef1.M7(ref int, long)' and 'IRef1.M7(ref long, ref int)'
                //        ref1.M7(i, c);    // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "ref1.M7").WithArguments("IRef1.M7(ref int, long)", "IRef1.M7(ref long, ref int)"),
                // (175,16): error CS1620: Argument 1 must be passed with the 'ref' keyword
                //        ref1.M7(l, l);    // CS1620
                Diagnostic(ErrorCode.ERR_BadArgRef, "l").WithArguments("1", "ref"),
                // (176,8): error CS0121: The call is ambiguous between the following methods or properties: 'IRef1.M7(ref int, long)' and 'IRef1.M7(ref long, ref int)'
                //        ref1.M7(c, i);    // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "ref1.M7").WithArguments("IRef1.M7(ref int, long)", "IRef1.M7(ref long, ref int)"),
                // (177,8): error CS0121: The call is ambiguous between the following methods or properties: 'IRef1.M7(ref int, long)' and 'IRef1.M7(ref long, ref int)'
                //        ref1.M7(c, c);    // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "ref1.M7").WithArguments("IRef1.M7(ref int, long)", "IRef1.M7(ref long, ref int)"),
                // (178,23): error CS1615: Argument 2 should not be passed with the 'ref' keyword
                //        ref1.M7(i, ref l);  // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "l").WithArguments("2", "ref"),
                // (179,16): error CS1620: Argument 1 must be passed with the 'ref' keyword
                //        ref1.M7(l, ref l);  // CS1620, CS1615
                Diagnostic(ErrorCode.ERR_BadArgRef, "l").WithArguments("1", "ref"),
                // (179,23): error CS1615: Argument 2 should not be passed with the 'ref' keyword
                //        ref1.M7(l, ref l);  // CS1620, CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "l").WithArguments("2", "ref"),
                // (180,23): error CS1615: Argument 2 should not be passed with the 'ref' keyword
                //        ref1.M7(c, ref l);  // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "l").WithArguments("2", "ref"),
                // (181,27): error CS1615: Argument 2 should not be passed with the 'ref' keyword
                //        ref1.M7(ref i, ref i);  // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "i").WithArguments("2", "ref"),
                // (188,19): error CS1620: Argument 2 must be passed with the 'ref' keyword
                //        ref1.M8(i, l);    // CS1620
                Diagnostic(ErrorCode.ERR_BadArgRef, "l").WithArguments("2", "ref"),
                // (189,8): error CS0121: The call is ambiguous between the following methods or properties: 'IRef1.M8(long, ref int)' and 'IRef1.M8(ref long, int)'
                //        ref1.M8(i, c);    // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "ref1.M8").WithArguments("IRef1.M8(long, ref int)", "IRef1.M8(ref long, int)"),
                // (190,8): error CS0121: The call is ambiguous between the following methods or properties: 'IRef1.M8(long, ref int)' and 'IRef1.M8(ref long, int)'
                //        ref1.M8(l, i);    // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "ref1.M8").WithArguments("IRef1.M8(long, ref int)", "IRef1.M8(ref long, int)"),
                // (191,19): error CS1620: Argument 2 must be passed with the 'ref' keyword
                //        ref1.M8(l, l);    // CS1620
                Diagnostic(ErrorCode.ERR_BadArgRef, "l").WithArguments("2", "ref"),
                // (192,19): error CS1620: Argument 2 must be passed with the 'ref' keyword
                //        ref1.M8(c, l);    // CS1620
                Diagnostic(ErrorCode.ERR_BadArgRef, "l").WithArguments("2", "ref"),
                // (193,8): error CS0121: The call is ambiguous between the following methods or properties: 'IRef1.M8(long, ref int)' and 'IRef1.M8(ref long, int)'
                //        ref1.M8(c, c);    // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "ref1.M8").WithArguments("IRef1.M8(long, ref int)", "IRef1.M8(ref long, int)"),
                // (194,23): error CS1503: Argument 2: cannot convert from 'ref long' to 'ref int'
                //        ref1.M8(i, ref l);   // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("2", "ref long", "ref int"),
                // (195,23): error CS1503: Argument 2: cannot convert from 'ref long' to 'ref int'
                //        ref1.M8(l, ref l);   // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("2", "ref long", "ref int"),
                // (196,23): error CS1503: Argument 2: cannot convert from 'ref long' to 'ref int'
                //        ref1.M8(c, ref l);   // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("2", "ref long", "ref int"),
                // (197,20): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        ref1.M8(ref i, i);   // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "i").WithArguments("1", "ref"),
                // (198,20): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        ref1.M8(ref i, l);   // CS1615, CS1620
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "i").WithArguments("1", "ref"),
                // (198,23): error CS1620: Argument 2 must be passed with the 'ref' keyword
                //        ref1.M8(ref i, l);   // CS1615, CS1620
                Diagnostic(ErrorCode.ERR_BadArgRef, "l").WithArguments("2", "ref"),
                // (199,20): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        ref1.M8(ref i, c);   // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "i").WithArguments("1", "ref"),
                // (200,20): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        ref1.M8(ref l, l);   // CS1615, CS1620
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "l").WithArguments("1", "ref"),
                // (200,23): error CS1620: Argument 2 must be passed with the 'ref' keyword
                //        ref1.M8(ref l, l);   // CS1615, CS1620
                Diagnostic(ErrorCode.ERR_BadArgRef, "l").WithArguments("2", "ref"),
                // (201,20): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        ref1.M8(ref i, ref i);   // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "i").WithArguments("1", "ref"),
                // (202,20): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        ref1.M8(ref i, ref l);   // CS1615, CS1503
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "i").WithArguments("1", "ref"),
                // (202,27): error CS1503: Argument 2: cannot convert from 'ref long' to 'ref int'
                //        ref1.M8(ref i, ref l);   // CS1615, CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("2", "ref long", "ref int"),
                // (203,20): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        ref1.M8(ref l, ref i);   // CS1615
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "l").WithArguments("1", "ref"),
                // (204,20): error CS1615: Argument 1 should not be passed with the 'ref' keyword
                //        ref1.M8(ref l, ref l);   // CS1615, CS1503
                Diagnostic(ErrorCode.ERR_BadArgExtraRef, "l").WithArguments("1", "ref"),
                // (204,27): error CS1503: Argument 2: cannot convert from 'ref long' to 'ref int'
                //        ref1.M8(ref l, ref l);   // CS1615, CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("2", "ref long", "ref int"),
                // (211,8): error CS0121: The call is ambiguous between the following methods or properties: 'IRef1.M9(ref long, ref int)' and 'IRef1.M9(ref int, ref long)'
                //        ref1.M9(i, i);    // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "ref1.M9").WithArguments("IRef1.M9(ref long, ref int)", "IRef1.M9(ref int, ref long)"),
                // (212,8): error CS0121: The call is ambiguous between the following methods or properties: 'IRef1.M9(ref long, ref int)' and 'IRef1.M9(ref int, ref long)'
                //        ref1.M9(i, c);    // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "ref1.M9").WithArguments("IRef1.M9(ref long, ref int)", "IRef1.M9(ref int, ref long)"),
                // (213,19): error CS1620: Argument 2 must be passed with the 'ref' keyword
                //        ref1.M9(l, l);    // CS1620
                Diagnostic(ErrorCode.ERR_BadArgRef, "l").WithArguments("2", "ref"),
                // (214,8): error CS0121: The call is ambiguous between the following methods or properties: 'IRef1.M9(ref long, ref int)' and 'IRef1.M9(ref int, ref long)'
                //        ref1.M9(c, i);    // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "ref1.M9").WithArguments("IRef1.M9(ref long, ref int)", "IRef1.M9(ref int, ref long)"),
                // (215,8): error CS0121: The call is ambiguous between the following methods or properties: 'IRef1.M9(ref long, ref int)' and 'IRef1.M9(ref int, ref long)'
                //        ref1.M9(c, c);    // CS0121
                Diagnostic(ErrorCode.ERR_AmbigCall, "ref1.M9").WithArguments("IRef1.M9(ref long, ref int)", "IRef1.M9(ref int, ref long)"),
                // (216,23): error CS1503: Argument 2: cannot convert from 'ref long' to 'ref int'
                //        ref1.M9(l, ref l);   // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("2", "ref long", "ref int"),
                // (217,23): error CS1620: Argument 2 must be passed with the 'ref' keyword
                //        ref1.M9(ref l, l);   // CS1620
                Diagnostic(ErrorCode.ERR_BadArgRef, "l").WithArguments("2", "ref"),
                // (218,20): error CS1503: Argument 1: cannot convert from 'ref int' to 'ref long'
                //        ref1.M9(ref i, ref i);   // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "i").WithArguments("1", "ref int", "ref long"),
                // (219,27): error CS1503: Argument 2: cannot convert from 'ref long' to 'ref int'
                //        ref1.M9(ref l, ref l);   // CS1503
                Diagnostic(ErrorCode.ERR_BadArgType, "l").WithArguments("2", "ref long", "ref int"));
        }

        [Fact]
        public void FailedToConvertToParameterArrayElementType()
        {
            var source = @"
class C
{
    static void Main()
    {
        M1(1, null);
        M1(null, 1);

        M2(""a"", null);
        M2(null, ""a"");

        M3(1, null);
        M3(null, 1);

        M3(""a"", null);
        M3(null, ""a"");

        M1(1, ""A"");
        M2(1, ""A"");
        M3<int>(1, ""A"");
        M3<string>(1, ""A"");
    }

    static void M1(params int[] a) { }
    static void M2(params string[] a) { }
    static void M3<T>(params T[] a) { }
}
";
            CreateCompilationWithMscorlib(source).VerifyDiagnostics(
                // (6,15): error CS1503: Argument 2: cannot convert from '<null>' to 'int'
                //         M1(1, null);
                Diagnostic(ErrorCode.ERR_BadArgType, "null").WithArguments("2", "<null>", "int"),
                // (7,12): error CS1503: Argument 1: cannot convert from '<null>' to 'int'
                //         M1(null, 1);
                Diagnostic(ErrorCode.ERR_BadArgType, "null").WithArguments("1", "<null>", "int"),
                // (12,15): error CS1503: Argument 2: cannot convert from '<null>' to 'int'
                //         M3(1, null);
                Diagnostic(ErrorCode.ERR_BadArgType, "null").WithArguments("2", "<null>", "int"),
                // (13,12): error CS1503: Argument 1: cannot convert from '<null>' to 'int'
                //         M3(null, 1);
                Diagnostic(ErrorCode.ERR_BadArgType, "null").WithArguments("1", "<null>", "int"),
                // (18,15): error CS1503: Argument 2: cannot convert from 'string' to 'int'
                //         M1(1, "A");
                Diagnostic(ErrorCode.ERR_BadArgType, @"""A""").WithArguments("2", "string", "int"),
                // (19,12): error CS1503: Argument 1: cannot convert from 'int' to 'string'
                //         M2(1, "A");
                Diagnostic(ErrorCode.ERR_BadArgType, "1").WithArguments("1", "int", "string"),
                // (20,20): error CS1503: Argument 2: cannot convert from 'string' to 'int'
                //         M3<int>(1, "A");
                Diagnostic(ErrorCode.ERR_BadArgType, @"""A""").WithArguments("2", "string", "int"),
                // (21,20): error CS1503: Argument 1: cannot convert from 'int' to 'string'
                //         M3<string>(1, "A");
                Diagnostic(ErrorCode.ERR_BadArgType, "1").WithArguments("1", "int", "string"));
        }

        [WorkItem(544571)]
        [Fact]
        public void TestResolveOverloads()
        {
            var source = @"
using System;
class Program
{
    static void M()
    {
    }
    static void M(long l)
    {
    }
    static void M(short s)
    {
    }
    static void M(int i)
    {
    }
    static void Main()
    {
        // Perform overload resolution here.
    }
}";
            var tree = Parse(source);
            var compilation = CSharpCompilation.Create("MyCompilation",
                syntaxTrees: new[] { tree }, references: new[] { MscorlibRef });
            var model = compilation.GetSemanticModel(tree);

            // Get MethodSymbols for all MethodDeclarationSyntax nodes with name 'M'.
            var methodSymbols = tree.GetCompilationUnitRoot()
                .DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Where(m => m.Identifier.ToString() == "M")
                .Select(m => (MethodSymbol)model.GetDeclaredSymbol(m));

            // Perform overload resolution at the position identified by the comment '// Perform ...' above.
            var position = source.IndexOf("//");
            OverloadResolutionResult<MethodSymbol> overloadResults = ((CSharpSemanticModel)model).ResolveOverloads(
                position,                                              // Position to determine scope and accessibility.
                ImmutableArray.CreateRange<MethodSymbol>(methodSymbols), // Candidate MethodSymbols.
                ImmutableArray.Create<TypeSymbol>(),                       // Type Arguments (if any).
                ImmutableArray.Create<ArgumentSyntax>(              // Arguments.
                    SyntaxFactory.Argument(
                        SyntaxFactory.LiteralExpression(                      // OR Syntax.ParseExpression("100")
                            SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal("100", 100)))));
            Assert.True(overloadResults.Succeeded);

            var results = string.Join("\r\n", overloadResults.Results
                .Select(result => string.Format("{0}: {1}{2}",
                    result.Resolution, result.Member, result.IsValid ? " [Selected Candidate]" : string.Empty)));

            Assert.Equal(@"Worse: Program.M(long)
Worse: Program.M(short)
ApplicableInNormalForm: Program.M(int) [Selected Candidate]", results);
        }

        [Fact]
        public void TypeInferenceFailures()
        {          
            var source = @"
public interface I { }
public interface I1<T> { }
public interface I2<S, T> { }

public class A : I { }
public class B { }
public class C { }

public class G1<T> : I1<T> { }
public class G2<S, T> : G1<S>, I2<S, T> { }

public class AggTest {
    public static A a = new A();
    public static I i = a;

    public static G1<A> g1a = new G1<A>();
    public static G1<G1<B>> g11b = new G1<G1<B>>();
    public static G1<G1<G1<C>>> g111c = new G1<G1<G1<C>>>();
    public static G1<G2<A, B>> g12ab = new G1<G2<A, B>>();

    public static G2<A, B> g2ab = new G2<A, B>();
    public static G2<G1<A>, B> g21ab = new G2<G1<A>, B>();
    public static G2<A, G1<B>> g2a1b = new G2<A, G1<B>>();
    public static G2<G1<A>, G1<B>> g21a1b = new G2<G1<A>, G1<B>>();

    public static G2<G2<A, B>, C> g22abc = new G2<G2<A, B>, C>();
    public static G2<A, G2<B, C>> g2a2bc = new G2<A, G2<B, C>>();
    public static G2<G2<A, B>, G1<C>> g22ab1c = new G2<G2<A, B>, G1<C>>();
    public static G2<G1<A>, G2<B, C>> g21a2bc = new G2<G1<A>, G2<B, C>>();

    public class B1 {
        // Nesting >= 2
        public static void M3<S, T>(G1<G2<S, T>> a) { }
        public static void M3<S, T>(G2<G1<S>, T> a) { }
        public static void M3<S, T>(G2<S, G1<T>> a) { }
        public static void M3<S, T>(G2<G1<S>, G1<T>> a) { }
    }

    public class ClassTestGen : B1 {
        public static void Run() {
            M3(null); // Can't infer
            M3(a); // Can't infer
            M3(i); // Can't infer
            M3(g1a); // Can't infer
            M3(g11b); // Can't infer
            M3(g111c); // Can't infer
            M3(g12ab); // OK
            M3(g2ab); // Can't infer
            M3(g21ab); // OK
            M3(g2a1b); // OK
            M3(g21a1b); // Ambiguous
            M3(g22abc); // OK
            M3(g2a2bc); // Can't infer
            M3(g22ab1c); // OK
            M3(g21a2bc); // OK
        }
    }
}
";
            CreateCompilationWithMscorlib(source).VerifyDiagnostics(
                // (43,13): error CS0411: The type arguments for method 'AggTest.B1.M3<S, T>(G1<G2<S, T>>)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //             M3(null); // Can't infer
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "M3").WithArguments("AggTest.B1.M3<S, T>(G1<G2<S, T>>)"),
                // (44,13): error CS0411: The type arguments for method 'AggTest.B1.M3<S, T>(G1<G2<S, T>>)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //             M3(a); // Can't infer
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "M3").WithArguments("AggTest.B1.M3<S, T>(G1<G2<S, T>>)"),
                // (45,13): error CS0411: The type arguments for method 'AggTest.B1.M3<S, T>(G1<G2<S, T>>)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //             M3(i); // Can't infer
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "M3").WithArguments("AggTest.B1.M3<S, T>(G1<G2<S, T>>)"),
                // (46,13): error CS0411: The type arguments for method 'AggTest.B1.M3<S, T>(G1<G2<S, T>>)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //             M3(g1a); // Can't infer
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "M3").WithArguments("AggTest.B1.M3<S, T>(G1<G2<S, T>>)"),
                // (47,13): error CS0411: The type arguments for method 'AggTest.B1.M3<S, T>(G1<G2<S, T>>)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //             M3(g11b); // Can't infer
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "M3").WithArguments("AggTest.B1.M3<S, T>(G1<G2<S, T>>)"),
                // (48,13): error CS0411: The type arguments for method 'AggTest.B1.M3<S, T>(G1<G2<S, T>>)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //             M3(g111c); // Can't infer
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "M3").WithArguments("AggTest.B1.M3<S, T>(G1<G2<S, T>>)"),

                // NOTE: Dev10 reports "AggTest.B1.M3<S,T>(G2<G1<S>,T>)" for the last two, but this seems just as good (type inference fails for both).

                // (50,13): error CS0411: The type arguments for method 'AggTest.B1.M3<S, T>(G1<G2<S, T>>)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //             M3(g2ab); // Can't infer
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "M3").WithArguments("AggTest.B1.M3<S, T>(G1<G2<S, T>>)"),
                // (55,13): error CS0411: The type arguments for method 'AggTest.B1.M3<S, T>(G1<G2<S, T>>)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //             M3(g2a2bc); // Can't infer
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "M3").WithArguments("AggTest.B1.M3<S, T>(G1<G2<S, T>>)"));
        }

        [WorkItem(528425)]
        [WorkItem(528425)]
        [Fact(Skip = "528425")]
        public void ExactInaccessibleMatch()
        {
            var source = @"
public class C
{
    public static void Main()
    {
        D d = new D();
        d.M(4, 5, ""b"");
    }
}

public class D
{
    public void M(int i)
    {
    }

    private void M(int i, int j, string s)
    {
    }
}
";
            CreateCompilationWithMscorlib(source).VerifyDiagnostics(
                // (7,9): error CS0122: 'D.M(int, int, string)' is inaccessible due to its protection level
                //         d.M(4, 5, "b");
                Diagnostic(ErrorCode.ERR_BadAccess, "d.M"));
        }

        [WorkItem(545382)]
        [Fact]
        public void Whidbey133503a()
        {
            var source = @"
class Ambig
{
    static void Main()
    {
        overload1(1, 1);
        overload2(1, 1);
    }

    // causes ambiguity because first and third method, but depending on the order, the compiler
    // reports them incorrectly.  VSWhidbey:133503
    static void overload1(byte b, foo f) { }
    static void overload1(sbyte b, bar f) { }
    static void overload1(int b, baz f) { }

    static void overload2(int b, baz f) { }
    static void overload2(sbyte b, bar f) { }
    static void overload2(byte b, foo f) { }
}

class foo
{
    public static implicit operator foo(int i)
    {
        return new foo();
    }
}

class bar
{
    public static implicit operator bar(int i)
    {
        return new bar();
    }
}

class baz
{
    public static implicit operator baz(int i)
    {
        return new baz();
    }
    public static implicit operator baz(foo f)
    {
        return new baz();
    }
}
";
            // TODO: We could do a better job of choosing which two methods are the ambiguous ones.

            CreateCompilationWithMscorlib(source).VerifyDiagnostics(
// (6,9): error CS0121: The call is ambiguous between the following methods or properties: 'Ambig.overload1(byte, foo)' and 'Ambig.overload1(sbyte, bar)'
//         overload1(1, 1);
Diagnostic(ErrorCode.ERR_AmbigCall, "overload1").WithArguments("Ambig.overload1(byte, foo)", "Ambig.overload1(sbyte, bar)"),
// (7,9): error CS0121: The call is ambiguous between the following methods or properties: 'Ambig.overload2(int, baz)' and 'Ambig.overload2(sbyte, bar)'
//         overload2(1, 1);
Diagnostic(ErrorCode.ERR_AmbigCall, "overload2").WithArguments("Ambig.overload2(int, baz)", "Ambig.overload2(sbyte, bar)"));
        }

        [WorkItem(545382)]
        [Fact]
        public void Whidbey133503b()
        {
            var source = @"
class Ambig
{
    static void Main()
    {
        F(new Q());
    }

    // causes ambiguity because of conversion cycle.  So the compiler
    // arbitrarily picks the 'first' 2 methods to report (which turn out to be 
    // the last 2 overloads declared). Also VSWhidbey:133503
    public static void F(P1 p)
    {
    }

    public static void F(P2 p)
    {
    }

    public static void F(P3 p)
    {
    }
}

public class P1
{
    public static implicit operator P1(P2 p)
    {
        return null;
    }
}

public class P2
{
    public static implicit operator P2(P3 p)
    {
        return null;
    }
}

public class P3
{
    public static implicit operator P3(P1 p)
    {
        return null;
    }
}

public class Q
{
    public static implicit operator P1(Q p)
    {
        return null;
    }
    public static implicit operator P2(Q p)
    {
        return null;
    }
    public static implicit operator P3(Q p)
    {
        return null;
    }
}
";
            CreateCompilationWithMscorlib(source).VerifyDiagnostics(
                // (6,9): error CS0121: The call is ambiguous between the following methods or properties: 'Ambig.F(P1)' and 'Ambig.F(P2)'
                //         F(new Q());
                Diagnostic(ErrorCode.ERR_AmbigCall, "F").WithArguments("Ambig.F(P1)", "Ambig.F(P2)"));
        }

        [WorkItem(545467)]
        [Fact]
        public void ClassPlusLambda1()
        {          

            // ACASEY: EricLi's comment provides good context on the
            // nature of the problems that arise in such situations and the behavior of the native
            // compiler.  However, the comments about Roslyn's behavior are no longer accurate.
            // Unfortunately, we discovered real-world code that depended on the native behavior
            // so we need to replicate it in Roslyn.  Effectively, null literal conversions become
            // standard implicit conversions (we expect the spec to be revised) and anonymous function
            // and method group conversions are specifically allowed if there is an explicit cast
            // (this will likely not be spec'd).  As for the betterness rule, we only go down the
            // delegate path if BOTH target types are delegate types.  Otherwise, the normal betterness
            // rules apply.

            // ERICLI: This test illustrates an unfortunate situation where neither Roslyn nor the
			// native compiler are compliant with the specification, and the native compiler's 
		    // behaviour is unusual. In this particular situation, the native compiler is 
			// getting the right answer completely by accident; it is the confluence of two incorrect
            // decisions adding up accidentally to a correct decision.
            //
            // Problem 1:
            //
            // The spec says that there must be a *standard implicit conversion* from the expression being
            // converted to the parameter type of the user-defined implicit conversion. Unfortunately, the
            // specification does not say that null literal conversions, constant numeric conversions,
            // method group conversions, or lambda conversions are "standard" conversions. It seems reasonable
            // that these be allowed; we will probably emend the specification to allow them.
            //
            // Now we get to the unusual part. The native compiler allows null literal and constant numeric
            // conversions to be counted as standard conversions; this is contrary to the exact wording of 
            // the specification but as we said, it is a reasonable rule and we should allow it in the spec.
            // The unusual part is: the native compiler effectively treats a lambda conversion and a method 
            // group conversion as a standard implicit conversion *if a cast appears in the code*.
            // 
            // That is, suppose there is an implicit conversion from Action to S.  The native compiler allows
            //
            // S s = (S)(()=>{});
            //
            // but does not allow
            //
            // S s = ()=>{};
            //
            // it is strange indeed that an *implicit* conversion should require a *cast*! 
            //
            // Roslyn allows all those conversions to be used when converting the expression to the parameter 
            // type of the user-defined implicit conversion, regardless of whether a cast appears in the code.
            //
            // Problem 2:
            //
            // The native compiler gets the "betterness" rules wrong when lambdas are involved. The right thing
            // to do is to first, check to see if one method has a more specific formal parameter type than the other.
            // If betterness cannot be determined by formal parameter types, and the argument is a lambda, then 
            // a special rule regarding the inferred return type of the lambda is used. What the native compiler does
            // is, if there is a lambda, then it skips doing the formal parameter type check and goes straight to the
            // lambda check.
            //
            // After some debate, we decided a while back to replicate this bug in Roslyn. 
            //
            // Now we see how these two bugs work together in the native compiler to produce the correct result
            // for the wrong reason.  Suppose we have a simplified version of the code below: r = r + (x=>x);
            // What happens?
            //
            // The correct behaviour according to the spec is to say that there are two possible operators,
            // MC + EXPR and MC + MC.  Are either of them *applicable*? Clearly both are good in their left
            // hand operand. Can the right-hand operand, a lambda, be converted via implicit conversion to 
            // the expression tree type? Obviously yes. Can the lambda be converted to MainClass?  
            // 
            // Not directly, because MainClass is not a delegate or expression tree type. But we examine
            // the user-defined conversions on MainClass and discover an implicit conversion from expression
            // tree to MainClass. Can we use that?
            //
            // The spec says no, because the conversion from lambda to expression tree is not a "standard"
            // implicit conversion.
            //
            // The native compiler says no because it enforces that rule when there is no cast in the code.
            // Had there been a cast in the code, the native compiler would say yes, there is.
            //
            // Roslyn says yes; it allows the lambda conversion as a standard conversion regardless of whether
            // there is a cast in the code.
            //
            // In the native compiler there is now only one operator in the candidate set of applicable operators;
            // the choice is therefore easy; the MC + EXPR wins.
            //
            // In Roslyn, there are now two operators in the candidate set of applicable operators. Which one wins?
            //
            // The correct behaviour is to say that the more specific one wins. Since there is an implicit conversion
            // from EXPR to MC but not from MC to EXPR, the MC + EXPR candidate must be better, so it wins.
            //
            // But that is not what Roslyn does; remember, Roslyn is being bug-compatible with the native compiler.
            // The native compiler does not check for betterness on types when a lambda is involved.
            // Therefore Roslyn checks the lambda, and is unable to deduce from it which candidate is better.
            //
            // Therefore Roslyn gives an ambiguity error, even though (1) by rights the candidate set should
            // contain a single operator, and (2) even if it contains two, one of them is clearly better.
            // 

            var source = @"
class MainClass
{
    static void Main()
    {
        MainClass r = new MainClass();
        // The lambda contains an ambiguity error, and therefore we cannot work out
        // the overload resolution problem on the  outermost addition, so we give
        // a bad binary operator error.
        r = r + ((MainClass x) => x + ((MainClass y) => (y + null)));
        // The lambda body no longer contains an error, thanks to the cast.
        // Therefore, this becomes an ambiguity error, for reasons detailed above.
        r = r + ((MainClass x) => x + (MainClass)((MainClass y) => (y + null)));
    }

    public static MainClass operator +(MainClass r1, MainClass r2) { return r1; }
    public static MainClass operator +(MainClass r1,
        System.Linq.Expressions.Expression<System.Func<MainClass, MainClass>> e)
    { return r1; }
    public static implicit operator MainClass(
        System.Linq.Expressions.Expression<System.Func<MainClass, MainClass>> e)
    { return new MainClass(); }
}

";
            // Roslyn matches the native behavior - it compiles cleanly.
            var comp = CreateCompilationWithMscorlibAndSystemCore(source);
            comp.VerifyDiagnostics();
        }

        [WorkItem(545467)]
        [Fact]
        public void ClassPlusLambda2()
        {          
            // Here we remove one of the operators from the previous test. Of course the
            // first one is no longer ambiguous, and the second one no longer works because
            // now the lambda contains an addition that has no applicable operator, and
            // so the outer addition cannot work either.

            var source = @"
class MainClass
{
    static void Main()
    {
        MainClass r = new MainClass();
        r = r + ((MainClass x) => x + ((MainClass y) => (y + null)));
        r = r + ((MainClass x) => x + (MainClass)((MainClass y) => (y + null)));
    }
    // public static MainClass operator +(MainClass r1, MainClass r2) { return r1; }
    public static MainClass operator +(MainClass r1,
        System.Linq.Expressions.Expression<System.Func<MainClass, MainClass>> e)
    { return r1; }
    public static implicit operator MainClass(
        System.Linq.Expressions.Expression<System.Func<MainClass, MainClass>> e)
    { return new MainClass(); }
}
";
            var comp = CreateCompilationWithMscorlibAndSystemCore(source);
            comp.VerifyDiagnostics(
                // (8,13): error CS0019: Operator '+' cannot be applied to operands of type 'MainClass' and 'lambda expression'
                //         r = r + ((MainClass x) => x + (MainClass)((MainClass y) => (y + null)));
                Diagnostic(ErrorCode.ERR_BadBinaryOps, "r + ((MainClass x) => x + (MainClass)((MainClass y) => (y + null)))").WithArguments("+", "MainClass", "lambda expression"));
        }

        [WorkItem(545467)]
        [Fact]
        public void ClassPlusLambda3()
        {  
            // In this variation we remove the other addition operator.  The native compiler
            // disallows three of these additions, because as mentioned above, it is inconsistent
            // about when a user-defined conversion may be from a lambda. The native compiler says
            // that the user-defined conversion may only be from a lambda if a cast appears in
            // the source; in three of the four conversions from lambdas here, there is no cast.
            //
            // Roslyn replicates this bug.

            var source = @"
class MainClass
{
    public static void Main()
    {
        MainClass r = new MainClass();
        r = r + ((MainClass x) => (x + ((MainClass y) => (y + null))));
        r = r + ((MainClass x) => (x + (MainClass)((MainClass y) => (y + null))));
        System.Func<MainClass, MainClass> f = x => x + (y => y);
    }

    public static MainClass operator +(MainClass r1, MainClass r2) { return r1; }
//  public static MainClass operator +(MainClass r1,
//      System.Linq.Expressions.Expression<System.Func<MainClass, MainClass>> e)
//  { return r1; }
    public static implicit operator MainClass(
        System.Linq.Expressions.Expression<System.Func<MainClass, MainClass>> e)
    { return new MainClass(); }
}
";
            // NOTE: roslyn suppresses the error for (x + (...)) in the first assignment,
            // but it's still an error (as indicated by the standalone test below).
            var comp = CreateCompilationWithMscorlibAndSystemCore(source);
            comp.VerifyDiagnostics(
                // (7,13): error CS0019: Operator '+' cannot be applied to operands of type 'MainClass' and 'lambda expression'
                //         r = r + ((MainClass x) => (x + ((MainClass y) => (y + null))));
                Diagnostic(ErrorCode.ERR_BadBinaryOps, "r + ((MainClass x) => (x + ((MainClass y) => (y + null))))").WithArguments("+", "MainClass", "lambda expression"),
                // (8,13): error CS0019: Operator '+' cannot be applied to operands of type 'MainClass' and 'lambda expression'
                //         r = r + ((MainClass x) => (x + (MainClass)((MainClass y) => (y + null))));
                Diagnostic(ErrorCode.ERR_BadBinaryOps, "r + ((MainClass x) => (x + (MainClass)((MainClass y) => (y + null))))").WithArguments("+", "MainClass", "lambda expression"),
                // (9,52): error CS0019: Operator '+' cannot be applied to operands of type 'MainClass' and 'lambda expression'
                //         System.Func<MainClass, MainClass> f = x => x + (y => y);
                Diagnostic(ErrorCode.ERR_BadBinaryOps, "x + (y => y)").WithArguments("+", "MainClass", "lambda expression"));
        }

        [WorkItem(545467)]
        [Fact]
        public void ClassPlusLambda4()
        {         
            // In this final variation, we remove the user-defined conversion. And now of course we cannot
            // add MainClass to the lambda at all; the native compiler and Roslyn agree.
            var source = @"
class MainClass
{
    static void Main()
    {
        MainClass r = new MainClass();
        r = r + ((MainClass x) => x + ((MainClass y) => (y + null)));
        r = r + ((MainClass x) => x + (MainClass)((MainClass y) => (y + null)));
    }
    public static MainClass operator +(MainClass r1, MainClass r2) { return r1; }
    public static MainClass operator +(MainClass r1,
        System.Linq.Expressions.Expression<System.Func<MainClass, MainClass>> e)
    { return r1; }
//  public static implicit operator MainClass(
//      System.Linq.Expressions.Expression<System.Func<MainClass, MainClass>> e)
//  { return new MainClass(); }
}
";
            var comp = CreateCompilationWithMscorlibAndSystemCore(source);
            comp.VerifyDiagnostics(
                // (7,13): error CS0019: Operator '+' cannot be applied to operands of type 'MainClass' and 'lambda expression'
                //         r = r + ((MainClass x) => x + ((MainClass y) => (y + null)));
                Diagnostic(ErrorCode.ERR_BadBinaryOps, "r + ((MainClass x) => x + ((MainClass y) => (y + null)))").WithArguments("+", "MainClass", "lambda expression"),
                // (8,13): error CS0019: Operator '+' cannot be applied to operands of type 'MainClass' and 'lambda expression'
                //         r = r + ((MainClass x) => x + (MainClass)((MainClass y) => (y + null)));
                Diagnostic(ErrorCode.ERR_BadBinaryOps, "r + ((MainClass x) => x + (MainClass)((MainClass y) => (y + null)))").WithArguments("+", "MainClass", "lambda expression"));
        }

        [WorkItem(546875)]
        [WorkItem(530930)]
        [Fact]
        public void BigVisitor()
        {
            // Reduced from Microsoft.Data.Schema.Sql.dll.

            var source = @"
public class Test
{
    static void Main()
    {
        var visitor = new ConcreteVisitor();
        visitor.Visit(new Class090());
    }
}
";
            var libRef = TestReferences.SymbolsTests.BigVisitor;

            var start = DateTime.UtcNow;
            CreateCompilationWithMscorlibAndSystemCore(source, new[] { libRef }).VerifyDiagnostics();
            var elapsed = DateTime.UtcNow - start;
            Assert.InRange(elapsed.TotalSeconds, 0, 5); // Was originally over 30 minutes, so we have some wiggle room here.
        }

        [WorkItem(546730), WorkItem(546739)]
        [Fact]
        public void TestNamedParamsParam()
        {
            CompileAndVerify(@"
class C
{
    static void M(
        int x,
        double y,
        params string[] z)
    {
        System.Console.WriteLine(1);
    }
    
    static void M(
        int x,
        params string[] z)
    {
        System.Console.WriteLine(2);
    }
    static void Main()
    {
        C.M(0, z: """");
    }
}", expectedOutput: "2").VerifyDiagnostics();
        }

        [WorkItem(531173)]
        [Fact]
        public void InvokeMethodOverridingNothing()
        {
            var source = @"
public class C
{
	public override T Override<T>(T t) 
	{ 
		return t;
	}

	public void Test<T>(T t)
	{
		Override(t);
	}
}
";
            CreateCompilationWithMscorlib(source).VerifyDiagnostics(
                // (4,20): error CS0115: 'C.Override<T>(T)': no suitable method found to override
                // 	public override T Override<T>(T t) 
                Diagnostic(ErrorCode.ERR_OverrideNotExpected, "Override").WithArguments("C.Override<T>(T)"));
        }

        [WorkItem(547186)]
        [Fact, WorkItem(531613)]
        public void IndexerWithoutAccessors()
        {
            var source = @"
class Program
{
    public static void Main()
    {
        C c = new C();
        c[""""] = 10;
    }
}

abstract class A
{
    public abstract int this[string arg] { set; }
}

class C : A
{
    public override int this[string arg]
    // Not finished typing yet.
";
            // Doesn't assert.
            CreateCompilationWithMscorlib(source).VerifyDiagnostics(
                // (18,41): error CS1514: { expected
                //     public override int this[string arg]
                Diagnostic(ErrorCode.ERR_LbraceExpected, ""),
                // (18,41): error CS1513: } expected
                //     public override int this[string arg]
                Diagnostic(ErrorCode.ERR_RbraceExpected, ""),
                // (18,41): error CS1513: } expected
                //     public override int this[string arg]
                Diagnostic(ErrorCode.ERR_RbraceExpected, ""),
                // (18,25): error CS0548: 'C.this[string]': property or indexer must have at least one accessor
                //     public override int this[string arg]
                Diagnostic(ErrorCode.ERR_PropertyWithNoAccessors, "this").WithArguments("C.this[string]"),
                // (16,7): error CS0534: 'C' does not implement inherited abstract member 'A.this[string].set'
                // class C : A
                Diagnostic(ErrorCode.ERR_UnimplementedAbstractMethod, "C").WithArguments("C", "A.this[string].set"));
        }

        [Fact]
        public void DynamicVsTypeParameters()
        {
            string source = @"
using System;

public class B<T>
{
    public void F(T p1) {  }
    public void F(dynamic p1) {  }
}

class C
{
    void M(B<C> b)
    {
        b.F(null); //-B<C>.F(C)
    }
}";
            TestOverloadResolutionWithDiff(source, new[] { CSharpRef, SystemCoreRef });
        }

        [Fact]
        public void DynamicByRef()
        {
            string source = @"
using System;

public class C
{
	public static int F(int p1, char p2, ref dynamic p3) { return 2; }
	public static int F(C p1, params dynamic[] p2) { return 3; }

	public static implicit operator int(C t) { return 1; }
	public static implicit operator C(int t) { return new C(); }

	static void M()
	{            
		dynamic d1 = null;
		C c = null;
 
		C.F(c, 'a', ref d1); //-C.F(int, char, ref dynamic)
	}
}";

            TestOverloadResolutionWithDiff(source);
        }

        [Fact, WorkItem(624410)]
        public void DynamicTypeInferenceAndPointer()
        {
            string source = @"
public unsafe class C
{
    public static void M()
    {
        dynamic x = null;
        Bar(x);
    }
 
    static void Bar<T>(D<T>.E*[] x) { }
}
 
class D<T>
{
    public enum E { }
}
";
            // Dev11 reports error CS0411: The type arguments for method 'C.Bar<T>(D<T>.E*[])' cannot be inferred from the usage. Try
            // specifying the type arguments explicitly.
            CreateCompilationWithMscorlibAndSystemCore(source, compOptions: TestOptions.UnsafeDll).VerifyDiagnostics();
        }

        [Fact, WorkItem(598032)]
        public void GenericVsOptionalParameter()
        {
            string source = @"
using System;
class C
{
    public static int Foo(int x, string y = null) { return 1; }
    public static int Foo<T>(T x) { return 0; }

    public static int M()
    {
        return Foo(0);  //-C.Foo(int, string)
    }
}
";
            // Roslyn is correctly following the spec here. Dev11 compares the two methods' parameter lists without first 
            // removing the parameters for which optional arguments are used. This is incorrect. It then concludes that the 
            // parameter lists are different and decides not to use part of the tie-breaker rules, 
            // but does use the "least number of optional args" tie-breaker.
            TestOverloadResolutionWithDiff(source);
        }

        [WorkItem(598029)]
        [Fact]
        public void TypeParameterInterfaceVersusNonInterface()
        {
            string source = @"
interface IA
{
    int Foo(int x = 0);
}
class C : IA
{
    public int Foo(int x)
    {
        return x;
    }
    static int M<T>(T x) where T : A, IA
    {
        return x.Foo(); //-IA.Foo(int)
    }
}
";

            TestOverloadResolutionWithDiff(source);
        }

        [WorkItem(649807)]
        [Fact]
        public void OverloadResolution649807()
        {
            var source = @"
public class Test
{
    public delegate dynamic nongenerics(dynamic id);
    public delegate T generics< T>(dynamic id);
    public dynamic Foo(nongenerics Meth, dynamic id)
    {
        return null;
    }
    public T Foo<T>(generics<T> Meth, dynamic id)
    {
        return default(T);
    }
    public dynamic method(dynamic id)
    {
        return System.String.Empty;
    }
    public dynamic testFoo(dynamic id)
    {
        return Foo(method, ""abc"");
    }
    static void Main(string[] args)
    {
    }
}";
            // Doesn't assert.
            CreateCompilationWithMscorlibAndSystemCore(source).VerifyDiagnostics(
                // (20,16): error CS0121: The call is ambiguous between the following methods or properties: 'Test.Foo(Test.nongenerics, dynamic)' and 'Test.Foo<T>(Test.generics<T>, dynamic)'
                //         return Foo(method, "abc");
                Diagnostic(ErrorCode.ERR_AmbigCall, "Foo").WithArguments("Test.Foo(Test.nongenerics, dynamic)", "Test.Foo<T>(Test.generics<T>, dynamic)")
                );
        }

        [WorkItem(662641)]
        [Fact]
        public void GenericMethodConversionToDelegateWithDynamic()
        {
            var source = @"
using System;
public delegate void D002<T1, T2>(T1 t1, T2 t2);
public delegate void D003(dynamic t1, object t2);
public class Foo
{
    static internal void M11<T1, T2>(T1 t1, T2 t2)
    {
    }
}
public struct start
{
    static void M(D002<dynamic, object> d) {}
    static public void Main()
    {
        dynamic d1 = null;
        object o1 = null;
        Foo.M11<dynamic, object>(d1, o1);
        Foo.M11(d1, o1);
        D002<dynamic, object> dd02 = new D002<dynamic, object>(Foo.M11);
        D002<dynamic, object> dd03 = Foo.M11;
        D002<dynamic, object> dd04 = (D002<dynamic, object>)Foo.M11;
        D003 dd05 = Foo.M11;
        M(Foo.M11);
        Console.WriteLine(dd02);
    }
}";
            CreateCompilationWithMscorlibAndSystemCore(source).VerifyDiagnostics(
                );
        }

        [WorkItem(690966)]
        [Fact]
        public void OptionalParameterInDelegateConversion()
        {
            var source = @"
using System;

class C
{
    static void M1(Func<string, int> f) { }
    static void M1(Func<string, string, int> f) { }
    
    static int M2(string s, string t = null) { return 0; }

    static void Main()
    {
        M1(M2);
    }
}
";
            var comp = CreateCompilationWithMscorlib(source);
            comp.VerifyDiagnostics();

            var tree = comp.SyntaxTrees.Single();
            var model = comp.GetSemanticModel(tree);

            var callSyntax = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();

            Assert.Equal("void C.M1(System.Func<System.String, System.String, System.Int32> f)",
                model.GetSymbolInfo(callSyntax).Symbol.ToTestDisplayString());
        }

        [WorkItem(718294)]
        [Fact]
        public void MethodGroupConversion_BetterCandidateHasOptionalParameter()
        {
            var source = @"
using System;

class Test
{
    void M(IViewable2 v)
    {
        v.View(v.Add);
    }
}

interface IViewable
{
    void View(Action viewer);
}

interface IViewable2 : IViewable
{
}

static class Extensions
{
    [Obsolete(""A"", error: false)]
    public static void Add(this IViewable2 @this) { }

    [Obsolete(""B"", error: false)]
    public static void Add(this IViewable @this, object obj = null) { }
}
";
            CreateCompilationWithMscorlibAndSystemCore(source).VerifyDiagnostics(
                // (8,16): warning CS0618: 'Extensions.Add(IViewable2)' is obsolete: 'A'
                //         v.View(v.Add);
                Diagnostic(ErrorCode.WRN_DeprecatedSymbolStr, "v.Add").WithArguments("Extensions.Add(IViewable2)", "A"));
        }

        [WorkItem(718294)]
        [Fact]
        public void MethodGroupConversion_BetterCandidateHasParameterArray()
        {
            var source = @"
using System;

class Test
{
    void M(IViewable2 v)
    {
        v.View(v.Add);
    }
}

interface IViewable
{
    void View(Action viewer);
}

interface IViewable2 : IViewable
{
}

static class Extensions
{
    [Obsolete(""A"", error: false)]
    public static void Add(this IViewable2 @this) { }

    [Obsolete(""B"", error: false)]
    public static void Add(this IViewable @this, params object[] obj) { }
}
";
            CreateCompilationWithMscorlibAndSystemCore(source).VerifyDiagnostics(
                // (8,16): warning CS0618: 'Extensions.Add(IViewable2)' is obsolete: 'A'
                //         v.View(v.Add);
                Diagnostic(ErrorCode.WRN_DeprecatedSymbolStr, "v.Add").WithArguments("Extensions.Add(IViewable2)", "A"));
        }

        [WorkItem(709114)]
        [Fact]
        public void RenameTypeParameterInOverride()
        {
            var source = @"
public class Base
{
    public virtual void M<T1>(T1 t) { }
}

public class Derived : Base
{
    public override void M<T2>(T2 t) { }

    void Test(int c)
    {
        M(c);
    }
}
";
            var comp = CreateCompilationWithMscorlib(source);
            comp.VerifyDiagnostics();

            var tree = comp.SyntaxTrees.Single();
            var model = comp.GetSemanticModel(tree);

            var callSyntax = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
            var methodSymbol = (MethodSymbol)model.GetSymbolInfo(callSyntax).Symbol;

            Assert.Equal(SpecialType.System_Int32, methodSymbol.TypeArguments.Single().SpecialType);
        }

        [Fact]
        [WorkItem(675327)]
        public void OverloadInheritanceAsync()
        {
            string source = @"
using System.Threading.Tasks;
using System;
class Test
{
    public virtual async Task<int> Foo<U>(Func<Task<U>> f)
    {
        await Task.Delay(10);
        return 1;
    }
}
class TestCase : Test
{
    public override async Task<int> Foo<T>(Func<Task<T>> f)
    {
        await Task.Delay(10);
        return 3;
    }
    public async Task<int> Foo(Func<Task<long>> f)
    {
        await Task.Delay(10);
        return 2;
    }
    public async void Run()
    {
        var xxx = await Foo(async () => { // Roslyn error here
            await Task.Delay(10);
            return 5m; });
        Console.WriteLine(xxx); // 3;
    }
}";
            CreateCompilationWithMscorlib45(source).VerifyDiagnostics();
        }

        [Fact]
        [WorkItem(675327)]
        public void OverloadInheritance001()
        {
            string source = @"
using System;
class Test
{
    public virtual int Foo<U>(Func<U> f)
    {
        return 1;
    }
}
class TestCase : Test
{
    public override int Foo<T>(Func<T> f)
    {
        return 3;
    }
    public void Run()
    {
        var xxx = Foo(() => { // Roslyn error here
            return 5m; });
        Console.WriteLine(xxx); // 3;
    }
}";
            CreateCompilationWithMscorlib45(source).VerifyDiagnostics();
        }

        [Fact, WorkItem(718294)]
        public void ResolveExtensionMethodGroupOverloadWithOptional()
        {
            string source = @"
using System;

class Viewable
{
    static void Main()
    {
        IViewable2<object> v = null;
        v.View(v.Add);
    }
}

interface IViewable<T>
{
    void View(Action<T> viewer);
}

interface IViewable2<T> : IViewable<T>
{
}

static class Extensions
{
    public static void Add<T>(this IViewable<T> @this, T item)
    {
    }

    public static void Add<T>(this IViewable2<T> @this, T item, object obj = null)
    {
    }
}";
            CreateCompilationWithMscorlib45(source).VerifyDiagnostics();
        }

        [Fact, WorkItem(667132)]
        public void ExtensionMethodOnComInterfaceMissingRefToken()
        {
            string source = @"using System;
using System.Runtime.InteropServices;
[ComImport, Guid(""cb4ac859-0589-483e-934d-b27845d5fe74"")]
interface IFoo
{
}
static class Program
{
    public static void Bar(this IFoo self, ref Guid id)
    {
        id = Guid.NewGuid();
    }
    static void Main()
    {
        Foo(null);
    }
    static void Foo(IFoo o)
    {
        Guid g = Guid.NewGuid();
        Console.WriteLine(g);
        o.Bar(g);
        Console.WriteLine(g);
    }
}";
            CreateCompilationWithMscorlib45(source).VerifyDiagnostics();
        }

        [Fact]
        [WorkItem(737971)]
        public void Repro737971a()
        {
            var source = @"
using System;

public class Color
{
}

public class ColorToColor
{
    public static implicit operator ColorToColor(Func<Color, Color> F) { return null; }
}

public class Test
{
    public void M()
    {
        N(_ => default(Color));
    }

    public void N(ColorToColor F) { }
    public void N(Func<Color, Color> F) { }
}
";

            var comp = CreateCompilationWithMscorlib(source);
            comp.VerifyDiagnostics();

            var tree = comp.SyntaxTrees.Single();
            var model = comp.GetSemanticModel(tree);

            var syntax = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
            var symbol = model.GetSymbolInfo(syntax).Symbol;

            // Func<Color, Color> is convertible to ColorToColor, but the converse is not true.
            Assert.Equal("void Test.N(System.Func<Color, Color> F)", symbol.ToTestDisplayString());
        }

        [Fact]
        [WorkItem(737971)]
        public void Repro737971b()
        {
            var source = @"
using System;

public class Color
{
}

public class ColorToColor
{
    public static implicit operator ColorToColor(Func<Color, Color> F) { throw null; }
}

public class Test
{
    public void M()
    {
        N(() => _ => default(Color));
    }

    public void N(Func<ColorToColor> F) { }
    public void N(Func<Func<Color, Color>> F) { }
}
";

            var comp = CreateCompilationWithMscorlib(source);
            comp.VerifyDiagnostics();

            var tree = comp.SyntaxTrees.Single();
            var model = comp.GetSemanticModel(tree);

            var syntax = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
            var symbol = model.GetSymbolInfo(syntax).Symbol;

            // Func<Func<Color, Color>> is convertible to Func<ColorToColor>, but the converse is not true.
            Assert.Equal("void Test.N(System.Func<System.Func<Color, Color>> F)", symbol.ToTestDisplayString());
        }

        [Fact]
        [WorkItem(754406)]
        public void TestBug754406()
        {
            string source =
@"interface I {}
class G<T> where T : I {}
class Program
{
    static void Main(string[] args)
    {
    }

    static void M<T>(G<T> gt1, params int[] i)
    {
        M(gt1, 1, 2);
    }
}";
            var comp = CreateCompilationWithMscorlibAndSystemCore(source);
            comp.VerifyDiagnostics(
                // (9,27): error CS0314: The type 'T' cannot be used as type parameter 'T' in the generic type or method 'G<T>'. There is no boxing conversion or type parameter conversion from 'T' to 'I'.
                //     static void M<T>(G<T> gt1, params int[] i)
                Diagnostic(ErrorCode.ERR_GenericConstraintNotSatisfiedTyVar, "gt1").WithArguments("G<T>", "I", "T", "T"),
                // (11,9): error CS0314: The type 'T' cannot be used as type parameter 'T' in the generic type or method 'G<T>'. There is no boxing conversion or type parameter conversion from 'T' to 'I'.
                //         M(gt1, 1, 2);
                Diagnostic(ErrorCode.ERR_GenericConstraintNotSatisfiedTyVar, "M").WithArguments("G<T>", "I", "T", "T")
                );
        }

        [Fact]
        [WorkItem(528811)]
        public void TestBug528811()
        {
            string source =
@"using System;

delegate byte DL();
class Test
{
    void foo()
    {
        EventHandler y = null;
        y += foo;
        y += x => 2;
    }
}";
            var comp = CreateCompilationWithMscorlibAndSystemCore(source);
            comp.VerifyDiagnostics(
                // (9,14): error CS0123: No overload for 'foo' matches delegate 'System.EventHandler'
                //         y += foo;
                Diagnostic(ErrorCode.ERR_MethDelegateMismatch, "foo").WithArguments("foo", "System.EventHandler"),
                // (10,14): error CS1593: Delegate 'System.EventHandler' does not take 1 arguments
                //         y += x => 2;
                Diagnostic(ErrorCode.ERR_BadDelArgCount, "x => 2").WithArguments("System.EventHandler", "1")
                );
        }

        [Fact]
        [WorkItem(655409)]
        public void TestBug655409()
        {
            string source =
@"
using System;
 
class C
{
    static void Main()
    {
        M(a => M(b => M(c => M(d => M(e => M(f => a))))));

        System.Console.WriteLine(""success"");
    }
 
    static T M<T>(Func<bool, T> x) { return default(T); }
    static T M<T>(Func<byte, T> x) { return default(T); }
    static T M<T>(Func<uint, T> x) { return default(T); }
    static T M<T>(Func<long, T> x) { return default(T); }
}

";
            var comp = CreateCompilationWithMscorlibAndSystemCore(source);
            comp.VerifyDiagnostics(
    // (8,44): error CS0121: The call is ambiguous between the following methods or properties: 'C.M<T>(System.Func<bool, T>)' and 'C.M<T>(System.Func<byte, T>)'
    //         M(a => M(b => M(c => M(d => M(e => M(f => a))))));
    Diagnostic(ErrorCode.ERR_AmbigCall, "M").WithArguments("C.M<T>(System.Func<bool, T>)", "C.M<T>(System.Func<byte, T>)").WithLocation(8, 44)
                );
        }
    }
}