// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
#if NET46
using System.Runtime.Remoting;
using System.Runtime.Remoting.Messaging;
#else
using System.Threading;
#endif
using System.Text;
using Xunit;
using Xunit.Sdk;
using System.Text.RegularExpressions;

namespace Microsoft.AspNetCore.Razor.Language.Legacy
{
    [IntializeTestFile]
    public abstract class ParserTestBase
    {
#if !NET46
        private static readonly AsyncLocal<string> _fileName = new AsyncLocal<string>();
        private static readonly AsyncLocal<bool> _isTheory = new AsyncLocal<bool>();
#endif

        internal ParserTestBase()
        {
            Factory = CreateSpanFactory();
            BlockFactory = CreateBlockFactory();
            TestProjectRoot = TestProject.GetProjectDirectory(GetType());
        }

        /// <summary>
        /// Set to true to autocorrect the locations of spans to appear in document order with no gaps.
        /// Use this when spans were not created in document order.
        /// </summary>
        protected bool FixupSpans { get; set; }

        internal SpanFactory Factory { get; private set; }

        internal BlockFactory BlockFactory { get; private set; }

#if GENERATE_BASELINES
        protected bool GenerateBaselines { get; set; } = true;
#else
        protected bool GenerateBaselines { get; set; } = false;
#endif

        protected string TestProjectRoot { get; }

        // Used by the test framework to set the 'base' name for test files.
        public static string FileName
        {
#if NET46
            get
            {
                var handle = (ObjectHandle)CallContext.LogicalGetData("ParserTestBase_FileName");
                return (string)handle.Unwrap();
            }
            set
            {
                CallContext.LogicalSetData("ParserTestBase_FileName", new ObjectHandle(value));
            }
#elif NETCOREAPP2_2
            get { return _fileName.Value; }
            set { _fileName.Value = value; }
#endif
        }

        public static bool IsTheory
        {
#if NET46
            get
            {
                var handle = (ObjectHandle)CallContext.LogicalGetData("ParserTestBase_IsTheory");
                return (bool)handle.Unwrap();
            }
            set
            {
                CallContext.LogicalSetData("ParserTestBase_IsTheory", new ObjectHandle(value));
            }
#elif NETCOREAPP2_2
            get { return _isTheory.Value; }
            set { _isTheory.Value = value; }
#endif
        }

        internal void AssertSyntaxTreeNodeMatchesBaseline(RazorSyntaxTree syntaxTree)
        {
            AssertSyntaxTreeNodeMatchesBaseline(syntaxTree.Root, syntaxTree.Diagnostics.ToArray());
        }

        internal void AssertSyntaxTreeNodeMatchesBaseline(Block root, params RazorDiagnostic[] diagnostics)
        {
            if (FileName == null)
            {
                var message = $"{nameof(AssertSyntaxTreeNodeMatchesBaseline)} should only be called from a parser test ({nameof(FileName)} is null).";
                throw new InvalidOperationException(message);
            }

            if (IsTheory)
            {
                var message = $"{nameof(AssertSyntaxTreeNodeMatchesBaseline)} should not be called from a [Theory] test.";
                throw new InvalidOperationException(message);
            }

            var baselineFileName = Path.ChangeExtension(FileName, ".syntaxtree.txt");
            var baselineDiagnosticsFileName = Path.ChangeExtension(FileName, ".diagnostics.txt");

            if (GenerateBaselines)
            {
                var baselineFullPath = Path.Combine(TestProjectRoot, baselineFileName);
                File.WriteAllText(baselineFullPath, SyntaxTreeNodeSerializer.Serialize(root));

                var baselineDiagnosticsFullPath = Path.Combine(TestProjectRoot, baselineDiagnosticsFileName);
                var lines = diagnostics.Select(SerializeDiagnostic).ToArray();
                if (lines.Any())
                {
                    File.WriteAllLines(baselineDiagnosticsFullPath, lines);
                }
                else if (File.Exists(baselineDiagnosticsFullPath))
                {
                    File.Delete(baselineDiagnosticsFullPath);
                }

                return;
            }

            var stFile = TestFile.Create(baselineFileName, GetType().GetTypeInfo().Assembly);
            if (!stFile.Exists())
            {
                throw new XunitException($"The resource {baselineFileName} was not found.");
            }

            var baseline = stFile.ReadAllText().Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            SyntaxTreeNodeVerifier.Verify(root, baseline);

            var baselineDiagnostics = string.Empty;
            var diagnosticsFile = TestFile.Create(baselineDiagnosticsFileName, GetType().GetTypeInfo().Assembly);
            if (diagnosticsFile.Exists())
            {
                baselineDiagnostics = diagnosticsFile.ReadAllText();
            }

            var actualDiagnostics = string.Concat(diagnostics.Select(d => SerializeDiagnostic(d) + "\r\n"));
            Assert.Equal(baselineDiagnostics, actualDiagnostics);
        }

