﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests
{
    /// <summary>
    /// Please add your tests to other files if possible:
    ///     * FlowDiagnosticTests.cs - all tests on Diagnostics
    ///     * IterationJumpYieldStatementTests.cs - while, do, for, foreach, break, continue, goto, iterator (yield break, yield return)
    ///     * TryLockUsingStatementTests.cs - try-catch-finally, lock, &amp; using statement
    /// </summary>
    public partial class FlowAnalysisTests : FlowTestBase
    {
        #region "Expressions"

        [WorkItem(545047)]
        [Fact]
        public void DataFlowsInAndNullable_Field()
        {
            // WARNING: if this test is edited, the test with the 
            //          test with the same name in VB must be modified too
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowStatements(@"
struct S
{
    public int F;
    public S(int f)
    {
        this.F = f;
    }
    static void Main(string[] args)
    {
        int? i = 1;
        S s = new S(1);

/*<bind>*/
        Console.WriteLine(i.Value);
        Console.WriteLine(s.F);
/*</bind>*/
    }
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal("i, s", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("i, s", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("args, i, s", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.Captured));
        }

        [Fact]
        public void DataFlowsOutAndStructField()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowStatements(@"
struct S
{
    public int F;
    public S(int f)
    {
        this.F = f;
    }
    static void Main(string[] args)
    {
        S s = new S(1);
/*<bind>*/
        s.F = 1;
/*</bind>*/
        var x = s.F;
    }
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal("s", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal("s", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal("s", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal("s", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("args, s, x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.Captured));
        }

        [Fact]
        public void DataFlowsInAndNullable_Property()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowStatements(@"
struct S
{
    public int F;
    public S(int f)
    {
        this.F = f;
    }
    public int P { get; set; }

    static void Main(string[] args)
    {
        int? i = 1;
        S s = new S(1);

/*<bind>*/
        Console.WriteLine(i.Value);
        Console.WriteLine(s.P);
/*</bind>*/
    }
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal("i, s", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("i, s", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal("s", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("args, i, s", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.Captured));
        }

        [WorkItem(538238)]
        [Fact]
        public void TestDataFlowsIn03()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
class Program
{
    static void Main(string[] args)
    {
        int x = 1;
        int y = 2;
        int z = /*<bind>*/x + y/*</bind>*/;
    }
}
");
            Assert.Equal("x, y", GetSymbolNamesSortedAndJoined(analysis.DataFlowsIn));
        }

        [Fact]
        public void TestDataFlowForValueTypes()
        {
            // WARNING: test matches the same test in VB (TestDataFlowForValueTypes)
            //          Keep the two tests in synch!

            var analysis = CompileAndAnalyzeDataFlowStatements(@"
class Tst
{
    public static void Main()
    {
        S0 a;
        S1 b;
        S2 c;
        S3 d;
        E0 e;
        E1 f;

/*<bind>*/
        Console.WriteLine(a);
        Console.WriteLine(b);
        Console.WriteLine(c);
        Console.WriteLine(d);
        Console.WriteLine(e);
        Console.WriteLine(f);
/*</bind>*/
    }
}


struct S0
{
}

struct S1
{
    public S0 s0;
}

struct S2
{
    public S0 s0;
    public int s1;
}

struct S3
{
    public S2 s;
    public object s1;
}

enum E0
{
}

enum E1
{
    V1
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.Captured));
            Assert.Equal("c, d, e, f", GetSymbolNamesSortedAndJoined(analysis.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.DataFlowsOut));
            Assert.Equal("a, b, c, d, e, f", GetSymbolNamesSortedAndJoined(analysis.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.ReadOutside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.VariablesDeclared));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.WrittenInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.WrittenOutside));
        }

        [WorkItem(538997)]
        [Fact]
        public void TestDataFlowsIn04()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
using System;
class Program
{
    static void Main()
    {
        string s = "";
        Func<string> f = /*<bind>*/s/*</bind>*/.ToString;
    }
}
");
            Assert.Equal("s", GetSymbolNamesSortedAndJoined(analysis.DataFlowsIn));
        }

        [Fact]
        public void TestDataFlowsOutExpression01()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
class C {
    public void F(int x)
    {
        int a = 1, y;
        int tmp = x +
/*<bind>*/
            (y = x = 2)
/*</bind>*/
            + (a = 2);
        int c = a + 4 + x + y;
    }
}");
            Assert.Equal("x, y", GetSymbolNamesSortedAndJoined(analysis.DataFlowsOut));
        }

        [WorkItem(540171)]
        [Fact]
        public void TestIncrement()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowExpression(@"
class C
{
    static void M(int i)
    {
        /*<bind>*/i++/*</bind>*/;
        M(i);
    }
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal("i", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal("i", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("i", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal("i", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal("i", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("i", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [WorkItem(543695)]
        [Fact]
        public void FlowAnalysisOnTypeOrNamespace1()
        {
            var results = CompileAndAnalyzeDataFlowExpression(@"
class C
{
    static void M(int i)
    {
        /*<bind>*/ System.Console /*</bind>*/ .WriteLine(i);
    }
}
");
            Assert.False(results.Succeeded);
        }

        [WorkItem(543695)]
        [Fact]
        public void FlowAnalysisOnTypeOrNamespace3()
        {
            var results = CompileAndAnalyzeDataFlowExpression(@"
public class A
{
    public class B
    {
        public static void M() { }
    }
}

class C
{
    static void M(int i)
    {
        /*<bind>*/ A.B /*</bind>*/ .M(i);
    }
}
");
            Assert.False(results.Succeeded);
        }

        [WorkItem(543695)]
        [Fact]
        public void FlowAnalysisOnTypeOrNamespace4()
        {
            var results = CompileAndAnalyzeDataFlowExpression(@"
public class A
{
    public class B
    {
        public static void M() { }
    }
}

class C
{
    static void M(int i)
    {
        /*<bind>*/ A /*</bind>*/ .B.M(i);
    }
}
");
            Assert.False(results.Succeeded);
        }

        [WorkItem(540183)]
        [Fact]
        public void DataFlowsOutIncrement01()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowExpression(@"
class C
{
    static void M(int i)
    {
        /*<bind>*/i++/*</bind>*/;
        M(i);
    }
}
");
            Assert.Equal("i", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("i", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
        }

        [WorkItem(6359, "DevDiv_Projects/Roslyn")]
        [Fact]
        public void DataFlowsOutPreDecrement01()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowExpression(@"
class Test
{
    string method(string s, int i)
    {
        string[] myvar = new string[i];
 
        myvar[0] = s;
        /*<bind>*/myvar[--i] = s + i.ToString()/*</bind>*/;
        return myvar[i];
    }
}");
            Assert.Equal("i", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("i", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
        }

        [Fact]
        public void TestBranchOfTernaryOperator()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowExpression(@"
class C {
    static void Main()
    {
        bool x = true;
        bool y = x ? 
/*<bind>*/
x
/*</bind>*/
 : true;
    }
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("x, y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [WorkItem(540832)]
        [Fact]
        public void TestAssignmentExpressionAsBranchOfTernaryOperator()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowExpression(@"
class C {
    static void Main()
    {
        int x;
        int y = true ? 
/*<bind>*/
x = 1
/*</bind>*/
 : x;
    }
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [Fact]
        public void TestAlwaysAssignedWithTernaryOperator()
        {
            var analysis = CompileAndAnalyzeDataFlowStatements(@"
class C
{
    public void F(int x)
    {
        int a, b, x = 100;
        /*<bind>*/
        int c = true ? a = 1 : b = 2;
        /*</bind>*/
    }
}");
            Assert.Equal("a, c", GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
        }

        [Fact]
        public void TestAlwaysAssigned04()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
class C {
    public static void Main(string[] args)
    {
        int i;
        i =
/*<bind>*/
        int.Parse(args[0].ToString())
/*</bind>*/
            ;
    }
}");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
        }

        [Fact]
        public void TestAlwaysAssigned05()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
class C {
    public static void Main(string[] args)
    {
        bool a = true;
        bool b;
        int c =
/*<bind>*/
        (b = a) && (b = !a)
/*</bind>*/
          ? 1 : 2;
    }
}");
            Assert.Equal("b", GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
        }

        [Fact]
        public void TestAlwaysAssigned06()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
class C {
    public static void Main(string[] args)
    {
        bool a = true;
        bool b;
        int c =
/*<bind>*/
        a && (b = !a)
/*</bind>*/
          ? 1 : 2;
    }
}");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
        }

        [Fact]
        public void TestAlwaysAssigned07()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
class C {
    public static void Main(string[] args)
    {
        bool a = true;
        bool b;
        int c =
/*<bind>*/
        (b = a) && !a
/*</bind>*/
          ? 1 : 2;
    }
}");
            Assert.Equal("b", GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
        }

        [Fact]
        public void TestAlwaysAssigned08()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
class C {
    public static void Main(string[] args)
    {
        bool a = true;
        bool b;
        int c =
/*<bind>*/
        (b = a) || (b = !a)
/*</bind>*/
          ? 1 : 2;
    }
}");
            Assert.Equal("b", GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
        }

        [Fact]
        public void TestAlwaysAssigned09()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
class C {
    public static void Main(string[] args)
    {
        bool a = true;
        bool b;
        int c =
/*<bind>*/
        a || (b = !a)
/*</bind>*/
          ? 1 : 2;
    }
}");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
        }

        [Fact]
        public void TestAlwaysAssigned10()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
class C {
    public static void Main(string[] args)
    {
        bool a = true;
        bool b;
        int c =
/*<bind>*/
        (b = a) || !a
/*</bind>*/
          ? 1 : 2;
    }
}");
            Assert.Equal("b", GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
        }

        [Fact]
        public void TestAlwaysAssigned11()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
class C {
    public static void Main(string[] args)
    {
        object a = new object;
        object b;
        object c =
/*<bind>*/
        (b = a) ?? (b = null)
/*</bind>*/
        ;
    }
}");
            Assert.Equal("b", GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
        }

        [Fact]
        public void TestAlwaysAssigned12()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
class C {
    public static void Main(string[] args)
    {
        object a = new object;
        object b;
        object c =
/*<bind>*/
        a ?? (b = null)
/*</bind>*/
        ;
    }
}");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
        }

        [Fact]
        public void TestAlwaysAssigned13()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
class C {
    public static void Main(string[] args)
    {
        object a = new object;
        object b;
        object c =
/*<bind>*/
        (b = a) ?? null
/*</bind>*/
        ;
    }
}");
            Assert.Equal("b", GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
        }

        [Fact]
        public void TestAlwaysAssigned14()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
class C {
    public static void Main(string[] args)
    {
        bool a = true;
        bool b, c, d, e, f;
        bool c = (b = a) ? (c = a) : (d = a) ? (e = a) : /*<bind>*/ (f = a) /*</bind>*/;
    }
}");
            Assert.Equal("f", GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
        }

        [Fact]
        public void TestAlwaysAssigned15()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
class C {
    public static void Main(string[] args)
    {
        bool a = true;
        bool b, c, d, e, f;
        bool c = (b = a) ? (c = a) : /*<bind>*/ (d = a) ? (e = a) : (f = a) /*</bind>*/;
    }
}");
            Assert.Equal("d", GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
        }

        [Fact]
        public void TestAlwaysAssigned16()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
class C {
    public static bool B(out bool b) { b = true; return b; }
    public static void Main(string[] args)
    {
        bool a, b;
        bool c = B(out a) && B(out /*<bind>*/b/*</bind>*/);
    }
}");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
        }

        [Fact]
        public void TestAlwaysAssigned17()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
class C {
    public static bool B(out bool b) { b = true; return b; }
    public static void Main(string[] args)
    {
        bool a, b;
        bool c = /*<bind>*/B(out a) && B(out b)/*</bind>*/;
    }
}");
            Assert.Equal("a", GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
        }

        [Fact]
        public void TestAlwaysAssigned18()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
class C {
    public static bool B(out bool b) { b = true; return b; }
    public static void Main(string[] args)
    {
        bool a, b;
        bool c = B(out a) || B(out /*<bind>*/b/*</bind>*/);
    }
}");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
        }

        [Fact]
        public void TestAlwaysAssigned19()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
class C {
    public static bool B(out bool b) { b = true; return b; }
    public static void Main(string[] args)
    {
        bool a, b;
        bool c = /*<bind>*/B(out a) || B(out b)/*</bind>*/;
    }
}");
            Assert.Equal("a", GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
        }

        [Fact]
        public void TestAlwaysAssigned22()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
class C {
    public static bool B(out bool b) { b = true; return b; }
    public static void Main(string[] args)
    {
        bool a, b;
        if (/*<bind>*/B(out a)/*</bind>*/) a = true; else b = true;
    }
}");
            Assert.Equal("a", GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
        }

        [Fact]
        public void TestAlwaysAssignedAndWrittenInside()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
class C {
    public static void Main(string[] args)
    {
        int i =
/*<bind>*/
        int.Parse(args[0].ToString())
/*</bind>*/
            ;
    }
}");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.WrittenInside));
        }

        [Fact]
        public void TestWrittenInside03()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
class C {
    public static void Main(string[] args)
    {
        int i;
        i =
/*<bind>*/
        int.Parse(args[0].ToString())
/*</bind>*/
            ;
    }
}");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.WrittenInside));
        }

        [Fact]
        public void TestReadWrite01()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
class C {
    public static void Main(string[] args)
    {
        int x = 3;
        /*<bind>*/x/*</bind>*/ = 3;
    }
}");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.WrittenInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.ReadOutside));
            Assert.Equal("args, x", GetSymbolNamesSortedAndJoined(analysis.WrittenOutside));
        }

        [Fact]
        public void TestReadWrite02()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
class C {
    public static void Main(string[] args)
    {
        int x = 3;
        /*<bind>*/x/*</bind>*/ += 3;
    }
}");
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(analysis.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.WrittenInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.ReadOutside));
            Assert.Equal("args, x", GetSymbolNamesSortedAndJoined(analysis.WrittenOutside));
        }

        [Fact]
        public void TestReadWrite03()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
class C {
    public static void M(out int x) { x = 1; }
    public static void Main(string[] args)
    {
        int x = 3;
        M(out /*<bind>*/x/*</bind>*/);
    }
}");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.WrittenInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.ReadOutside));
            Assert.Equal("args, x", GetSymbolNamesSortedAndJoined(analysis.WrittenOutside));
        }

        [Fact]
        public void TestReadWrite04()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
class C {
    public static void M(ref int x) { x = 1; }
    public static void Main(string[] args)
    {
        int x = 3;
        M(ref /*<bind>*/x/*</bind>*/);
    }
}");
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(analysis.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.WrittenInside));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(analysis.ReadOutside));
            Assert.Equal("args, x", GetSymbolNamesSortedAndJoined(analysis.WrittenOutside));
        }

        [Fact]
        public void TestAssignmentExpressionSelection()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowExpression(@"
class C {
    static void Main()
    {
        int x = (
/*<bind>*/
x = 1
/*</bind>*/
) + x;
    }
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [Fact]
        public void TestSingleVariableSelection()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowExpression(@"
class C {
    static void Main()
    {
        bool x = true;
        bool y = x | 
/*<bind>*/
x
/*</bind>*/
;
    }
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("x, y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [Fact]
        public void TestParenthesizedAssignmentExpressionSelection()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowExpression(@"
class C {
    static void Main()
    {
        bool x = true;
        bool y = x | 
/*<bind>*/
(x = x)
/*</bind>*/
 | x;
    }
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("x, y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [Fact]
        public void TestRefArgumentSelection()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowExpression(@"
class C {
    static void Main()
    {
        int x = 0;
        Foo(ref 
/*<bind>*/
x
/*</bind>*/
);
        System.Console.WriteLine(x);
    }

    static void Foo(ref int x) { }
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [WorkItem(540066)]
        [Fact]
        public void AnalysisOfBadRef()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowExpression(@"
class C
{
    static void Main()
    {
        /*<bind>*/Main(ref 1)/*</bind>*/;
    }
}");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
        }

        [Fact]
        public void TestAlwaysAssigned20NullCoalescing()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
class C {
    public static object B(out object b) { b = null; return b; }
    public static void Main(string[] args)
    {
        object a, b;
        object c = B(out a) ?? B(out /*<bind>*/b/*</bind>*/);
    }
}");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
        }

        [WorkItem(528662)]
        [Fact]
        public void TestNullCoalescingWithConstNullLeft()
        {
            var dataFlows = CompileAndAnalyzeDataFlowExpression(@"
struct STest {
    
    public static string SM()
    {
        const string s = null;
        var ss = ""Q"";
        var ret = /*<bind>*/( s ?? (ss = ""C""))/*</bind>*/ + ss;
        return ret;
    }
}");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlows.VariablesDeclared));
            Assert.Equal("ss", GetSymbolNamesSortedAndJoined(dataFlows.AlwaysAssigned));
            Assert.Equal("s", GetSymbolNamesSortedAndJoined(dataFlows.DataFlowsIn));
            Assert.Equal("ss", GetSymbolNamesSortedAndJoined(dataFlows.DataFlowsOut));
        }

        [WorkItem(528662)]
        [Fact]
        public void TestNullCoalescingWithConstNotNullLeft()
        {
            var dataFlows = CompileAndAnalyzeDataFlowExpression(@"
class Test {
    
    public static string SM()
    {
        const string s = ""Not Null"";
        var ss = ""QC"";
        var ret = /*<bind>*/ s ?? ss /*</bind>*/ + ""\r\n"";
        return ret;
    }
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlows.VariablesDeclared));
            Assert.Equal("s", GetSymbolNamesSortedAndJoined(dataFlows.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlows.DataFlowsOut));
        }

        [WorkItem(8935, "DevDiv_Projects/Roslyn")]
        [Fact]
        public void TestDefaultOperator01()
        {
            var dataFlows = CompileAndAnalyzeDataFlowExpression(@"
using System;
using System.Collections.Generic;

class Test<T> {
    public T GetT()     {
        return /*<bind>*/ default(T) /*</bind>*/;
    }
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlows.VariablesDeclared));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlows.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlows.DataFlowsOut));
        }

        [Fact]
        public void TestTypeOfOperator01()
        {
            var dataFlows = CompileAndAnalyzeDataFlowExpression(@"
using System;
using System.Collections.Generic;

class Test<T>
{
    public short GetT(T t)
    {
        if (/*<bind>*/ typeof(T) == typeof(int) /*</bind>*/)
            return 123;

        return 456;
    }
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlows.VariablesDeclared));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlows.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlows.DataFlowsOut));
        }

        [Fact]
        public void TestIsOperator01()
        {
            var dataFlows = CompileAndAnalyzeDataFlowExpression(@"
using System;
using System.Collections.Generic;

struct Test<T>
{
    public string GetT(T t)
    {
        if /*<bind>*/(t is string)/*</bind>*/
            return ""SSS"";

        return null;
    }
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlows.VariablesDeclared));
            Assert.Equal("t", GetSymbolNamesSortedAndJoined(dataFlows.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlows.DataFlowsOut));
        }

        [Fact]
        public void TestAsOperator01()
        {
            var dataFlows = CompileAndAnalyzeDataFlowExpression(@"
using System;
using System.Collections.Generic;

struct Test<T>
{
    public string GetT(T t)
    {
        string ret = null;
        if (t is string)
            ret = /*<bind>*/t as string/*</bind>*/;

        return ret;
    }
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlows.VariablesDeclared));
            Assert.Equal("t", GetSymbolNamesSortedAndJoined(dataFlows.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlows.DataFlowsOut));
        }

        [WorkItem(4028, "DevDiv_Projects/Roslyn")]
        [Fact]
        public void TestArrayInitializer()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowExpression(@"
class C {
    static void Main()
    {
        int y = 1;
        int[,] x = { { 
/*<bind>*/
y
/*</bind>*/
 } };
    }
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal("y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("x, y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [WorkItem(539286)]
        [Fact]
        public void TestAnalysisInFieldInitializers()
        {
            var results1 = CompileAndAnalyzeDataFlowExpression(@"
using System;
class C {
    static void Main()
    {
        Func<int, int> f = p => 
        {
            int x = 1;
            int y = 1;
            return /*<bind>*/1 + (x=2) + p + y/*</bind>*/;
        };

        f(1);
    }
}
");
            var results2 = CompileAndAnalyzeDataFlowExpression(@"
using System;
class C {
    static Func<int, int> f = p => 
    {
        int x = 1;
        int y = 1;
        return /*<bind>*/1 + (x=2) + p + y/*</bind>*/;
    };

    static void Main()
    {
        int r = f(1);
    }
}
");
            Assert.Equal(GetSymbolNamesSortedAndJoined(results1.AlwaysAssigned),
                GetSymbolNamesSortedAndJoined(results2.AlwaysAssigned));
            Assert.Equal(GetSymbolNamesSortedAndJoined(results1.Captured),
                GetSymbolNamesSortedAndJoined(results2.Captured));
            Assert.Equal(GetSymbolNamesSortedAndJoined(results1.DataFlowsIn),
                GetSymbolNamesSortedAndJoined(results2.DataFlowsIn));
            Assert.Equal(GetSymbolNamesSortedAndJoined(results1.DataFlowsOut),
                GetSymbolNamesSortedAndJoined(results2.DataFlowsOut));
            Assert.Equal(GetSymbolNamesSortedAndJoined(results1.ReadInside),
                GetSymbolNamesSortedAndJoined(results2.ReadInside));
            Assert.Equal(GetSymbolNamesSortedAndJoined(results1.ReadOutside),
                string.Join(", ", new string[] { "f" }.Concat((results2.ReadOutside).Select(symbol => symbol.Name)).OrderBy(name => name)));
            Assert.Equal(GetSymbolNamesSortedAndJoined(results1.WrittenInside),
                GetSymbolNamesSortedAndJoined(results2.WrittenInside));
            Assert.Equal(GetSymbolNamesSortedAndJoined(results1.WrittenOutside),
                string.Join(", ", new string[] { "f" }.Concat((results2.WrittenOutside).Select(symbol => symbol.Name)).OrderBy(name => name)));
        }

        [WorkItem(539286)]
        [Fact]
        public void TestAnalysisInSimpleFieldInitializers()
        {
            var results1 = CompileAndAnalyzeDataFlowExpression(@"
using System;
class C {
    int x = 1;
    int y = 1;
    int z = /*<bind>*/1 + (x=2) + p + y/*</bind>*/;
}
");
            var results2 = CompileAndAnalyzeDataFlowExpression(@"
using System;
class C {
    int x = 1;
    int y = 1;
    static void Main()
    {
        /*<bind>*/1 + (x=2) + p + y/*</bind>*/;
    }
}
");

            //  NOTE: 'f' should not be reported in results1.AlwaysAssigned, this issue will be addressed separately
            Assert.Equal(GetSymbolNamesSortedAndJoined(results1.AlwaysAssigned),
                GetSymbolNamesSortedAndJoined(results2.AlwaysAssigned));
            Assert.Equal(GetSymbolNamesSortedAndJoined(results1.Captured),
                GetSymbolNamesSortedAndJoined(results2.Captured));
            Assert.Equal(GetSymbolNamesSortedAndJoined(results1.DataFlowsIn),
                GetSymbolNamesSortedAndJoined(results2.DataFlowsIn));
            Assert.Equal(GetSymbolNamesSortedAndJoined(results1.DataFlowsOut),
                GetSymbolNamesSortedAndJoined(results2.DataFlowsOut));
            Assert.Equal(GetSymbolNamesSortedAndJoined(results1.ReadInside),
                GetSymbolNamesSortedAndJoined(results2.ReadInside));
            Assert.Equal(GetSymbolNamesSortedAndJoined(results1.ReadOutside),
                GetSymbolNamesSortedAndJoined(results2.ReadOutside));
            Assert.Equal(GetSymbolNamesSortedAndJoined(results1.WrittenInside),
                GetSymbolNamesSortedAndJoined(results2.WrittenInside));
            Assert.Equal(GetSymbolNamesSortedAndJoined(results1.WrittenOutside),
                GetSymbolNamesSortedAndJoined(results2.WrittenOutside));
        }

        [WorkItem(541968)]
        [Fact]
        public void ConstantFieldInitializerExpression()
        {
            var dataFlows = CompileAndAnalyzeDataFlowExpression(@"
using System;
public class Aa
{
    const int myLength = /*<bind>*/5/*</bind>*/;
}
");

            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlows.VariablesDeclared));
        }

        [WorkItem(541968)]
        [Fact]
        public void ConstantFieldInitializerExpression2()
        {
            var dataFlows = CompileAndAnalyzeDataFlowExpression(@"
using System;
public class Aa
{
    // NOTE: illegal, but still a region we should handle.
    const bool myLength = true || ((Func<int, int>)(x => { int y = x; return /*<bind>*/y/*</bind>*/; }))(1) == 2;
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlows.VariablesDeclared));
            Assert.Equal("System.Int32 y", dataFlows.DataFlowsIn.Single().ToTestDisplayString());
            Assert.Equal("System.Int32 y", dataFlows.ReadInside.Single().ToTestDisplayString());
        }

        [WorkItem(541968)]
        [Fact]
        public void FieldInitializerExpression()
        {
            var dataFlows = CompileAndAnalyzeDataFlowExpression(@"
using System;
public class Aa
{
    bool myLength = true || ((Func<int, int>)(x => { int y = x; return /*<bind>*/y/*</bind>*/; }))(1) == 2;
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlows.VariablesDeclared));
            Assert.Equal("System.Int32 y", dataFlows.DataFlowsIn.Single().ToTestDisplayString());
            Assert.Equal("System.Int32 y", dataFlows.ReadInside.Single().ToTestDisplayString());
        }

        [WorkItem(542454)]
        [Fact]
        public void IdentifierNameInObjectCreationExpr()
        {
            var dataFlows = CompileAndAnalyzeDataFlowExpression(@"
class myClass
{
    static int Main()
    {
        myClass oc = new /*<bind>*/myClass/*</bind>*/();
        return 0;
    }
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlows.VariablesDeclared));
        }

        [WorkItem(542463)]
        [Fact]
        public void MethodGroupInDelegateCreation()
        {
            var dataFlows = CompileAndAnalyzeDataFlowExpression(@"
class C
{
    void Method()
    {
        System.Action a = new System.Action(/*<bind>*/Method/*</bind>*/);
    }
}
");

            Assert.Equal("this", dataFlows.ReadInside.Single().Name);
        }

        [WorkItem(542771)]
        [Fact]
        public void BindInCaseLabel()
        {
            var dataFlows = CompileAndAnalyzeDataFlowExpression(@"
class TestShapes
{
    static void Main()
    {
        color s = color.blue;
        switch (s)
        {
            case true ? /*<bind>*/ color.blue /*</bind>*/ : color.blue:
                break;
            default: goto default;
        }
    }
}
enum color { blue, green }");
            var tmp = dataFlows.VariablesDeclared; // ensure no exception thrown
            Assert.Empty(dataFlows.VariablesDeclared);
        }

        [WorkItem(542915)]
        [Fact]
        public void BindLiteralExprInEnumDecl()
        {
            var dataFlows = CompileAndAnalyzeDataFlowExpression(@"
enum Number
{
    Zero = /*<bind>*/0/*</bind>*/
}
");
            Assert.True(dataFlows.Succeeded);
            Assert.Empty(dataFlows.VariablesDeclared);
        }

        [WorkItem(542944)]
        [Fact]
        public void AssignToConst()
        {
            var analysis = CompileAndAnalyzeDataFlowStatements(@"
class Program
{
    static void Main(string[] args)
    {
        const string a = null;
        /*<bind>*/a = null;/*</bind>*/
    }
}
");
            Assert.Equal("a", GetSymbolNamesSortedAndJoined(analysis.WrittenInside));
        }

        [WorkItem(543987)]
        [Fact]
        public void TestAddressOfUnassignedStructLocal()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
class Program
{
    static void Main()
    {
        int x;
        int* px = /*<bind>*/&x/*</bind>*/;
    }
}
");
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.Captured));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(analysis.UnsafeAddressTaken));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.VariablesDeclared));
            
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.DataFlowsOut));
            
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.ReadOutside));
            
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(analysis.WrittenInside));
            Assert.Equal("px", GetSymbolNamesSortedAndJoined(analysis.WrittenOutside));
        }

        [WorkItem(543987)]
        [Fact]
        public void TestAddressOfAssignedStructLocal()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
class Program
{
    static void Main()
    {
        int x = 1;
        int* px = /*<bind>*/&x/*</bind>*/;
    }
}
");
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.Captured));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(analysis.UnsafeAddressTaken));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.VariablesDeclared));

            Assert.Equal("x", GetSymbolNamesSortedAndJoined(analysis.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.DataFlowsOut));

            Assert.Equal("x", GetSymbolNamesSortedAndJoined(analysis.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.ReadOutside));

            Assert.Equal("x", GetSymbolNamesSortedAndJoined(analysis.WrittenInside));
            Assert.Equal("px, x", GetSymbolNamesSortedAndJoined(analysis.WrittenOutside));
        }

        [WorkItem(543987)]
        [Fact]
        public void TestAddressOfUnassignedStructField()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
public struct S
{
    public int x;
    public int y;
}

class Program
{
    static void Main()
    {
        S s;
        int* px = /*<bind>*/&s.x/*</bind>*/;
    }
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.Captured));
            Assert.Equal("s", GetSymbolNamesSortedAndJoined(analysis.UnsafeAddressTaken));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.VariablesDeclared));

            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.DataFlowsOut));

            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.ReadOutside));

            Assert.Equal("s", GetSymbolNamesSortedAndJoined(analysis.WrittenInside));
            Assert.Equal("px", GetSymbolNamesSortedAndJoined(analysis.WrittenOutside));
        }

        [WorkItem(543987)]
        [Fact]
        public void TestAddressOfAssignedStructField()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
public struct S
{
    public int x;
    public int y;
}

class Program
{
    static void Main()
    {
        S s;
        s.x = 2;
        int* px = /*<bind>*/&s.x/*</bind>*/;
    }
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.Captured));
            Assert.Equal("s", GetSymbolNamesSortedAndJoined(analysis.UnsafeAddressTaken));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.VariablesDeclared));

            Assert.Equal("s", GetSymbolNamesSortedAndJoined(analysis.DataFlowsIn));
            Assert.Equal("s", GetSymbolNamesSortedAndJoined(analysis.DataFlowsOut));

            Assert.Equal("s", GetSymbolNamesSortedAndJoined(analysis.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.ReadOutside));

            Assert.Equal("s", GetSymbolNamesSortedAndJoined(analysis.WrittenInside));
            Assert.Equal("px, s", GetSymbolNamesSortedAndJoined(analysis.WrittenOutside));
        }

        [Fact]
        public void TestAddressOfAssignedStructField2()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
public struct S
{
    public int x;
}

class Program
{
    static void Main()
    {
        S s;
        s.x = 2;
        int* px = /*<bind>*/&s.x/*</bind>*/;
    }
}
");
            // Really ???
            Assert.Equal("s", GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));

            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.Captured));
            Assert.Equal("s", GetSymbolNamesSortedAndJoined(analysis.UnsafeAddressTaken));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.VariablesDeclared));

            Assert.Equal("s", GetSymbolNamesSortedAndJoined(analysis.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.DataFlowsOut));

            Assert.Equal("s", GetSymbolNamesSortedAndJoined(analysis.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.ReadOutside));

            Assert.Equal("s", GetSymbolNamesSortedAndJoined(analysis.WrittenInside));
            Assert.Equal("px, s", GetSymbolNamesSortedAndJoined(analysis.WrittenOutside));
        }

        // Make sure that assignment is consistent with address-of.
        [Fact]
        public void TestAssignToStructField()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
