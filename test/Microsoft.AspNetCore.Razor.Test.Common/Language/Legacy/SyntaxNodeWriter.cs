﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Razor.Language.Legacy;

namespace Microsoft.AspNetCore.Razor.Language.Syntax
{
    internal class SyntaxNodeWriter : SyntaxRewriter
    {
        private readonly TextWriter _writer;
        private bool _visitedRoot;

        public SyntaxNodeWriter(TextWriter writer)
        {
            _writer = writer;
        }

        public int Depth { get; set; }

        public override SyntaxNode Visit(SyntaxNode node)
        {
            if (node is SyntaxToken token)
            {
                return VisitToken(token);
            }

            WriteNode(node);
            return node;
        }

        public override SyntaxNode VisitToken(SyntaxToken token)
        {
            WriteToken(token);
            return base.VisitToken(token);
        }

        public override SyntaxNode VisitTrivia(SyntaxTrivia trivia)
        {
            WriteTrivia(trivia);
            return base.VisitTrivia(trivia);
        }

        private void WriteNode(SyntaxNode node)
        {
            WriteIndent();
            Write(node.Kind);
            WriteSeparator();
            Write($"[{node.Position}..{node.EndPosition})");
            WriteSeparator();
            Write($"FullWidth: {node.FullWidth}");

            var annotation = node.GetAnnotations().FirstOrDefault(a => a.Kind == SyntaxConstants.SpanContextKind);
            if (annotation != null && annotation.Data is SpanContext context)
            {
                WriteSpanContext(context);
            }

            if (!_visitedRoot)
            {
                WriteSeparator();
                Write($"[{node.ToFullString()}]");
                _visitedRoot = true;
            }
        }

        private void WriteToken(SyntaxToken token)
        {
            WriteIndent();
            var content = token.IsMissing ? "<Missing>" : token.Content;
            var diagnostics = token.GetDiagnostics();
            var tokenString = $"{token.Kind};[{content}];{string.Join(", ", diagnostics.Select(diagnostic => diagnostic.Id + diagnostic.Span))}";
            Write(tokenString);
        }

        private void WriteTrivia(SyntaxTrivia trivia)
        {
            throw new NotImplementedException();
        }

        private void WriteSpanContext(SpanContext context)
        {
            WriteSeparator();
            Write($"Gen<{context.ChunkGenerator}>");
            WriteSeparator();
            Write(context.EditHandler);
        }

        protected void WriteIndent()
        {
            for (var i = 0; i < Depth; i++)
            {
                for (var j = 0; j < 4; j++)
                {
                    Write(' ');
                }
            }
        }

        protected void WriteSeparator()
        {
            Write(" - ");
        }

        protected void WriteNewLine()
        {
            _writer.WriteLine();
        }

        protected void Write(object value)
        {
            if (value is string stringValue)
            {
                stringValue = stringValue.Replace(Environment.NewLine, "LF");
                _writer.Write(stringValue);
                return;
            }

            _writer.Write(value);
        }
    }
}