        private static string SerializeDiagnostic(RazorDiagnostic diagnostic)
        {
            var content = RazorDiagnosticSerializer.Serialize(diagnostic);
            var normalized = NormalizeNewLines(content);

            return normalized;
        }

        private static string NormalizeNewLines(string content)
        {
            return Regex.Replace(content, "(?<!\r)\n", "\r\n", RegexOptions.None, TimeSpan.FromSeconds(10));
        }

        internal virtual void BaselineTest(RazorSyntaxTree syntaxTree, bool verifySyntaxTree = true)
        {
            if (verifySyntaxTree)
            {
                SyntaxTreeVerifier.Verify(syntaxTree);
            }

            AssertSyntaxTreeNodeMatchesBaseline(syntaxTree);
        }

        internal virtual void BaselineTest(Block root, bool verifySyntaxTree = true, params RazorDiagnostic[] diagnostics)
        {
            if (verifySyntaxTree)
            {
                SyntaxTreeVerifier.Verify(root);
            }

            AssertSyntaxTreeNodeMatchesBaseline(root, diagnostics);
        }

        internal RazorSyntaxTree ParseBlock(string document, bool designTime)
        {
            return ParseBlock(RazorLanguageVersion.Latest, document, designTime);
        }

        internal RazorSyntaxTree ParseBlock(RazorLanguageVersion version, string document, bool designTime)
        {
            return ParseBlock(version, document, null, designTime);
        }

        internal RazorSyntaxTree ParseBlock(string document, IEnumerable<DirectiveDescriptor> directives, bool designTime)
        {
            return ParseBlock(RazorLanguageVersion.Latest, document, directives, designTime);
        }

        internal abstract RazorSyntaxTree ParseBlock(RazorLanguageVersion version, string document, IEnumerable<DirectiveDescriptor> directives, bool designTime);

        internal RazorSyntaxTree ParseDocument(string document, bool designTime = false)
        {
            return ParseDocument(RazorLanguageVersion.Latest, document, designTime);
        }

        internal RazorSyntaxTree ParseDocument(RazorLanguageVersion version, string document, bool designTime = false)
        {
            return ParseDocument(version, document, null, designTime);
        }

        internal RazorSyntaxTree ParseDocument(string document, IEnumerable<DirectiveDescriptor> directives, bool designTime = false)
        {
            return ParseDocument(RazorLanguageVersion.Latest, document, directives, designTime);
        }

        internal virtual RazorSyntaxTree ParseDocument(RazorLanguageVersion version, string document, IEnumerable<DirectiveDescriptor> directives, bool designTime = false)
        {
            directives = directives ?? Array.Empty<DirectiveDescriptor>();

            var source = TestRazorSourceDocument.Create(document, filePath: null, normalizeNewLines: true);

            var options = CreateParserOptions(version, directives, designTime);
            var context = new ParserContext(source, options);

            var codeParser = new CSharpCodeParser(directives, context);
            var markupParser = new HtmlMarkupParser(context);

            codeParser.HtmlParser = markupParser;
            markupParser.CodeParser = codeParser;

            markupParser.ParseDocument();

            var root = context.Builder.Build();
            var diagnostics = context.ErrorSink.Errors;

            var codeDocument = RazorCodeDocument.Create(source);

            var syntaxTree = RazorSyntaxTree.Create(root, source, diagnostics, options);
            codeDocument.SetSyntaxTree(syntaxTree);

            var defaultDirectivePass = new DefaultDirectiveSyntaxTreePass();
            syntaxTree = defaultDirectivePass.Execute(codeDocument, syntaxTree);

            return syntaxTree;
        }

        internal virtual RazorSyntaxTree ParseHtmlBlock(RazorLanguageVersion version, string document, IEnumerable<DirectiveDescriptor> directives, bool designTime = false)
        {
            directives = directives ?? Array.Empty<DirectiveDescriptor>();

            var source = TestRazorSourceDocument.Create(document, filePath: null, normalizeNewLines: true);
            var options = CreateParserOptions(version, directives, designTime);
            var context = new ParserContext(source, options);

            var parser = new HtmlMarkupParser(context);
            parser.CodeParser = new CSharpCodeParser(directives, context)
            {
                HtmlParser = parser,
            };

            parser.ParseBlock();

            var root = context.Builder.Build();
            var diagnostics = context.ErrorSink.Errors;

            return RazorSyntaxTree.Create(root, source, diagnostics, options);
        }

