﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;

namespace Microsoft.CodeAnalysis.Test.Utilities
{
    public sealed class TempRoot : IDisposable
    {
        private readonly List<IDisposable> temps = new List<IDisposable>();
        public static readonly string Root;

        static TempRoot()
        {
            Root = Path.Combine(Path.GetTempPath(), "RoslynTests");
            Directory.CreateDirectory(Root);
        }

        public void Dispose()
        {
            if (temps != null)
            {
                DisposeAll(temps);
                temps.Clear();
            }
        }

        private static void DisposeAll(IEnumerable<IDisposable> temps)
        {
            foreach (var temp in temps)
            {
                try
                {
                    if (temp != null)
                    {
                        temp.Dispose();
                    }
                }
                catch
                {
                    // ignore
                }
            }
        }

        public TempDirectory CreateDirectory()
        {
            var dir = new DisposableDirectory(this);
            temps.Add(dir);
            return dir;
        }

        public TempFile CreateFile(string prefix = null, string extension = null, string directory = null)
        {
            return AddFile(new DisposableFile(prefix, extension, directory));
        }

        public DisposableFile AddFile(DisposableFile file)
        {
            temps.Add(file);
            return file;
        }

        internal static void CreateStream(string fullPath)
        {
            using (var file = new FileStream(fullPath, FileMode.CreateNew)) { }
        }
    }
}