public struct S
{
    public int x;
    public int y;
}

class Program
{
    static void Main()
    {
        S s;
        int x = /*<bind>*/s.x = 1/*</bind>*/;
    }
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.Captured));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.VariablesDeclared));

            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.DataFlowsOut));

            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.ReadOutside));

            Assert.Equal("s", GetSymbolNamesSortedAndJoined(analysis.WrittenInside));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(analysis.WrittenOutside));
        }

        [Fact, WorkItem(544314)]
        public void TestOmittedLambdaPointerTypeParameter()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
using System;

unsafe public class Test
{
    public delegate int D(int* p);
    public static void Main()
    {
		int i = 10;
		int* p = &i;
		D d = /*<bind>*/delegate { return *p;}/*</bind>*/;
	}
}
");
            Assert.Null(GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
            Assert.Equal("p", GetSymbolNamesSortedAndJoined(analysis.Captured));
            Assert.Equal("i", GetSymbolNamesSortedAndJoined(analysis.UnsafeAddressTaken));
            Assert.Equal("", GetSymbolNamesSortedAndJoined(analysis.VariablesDeclared));

            Assert.Equal("p", GetSymbolNamesSortedAndJoined(analysis.DataFlowsIn));
            Assert.Null(GetSymbolNamesSortedAndJoined(analysis.DataFlowsOut));

            Assert.Equal("p", GetSymbolNamesSortedAndJoined(analysis.ReadInside));
            Assert.Equal("i", GetSymbolNamesSortedAndJoined(analysis.ReadOutside));

            Assert.Equal("", GetSymbolNamesSortedAndJoined(analysis.WrittenInside));
            Assert.Equal("d, i, p", GetSymbolNamesSortedAndJoined(analysis.WrittenOutside));
        }

        [Fact]
        public void TestObjectInitializerExpression()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
