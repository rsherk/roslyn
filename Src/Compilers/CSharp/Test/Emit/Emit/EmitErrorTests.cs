﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp.UnitTests.Emit;
using Microsoft.CodeAnalysis.Test.Utilities;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Test.Utilities;
using Xunit;
using Microsoft.CodeAnalysis.CSharp.Test.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests
{
    /// <summary>
    /// this place is dedicated to emit/codegen related error tests
    /// </summary>
    public class EmitErrorTests : EmitMetadataTestBase
    {
        #region "Mixed Error Tests"

        [WorkItem(543039)]
        [Fact]
        public void BadConstantInOtherAssemblyUsedByField()
        {
            string source1 = @"
public class A
{
    public const int x = x;
}
";
            var compilation1 = CreateCompilationWithMscorlib(source1);
            compilation1.VerifyDiagnostics(
                // (4,22): error CS0110: The evaluation of the constant value for 'A.x' involves a circular definition
                Diagnostic(CSharp.ErrorCode.ERR_CircConstValue, "x").WithArguments("A.x"));

            string source2 = @"
public class B
{
    public const int y = A.x;

    public static void Main()
    {
        System.Console.WriteLine(""Hello"");
    }
}
";
            VerifyEmitDiagnostics(source2, compilation1);
        }

        [WorkItem(543039)]
        [Fact]
        public void BadConstantInOtherAssemblyUsedByLocal()
        {
            string source1 = @"
public class A
{
    public const int x = x;
}
";
            var compilation1 = CreateCompilationWithMscorlib(source1);
            compilation1.VerifyDiagnostics(
                // (4,22): error CS0110: The evaluation of the constant value for 'A.x' involves a circular definition
                Diagnostic(CSharp.ErrorCode.ERR_CircConstValue, "x").WithArguments("A.x"));

            string source2 = @"
public class B
{
    public static void Main()
    {
        const int y = A.x;
        System.Console.WriteLine(""Hello"");
    }
}
";
            VerifyEmitDiagnostics(source2, compilation1,
                // (6,19): warning CS0219: The variable 'y' is assigned but its value is never used
                Diagnostic(ErrorCode.WRN_UnreferencedVarAssg, "y").WithArguments("y"));
        }

        [WorkItem(543039)]
        [Fact]
        public void BadDefaultArgumentInOtherAssembly()
        {
            string source1 = @"
public class A
{
    public const int x = x;

    public static int Foo(int y = x) { return y; }
}
";
            var compilation1 = CreateCompilationWithMscorlib(source1);
            compilation1.VerifyDiagnostics(
                // (4,22): error CS0110: The evaluation of the constant value for 'A.x' involves a circular definition
                Diagnostic(CSharp.ErrorCode.ERR_CircConstValue, "x").WithArguments("A.x"));

            string source2 = @"
public class B
{
    public static void Main()
    {
        System.Console.WriteLine(A.Foo());
    }
}
";
            VerifyEmitDiagnostics(source2, compilation1);
        }

        [WorkItem(543039)]
        [Fact]
        public void BadDefaultArgumentInOtherAssembly_Decimal()
        {
            string source1 = @"
public class A
{
    public const decimal x = x;

    public static decimal Foo(decimal y = x) { return y; }
}
";
            var compilation1 = CreateCompilationWithMscorlib(source1);
            compilation1.VerifyDiagnostics(
                // (4,22): error CS0110: The evaluation of the constant value for 'A.x' involves a circular definition
                Diagnostic(CSharp.ErrorCode.ERR_CircConstValue, "x").WithArguments("A.x"));

            string source2 = @"
public class B
{
    public static void Main()
    {
        System.Console.WriteLine(A.Foo());
    }
}
";
            VerifyEmitDiagnostics(source2, compilation1);
        }

        [WorkItem(543039)]
        [Fact]
        public void BadReturnTypeInOtherAssembly()
        {
            string source1 = @"
public class A
{
    public static Missing Foo() { return null; }
}
";
            var compilation1 = CreateCompilationWithMscorlib(source1);
            compilation1.VerifyDiagnostics(
                // (4,19): error CS0246: The type or namespace name 'Missing' could not be found (are you missing a using directive or an assembly reference?)
                Diagnostic(ErrorCode.ERR_SingleTypeNameNotFound, "Missing").WithArguments("Missing"));

            string source2 = @"
public class B
{
    public static void Main()
    {
        var f = A.Foo();
        System.Console.WriteLine(f);
    }
}
";
            VerifyEmitDiagnostics(source2, compilation1);
        }

        private static void VerifyEmitDiagnostics(string source2, CSharpCompilation compilation1, params DiagnosticDescription[] expectedDiagnostics)
        {
            var compilation2 = CreateCompilationWithMscorlib(source2, new MetadataReference[] { new CSharpCompilationReference(compilation1) });
            compilation2.VerifyDiagnostics(expectedDiagnostics);

            using (var executableStream = new MemoryStream())
            {
                var result = compilation2.Emit(executableStream);
                Assert.False(result.Success);

                result.Diagnostics.Verify(expectedDiagnostics.Concat(new[] 
                {
                    // error CS7038: Failed to emit module 'Test'.
                    Diagnostic(ErrorCode.ERR_ModuleEmitFailure).WithArguments(compilation2.AssemblyName)
                }).ToArray());
            }

            using (var executableStream = new MemoryStream())
            {
                var result = compilation2.EmitMetadataOnly(executableStream);
                Assert.True(result.Success);
                result.Diagnostics.Verify();
            }
        }

        [Fact(), WorkItem(530211)]
        public void ModuleNameMismatch()
        {
            var netModule = CreateCompilationWithMscorlib(
@"
class Test
{}
", compOptions: TestOptions.NetModule, assemblyName: "ModuleNameMismatch");

            CompileAndVerify(netModule, verify: false);
            var moduleImage = netModule.EmitToArray();

            var tempDir = Temp.CreateDirectory();

            var match = tempDir.CreateFile("ModuleNameMismatch.netmodule");
            var mismatch = tempDir.CreateFile("ModuleNameMismatch.mod");
            match.WriteAllBytes(moduleImage);
            mismatch.WriteAllBytes(moduleImage);

            var source = @"
class Module1
{
    public static void Main()
    {}
}
";
            var compilation1 = CreateCompilationWithMscorlib(source, new MetadataReference [] {new MetadataFileReference(match.Path, MetadataImageKind.Module)}, 
                                                             compOptions: TestOptions.Exe );
            CompileAndVerify(compilation1);

            var compilation2 = CreateCompilationWithMscorlib(source, new MetadataReference [] {new MetadataFileReference(mismatch.Path, MetadataImageKind.Module)}, 
                                                             compOptions: TestOptions.Exe );

            compilation2.VerifyDiagnostics(
                // error CS7086: Module name 'ModuleNameMismatch.netmodule' stored in 'ModuleNameMismatch.mod' must match its filename.
                Diagnostic(ErrorCode.ERR_NetModuleNameMismatch).WithArguments("ModuleNameMismatch.netmodule", "ModuleNameMismatch.mod"));

            var imageData = ModuleMetadata.CreateFromImage(moduleImage);

            var compilation3 = CreateCompilationWithMscorlib(source, new MetadataReference[] { new MetadataImageReference(imageData, fullPath: match.Path) },
                                                             compOptions: TestOptions.Exe);

            CompileAndVerify(compilation3);

            var compilation4 = CreateCompilationWithMscorlib(source, new MetadataReference[] { new MetadataImageReference(imageData, fullPath: mismatch.Path) },
                                                             compOptions: TestOptions.Exe);

            compilation4.VerifyDiagnostics(
                // error CS7086: Module name 'ModuleNameMismatch.netmodule' stored in 'ModuleNameMismatch.mod' must match its filename.
                Diagnostic(ErrorCode.ERR_NetModuleNameMismatch).WithArguments("ModuleNameMismatch.netmodule", "ModuleNameMismatch.mod"));
        }

        [Fact]
        public void CS0204_ERR_TooManyLocals()
        {
            var builder = new System.Text.StringBuilder();
            builder.Append(@"
public class A
{
    public static int Main ()
        {
");
            for (int i = 0; i < 65536; i++)
            {
                builder.AppendLine(string.Format("    int i{0} = {0};", i));
            }

            builder.Append(@"
        return 1;
        }
}
");

            //Compiling this with optimizations enabled causes the stack scheduler to eliminate a bunch of these locals.
            //It could eliminate 'em all, but doesn't.
            var warnOpts = new System.Collections.Generic.Dictionary<string, ReportDiagnostic>();
            warnOpts.Add(MessageProvider.Instance.GetIdForErrorCode((int)ErrorCode.WRN_UnreferencedVarAssg), ReportDiagnostic.Suppress);
            var compilation1 = CreateCompilationWithMscorlib(builder.ToString(), null, OptionsDll.WithSpecificDiagnosticOptions(warnOpts).WithOptimizations(false));
            compilation1.VerifyEmitDiagnostics(
                // (4,23): error CS0204: Only 65534 locals, including those generated by the compiler, are allowed
                //     public static int Main ()
                Diagnostic(ErrorCode.ERR_TooManyLocals, "Main"));
        }

        #endregion
    }
}