        internal RazorSyntaxTree ParseCodeBlock(string document, bool designTime = false)
        {
            return ParseCodeBlock(RazorLanguageVersion.Latest, document, Enumerable.Empty<DirectiveDescriptor>(), designTime);
        }

        internal virtual RazorSyntaxTree ParseCodeBlock(
            RazorLanguageVersion version,
            string document,
            IEnumerable<DirectiveDescriptor> directives,
            bool designTime)
        {
            directives = directives ?? Array.Empty<DirectiveDescriptor>();

            var source = TestRazorSourceDocument.Create(document, filePath: null, normalizeNewLines: true);
            var options = CreateParserOptions(version, directives, designTime);
            var context = new ParserContext(source, options);

            var parser = new CSharpCodeParser(directives, context);
            parser.HtmlParser = new HtmlMarkupParser(context)
            {
                CodeParser = parser,
            };

            parser.ParseBlock();

            var root = context.Builder.Build();
            var diagnostics = context.ErrorSink.Errors;

            return RazorSyntaxTree.Create(root, source, diagnostics, options);
        }

        internal SpanFactory CreateSpanFactory()
        {
            return new SpanFactory();
        }

        internal abstract BlockFactory CreateBlockFactory();

        internal virtual void ParseBlockTest(string document)
        {
            ParseBlockTest(document, null, false, new RazorDiagnostic[0]);
        }

        internal virtual void ParseBlockTest(string document, bool designTime)
        {
            ParseBlockTest(document, null, designTime, new RazorDiagnostic[0]);
        }

        internal virtual void ParseBlockTest(string document, IEnumerable<DirectiveDescriptor> directives)
        {
            ParseBlockTest(document, directives, null);
        }

        internal virtual void ParseBlockTest(string document, params RazorDiagnostic[] expectedErrors)
        {
            ParseBlockTest(document, false, expectedErrors);
        }

        internal virtual void ParseBlockTest(string document, bool designTime, params RazorDiagnostic[] expectedErrors)
        {
            ParseBlockTest(document, null, designTime, expectedErrors);
        }

        internal virtual void ParseBlockTest(RazorLanguageVersion version, string document)
        {
            ParseBlockTest(version, document, expectedRoot: null);
        }

        internal virtual void ParseBlockTest(RazorLanguageVersion version, string document, Block expectedRoot)
        {
            ParseBlockTest(version, document, expectedRoot, false, null);
        }

        internal virtual void ParseBlockTest(string document, Block expectedRoot)
        {
            ParseBlockTest(document, expectedRoot, false, null);
        }

        internal virtual void ParseBlockTest(string document, IEnumerable<DirectiveDescriptor> directives, Block expectedRoot)
        {
            ParseBlockTest(document, directives, expectedRoot, false, null);
        }

        internal virtual void ParseBlockTest(string document, Block expectedRoot, bool designTime)
        {
            ParseBlockTest(document, expectedRoot, designTime, null);
        }

        internal virtual void ParseBlockTest(string document, Block expectedRoot, params RazorDiagnostic[] expectedErrors)
        {
            ParseBlockTest(document, expectedRoot, false, expectedErrors);
        }

        internal virtual void ParseBlockTest(string document, IEnumerable<DirectiveDescriptor> directives, Block expectedRoot, params RazorDiagnostic[] expectedErrors)
        {
            ParseBlockTest(document, directives, expectedRoot, false, expectedErrors);
        }

        internal virtual void ParseBlockTest(string document, Block expected, bool designTime, params RazorDiagnostic[] expectedErrors)
        {
            ParseBlockTest(document, null, expected, designTime, expectedErrors);
        }

        internal virtual void ParseBlockTest(RazorLanguageVersion version, string document, Block expected, bool designTime, params RazorDiagnostic[] expectedErrors)
        {
            ParseBlockTest(version, document, null, expected, designTime, expectedErrors);
        }

        internal virtual void ParseBlockTest(string document, IEnumerable<DirectiveDescriptor> directives, Block expected, bool designTime, params RazorDiagnostic[] expectedErrors)
        {
            ParseBlockTest(RazorLanguageVersion.Latest, document, directives, expected, designTime, expectedErrors);
        }

