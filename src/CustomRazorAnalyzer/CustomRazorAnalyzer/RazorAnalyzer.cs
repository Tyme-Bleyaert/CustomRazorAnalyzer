using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class RazorAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        id: "RAZ001",
        title: "Fix TODO in Razor @code block",
        messageFormat: "TODO found in Razor @code block",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterAdditionalFileAction(ctx =>
        {
            var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
            var file = ctx.AdditionalFile;

            if (file.Path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
            {
                var text = file.GetText(ctx.CancellationToken);
                if (text == null)
                    return;

                var razorContent = text.ToString();
                var commentPattern = @"@\*(.*?)\*@";
                var commentMatches = Regex.Matches(razorContent, commentPattern);

                // Precompute line start positions
                string[] lines = razorContent.Split(["\r\n", "\n"], StringSplitOptions.None);
                int[] lineStartPositions = new int[lines.Length];
                int pos = 0;
                for (int i = 0; i < lines.Length; i++)
                {
                    lineStartPositions[i] = pos;
                    // +1 for '\n' or +2 for '\r\n'? To be safe, count exact length:
                    pos += lines[i].Length + 1; // Assume '\n'; okay since splitting on both

                    // But if original had \r\n, length of line + 2 might be needed
                    // This approximation works if lines end in \n or \r\n consistently
                }

                foreach (Match commentMatch in commentMatches)
                {
                    int matchIndex = commentMatch.Index;
                    // Find which line this index belongs to by binary search or linear scan
                    int lineNumber = Array.FindLastIndex(lineStartPositions, start => start <= matchIndex);
                    if (commentMatch.Success)
                    {
                        string extracted = commentMatch.Groups[1].Value;
                        //TODO add razor comment syntax to diagnostics as well.
                    }
                }

                // Simple regex to extract @code blocks
                var matches = Regex.Matches(razorContent, @"@code\s*{([^}]*)}", RegexOptions.Singleline);

                foreach (Match match in matches)
                {
                    int matchIndex = match.Index;
                    // Find which line this index belongs to by binary search or linear scan
                    int lineNumber = Array.FindLastIndex(lineStartPositions, start => start <= matchIndex);

                    var codeContent = match.Groups[1].Value;

                    var tree = CSharpSyntaxTree.ParseText(codeContent, path: file.Path);
                    var root = tree.GetRoot(ctx.CancellationToken);

                    var todos = root.DescendantTrivia().Where(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) && t.ToString().Contains("TODO"));

                    foreach (var todo in todos)
                    {
                        var lineSpan = todo.GetLocation().GetLineSpan();
                        var fakeLinePos = new FileLinePositionSpan(
                            lineSpan.Path,
                            new LinePosition(lineSpan.StartLinePosition.Line - 1 + lineNumber, lineSpan.StartLinePosition.Character),
                            new LinePosition(lineSpan.EndLinePosition.Line - 1 + lineNumber, lineSpan.EndLinePosition.Character)
                        );

                        var diagnostic = Diagnostic.Create(Rule, Location.Create(file.Path, todo.Span, fakeLinePos.Span));
                        diagnostics.Add(diagnostic);
                    }
                }
            }

            foreach (var diag in diagnostics)
                ctx.ReportDiagnostic(diag);

        });
    }

}