public class MemberInitializerTest
{   
    public int x;
    public int y { get; set; }

    public static void Main()
    {
        var i = /*<bind>*/ new MemberInitializerTest() { x = 1, y = 2 } /*</bind>*/;
    }
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.Captured));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.VariablesDeclared));

            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.DataFlowsOut));

            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.ReadOutside));

            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.WrittenInside));
            Assert.Equal("i", GetSymbolNamesSortedAndJoined(analysis.WrittenOutside));
        }

        [Fact]
        public void TestObjectInitializerExpression_LocalAccessed()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
public class MemberInitializerTest
{   
    public int x;
    public int y { get; set; }

    public static void Main()
    {
        int x = 0, y = 0;
        var i = /*<bind>*/ new MemberInitializerTest() { x = x, y = y } /*</bind>*/;
    }
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.Captured));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.VariablesDeclared));

            Assert.Equal("x, y", GetSymbolNamesSortedAndJoined(analysis.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.DataFlowsOut));

            Assert.Equal("x, y", GetSymbolNamesSortedAndJoined(analysis.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.ReadOutside));

            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.WrittenInside));
            Assert.Equal("i, x, y", GetSymbolNamesSortedAndJoined(analysis.WrittenOutside));
        }

        [Fact]
        public void TestObjectInitializerExpression_InvalidAccess()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
public class MemberInitializerTest
{   
    public int x;
    public int y { get; set; }

    public static void Main()
    {
        var i = /*<bind>*/ new MemberInitializerTest() { x = x, y = y } /*</bind>*/;
        int x = 0, y = 0;
    }
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.Captured));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.VariablesDeclared));

            Assert.Equal("x, y", GetSymbolNamesSortedAndJoined(analysis.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.DataFlowsOut));

            Assert.Equal("x, y", GetSymbolNamesSortedAndJoined(analysis.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.ReadOutside));

            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.WrittenInside));
            Assert.Equal("i, x, y", GetSymbolNamesSortedAndJoined(analysis.WrittenOutside));
        }

        [Fact]
        public void TestObjectInitializerExpression_LocalAccessed_InitializerExpressionSyntax()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
public class MemberInitializerTest
{   
    public int x;
    public int y { get; set; }

    public static void Main()
    {
        int x = 0, y = 0;
        var i = new MemberInitializerTest() /*<bind>*/ { x = x, y = y } /*</bind>*/;
    }
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.Captured));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.VariablesDeclared));

            Assert.Equal("x, y", GetSymbolNamesSortedAndJoined(analysis.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.DataFlowsOut));

            Assert.Equal("x, y", GetSymbolNamesSortedAndJoined(analysis.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.ReadOutside));

            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.WrittenInside));
            Assert.Equal("i, x, y", GetSymbolNamesSortedAndJoined(analysis.WrittenOutside));
        }

        [Fact]
        public void TestObjectInitializerExpression_NestedObjectInitializer()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
public class Foo
{
    public int z;
}
public class MemberInitializerTest
{   
    public int x;
    public Foo y { get; set; }

    public static void Main()
    {
        int x = 0, z = 0;
        var i = new MemberInitializerTest() { x = x, y = /*<bind>*/ { z = z } /*</bind>*/ };
    }
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.Captured));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.VariablesDeclared));

            Assert.Equal("z", GetSymbolNamesSortedAndJoined(analysis.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.DataFlowsOut));

            Assert.Equal("z", GetSymbolNamesSortedAndJoined(analysis.ReadInside));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(analysis.ReadOutside));

            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.WrittenInside));
            Assert.Equal("i, x, z", GetSymbolNamesSortedAndJoined(analysis.WrittenOutside));
        }

        [Fact]
        public void TestObjectInitializerExpression_VariableCaptured()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
public class Foo
{
    public delegate int D();
    public D z;
}
public class MemberInitializerTest
{   
    public int x;
    public Foo y { get; set; }

    public static void Main()
    {
        int x = 0, z = 0;
        var i = new MemberInitializerTest() /*<bind>*/ { x = x, y =  { z = () => z } } /*</bind>*/;
    }
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
            Assert.Equal("z", GetSymbolNamesSortedAndJoined(analysis.Captured));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.VariablesDeclared));

            Assert.Equal("x, z", GetSymbolNamesSortedAndJoined(analysis.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.DataFlowsOut));

            Assert.Equal("x, z", GetSymbolNamesSortedAndJoined(analysis.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.ReadOutside));

            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.WrittenInside));
            Assert.Equal("i, x, z", GetSymbolNamesSortedAndJoined(analysis.WrittenOutside));
        }

        [Fact]
        public void TestCollectionInitializerExpression()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
using System;
using System.Collections.Generic;
using System.Collections;

class Test
{
    public static int Main()
    {
        List<int> list = /*<bind>*/ new List<int>() { 1, 2, 3, 4, 5 } /*</bind>*/;
        return 0;
    }
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.Captured));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.VariablesDeclared));

            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.DataFlowsOut));

            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.ReadOutside));

            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.WrittenInside));
            Assert.Equal("list", GetSymbolNamesSortedAndJoined(analysis.WrittenOutside));
        }

        [Fact]
        public void TestCollectionInitializerExpression_LocalAccessed()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
using System;
using System.Collections.Generic;
using System.Collections;

class Test
{
    public static int Main()
    {
        int x = 1;
        List<int> list = new List<int>() /*<bind>*/ { x } /*</bind>*/;
        return 0;
    }
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.Captured));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.VariablesDeclared));

            Assert.Equal("x", GetSymbolNamesSortedAndJoined(analysis.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.DataFlowsOut));

            Assert.Equal("x", GetSymbolNamesSortedAndJoined(analysis.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.ReadOutside));

            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.WrittenInside));
            Assert.Equal("list, x", GetSymbolNamesSortedAndJoined(analysis.WrittenOutside));
        }

        [Fact]
        public void TestCollectionInitializerExpression_ComplexElementInitializer()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
using System;
using System.Collections.Generic;
using System.Collections;

