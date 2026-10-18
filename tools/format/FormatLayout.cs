using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

internal static class FormatLayout
{
    private static int Main(string[] args)
    {
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string path = Path.Combine(args[0], name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        return Run(args.Skip(1).ToArray());
    }

    private static int Run(string[] args)
    {
        bool check = args[0] == "check";
        int failures = 0;
        foreach (string file in File.ReadAllLines(args[1]))
        {
            string original = File.ReadAllText(file);
            SourceText text = CompactShortExpressions(SourceText.From(original));
            SyntaxNode root = CSharpSyntaxTree.ParseText(text).GetRoot();
            var positions = new SortedSet<int>();
            Action<SyntaxToken, SyntaxToken> separate = (left, right) =>
            {
                if (left.RawKind != 0 && right.RawKind != 0 && !right.IsKind(SyntaxKind.EndOfFileToken)
                && text.Lines.GetLineFromPosition(left.Span.End).LineNumber
                == text.Lines.GetLineFromPosition(right.SpanStart).LineNumber)
                    positions.Add(right.SpanStart);
            };
            foreach (BlockSyntax block in root.DescendantNodes().OfType<BlockSyntax>())
            {
                if (block.Statements.Count == 0)
                    continue;
                separate(block.OpenBraceToken.GetPreviousToken(), block.OpenBraceToken);
                separate(block.OpenBraceToken, block.OpenBraceToken.GetNextToken());
                separate(block.CloseBraceToken.GetPreviousToken(), block.CloseBraceToken);
            }
            foreach (SyntaxToken token in root.DescendantTokens())
            {
                if (token.IsKind(SyntaxKind.SemicolonToken) && !(token.Parent is ForStatementSyntax))
                    separate(token, token.GetNextToken());
            }
            foreach (SyntaxNode node in root.DescendantNodes())
            {
                if (node.Span.Length < 120 || text.Lines.GetLineFromPosition(node.SpanStart).LineNumber
                != text.Lines.GetLineFromPosition(node.Span.End).LineNumber)
                    continue;
                if (node is ArgumentListSyntax arguments && arguments.Arguments.Count > 1)
                {
                    separate(arguments.OpenParenToken, arguments.Arguments[0].GetFirstToken());
                    foreach (SyntaxToken comma in arguments.Arguments.GetSeparators())
                        separate(comma, comma.GetNextToken());
                    separate(arguments.CloseParenToken.GetPreviousToken(), arguments.CloseParenToken);
                }
                else if (node is InitializerExpressionSyntax initializer && initializer.Expressions.Count > 1)
                {
                    separate(initializer.OpenBraceToken, initializer.Expressions[0].GetFirstToken());
                    foreach (SyntaxToken comma in initializer.Expressions.GetSeparators())
                        separate(comma, comma.GetNextToken());
                    separate(initializer.CloseBraceToken.GetPreviousToken(), initializer.CloseBraceToken);
                }
                else if (node is BinaryExpressionSyntax binary
                && (binary.IsKind(SyntaxKind.LogicalAndExpression) || binary.IsKind(SyntaxKind.LogicalOrExpression)))
                {
                    separate(binary.OperatorToken.GetPreviousToken(), binary.OperatorToken);
                }
            }
            SourceText expanded = text.WithChanges(
                positions.Select(position => new TextChange(new TextSpan(position, 0), "\n"))
            );
            string formatted = expanded.ToString();
            if (formatted == original)
                continue;
            failures++;
            if (check)
                Console.Error.WriteLine(
                    file + ": expand compressed blocks, statements or long expressions with ./format.ps1"
                );
            else
            {
                File.WriteAllText(file, formatted, new UTF8Encoding(false));
            }
        }
        return check && failures > 0 ? 1 : 0;
    }

    private static SourceText CompactShortExpressions(SourceText text)
    {
        SyntaxNode root = CSharpSyntaxTree.ParseText(text).GetRoot();
        var changes = new List<TextChange>();
        foreach (var node in root.DescendantNodes().OrderBy(n => n.SpanStart).ThenByDescending(n => n.Span.Length))
        {
            bool emptyArguments = node is ArgumentListSyntax arguments && arguments.Arguments.Count == 0;
            bool emptyParameters = node is ParameterListSyntax parameters && parameters.Parameters.Count == 0;
            bool shortBinary = node is BinaryExpressionSyntax binary
                && !binary.IsKind(SyntaxKind.LogicalAndExpression)
                && !binary.IsKind(SyntaxKind.LogicalOrExpression);
            if (!emptyArguments && !emptyParameters && !shortBinary)
                continue;
            if (text.Lines.GetLineFromPosition(node.SpanStart).LineNumber
                == text.Lines.GetLineFromPosition(node.Span.End).LineNumber)
                continue;
            if (node.DescendantTrivia().Any(t => !t.IsKind(SyntaxKind.WhitespaceTrivia)
                && !t.IsKind(SyntaxKind.EndOfLineTrivia)))
                continue;
            string compact = node.WithoutTrivia().NormalizeWhitespace().ToFullString();
            int column = node.SpanStart - text.Lines.GetLineFromPosition(node.SpanStart).Start;
            if (compact.Contains('\n') || compact.Contains('\r') || column + compact.Length > 120)
                continue;
            if (changes.Any(change => change.Span.OverlapsWith(node.Span)))
                continue;
            changes.Add(new TextChange(node.Span, compact));
        }
        return text.WithChanges(changes);
    }

}
