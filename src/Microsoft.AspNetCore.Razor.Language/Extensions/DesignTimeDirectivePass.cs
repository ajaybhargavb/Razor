﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Razor.Language.Intermediate;

namespace Microsoft.AspNetCore.Razor.Language.Extensions
{
    internal class DesignTimeDirectivePass : IntermediateNodePassBase, IRazorDirectiveClassifierPass
    {
        internal const string DesignTimeVariable = "__o";

        // This needs to run before other directive classifiers.
        public override int Order => -10;

        protected override void ExecuteCore(RazorCodeDocument codeDocument, DocumentIntermediateNode documentNode)
        {
            var walker = new DesignTimeHelperWalker();
            walker.VisitDocument(documentNode);
        }

        internal class DesignTimeHelperWalker : IntermediateNodeWalker
        {
            private DesignTimeDirectiveIntermediateNode _directiveNode;

            public override void VisitClassDeclaration(ClassDeclarationIntermediateNode node)
            {
                var designTimeHelperDeclaration = new CSharpCodeIntermediateNode();
                IntermediateNodeBuilder.Create(designTimeHelperDeclaration)
                    .Add(new IntermediateToken()
                    {
                        Kind = IntermediateToken.TokenKind.CSharp,
                        Content = $"private static {typeof(object).FullName} {DesignTimeVariable} = null;"
                    });

                node.Children.Insert(0, designTimeHelperDeclaration);

                _directiveNode = new DesignTimeDirectiveIntermediateNode();

                VisitDefault(node);

                node.Children.Insert(0, _directiveNode);
            }

            public override void VisitDirectiveToken(DirectiveTokenIntermediateNode node)
            {
                _directiveNode.Children.Add(node);
            }
        }
    }
}