class Test
{
    public static int Main()
    {
        int x = 1;
        List<int> list = new List<int>() { /*<bind>*/ { x } /*</bind>*/ };
        return 0;
    }
}
");
            // Nice to have: "x" flows in, "x" read inside, "list, x" written outside.
            Assert.False(analysis.Succeeded);
        }

        [Fact]
        public void TestCollectionInitializerExpression_VariableCaptured()
        {
            var analysis = CompileAndAnalyzeDataFlowExpression(@"
using System;
using System.Collections.Generic;
using System.Collections;

class Test
{
    public delegate int D();
    public static int Main()
    {
        int x = 1;
        List<D> list = new List<D>() /*<bind>*/ { () => x } /*</bind>*/;
        return 0;
    }
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(analysis.Captured));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.VariablesDeclared));

            Assert.Equal("x", GetSymbolNamesSortedAndJoined(analysis.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.DataFlowsOut));

            Assert.Equal("x", GetSymbolNamesSortedAndJoined(analysis.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.ReadOutside));

            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.WrittenInside));
            Assert.Equal("list, x", GetSymbolNamesSortedAndJoined(analysis.WrittenOutside));
        }

        [Fact]
        public void ObjectInitializerInField()
        {
            var dataFlows = CompileAndAnalyzeDataFlowExpression(@"
using System;
using System.Collections.Generic;

class C {  public Func<int, int, int> dele;  }

public class Test
{
    C c = /*<bind>*/new C { dele = delegate(int x, int y) { return x + y; } }/*</bind>*/;
}
");
            Assert.Equal("x, y", GetSymbolNamesSortedAndJoined(dataFlows.VariablesDeclared));
            Assert.Null(GetSymbolNamesSortedAndJoined(dataFlows.AlwaysAssigned));
            Assert.Null(GetSymbolNamesSortedAndJoined(dataFlows.Captured));
            Assert.Null(GetSymbolNamesSortedAndJoined(dataFlows.DataFlowsIn));
            Assert.Null(GetSymbolNamesSortedAndJoined(dataFlows.DataFlowsOut));
            Assert.Equal("x, y", GetSymbolNamesSortedAndJoined(dataFlows.ReadInside));
            Assert.Equal("x, y", GetSymbolNamesSortedAndJoined(dataFlows.WrittenInside));
            Assert.Null(GetSymbolNamesSortedAndJoined(dataFlows.ReadOutside));
            Assert.Null(GetSymbolNamesSortedAndJoined(dataFlows.WrittenOutside));
        }

        [Fact]
        public void CollectionInitializerInField()
        {
            var dataFlows = CompileAndAnalyzeDataFlowExpression(@"
using System;
using System.Collections.Generic;

class C {  public Func<int, int, int> dele;  }

public class Test
{
    List<Func<int, int, int>> list = /*<bind>*/new List<Func<int, int, int>>() { (x, y) => { return x + y; } }/*</bind>*/;
}
");
            Assert.Equal("x, y", GetSymbolNamesSortedAndJoined(dataFlows.VariablesDeclared));
            Assert.Null(GetSymbolNamesSortedAndJoined(dataFlows.AlwaysAssigned));
            Assert.Null(GetSymbolNamesSortedAndJoined(dataFlows.Captured));
            Assert.Null(GetSymbolNamesSortedAndJoined(dataFlows.DataFlowsIn));
            Assert.Null(GetSymbolNamesSortedAndJoined(dataFlows.DataFlowsOut));
            Assert.Equal("x, y", GetSymbolNamesSortedAndJoined(dataFlows.ReadInside));
            Assert.Equal("x, y", GetSymbolNamesSortedAndJoined(dataFlows.WrittenInside));
            Assert.Null(GetSymbolNamesSortedAndJoined(dataFlows.ReadOutside));
            Assert.Null(GetSymbolNamesSortedAndJoined(dataFlows.WrittenOutside));
        }

        [Fact(), WorkItem(529329)]
        public void QueryAsFieldInitializer()
        {
            var dataFlows = CompileAndAnalyzeDataFlowExpression(@"
using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections;

class Test
{
    public IEnumerable e = /*<bind>*/
               from x in new[] { 1, 2, 3 }
               where BadExpression
               let y = x.ToString()
               select y /*</bind>*/;
}
");
            Assert.Equal("x, y", GetSymbolNamesSortedAndJoined(dataFlows.VariablesDeclared));
            Assert.Null(GetSymbolNamesSortedAndJoined(dataFlows.AlwaysAssigned));
            Assert.Equal("x, y", GetSymbolNamesSortedAndJoined(dataFlows.Captured));
            Assert.Null(GetSymbolNamesSortedAndJoined(dataFlows.DataFlowsIn));
            Assert.Null(GetSymbolNamesSortedAndJoined(dataFlows.DataFlowsOut));
            Assert.Equal("x, y", GetSymbolNamesSortedAndJoined(dataFlows.ReadInside));
            Assert.Equal("x, y", GetSymbolNamesSortedAndJoined(dataFlows.WrittenInside));
            Assert.Null(GetSymbolNamesSortedAndJoined(dataFlows.ReadOutside));
            Assert.Null(GetSymbolNamesSortedAndJoined(dataFlows.WrittenOutside));
        }

        [WorkItem(544361)]
        [Fact]
        public void FullQueryExpression()
        {
            var dataFlows = CompileAndAnalyzeDataFlowExpression(
@"using System.Linq;
 
class Program
{
    static void Main(string[] args)
    {
        var q = /*<bind>*/from arg in args
                group arg by arg.Length into final
                select final/*</bind>*/;
    }
}");
            Assert.Equal("args", GetSymbolNamesSortedAndJoined(dataFlows.DataFlowsIn));
        }

        [WorkItem(669341)]
        [Fact]
        public void ReceiverRead()
        {
            var dataFlows = CompileAndAnalyzeDataFlowExpression(
@"using System;

struct X
{
    public Y y;
}
struct Y
{
    public Z z;
}
struct Z
{
    public int Value;
}

class Test
{
    static void Main()
    {
        X x = new X();
        var value = /*<bind>*/x.y/*</bind>*/.z.Value;
    }
}");
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlows.ReadInside));
            Assert.Null(GetSymbolNamesSortedAndJoined(dataFlows.WrittenInside));
        }

        [WorkItem(669341)]
        [Fact]
        public void ReceiverWritten()
        {
            var dataFlows = CompileAndAnalyzeDataFlowExpression(
@"using System;

struct X
{
    public Y y;
}
struct Y
{
    public Z z;
}
struct Z
{
    public int Value;
}

class Test
{
    static void Main()
    {
        X x = new X();
        /*<bind>*/x.y/*</bind>*/.z.Value = 3;
    }
}");
            Assert.Null(GetSymbolNamesSortedAndJoined(dataFlows.ReadInside));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlows.WrittenInside));
        }

        [WorkItem(669341)]
        [Fact]
        public void ReceiverReadAndWritten()
        {
            var dataFlows = CompileAndAnalyzeDataFlowExpression(
@"using System;

struct X
{
    public Y y;
}
struct Y
{
    public Z z;
}
struct Z
{
    public int Value;
}

class Test
{
    static void Main()
    {
        X x = new X();
        /*<bind>*/x.y/*</bind>*/.z.Value += 3;
    }
}");
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlows.ReadInside));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlows.WrittenInside));
        }

        #endregion

        #region "Statements"

        [Fact]
        public void TestDataReadWrittenIncDecOperator()
        {
            var analysisResults = CompileAndAnalyzeControlAndDataFlowStatements(@"
class C {
    static short Main()
    {
        short x = 0, y = 1, z = 2;
/*<bind>*/
        x++; y--;
/*</bind>*/
        return y;
    }
}
");
            var controlFlowAnalysisResults = analysisResults.Item1;
            var dataFlowAnalysisResults = analysisResults.Item2;
            Assert.Equal(0, controlFlowAnalysisResults.EntryPoints.Count());
            Assert.Equal(0, controlFlowAnalysisResults.ExitPoints.Count());
            Assert.Equal(0, controlFlowAnalysisResults.ReturnStatements.Count());
            Assert.True(controlFlowAnalysisResults.StartPointIsReachable);
            Assert.True(controlFlowAnalysisResults.EndPointIsReachable);
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal("x, y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal("x, y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal("y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("x, y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal("y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal("x, y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("x, y, z", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [Fact]
        public void TestTernaryExpressionWithAssignments()
        {
            var analysisResults = CompileAndAnalyzeControlAndDataFlowStatements(@"
class C {
    static void Main()
    {
        bool x = true;
        int y;
/*<bind>*/
        int z = x ? y = 1 : y = 2;
/*</bind>*/
        y.ToString();
    }
}
");
            var controlFlowAnalysisResults = analysisResults.Item1;
            var dataFlowAnalysisResults = analysisResults.Item2;
            Assert.Equal(0, controlFlowAnalysisResults.EntryPoints.Count());
            Assert.Equal(0, controlFlowAnalysisResults.ExitPoints.Count());
            Assert.Equal(0, controlFlowAnalysisResults.ReturnStatements.Count());
            Assert.True(controlFlowAnalysisResults.EndPointIsReachable);
            Assert.Equal("z", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal("y, z", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal("y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal("y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal("y, z", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [WorkItem(542231)]
        [Fact]
        public void TestUnreachableRegion()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowStatements(
@"class C
{
    public static void Main(string[] args)
    {
        int i;
        return;
        /*<bind>*/
        i = i + 1;
        /*</bind>*/
        int j = i;
    }
}");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
        }

        [WorkItem(542231)]
        [Fact]
        public void TestUnreachableRegion2()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowStatements(
@"class C
{
    public static void Main(string[] args)
    {
        string i = 0, j = 0, k = 0, l = 0;
        goto l1;
        /*<bind>*/
        Console.WriteLine(i);
        j = 1;
l1:
        Console.WriteLine(j);
        k = 1;
        goto l2;
        Console.WriteLine(k);
        l = 1;
l3:
        Console.WriteLine(l);
        i = 1;
        /*</bind>*/
l2:
        Console.WriteLine(i + j + k + l);
        goto l3;
    }
}");
            Assert.Equal("j, l", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal("i, k", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
        }

        [WorkItem(542231)]
        [Fact]
        public void TestUnreachableRegionInExpression()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowExpression(
@"class C
{
    public static bool Main()
    {
        int i, j;
        return false && /*<bind>*/((i = i + 1) == 2 || (j = i) == 3)/*</bind>*/;
    }
}");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
        }

        [Fact]
        public void TestDeclarationWithSelfReferenceAndTernaryOperator()
        {
            var analysisResults = CompileAndAnalyzeControlAndDataFlowStatements(@"
class C {
    static void Main()
    {
/*<bind>*/
        int x = true ? 1 : x;
/*</bind>*/
    }
}
");
            var controlFlowAnalysisResults = analysisResults.Item1;
            var dataFlowAnalysisResults = analysisResults.Item2;
            Assert.Equal(0, controlFlowAnalysisResults.EntryPoints.Count());
            Assert.Equal(0, controlFlowAnalysisResults.ExitPoints.Count());
            Assert.Equal(0, controlFlowAnalysisResults.ReturnStatements.Count());
            Assert.True(controlFlowAnalysisResults.EndPointIsReachable);
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [Fact]
        public void TestDeclarationWithTernaryOperatorAndAssignment()
        {
            var analysisResults = CompileAndAnalyzeControlAndDataFlowStatements(@"
class C {
    static void Main()
    {
/*<bind>*/
        int x, z, y = true ? 1 : x = z;
/*</bind>*/
    }
}
");
            var controlFlowAnalysisResults = analysisResults.Item1;
            var dataFlowAnalysisResults = analysisResults.Item2;
            Assert.Equal(0, controlFlowAnalysisResults.EntryPoints.Count());
            Assert.Equal(0, controlFlowAnalysisResults.ExitPoints.Count());
            Assert.Equal(0, controlFlowAnalysisResults.ReturnStatements.Count());
            Assert.True(controlFlowAnalysisResults.EndPointIsReachable);
            Assert.Equal("x, y, z", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal("y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("z", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal("x, y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [WorkItem(542435)]
        [Fact]
        public void NullArgsToAnalyzeControlFlowStatements()
        {
            var compilation = CreateCompilationWithMscorlib(@"
class C
{
    static void Main()
    {
        int i = 10;
    }
}
");

            var semanticModel = compilation.GetSemanticModel(compilation.SyntaxTrees[0]);
            var statement = compilation.SyntaxTrees[0].GetCompilationUnitRoot().DescendantNodesAndSelf().OfType<StatementSyntax>().First();
            Assert.Throws<ArgumentNullException>(() => semanticModel.AnalyzeControlFlow(statement, null));
            Assert.Throws<ArgumentNullException>(() => semanticModel.AnalyzeControlFlow(null, statement));
            Assert.Throws<ArgumentNullException>(() => semanticModel.AnalyzeControlFlow(null));
            Assert.Throws<ArgumentNullException>(() => semanticModel.AnalyzeDataFlow(null, statement));
            Assert.Throws<ArgumentNullException>(() => semanticModel.AnalyzeDataFlow(statement, null));
            Assert.Throws<ArgumentNullException>(() => semanticModel.AnalyzeDataFlow((StatementSyntax)null));
        }

        [WorkItem(542507)]
        [Fact]
        public void DateFlowAnalyzeForLocalWithInvalidRHS()
        {
            // Case 1
            var analysis = CompileAndAnalyzeDataFlowStatements(@"
using System;

public class Test
{
    public delegate int D();
    public void foo(ref D d)
    {
/*<bind>*/
        d = { return 10;};
/*</bind>*/
    }
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.VariablesDeclared));

            // Case 2
            analysis = CompileAndAnalyzeDataFlowStatements(@"
using System;

public class Gen<T>
{
    public void DefaultTest()
    {
/*<bind>*/
        object obj = default (new Gen<T>());
/*</bind>*/
    }
}
");
            Assert.Equal("obj", GetSymbolNamesSortedAndJoined(analysis.VariablesDeclared));
        }

        [Fact]
        public void TestEntryPoints01()
        {
            var analysis = CompileAndAnalyzeControlFlowStatements(@"
class C {
    public void F()
    {
        goto L1; // 1
/*<bind>*/
        L1: ;
/*</bind>*/
        goto L1; // 2
    }
}");
            Assert.Equal(1, analysis.EntryPoints.Count());
        }

        [Fact]
        public void TestExitPoints01()
        {
            var analysis = CompileAndAnalyzeControlFlowStatements(@"
class C {
    public void F(int x)
    {
        L1: ; // 1
/*<bind>*/
        if (x == 0) goto L1;
        if (x == 1) goto L2;
        if (x == 3) goto L3;
        L3: ;
/*</bind>*/
        L2: ; // 2
    }
}");
            Assert.Equal(2, analysis.ExitPoints.Count());
        }

        [Fact]
        public void TestRegionCompletesNormally01()
        {
            var analysis = CompileAndAnalyzeControlFlowStatements(@"
class C {
    public void F(int x)
    {
/*<bind>*/
        goto L1;
/*</bind>*/
        L1: ;
    }
}");
            Assert.True(analysis.StartPointIsReachable);
            Assert.False(analysis.EndPointIsReachable);
        }

        [Fact]
        public void TestRegionCompletesNormally02()
        {
            var analysis = CompileAndAnalyzeControlFlowStatements(@"
class C {
    public void F(int x)
    {
/*<bind>*/
        x = 2;
/*</bind>*/
    }
}");
            Assert.True(analysis.EndPointIsReachable);
        }

        [Fact]
        public void TestRegionCompletesNormally03()
        {
            var analysis = CompileAndAnalyzeControlFlowStatements(@"
class C {
    public void F(int x)
    {
/*<bind>*/
        if (x == 0) return;
/*</bind>*/
    }
}");
            Assert.True(analysis.EndPointIsReachable);
            Assert.Equal(1, analysis.ExitPoints.Count());
        }

        [Fact]
        public void TestVariablesDeclared01()
        {
            var analysis = CompileAndAnalyzeDataFlowStatements(@"
class C {
    public void F(int x)
    {
        int a;
/*<bind>*/
        int b;
        int x, y = 1;
        { var z = ""a""; }
/*</bind>*/
        int c;
    }
}");
            Assert.Equal("b, x, y, z", GetSymbolNamesSortedAndJoined(analysis.VariablesDeclared));
        }

        [Fact]
        public void TestVariablesInitializedWithSelfReference()
        {
            var analysis = CompileAndAnalyzeDataFlowStatements(@"
class C {
    public void F(int x)
    {
/*<bind>*/
        int x = x = 1;
        int y, z = 1;
/*</bind>*/
    }
}");
            Assert.Equal("x, y, z", GetSymbolNamesSortedAndJoined(analysis.VariablesDeclared));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.DataFlowsOut));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.DataFlowsIn));
            Assert.Equal("x, z", GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
        }

        [Fact]
        public void AlwaysAssignedUnreachable()
        {
            var analysis = CompileAndAnalyzeDataFlowStatements(@"
class C {
    public void F(int x)
    {
        int y;
/*<bind>*/
        if (x == 1)
        {
            y = 2;
            return;
        }
        else
        {
            y = 3;
            throw new Exception();
        }
/*</bind>*/
        int = y;
    }
}");
            Assert.Equal("y", GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
        }

        [WorkItem(538170)]
        [Fact]
        public void TestVariablesDeclared02()
        {
            var analysis = CompileAndAnalyzeDataFlowStatements(@"
class C {
    public void F(int x)
/*<bind>*/
    {
        int a;
        int b;
        int x, y = 1;
        { string z = ""a""; }
        int c;
    }
/*</bind>*/
}");
            Assert.Equal("a, b, c, x, y, z", GetSymbolNamesSortedAndJoined(analysis.VariablesDeclared));
        }

        [WorkItem(541280)]
        [Fact]
        public void TestVariablesDeclared03()
        {
            var analysis = CompileAndAnalyzeDataFlowStatements(@"
class C {
    public void F()
/*<bind>*/
    {
        int a = 0;
        long a = 1;
    }
/*</bind>*/
}");
            Assert.Equal("a, a", GetSymbolNamesSortedAndJoined(analysis.VariablesDeclared));
            var intsym = analysis.VariablesDeclared.First() as LocalSymbol;
            var longsym = analysis.VariablesDeclared.Last() as LocalSymbol;
            Assert.Equal("Int32", intsym.Type.Name);
            Assert.Equal("Int64", longsym.Type.Name);
        }

        [WorkItem(539229)]
        [Fact]
        public void UnassignedVariableFlowsOut01()
        {
            var analysis = CompileAndAnalyzeDataFlowStatements(@"
class C {
    static void Main(string[] args)
    {
        int i = 10;
/*<bind>*/
        int j = j + i;
/*</bind>*/
        Console.Write(i);
        Console.Write(j); 
    }
}");
            Assert.Equal("j", GetSymbolNamesSortedAndJoined(analysis.VariablesDeclared));
            Assert.Equal("i", GetSymbolNamesSortedAndJoined(analysis.DataFlowsIn));
            Assert.Equal("j", GetSymbolNamesSortedAndJoined(analysis.DataFlowsOut));
            Assert.Equal("i, j", GetSymbolNamesSortedAndJoined(analysis.ReadInside));
            Assert.Equal("i, j", GetSymbolNamesSortedAndJoined(analysis.ReadOutside));
            Assert.Equal("j", GetSymbolNamesSortedAndJoined(analysis.WrittenInside));
            Assert.Equal("args, i", GetSymbolNamesSortedAndJoined(analysis.WrittenOutside));
        }

        [Fact]
        public void TestDataFlowsIn01()
        {
            var analysis = CompileAndAnalyzeDataFlowStatements(@"
class C {
    public void F(int x)
    {
        int a = 1, y = 2;
/*<bind>*/
        int b = a + x + 3;
/*</bind>*/
        int c = a + 4 + y;
    }
}");
            Assert.Equal("a, x", GetSymbolNamesSortedAndJoined(analysis.DataFlowsIn));
        }

        [Fact]
        public void TestOutParameter01()
        {
            var analysis = CompileAndAnalyzeDataFlowStatements(@"
class Program
{
    void Test<T>(out T t) where T : class, new()
    {
/*<bind>*/
        T t1;
        Test(out t1);
        t = t1;
/*</bind>*/
        System.Console.WriteLine(t1.ToString());
    }
}
");
            Assert.Equal("this", GetSymbolNamesSortedAndJoined(analysis.DataFlowsIn));
            Assert.Equal("t, t1", GetSymbolNamesSortedAndJoined(analysis.ReadOutside));
        }

        [Fact]
        public void TestDataFlowsOut01()
        {
            var analysis = CompileAndAnalyzeDataFlowStatements(@"
class C {
    public void F(int x)
    {
        int a = 1, y;
/*<bind>*/
        if (x == 1) y = x = 2;
/*</bind>*/
        int c = a + 4 + x + y;
    }
}");
            Assert.Equal("x, y", GetSymbolNamesSortedAndJoined(analysis.DataFlowsOut));
        }

        [WorkItem(538146)]
        [Fact]
        public void TestDataFlowsOut02()
        {
            var analysis = CompileAndAnalyzeDataFlowStatements(@"class Program
{
    void Test(string[] args)
    {
/*<bind>*/
        int s = 10, i = 1;
        int b = s + i;
/*</bind>*/
        System.Console.WriteLine(s);
        System.Console.WriteLine(i);
    }
}");
            Assert.Equal("i, s", GetSymbolNamesSortedAndJoined(analysis.DataFlowsOut));
        }

        [Fact]
        public void TestDataFlowsOut03()
        {
            var analysis = CompileAndAnalyzeDataFlowStatements(
@"using System.Text;
class Program
{
    private static string Main()
    {
        StringBuilder builder = new StringBuilder();
/*<bind>*/
        builder.Append(""Hello"");
        builder.Append("" From "");
        builder.Append("" Roslyn"");
/*</bind>*/
        return builder.ToString();
    }
}");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.DataFlowsOut));
        }

        [Fact]
        public void TestDataFlowsOut04()
        {
            var analysis = CompileAndAnalyzeDataFlowStatements(@"class Program
{
    void F(out int x)
    {
        /*<bind>*/
        x = 12;
        /*</bind>*/
    }
}");
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(analysis.DataFlowsOut));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(analysis.ReadOutside));
        }

        [Fact]
        public void TestDataFlowsOut05()
        {
            var analysis = CompileAndAnalyzeDataFlowStatements(@"class Program
{
    void F(out int x)
    {
        /*<bind>*/
        x = 12;
        return;
        /*</bind>*/
    }
}");
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(analysis.DataFlowsOut));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(analysis.ReadOutside));
        }

        [Fact]
        public void TestDataFlowsOut06()
        {
            var analysis = CompileAndAnalyzeDataFlowStatements(@"class Program
{
    void F(bool b)
    {
        int i = 1;
        while (b)
        {
            /*<bind>*/
            i = i + 1;
            /*</bind>*/
        }
    }
}");
            Assert.Equal("i", GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
            Assert.Equal("i", GetSymbolNamesSortedAndJoined(analysis.DataFlowsOut));
            Assert.Equal("b", GetSymbolNamesSortedAndJoined(analysis.ReadOutside));
        }

        [Fact]
        public void TestDataFlowsOut07()
        {
            var analysis = CompileAndAnalyzeDataFlowStatements(@"class Program
{
    void F(bool b)
    {
        int i;
        /*<bind>*/
        i = 2;
        goto next;
        /*</bind>*/
    next:
        int j = i;
    }
}");
            Assert.Equal("i", GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
            Assert.Equal("i", GetSymbolNamesSortedAndJoined(analysis.DataFlowsOut));
            Assert.Equal("i", GetSymbolNamesSortedAndJoined(analysis.ReadOutside));
        }

        [WorkItem(540793)]
        [Fact]
        public void TestDataFlowsOut08()
        {
            var analysis = CompileAndAnalyzeDataFlowStatements(@"class Program
{
    void F(bool b)
    {
        int i = 2;
        try
        {
            /*<bind>*/
            i = 1;
            /*</bind>*/
        }
        finally
        {
           int j = i;
        }
    }
}");
            Assert.Equal("i", GetSymbolNamesSortedAndJoined(analysis.DataFlowsOut));
        }

        [Fact]
        public void TestDataFlowsOut09()
        {
            var analysis = CompileAndAnalyzeDataFlowStatements(@"class Program
{
    void Test(string[] args)
    {
        int i;
        string s;

        /*<bind>*/i = 10;
        s = args[0] + i.ToString();/*</bind>*/
    }
}");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.DataFlowsOut));
        }

        [Fact]
        public void TestDataFlowsOut10()
        {
            var analysis = CompileAndAnalyzeDataFlowStatements(@"
class Program
{
    static void Main(string[] args)
    {
        int x = 10;
/*<bind>*/
        int y;
        if (x == 10)
            y = 5;
/*</bind>*/
        Console.WriteLine(y);
    }
}
");
            Assert.Equal("y", GetSymbolNamesSortedAndJoined(analysis.DataFlowsOut));
        }

        [Fact]
        public void TestAlwaysAssigned01()
        {
            var analysis = CompileAndAnalyzeDataFlowStatements(@"
class C {
    public void F(int x)
    {
        int a = 1, y = 1;
/*<bind>*/
        if (x == 2) a = 3; else a = 4;
        x = 4;
        if (x == 3) y = 12;
/*</bind>*/
        int c = a + 4 + y;
    }
}");
            Assert.Equal("a, x", GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
        }

        [Fact]
        public void TestAlwaysAssigned02()
        {
            var analysis = CompileAndAnalyzeDataFlowStatements(@"
class C {
    public void F(int x)
    {
/*<bind>*/
        const int a = 1;
/*</bind>*/
    }
}");
            Assert.Equal("a", GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
        }

        [WorkItem(540795)]
        [Fact]
        public void TestAlwaysAssigned03()
        {
            var analysis = CompileAndAnalyzeDataFlowStatements(@"
class Always {
    public void F()
    {
        ushort x = 0, y = 1, z;
/*<bind>*/
        x++;
        return;
        uint z = y;
/*</bind>*/
    }
}");
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
        }

        [Fact]
        public void TestReadInside01()
        {
            var analysis = CompileAndAnalyzeDataFlowStatements(@"
class Program
{
    void Test<T>(out T t) where T : class, new()
    {
/*<bind>*/
        T t1;
        Test(out t1);
        t = t1;
/*</bind>*/
        System.Console.WriteLine(t1.ToString());
    }
}
");
            Assert.Equal("t1, this", GetSymbolNamesSortedAndJoined(analysis.ReadInside));
        }

        [Fact]
        public void TestAlwaysAssignedDuplicateVariables()
        {
            var analysis = CompileAndAnalyzeDataFlowStatements(@"
class C {
    public void F(int x)
    {
/*<bind>*/
        int a, a, b, b;
        b = 1;
/*</bind>*/
    }
}");
            Assert.Equal("b", GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
        }

        [Fact]
        public void TestAccessedInsideOutside()
        {
            var analysis = CompileAndAnalyzeDataFlowStatements(@"
class C {
    public void F(int x)
    {
        int a, b, c, d, e, f, g, h, i;
        a = 1;
        c = b = a + x;
/*<bind>*/
        d = c;
        e = f = d;
/*</bind>*/
        g = e;
        h = i = g;
    }
}");
            Assert.Equal("c, d", GetSymbolNamesSortedAndJoined(analysis.ReadInside));
            Assert.Equal("d, e, f", GetSymbolNamesSortedAndJoined(analysis.WrittenInside));
            Assert.Equal("a, e, g, x", GetSymbolNamesSortedAndJoined(analysis.ReadOutside));
            Assert.Equal("a, b, c, g, h, i, this, x", GetSymbolNamesSortedAndJoined(analysis.WrittenOutside));
        }

        [Fact]
        public void TestAlwaysAssignedThroughParenthesizedExpression()
        {
            var analysis = CompileAndAnalyzeDataFlowStatements(@"
class C {
    public void F(int x)
    {
/*<bind>*/
        int a = 1, b, c, d, e;
        b = 2;
        (c) = 3;
        ((d)) = 4;
/*</bind>*/
    }
}");
            Assert.Equal("a, b, c, d", GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
        }

        [Fact]
        public void TestAlwaysAssignedThroughCheckedExpression()
        {
            var analysis = CompileAndAnalyzeDataFlowStatements(@"
class C {
    public void F(int x)
    {
/*<bind>*/
        int e, f, g;
        checked(e) = 5;
        (unchecked(f)) = 5;
/*</bind>*/
    }
}");
            Assert.Equal("e, f", GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
        }

        [Fact]
        public void TestAlwaysAssignedUsingAlternateNames()
        {
            var analysis = CompileAndAnalyzeDataFlowStatements(@"
class C {
    public void F(int x)
    {
/*<bind>*/
        int green, blue, red, yellow, brown;
        @green = 1;
        blu\u0065 = 2;
        re܏d = 3;
        yellow\uFFF9 = 4;
        @brown\uFFF9 = 5;
/*</bind>*/
    }
}");
            Assert.Equal("blue, brown, green, red, yellow", GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
        }

        [Fact]
        public void TestAlwaysAssignedViaPassingAsOutParameter()
        {
            var analysis = CompileAndAnalyzeDataFlowStatements(@"
class C {
    public void F(int x)
    {
/*<bind>*/
        int a;
        G(out a);
/*</bind>*/
    }

    void G(out int x) { x = 1; }
}");
            Assert.Equal("a", GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
        }

        [Fact]
        public void TestAlwaysAssignedWithExcludedAssignment()
        {
            var analysis = CompileAndAnalyzeDataFlowStatements(@"
partial class C
{
    public void F(int x)
    {
        /*<bind>*/
        int a, b;
        G(a = x = 1);
        H(b = 2);
        /*</bind>*/
    }

    partial void G(int x);
    partial void H(int x);
    partial void H(int x) { }
}");
            Assert.Equal("b", GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
        }

        [Fact]
        public void TestDeclarationWithSelfReference()
        {
            var analysisResults = CompileAndAnalyzeControlAndDataFlowStatements(@"
class C {
    static void Main()
    {
/*<bind>*/
        int x = x;
/*</bind>*/
    }
}
");
            var controlFlowAnalysisResults = analysisResults.Item1;
            var dataFlowAnalysisResults = analysisResults.Item2;
            Assert.Equal(0, controlFlowAnalysisResults.EntryPoints.Count());
            Assert.Equal(0, controlFlowAnalysisResults.ExitPoints.Count());
            Assert.Equal(0, controlFlowAnalysisResults.ReturnStatements.Count());
            Assert.True(controlFlowAnalysisResults.EndPointIsReachable);
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [Fact]
        public void TestIfStatementWithAssignments()
        {
            var analysisResults = CompileAndAnalyzeControlAndDataFlowStatements(@"
class C {
    static void Main()
    {
        bool x = true;
        int y;
/*<bind>*/
        if (x) y = 1; else y = 2;
/*</bind>*/
        y.ToString();
    }
}
");
            var controlFlowAnalysisResults = analysisResults.Item1;
            var dataFlowAnalysisResults = analysisResults.Item2;
            Assert.Equal(0, controlFlowAnalysisResults.EntryPoints.Count());
            Assert.Equal(0, controlFlowAnalysisResults.ExitPoints.Count());
            Assert.Equal(0, controlFlowAnalysisResults.ReturnStatements.Count());
            Assert.True(controlFlowAnalysisResults.EndPointIsReachable);
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal("y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal("y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal("y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal("y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [Fact]
        public void TestIfStatementWithConstantCondition()
        {
            var analysisResults = CompileAndAnalyzeControlAndDataFlowStatements(@"
class C {
    static void Main()
    {
        bool x = true;
        int y;
/*<bind>*/
        if (true) y = x;
/*</bind>*/
        y.ToString();
    }
}
");
            var controlFlowAnalysisResults = analysisResults.Item1;
            var dataFlowAnalysisResults = analysisResults.Item2;
            Assert.Equal(0, controlFlowAnalysisResults.EntryPoints.Count());
            Assert.Equal(0, controlFlowAnalysisResults.ExitPoints.Count());
            Assert.Equal(0, controlFlowAnalysisResults.ReturnStatements.Count());
            Assert.True(controlFlowAnalysisResults.EndPointIsReachable);
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal("y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal("y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal("y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal("y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [Fact]
        public void TestIfStatementWithNonConstantCondition()
        {
            var analysisResults = CompileAndAnalyzeControlAndDataFlowStatements(@"
class C {
    static void Main()
    {
        bool x = true;
        int y;
/*<bind>*/
        if (true | x) y = x;
/*</bind>*/
        y.ToString();
    }
}
");
            var controlFlowAnalysisResults = analysisResults.Item1;
            var dataFlowAnalysisResults = analysisResults.Item2;
            Assert.Equal(0, controlFlowAnalysisResults.EntryPoints.Count());
            Assert.Equal(0, controlFlowAnalysisResults.ExitPoints.Count());
            Assert.Equal(0, controlFlowAnalysisResults.ReturnStatements.Count());
            Assert.True(controlFlowAnalysisResults.EndPointIsReachable);
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal("y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal("y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal("y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        //        [Obsolete]
        //        [Fact]
        //        public void TestNonStatementSelection()
        //        {
        //            var analysisResults = CompileAndAnalyzeControlAndDataFlowRegion(@"
        //class C {
        //    static void Main()
        //    {
        //        
        // /*<bind>*/
        //int
        // /*</bind>*/
        // x = 1;
        //    }
        //}
        //");
        //            var controlFlowAnalysisResults = analysisResults.Item1;
        //            var dataFlowAnalysisResults = analysisResults.Item2;
        //            Assert.True(controlFlowAnalysisResults.Succeeded);
        //            Assert.True(dataFlowAnalysisResults.Succeeded);
        //            Assert.Equal(0, controlFlowAnalysisResults.EntryPoints.Count());
        //            Assert.Equal(0, controlFlowAnalysisResults.ExitPoints.Count());
        //            Assert.Equal(0, controlFlowAnalysisResults.ReturnStatements.Count());
        //            Assert.True(controlFlowAnalysisResults.EndPointIsReachable);
        //            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
        //            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
        //            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
        //            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
        //            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
        //            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
        //            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        //        }

        [Fact]
        public void TestInvocation()
        {
            var analysisResults = CompileAndAnalyzeControlAndDataFlowStatements(@"
class C {
    static void Main()
    {
        int x = 1, y = 1;
/*<bind>*/
        Foo(x);
/*</bind>*/
    }

    static void Foo(int x) { }
}
");
            var controlFlowAnalysisResults = analysisResults.Item1;
            var dataFlowAnalysisResults = analysisResults.Item2;
            Assert.Equal(0, controlFlowAnalysisResults.EntryPoints.Count());
            Assert.Equal(0, controlFlowAnalysisResults.ExitPoints.Count());
            Assert.Equal(0, controlFlowAnalysisResults.ReturnStatements.Count());
            Assert.True(controlFlowAnalysisResults.EndPointIsReachable);
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("x, y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [Fact]
        public void TestInvocationWithAssignmentInArguments()
        {
            var analysisResults = CompileAndAnalyzeControlAndDataFlowStatements(@"
class C {
    static void Main()
    {
        int x = 1, y = 1;
/*<bind>*/
        Foo(x = y, y = 2);
/*</bind>*/
        int z = x + y;
    }

    static void Foo(int x, int y) { }
}
");
            var controlFlowAnalysisResults = analysisResults.Item1;
            var dataFlowAnalysisResults = analysisResults.Item2;
            Assert.Equal(0, controlFlowAnalysisResults.EntryPoints.Count());
            Assert.Equal(0, controlFlowAnalysisResults.ExitPoints.Count());
            Assert.Equal(0, controlFlowAnalysisResults.ReturnStatements.Count());
            Assert.True(controlFlowAnalysisResults.EndPointIsReachable);
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal("x, y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal("y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal("x, y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal("x, y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal("x, y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("x, y, z", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [Fact, WorkItem(538979)]
        public void AssertFromInvalidLocalDeclaration()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowExpression(@"
using System;
public class MyClass
{
    public static int Main()
    {
       variant /*<bind>*/ v = new byte(2) /*</bind>*/;   // CS0246
        byte b = v;              // CS1729
        return 1;
    }
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
        }

        [Fact, WorkItem(538979)]
        public void AssertFromInvalidKeywordAsExpr()
        {
            var analysisResults = CompileAndAnalyzeControlAndDataFlowStatements(@"
class B : A
{
    public float M()
    {
/*<bind>*/
        {
            return base; // CS0175
        }
/*</bind>*/
    }
}

class A {}
");
            var controlFlowAnalysisResults = analysisResults.Item1;
            //var dataFlowAnalysisResults = analysisResults.Item2;
            Assert.Equal(0, controlFlowAnalysisResults.EntryPoints.Count());
            Assert.Equal(1, controlFlowAnalysisResults.ExitPoints.Count());
            Assert.False(controlFlowAnalysisResults.EndPointIsReachable);
        }

        [WorkItem(539071)]
        [Fact]
        public void AssertFromFoldConstantEnumConversion()
        {
            var analysisResults = CompileAndAnalyzeControlAndDataFlowStatements(@"
enum E { x, y, z }

class Test
{
    static int Main()
    {
/*<bind>*/
        E v = E.x;
        if (v != (E)((int)E.z - 1))
            return 0;
/*</bind>*/
        return 1;
    }
}
");
            var controlFlowAnalysisResults = analysisResults.Item1;
            //var dataFlowAnalysisResults = analysisResults.Item2;
            Assert.Equal(0, controlFlowAnalysisResults.EntryPoints.Count());
            Assert.Equal(1, controlFlowAnalysisResults.ExitPoints.Count());
            Assert.True(controlFlowAnalysisResults.EndPointIsReachable);
        }

        [Fact]
        public void ByRefParameterNotInAppropriateCollections2()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowStatements(@"
class Program
{
    void Test<T>(ref T t)
    {
/*<bind>*/
        T t1 = GetValue<T>(ref t);
/*</bind>*/
        System.Console.WriteLine(t1.ToString());
    }
    T GetValue<T>(ref T t)
    {
        return t;
    }
}
");
            Assert.Equal("t1", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal("t1", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal("t, this", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal("t, t1", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("t, this", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal("t, t1", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal("t, t1", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("t, this", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [Fact]
        public void UnreachableDeclaration()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowStatements(@"
class Program
{
    void F()
    {
/*<bind>*/
        int x;
/*</bind>*/
        System.Console.WriteLine(x);
    }
}
");
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("this", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [Fact]
        public void Parameters01()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowStatements(@"
class Program
{
    void F(int x, ref int y, out int z)
    {
/*<bind>*/
        y = z = 3;
/*</bind>*/
    }
}
");
            Assert.Equal("y, z", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal("y, z", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("y, z", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal("this, x, y", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [WorkItem(528308)]
        [Fact]
        public void RegionForIfElseIfWithoutElse()
        {
            var analysisResults = CompileAndAnalyzeControlAndDataFlowStatements(@"
using System;
public class Test
{
    ushort TestCase(ushort p)
    {
        /*<bind>*/
        if (p > 0)
        {
            return --p;
        }
        else if (p < 0)
        {
            return ++p;
        }
        /*</bind>*/
        // else
        {
            return 0;
        }
    }
}
");
            var controlFlowAnalysisResults = analysisResults.Item1;
            var dataFlowAnalysisResults = analysisResults.Item2;
            Assert.Empty(controlFlowAnalysisResults.EntryPoints);
            Assert.Equal(2, controlFlowAnalysisResults.ExitPoints.Count());
            Assert.True(controlFlowAnalysisResults.EndPointIsReachable);
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal("p", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("p", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal("p", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("p, this", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        //        [Obsolete]
        //        [Fact]
        //        public void TestBadRegion()
        //        {
        //            var analysisResults = CompileAndAnalyzeControlAndDataFlowRegion(@"
        //class C {
        //    static void Main()
        //    {
        //        int a = 1;
        //        int b = 1;
        // 
        //        if(a > 1)
        // /*<bind>*/
        //            a = 1;
        //        b = 2;
        // /*</bind>*/
        //    }
        //}
        //");
        //            var controlFlowAnalysisResults = analysisResults.Item1;
        //            var dataFlowAnalysisResults = analysisResults.Item2;
        //            Assert.False(controlFlowAnalysisResults.Succeeded);
        //            Assert.False(dataFlowAnalysisResults.Succeeded);
        //            Assert.Equal(0, controlFlowAnalysisResults.EntryPoints.Count());
        //            Assert.Equal(0, controlFlowAnalysisResults.ExitPoints.Count());
        //            Assert.Equal(0, controlFlowAnalysisResults.ReturnStatements.Count());
        //            Assert.True(controlFlowAnalysisResults.EndPointIsReachable);
        //            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
        //            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
        //            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
        //            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
        //            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
        //            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
        //            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
        //            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        //        }

        [WorkItem(541331)]
        [Fact]
        public void AttributeOnAccessorInvalid()
        {
            var analysisResults = CompileAndAnalyzeControlAndDataFlowStatements(@"
using System;

public class C
{
    public class AttributeX : Attribute { }

    public int Prop
    {
        get /*<bind>*/{ return 1; }/*</bind>*/
        protected [AttributeX] set { }
    }
}
");
            var controlFlowAnalysisResults = analysisResults.Item1;
            Assert.Empty(controlFlowAnalysisResults.EntryPoints);
            Assert.Equal(1, controlFlowAnalysisResults.ExitPoints.Count());
        }

        [WorkItem(541585)]
        [Fact]
        public void BadAssignThis()
        {
            var analysisResults = CompileAndAnalyzeControlAndDataFlowStatements(@"
class Program
{
    static void Main(string[] args)
    {
        /*<bind>*/
         this = new S();
        /*</bind>*/
    }
}
 
struct S
{
}");
            var controlFlowAnalysisResults = analysisResults.Item1;
            var dataFlowAnalysisResults = analysisResults.Item2;
            Assert.Equal(0, controlFlowAnalysisResults.EntryPoints.Count());
            Assert.Equal(0, controlFlowAnalysisResults.ExitPoints.Count());
            Assert.Equal(0, controlFlowAnalysisResults.ReturnStatements.Count());
            Assert.True(controlFlowAnalysisResults.EndPointIsReachable);
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("args", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [WorkItem(528623)]
        [Fact]
        public void TestElementAccess01()
        {
            var analysis = CompileAndAnalyzeDataFlowStatements(@"
public class Test 
{
    public void M(long[] p)
    { 
        var v = new long[] { 1, 2, 3 };
/*<bind>*/
        v[0] = p[0];
        p[0] = v[1];
/*</bind>*/
        v[1] = v[0];
        p[2] = p[0];
    }    
}
");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.VariablesDeclared));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
            Assert.Equal("p, v", GetSymbolNamesSortedAndJoined(analysis.DataFlowsIn));
            // By Design
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.DataFlowsOut));
            Assert.Equal("p, v", GetSymbolNamesSortedAndJoined(analysis.ReadInside));
            // By Design
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.WrittenInside));
            Assert.Equal("p, v", GetSymbolNamesSortedAndJoined(analysis.ReadOutside));
            Assert.Equal("p, this, v", GetSymbolNamesSortedAndJoined(analysis.WrittenOutside));
        }

        [WorkItem(541947)]
        [Fact]
        public void BindPropertyAccessorBody()
        {
            var results = CompileAndAnalyzeControlAndDataFlowStatements(@"
using System;
public class A
{
     public int P
     {
       get /*<bind>*/ { return 0; } /*</bind>*/
     }
}
");

            var ctrlFlows = results.Item1;
            var dataFlows = results.Item2;

            Assert.False(ctrlFlows.EndPointIsReachable);
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlows.VariablesDeclared));
        }

        [WorkItem(8926, "DevDiv_Projects/Roslyn")]
        [WorkItem(542346)]
        [WorkItem(528775)]
        [Fact]
        public void BindEventAccessorBody()
        {
            var results = CompileAndAnalyzeControlAndDataFlowStatements(@"
using System;
public class A
{
    public delegate void D();
    public event D E
    {
        add { /*NA*/ }
        remove /*<bind>*/ { /*NA*/ } /*</bind>*/
    }
}
");

            var ctrlFlows = results.Item1;
            var dataFlows = results.Item2;

            Assert.True(ctrlFlows.EndPointIsReachable);
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlows.VariablesDeclared));
        }

        [WorkItem(541980)]
        [Fact]
        public void BindDuplicatedAccessor()
        {
            var results = CompileAndAnalyzeControlAndDataFlowStatements(@"
using System;
public class A
{
    public int P
    {
        get { return 1;}
        get /*<bind>*/ { return 0; } /*</bind>*/
    }
}
");

            var ctrlFlows = results.Item1;
            var dataFlows = results.Item2;

            var tmp = ctrlFlows.EndPointIsReachable; // ensure no exception thrown
            Assert.Empty(dataFlows.VariablesDeclared);
        }

        [WorkItem(543737)]
        [Fact]
        public void BlockSyntaxInAttributeDecl()
        {
            {
                var compilation = CreateCompilationWithMscorlib(@"
[Attribute(delegate.Class)] 
public class C {
  public static int Main () {
    return 1;
  }
}
");
                var tree = compilation.SyntaxTrees.First();
                var index = tree.GetCompilationUnitRoot().ToFullString().IndexOf(".Class)");
                var tok = tree.GetCompilationUnitRoot().FindToken(index);
                var node = tok.Parent as StatementSyntax;
                var model = compilation.GetSemanticModel(tree);
                var controlFlowAnalaysis = model.AnalyzeControlFlow(node, node);

                Assert.False(controlFlowAnalaysis.Succeeded);
            }
            {
                var results = CompileAndAnalyzeControlAndDataFlowStatements(@"
[Attribute(x => { /*<bind>*/int y = 12;/*</bind>*/ })] 
public class C {
  public static int Main () {
    return 1;
  }
}
");
                Assert.False(results.Item1.Succeeded);
                Assert.False(results.Item2.Succeeded);
            }
        }

        [Fact, WorkItem(529273)]
        public void IncrementDecrementOnNullable()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowStatements(@"
class C
{
    void M(ref sbyte p1, ref sbyte? p2)
    {
        byte? local_0 = 2;
        short? local_1;
        ushort non_nullable = 99;

        /*<bind>*/
        p1++;
        p2 = (sbyte?) (local_0.Value - 1);
        local_1 = (byte)(p2.Value + 1);
        var ret = local_1.HasValue ? local_1.Value : 0;
        --non_nullable;
        /*</bind>*/
    }
}
");
            Assert.Equal("ret", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal("local_1, non_nullable, p1, p2, ret", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal("local_0, non_nullable, p1", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal("p1, p2", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("local_0, local_1, non_nullable, p1, p2", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal("p1, p2", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal("local_1, non_nullable, p1, p2, ret", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("local_0, non_nullable, p1, p2, this", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        #endregion

        #region "lambda"

        [Fact]
        public void TestReturnStatements03()
        {
            var analysis = CompileAndAnalyzeControlFlowStatements(@"
using System;
class C {
    public void F(int x)
    {
        if (x == 0) return;
/*<bind>*/
        if (x == 1) return;
        Func<int,int> f = (int i) => { return i+1; };
        if (x == 2) return;
/*</bind>*/
    }
}");
            Assert.Equal(2, analysis.ExitPoints.Count());
        }

        [Fact]
        public void TestReturnStatements04()
        {
            var analysis = CompileAndAnalyzeControlFlowStatements(@"
using System;
class C {
    public void F(int x)
    {
        if (x == 0) return;
        if (x == 1) return;
        Func<int,int> f = (int i) =>
        {
/*<bind>*/
            return i+1;
/*</bind>*/
        }
        ;
        if (x == 2) return;
    }
}");
            Assert.Equal(1, analysis.ExitPoints.Count());
        }

        [Fact]
        public void TestReturnStatements05()
        {
            var analysis = CompileAndAnalyzeControlFlowStatements(@"
using System;
class C {
    public void F(int x)
    {
        if (x == 0) return;
        if (x == 1) return;
/*<bind>*/
        Func<int,int?> f = (int i) =>
        {
            return i == 1 ? i+1 : null;
        }
        ;
/*</bind>*/
        if (x == 2) return; 
    }
}");
            Assert.True(analysis.Succeeded);
            Assert.Empty(analysis.ReturnStatements);
        }

        [Fact]
        public void TestReturnStatements06()
        {
            var analysis = CompileAndAnalyzeControlFlowStatements(@"
        using System;
        class C {
            public void F(uint? x)
            {
                if (x == null) return;
                if (x.Value == 1) return;
        /*<bind>*/
                Func<uint?, ulong?> f = (i) =>
                {
                    return i.Value +1;
                }
                ;
                if (x.Value == 2) return;
        /*</bind>*/
            }
        }");

            Assert.True(analysis.Succeeded);
            Assert.Equal(1, analysis.ExitPoints.Count());
        }

        [WorkItem(541198)]
        [Fact]
        public void TestReturnStatements07()
        {
            var analysis = CompileAndAnalyzeControlFlowStatements(@"
using System;
class C {
    public int F(int x)
    {
        Func<int,int> f = (int i) =>
        {
        goto XXX;
/*<bind>*/
        return 1;
/*</bind>*/
        }
        ;
    }
}");
            Assert.Equal(1, analysis.ExitPoints.Count());
        }

        [Fact]
        public void TestReturnFromLambda()
        {
            var analysisResults = CompileAndAnalyzeControlAndDataFlowStatements(@"
using System;
using System.Linq;
class Program
{
    static void Main(string[] args)
    {
        int i = 1;
        Func<int> lambda = () => { /*<bind>*/return i;/*</bind>*/ };
    }
}
");
            var controlFlowAnalysisResults = analysisResults.Item1;
            var dataFlowAnalysisResults = analysisResults.Item2;
            Assert.Equal(0, controlFlowAnalysisResults.EntryPoints.Count());
            Assert.Equal(1, controlFlowAnalysisResults.ExitPoints.Count());
            Assert.False(controlFlowAnalysisResults.EndPointIsReachable);
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal("i", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("i", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("args, i, lambda", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [Fact]
        public void DataFlowsOutLambda01()
        {
            var analysisResults = CompileAndAnalyzeControlAndDataFlowStatements(@"
using System;

delegate void D();
class Program
{
    static void Main(string[] args)
    {
        int i = 12;
        D d = () => {
            /*<bind>*/
            i = 14;
            return;
            /*</bind>*/
        };
        int j = i;
    }
}
");
            //var controlFlowAnalysisResults = analysisResults.Item1;
            var dataFlowAnalysisResults = analysisResults.Item2;
            Assert.Equal("i", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
        }

        [Fact]
        public void DataFlowsOutLambda02()
        {
            var analysisResults = CompileAndAnalyzeControlAndDataFlowStatements(@"
using System;

delegate void D();
class Program
{
    static void Main()
    {
        int? i = 12;
        D d = () => {
            /*<bind>*/
            i = 14;
            /*</bind>*/
            return;
        };
        int j = i.Value;
    }
}
");
            //var controlFlowAnalysisResults = analysisResults.Item1;
            var dataFlowAnalysisResults = analysisResults.Item2;
            Assert.Equal("i", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
        }

        [Fact]
        public void DataFlowsOutLambda03()
        {
            var analysisResults = CompileAndAnalyzeControlAndDataFlowStatements(@"
using System;

delegate void D();
class Program
{
    static void Main(string[] args)
    {
        int i = 12;
        D d = () => {
            /*<bind>*/
            i = 14;
            /*</bind>*/
        };
        int j = i;
    }
}
");
            //var controlFlowAnalysisResults = analysisResults.Item1;
            var dataFlowAnalysisResults = analysisResults.Item2;
            Assert.Equal("i", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
        }

        [WorkItem(538984)]
        [Fact]
        public void TestReadInside02()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowExpression(@"
class A
{
    void Method()
    {
        System.Func<int, int> a = x => /*<bind>*/x * x/*</bind>*/;
    }
}");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("a, this, x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [Fact]
        public void TestCaptured02()
        {
            var analysis = CompileAndAnalyzeDataFlowStatements(@"
using System;
class C
{
    int field = 123;
    public void F(int x)
    {
        const int a = 1, y = 1;
/*<bind>*/
        Func<int> lambda = () => x + y + field;
/*</bind>*/
        int c = a + 4 + y;
    }
}");
            Assert.Equal("this, x", GetSymbolNamesSortedAndJoined(analysis.Captured));
            Assert.Equal("this, x, y", GetSymbolNamesSortedAndJoined(analysis.DataFlowsIn));
        }

        [Fact, WorkItem(539648), WorkItem(529185)]
        public void ReturnsInsideLambda()
        {
            var analysisResults = CompileAndAnalyzeControlAndDataFlowStatements(@"
using System;
class Program
{
    delegate R Func<T, R>(T t);
    static void Main(string[] args)
    {
        /*<bind>*/
        Func<int, int> f = (arg) =>
        {
            int s = 3;
            return s;
        };
        /*</bind>*/
        f.Invoke(2);
    }
}");
            var controlFlowAnalysisResults = analysisResults.Item1;
            var dataFlowAnalysisResults = analysisResults.Item2;
            Assert.Empty(controlFlowAnalysisResults.ReturnStatements);
            Assert.Equal("f", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("arg, f, s", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
        }

        [WorkItem(539861)]
        [Fact]
        public void VariableDeclaredLambda01()
        {
            var analysisResults = CompileAndAnalyzeControlAndDataFlowStatements(@"
using System;
class Program
{
     delegate void TestDelegate(ref int x);
     static void Main(string[] args)
    {
        /*<bind>*/
        TestDelegate testDel = (ref int x) => {  };
        /*</bind>*/
        int p = 2;
        testDel(ref p);
        Console.WriteLine(p);
    }
}
");
            //var controlFlowAnalysisResults = analysisResults.Item1;
            var dataFlowAnalysisResults = analysisResults.Item2;
            Assert.Equal("testDel, x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal("testDel, x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
        }

        [WorkItem(539861)]
        [Fact]
        public void VariableDeclaredLambda02()
        {
            var results1 = CompileAndAnalyzeDataFlowStatements(@"
using System;
class Program
{
    delegate void TestDelegate(ref int? x);
    static void Main()
    {
        /*<bind>*/
        TestDelegate testDel = (ref int? x) => { int y = x; x.Value = 10; };
        /*</bind>*/
        int? p = 2;
        testDel(ref p);
        Console.WriteLine(p);
    }
}
");

            Assert.Equal("testDel, x, y", GetSymbolNamesSortedAndJoined(results1.VariablesDeclared));
            Assert.Equal("testDel", GetSymbolNamesSortedAndJoined(results1.AlwaysAssigned));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(results1.Captured));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(results1.DataFlowsIn));
            Assert.Equal("testDel", GetSymbolNamesSortedAndJoined(results1.DataFlowsOut));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(results1.ReadInside));
            Assert.Equal("p, testDel", GetSymbolNamesSortedAndJoined(results1.ReadOutside));
            Assert.Equal("testDel, x, y", GetSymbolNamesSortedAndJoined(results1.WrittenInside));
            Assert.Equal("p", GetSymbolNamesSortedAndJoined(results1.WrittenOutside));
        }

        [WorkItem(540449)]
        [Fact]
        public void AnalysisInsideLambdas()
        {
            var results1 = CompileAndAnalyzeDataFlowExpression(@"
using System;
class C {
    static void Main()
    {
        Func<int, int> f = p => 
        {
            int x = 1;
            int y = 1;
            return /*<bind>*/1 + (x=2) + p + y/*</bind>*/;
        };
    }
}
");

            Assert.Equal("x", GetSymbolNamesSortedAndJoined(results1.AlwaysAssigned));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(results1.Captured));
            Assert.Equal("p, y", GetSymbolNamesSortedAndJoined(results1.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(results1.DataFlowsOut));
            Assert.Equal("p, y", GetSymbolNamesSortedAndJoined(results1.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(results1.ReadOutside));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(results1.WrittenInside));
            Assert.Equal("f, p, x, y", GetSymbolNamesSortedAndJoined(results1.WrittenOutside));
        }

        [WorkItem(528622)]
        [Fact]
        public void AlwayaAssignedParameterLambda()
        {
            var dataFlows = CompileAndAnalyzeDataFlowExpression(@"
using System;

internal class Test
{
    void M(sbyte[] ary)
    {
        /*<bind>*/
        ( (Action<short>)(x => { Console.Write(x); }) 
        )(ary[0])/*</bind>*/;
    }
}
");

            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlows.AlwaysAssigned));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlows.Captured));
            Assert.Equal("ary", GetSymbolNamesSortedAndJoined(dataFlows.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlows.DataFlowsOut));
            Assert.Equal("ary, x", GetSymbolNamesSortedAndJoined(dataFlows.ReadInside));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlows.WrittenInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlows.ReadOutside));
            Assert.Equal("ary, this", GetSymbolNamesSortedAndJoined(dataFlows.WrittenOutside));
        }

        [WorkItem(541946)]
        [Fact]
        public void LambdaInTenaryWithEmptyBody()
        {
            var results = CompileAndAnalyzeControlAndDataFlowStatements(@"
using System;

public delegate void D();
public class A
{
    void M()
    {
        int i = 0;
/*<bind>*/
        D d = true ? (D)delegate { i++; } : delegate {  };
/*</bind>*/
    }
}
");

            var ctrlFlows = results.Item1;
            var dataFlows = results.Item2;

            Assert.True(ctrlFlows.EndPointIsReachable);
            Assert.Equal("d", GetSymbolNamesSortedAndJoined(dataFlows.VariablesDeclared));
            Assert.Equal("d", GetSymbolNamesSortedAndJoined(dataFlows.AlwaysAssigned));
            Assert.Equal("i", GetSymbolNamesSortedAndJoined(dataFlows.Captured));
            Assert.Equal("i", GetSymbolNamesSortedAndJoined(dataFlows.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlows.DataFlowsOut));
            Assert.Equal("i", GetSymbolNamesSortedAndJoined(dataFlows.ReadInside));
            Assert.Equal("d, i", GetSymbolNamesSortedAndJoined(dataFlows.WrittenInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlows.ReadOutside));
            Assert.Equal("i, this", GetSymbolNamesSortedAndJoined(dataFlows.WrittenOutside));
        }

        [Fact]
        public void ForEachVariableInLambda()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowExpression(@"
using System;
class Program
{
    static void Main()
    {
        var nums = new int?[] { 4, 5 };

        foreach (var num in /*<bind>*/nums/*</bind>*/)
        {
            Func<int, int> f = x => x + num.Value;
            Console.WriteLine(f(0));
        }
    }
}");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal("num", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.Captured));
            Assert.Equal("nums", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("nums", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal("f, num, x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("f, num, nums, x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [WorkItem(543398)]
        [Fact]
        public void LambdaBlockSyntax()
        {
            var source = @"
using System;
class c1
{
    void M()
    {
        var a = 0;

        foreach(var l in """")
        {
            Console.WriteLine(l);
            a = (int) l;
            l = (char) a;
        }

        Func<int> f = ()=>
        {
            var c = a; a = c; return 0;
        };

        var b = 0;
        Console.WriteLine(b);
    }
}";
            var tree = SyntaxFactory.ParseSyntaxTree(source);
            var comp = CSharpCompilation.Create("FlowAnalysis", syntaxTrees: new[] { tree });
            var model = comp.GetSemanticModel(tree);

            var methodBlock = tree.GetCompilationUnitRoot().DescendantNodes().OfType<BlockSyntax>().First();
            var foreachStatement = methodBlock.DescendantNodes().OfType<ForEachStatementSyntax>().First();
            var foreachBlock = foreachStatement.DescendantNodes().OfType<BlockSyntax>().First();
            var lambdaExpression = methodBlock.DescendantNodes().OfType<ParenthesizedLambdaExpressionSyntax>().First();
            var lambdaBlock = lambdaExpression.DescendantNodes().OfType<BlockSyntax>().First();

            var flowAnalysis = model.AnalyzeDataFlow(methodBlock);
            Assert.Equal(4, flowAnalysis.ReadInside.Count());
            Assert.Equal(5, flowAnalysis.WrittenInside.Count());
            Assert.Equal(5, flowAnalysis.VariablesDeclared.Count());

            flowAnalysis = model.AnalyzeDataFlow(foreachBlock);
            Assert.Equal(2, flowAnalysis.ReadInside.Count());
            Assert.Equal(2, flowAnalysis.WrittenInside.Count());
            Assert.Equal(0, flowAnalysis.VariablesDeclared.Count());

            flowAnalysis = model.AnalyzeDataFlow(lambdaBlock);
            Assert.Equal(2, flowAnalysis.ReadInside.Count());
            Assert.Equal(2, flowAnalysis.WrittenInside.Count());
            Assert.Equal(1, flowAnalysis.VariablesDeclared.Count());
        }

        #endregion

        #region "query expressions"

        [Fact]
        public void QueryExpression01()
        {
            var analysisResults = CompileAndAnalyzeControlAndDataFlowStatements(@"
using System;
using System.Linq;

class Program
{
    static void Main()
    {
        var nums = new int[] { 1, 2, 3, 4 };
/*<bind>*/
        var q2 = from x in nums
                where (x > 2)
                where x > 3
                select x;
/*</bind>*/
    }
}");
            var dataFlowAnalysisResults = analysisResults.Item2;
            Assert.Equal("q2, x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal("q2", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal("nums", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("nums, x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal("q2, x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("nums", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [Fact]
        public void QueryExpression02()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowExpression(@"
using System;
using System.Linq;

class Program
{
    static void Main()
    {
        var nums = new sbyte[] { 1, 2, 3, 4 };

        var q2 = from x in nums
                where (x > 2)
                select /*<bind>*/ x+1 /*</bind>*/;
    }
}");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal("nums, x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("nums, q2, x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [Fact]
        public void QueryExpression03()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowExpression(@"
using System;
using System.Linq;

class Program
{
    static void Main()
    {
        var nums = new int?[] { 1, 2, null, 4 };
        var q2 = from x in nums
                 group x.Value + 1 by /*<bind>*/ x.Value % 2 /*</bind>*/;
    }
}");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal("nums, x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("nums, q2, x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [Fact]
        public void QueryExpression04()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowExpression(@"
using System;
using System.Linq;

class Program
{
    static void Main()
    {
        var nums = new uint[] { 1, 2, 3, 4 };
        var q2 = from int x in nums where x < 3 select /*<bind>*/ x /*</bind>*/;
    }
}");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal("nums, x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("nums, q2, x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [Fact]
        public void QueryExpression05()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowExpression(@"
using System;
using System.Linq;

class Program
{
    static void Main()
    {
        var nums = new sbyte[] { 1, 2, 3, 4 };
        var q2 = from int x in nums where x < 3 group /*<bind>*/ x /*</bind>*/ by x%2;
    }
}");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal("nums, x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("nums, q2, x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [WorkItem(541916)]
        [Fact]
        public void ForEachVariableInQueryExpr()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowExpression(@"
using System;
using System.Linq;

class Program
{
    static void Main()
    {
        var nums = new int[] { 4, 5 };

        foreach (var num in nums)
        {
            var q = from n in /*<bind>*/ nums /*</bind>*/ select num;
        }
    }
}");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal("num", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.Captured));
            Assert.Equal("nums", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("nums", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal("num, nums", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("n, num, nums, q", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [WorkItem(541945)]
        [Fact]
        public void ForVariableInQueryExpr()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowExpression(@"
using System;
using System.Linq;

class Program
{
    static void Main()
    {
        var nums = new int[] { 4, 5 };

        for (int num = 0; num < 10; num++)
        {
            var q = from n in /*<bind>*/ nums /*</bind>*/ select num;
        }
    }
}");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal("num", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.Captured));
            Assert.Equal("nums", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("nums", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal("num", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("n, num, nums, q", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [WorkItem(541926)]
        [Fact]
        public void Bug8863()
        {
            var analysisResults = CompileAndAnalyzeControlAndDataFlowStatements(@"
using System.Linq;
class C
{
    static void Main(string[] args)
    {
        /*<bind>*/
        var temp = from x in ""abc""
                   let z = x.ToString()
                   select z into w
                   select w;
        /*</bind>*/
    }
}");
            var dataFlowAnalysisResults = analysisResults.Item2;
            Assert.Equal("temp, w, x, z", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal("temp", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            //Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.Captured));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("w, x, z", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal("temp, w, x, z", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("args", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [Fact]
        public void Bug9415()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowExpression(@"
using System;
using System.Collections.Generic;
using System.Linq;
 
class Program
{
    static void Main(string[] args)
    {
        var q1 = from x in new int[] { /*<bind>*/4/*</bind>*/, 5 }
                 orderby x
                 select x;
    }
}");
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            //Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.Captured));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("args, q1, x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [WorkItem(543546)]
        [Fact]
        public void GroupByClause()
        {
            var analysisResults = CompileAndAnalyzeDataFlowExpression(@"
using System.Linq;

public class Test
{
    public static void Main()
    {
        var strings = new string[] { };
        var q = from s in strings
                select s into t
                    /*<bind>*/group t by t.Length/*</bind>*/;
    }
}");
            var dataFlowAnalysisResults = analysisResults;
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
        }

        #endregion query expressions

        #region "switch statement tests"

        [Fact]
        public void LocalInOtherSwitchCase()
        {
            var dataFlows = CompileAndAnalyzeDataFlowExpression(
@"using System;
using System.Linq;
public class Test
{
    public static void Main()
    {
        int ret = 6;
        switch (ret)
        {
            case 1:
                int i = 10; break;
            case 2:
                var q1 = from j in new int[] { 3, 4 } select /*<bind>*/i/*</bind>*/;
                break;
        }
    }
}");
            Assert.Empty(dataFlows.DataFlowsOut);
        }

        [WorkItem(541639)]
        [Fact]
        public void VariableDeclInsideSwitchCaptureInLambdaExpr()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowExpression(@"
using System;

class C
{
    public static void Main()
    {
        switch (10)
        {
            default:
                int i = 10;
                Func<int> f1 = () => /*<bind>*/i/*</bind>*/;
                break;
        }
    }
}
");
            Assert.Equal("i", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.Captured));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal("i", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("i", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("f1, i", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [WorkItem(541710)]
        [Fact]
        public void ArrayCreationExprInForEachInsideSwitchSection()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowExpression(@"
class Program
{
    static void Main()
    {
        switch ('2')
        {
            default:
                break;
            case '2':
                foreach (var i100 in new int[] {4, /*<bind>*/5/*</bind>*/ })
                {
                }
                break;
        }
    }
}
");
            Assert.Empty(dataFlowAnalysisResults.Captured);
            Assert.Empty(dataFlowAnalysisResults.VariablesDeclared);
            Assert.Empty(dataFlowAnalysisResults.AlwaysAssigned);
            Assert.Empty(dataFlowAnalysisResults.DataFlowsIn);
            Assert.Empty(dataFlowAnalysisResults.DataFlowsOut);
            Assert.Empty(dataFlowAnalysisResults.ReadInside);
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Empty(dataFlowAnalysisResults.WrittenInside);
            Assert.Equal("i100", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [Fact]
        public void RegionInsideSwitchExpression()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowExpression(@"
class Program
{
    static void Main()
    {
        switch ('2')
        {
            default:
                break;
            case '2':
                switch (/*<bind>*/'2'/*</bind>*/)
                {
                     case '2': break;
                }
                break;
        }
    }
}
");
            Assert.Empty(dataFlowAnalysisResults.Captured);
            Assert.Empty(dataFlowAnalysisResults.VariablesDeclared);
            Assert.Empty(dataFlowAnalysisResults.AlwaysAssigned);
            Assert.Empty(dataFlowAnalysisResults.DataFlowsIn);
            Assert.Empty(dataFlowAnalysisResults.DataFlowsOut);
            Assert.Empty(dataFlowAnalysisResults.ReadInside);
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Empty(dataFlowAnalysisResults.WrittenInside);
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));

        }

        [Fact]
        public void NullableAsSwitchExpression()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowStatements(@"
using System;

class C
{
    public void F(ulong? p)
    {
/*<bind>*/
        switch (p)
        {
            case null:
                break;
            case 1:
                goto case null;
            default:
                break;
        }
/*</bind>*/
    }
}
");
            Assert.Empty(dataFlowAnalysisResults.Captured);
            Assert.Empty(dataFlowAnalysisResults.VariablesDeclared);
            Assert.Empty(dataFlowAnalysisResults.AlwaysAssigned);
            Assert.Equal("p", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Empty(dataFlowAnalysisResults.DataFlowsOut);
            Assert.Equal("p", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Empty(dataFlowAnalysisResults.WrittenInside);
            Assert.Equal("p, this", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));

        }

        #endregion

        #region "Misc."

        [Fact, WorkItem(11298, "DevDiv_Projects/Roslyn")]
        public void BaseExpressionSyntax()
        {
            var source = @"
using System;

public class BaseClass
{
    public virtual void MyMeth()
    {
    }
}

public class MyClass : BaseClass
{
    public override void MyMeth()
    {
        base.MyMeth();
    }
    delegate BaseClass D();
    public void OtherMeth()
    {
        D f = () => base;
    }
    public static void Main()
    {
    }
}";
            var comp = CreateCompilationWithMscorlib(source);
            var tree = comp.SyntaxTrees.Single();
            var model = comp.GetSemanticModel(tree);

            var invocation = tree.GetCompilationUnitRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
            var flowAnalysis = model.AnalyzeDataFlow(invocation);
            Assert.Empty(flowAnalysis.Captured);
            Assert.Equal("MyClass @this", flowAnalysis.DataFlowsIn.Single().ToTestDisplayString());
            Assert.Empty(flowAnalysis.DataFlowsOut);
            Assert.Equal("MyClass @this", flowAnalysis.ReadInside.Single().ToTestDisplayString());
            Assert.Empty(flowAnalysis.WrittenInside);
            Assert.Equal("MyClass @this", flowAnalysis.WrittenOutside.Single().ToTestDisplayString());

            var lambda = tree.GetCompilationUnitRoot().DescendantNodes().OfType<ParenthesizedLambdaExpressionSyntax>().Single();
            flowAnalysis = model.AnalyzeDataFlow(lambda);
            Assert.Equal("MyClass @this", flowAnalysis.Captured.Single().ToTestDisplayString());
            Assert.Equal("MyClass @this", flowAnalysis.DataFlowsIn.Single().ToTestDisplayString());
            Assert.Empty(flowAnalysis.DataFlowsOut);
            Assert.Equal("MyClass @this", flowAnalysis.ReadInside.Single().ToTestDisplayString());
            Assert.Empty(flowAnalysis.WrittenInside);
            Assert.Equal("f, this", GetSymbolNamesSortedAndJoined(flowAnalysis.WrittenOutside));
        }

        [WorkItem(543101)]
        [Fact]
        public void AnalysisInsideBaseClause()
        {
            var analysisResults = CompileAndAnalyzeDataFlowExpression(@"
class A
{
    A(int x) : this(/*<bind>*/x.ToString()/*</bind>*/) { }
    A(string x) { }
}
");
            var dataFlowAnalysisResults = analysisResults;
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.VariablesDeclared));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.AlwaysAssigned));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.Captured));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.DataFlowsOut));
            Assert.Equal("x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.ReadOutside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenInside));
            Assert.Equal("this, x", GetSymbolNamesSortedAndJoined(dataFlowAnalysisResults.WrittenOutside));
        }

        [WorkItem(543758)]
        [Fact]
        public void BlockSyntaxOfALambdaInAttributeArg()
        {
            var controlFlowAnalysisResults = CompileAndAnalyzeControlFlowStatements(@"
class Test
{
    [Attrib(() => /*<bind>*/{ }/*</bind>*/)]
    public static void Main()
    {
    }
}
");
            Assert.False(controlFlowAnalysisResults.Succeeded);
        }

        [WorkItem(529196)]
        [Fact()]
        public void DefaultValueOfOptionalParam()
        {
            var dataFlowAnalysisResults = CompileAndAnalyzeDataFlowExpression(@"
public class Derived
{
    public void Foo(int x = /*<bind>*/ 2 /*</bind>*/)
    {
    }
}
");
            Assert.True(dataFlowAnalysisResults.Succeeded);
        }

        [Fact]
        public void GenericStructureCycle()
        {
            var source =
@"struct S<T>
{
    public S<S<T>> F;
}
class C
{
    static void M()
    {
        S<object> o;
    }
}";
            var compilation = CreateCompilation(source);
            var tree = compilation.SyntaxTrees[0];
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetCompilationUnitRoot();
            var statement = GetFirstNode<StatementSyntax>(tree, root.ToFullString().IndexOf("S<object> o"));
            var analysis = model.AnalyzeDataFlow(statement);
            Assert.True(analysis.Succeeded);
            Assert.Equal("o", GetSymbolNamesSortedAndJoined(analysis.VariablesDeclared));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.Captured));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.DataFlowsOut));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.ReadOutside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.WrittenInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.WrittenOutside));
        }

        [WorkItem(529320)]
        [Fact(Skip = "529320")]
        public void GenericStructureCycleFromMetadata()
        {
            var ilSource =
@".class public sealed S<T> extends System.ValueType
{
  .field public valuetype S<valuetype S<!T>> F
}";
            var source =
@"class C
{
    static void M()
    {
        S<object> o;
    }
}";
            var compilation = CreateCompilationWithCustomILSource(source, ilSource);
            var tree = compilation.SyntaxTrees[0];
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetCompilationUnitRoot();
            var statement = GetFirstNode<StatementSyntax>(tree, root.ToFullString().IndexOf("S<object> o"));
            var analysis = model.AnalyzeDataFlow(statement);
            Assert.True(analysis.Succeeded);
            Assert.Equal("o", GetSymbolNamesSortedAndJoined(analysis.VariablesDeclared));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.AlwaysAssigned));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.Captured));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.DataFlowsIn));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.DataFlowsOut));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.ReadInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.ReadOutside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.WrittenInside));
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.WrittenOutside));
        }

        [WorkItem(545372)]
        [Fact]
        public void AnalysisInSyntaxError01()
        {
            var source =
@"using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
 
class Program
{
    static void Main(string[] args)
    {
        Expression<Func<int>> f3 = () => switch (args[0]) {};
    }
}";
            var compilation = CreateCompilation(source);
            var tree = compilation.SyntaxTrees[0];
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetCompilationUnitRoot();
            var statement = GetLastNode<StatementSyntax>(tree, root.ToFullString().IndexOf("switch"));
            Assert.Equal("switch (args[0]) {}", statement.ToFullString());
            var analysis = model.AnalyzeDataFlow(statement);
            Assert.True(analysis.Succeeded);
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.WrittenInside));
            Assert.Equal("args, f3", GetSymbolNamesSortedAndJoined(analysis.WrittenOutside));
        }

        [WorkItem(546964)]
        [Fact]
        public void AnalysisWithMissingMember()
        {
            var source =
@"class C
{
    void Foo(string[] args)
    {
        foreach (var s in args)
        {
            this.EditorOperations = 1;
        }
    }
}";
            var compilation = CreateCompilation(source);
            var tree = compilation.SyntaxTrees[0];
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetCompilationUnitRoot();
            var statement = GetLastNode<StatementSyntax>(tree, root.ToFullString().IndexOf("EditorOperations"));
            Assert.Equal("this.EditorOperations = 1;", statement.ToString());
            var analysis = model.AnalyzeDataFlow(statement);
            Assert.True(analysis.Succeeded);
            var v = analysis.DataFlowsOut;
        }

        [Fact, WorkItem(547059)]
        public void ObjectInitIncompleteCodeInQuery()
        {
            var source = @"
using System.Collections.Generic;
using System.Linq;

class Program
{
    static void Main()
    {
        var symlist = new List<ISymbol>();
        var expList = from s in symlist
                      select new ExportedSymbol() { S
    }
}

public interface ISymbol
{ }

public class ExportedSymbol
{
    public ISymbol Symbol;
    public byte UseBits;
}
";

            var compilation = CreateCompilation(source);
            var tree = compilation.SyntaxTrees[0];
            var model = compilation.GetSemanticModel(tree);
            var statement = tree.GetCompilationUnitRoot().DescendantNodes().OfType<BlockSyntax>().FirstOrDefault();
            var expectedtext = @"    {
        var symlist = new List<ISymbol>();
        var expList = from s in symlist
                      select new ExportedSymbol() { S
    }
}
";
            Assert.Equal(expectedtext, statement.ToFullString());
            var analysis = model.AnalyzeDataFlow(statement);
            Assert.True(analysis.Succeeded);
        }

        [Fact]
        public void StaticSetterAssignedInCtor()
        {
            var source =
@"class C
{
    C()
    {
        P = new object();
    }
    static object P { get; set; }
}";
            var compilation = CreateCompilationWithMscorlib(source);
            var tree = compilation.SyntaxTrees[0];
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetCompilationUnitRoot();
            var statement = GetFirstNode<StatementSyntax>(tree, root.ToFullString().IndexOf("P = new object()"));
            var analysis = model.AnalyzeDataFlow(statement);
            Assert.True(analysis.Succeeded);
        }

        [Fact]
        public void FieldBeforeAssignedInStructCtor()
        {
            var source =
@"struct S
{
    object value;
    S(object x)
    {
        S.Equals(value , value);
        this.value = null;
    }
}";
            var compilation = CreateCompilationWithMscorlib(source);
            compilation.VerifyDiagnostics(
                // (6,18): error CS0170: Use of possibly unassigned field 'value'
                //         S.Equals(value , value);
                Diagnostic(ErrorCode.ERR_UseDefViolationField, "value").WithArguments("value")
                );
            var tree = compilation.SyntaxTrees[0];
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            var expression = GetLastNode<ExpressionSyntax>(tree, root.ToFullString().IndexOf("value "));
            var analysis = model.AnalyzeDataFlow(expression);
            Assert.True(analysis.Succeeded);
            Assert.Equal(null, GetSymbolNamesSortedAndJoined(analysis.DataFlowsOut));
        }

        #endregion
    }
}