        internal virtual void ParseBlockTest(RazorLanguageVersion version, string document, IEnumerable<DirectiveDescriptor> directives, Block expected, bool designTime, params RazorDiagnostic[] expectedErrors)
        {
            var result = ParseBlock(version, document, directives, designTime);

            BaselineTest(result);
        }

        internal virtual void SingleSpanBlockTest(string document)
        {
            SingleSpanBlockTest(document, default, default);
        }

        internal virtual void SingleSpanBlockTest(string document, BlockKindInternal blockKind, SpanKindInternal spanType, AcceptedCharactersInternal acceptedCharacters = AcceptedCharactersInternal.Any)
        {
            SingleSpanBlockTest(document, blockKind, spanType, acceptedCharacters, expectedError: null);
        }

        internal virtual void SingleSpanBlockTest(string document, string spanContent, BlockKindInternal blockKind, SpanKindInternal spanType, AcceptedCharactersInternal acceptedCharacters = AcceptedCharactersInternal.Any)
        {
            SingleSpanBlockTest(document, spanContent, blockKind, spanType, acceptedCharacters, expectedErrors: null);
        }

        internal virtual void SingleSpanBlockTest(string document, BlockKindInternal blockKind, SpanKindInternal spanType, params RazorDiagnostic[] expectedError)
        {
            SingleSpanBlockTest(document, document, blockKind, spanType, expectedError);
        }

        internal virtual void SingleSpanBlockTest(string document, string spanContent, BlockKindInternal blockKind, SpanKindInternal spanType, params RazorDiagnostic[] expectedErrors)
        {
            SingleSpanBlockTest(document, spanContent, blockKind, spanType, AcceptedCharactersInternal.Any, expectedErrors ?? new RazorDiagnostic[0]);
        }

        internal virtual void SingleSpanBlockTest(string document, BlockKindInternal blockKind, SpanKindInternal spanType, AcceptedCharactersInternal acceptedCharacters, params RazorDiagnostic[] expectedError)
        {
            SingleSpanBlockTest(document, document, blockKind, spanType, acceptedCharacters, expectedError);
        }

        internal virtual void SingleSpanBlockTest(string document, string spanContent, BlockKindInternal blockKind, SpanKindInternal spanType, AcceptedCharactersInternal acceptedCharacters, params RazorDiagnostic[] expectedErrors)
        {
            var result = ParseBlock(document, designTime: false);

            BaselineTest(result);
        }

        internal virtual void ParseDocumentTest(string document)
        {
            ParseDocumentTest(document, null, false);
        }

        internal virtual void ParseDocumentTest(string document, IEnumerable<DirectiveDescriptor> directives)
        {
            ParseDocumentTest(document, directives, expected: null);
        }

        internal virtual void ParseDocumentTest(string document, Block expectedRoot)
        {
            ParseDocumentTest(document, expectedRoot, false, null);
        }

        internal virtual void ParseDocumentTest(string document, Block expectedRoot, params RazorDiagnostic[] expectedErrors)
        {
            ParseDocumentTest(document, expectedRoot, false, expectedErrors);
        }

        internal virtual void ParseDocumentTest(string document, IEnumerable<DirectiveDescriptor> directives, Block expected, params RazorDiagnostic[] expectedErrors)
        {
            ParseDocumentTest(document, directives, expected, false, expectedErrors);
        }

        internal virtual void ParseDocumentTest(string document, bool designTime)
        {
            ParseDocumentTest(document, null, designTime);
        }

        internal virtual void ParseDocumentTest(string document, Block expectedRoot, bool designTime)
        {
            ParseDocumentTest(document, expectedRoot, designTime, null);
        }

        internal virtual void ParseDocumentTest(string document, Block expected, bool designTime, params RazorDiagnostic[] expectedErrors)
        {
            ParseDocumentTest(document, null, expected, designTime, expectedErrors);
        }

        internal virtual void ParseDocumentTest(string document, IEnumerable<DirectiveDescriptor> directives, Block expected, bool designTime, params RazorDiagnostic[] expectedErrors)
        {
            var result = ParseDocument(document, directives, designTime);

            BaselineTest(result);
        }

        private static RazorParserOptions CreateParserOptions(
            RazorLanguageVersion version,
            IEnumerable<DirectiveDescriptor> directives,
            bool designTime)
        {
            return new DefaultRazorParserOptions(
                directives.ToArray(),
                designTime,
                parseLeadingDirectives: false,
                version: version);
        }
    }
}