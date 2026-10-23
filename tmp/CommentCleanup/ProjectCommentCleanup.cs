using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

internal static class ProjectCommentCleanup
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".cs", ".shader", ".cginc", ".hlsl", ".compute", ".glsl", ".usf", ".ush" };
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview, DocumentationMode.Parse);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private sealed class Entry
    {
        public string path { get; set; }
        public string extension { get; set; }
        public string beforeSha256 { get; set; }
        public string afterSha256 { get; set; }
        public string beforeCodeTokenSha256 { get; set; }
        public string afterCodeTokenSha256 { get; set; }
        public string encoding { get; set; }
        public int conditionalPasses { get; set; }
        public int commentSpans { get; set; }
        public int commentCharacters { get; set; }
        public bool changed { get; set; }
        public bool codeTokensIdentical { get; set; }
        public bool lineEndingsIdentical { get; set; }
        public bool noCommentTriviaRemain { get; set; }
        public bool licenseNoticeInComments { get; set; }
        public string backupPath { get; set; }
        internal byte[] Before, After;
    }

    private sealed class Rewrite
    {
        public string Text;
        public readonly List<(TextSpan Old, int NewStart, int NewLength)> Conditions = new();
        public int OriginalPosition(int position)
        {
            int delta = 0;
            foreach (var replacement in Conditions)
            {
                if (position < replacement.NewStart) break;
                if (position < replacement.NewStart + replacement.NewLength)
                    throw new InvalidOperationException("Comment unexpectedly overlaps a rewritten condition.");
                delta += replacement.NewLength - replacement.Old.Length;
            }
            return position - delta;
        }
    }

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--self-test") { SelfTest(); return 0; }
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < args.Length; i += 2)
            {
                if (i + 1 >= args.Length || !args[i].StartsWith("--")) throw new ArgumentException("Expected --key value pairs.");
                options.Add(args[i], args[i + 1]);
            }
            string root = Path.GetFullPath(options["--root"]);
            string mode = options.TryGetValue("--mode", out string value) ? value : "dry-run";
            if (mode != "dry-run" && mode != "apply") throw new ArgumentException("Mode must be dry-run or apply.");
            string reportPath = Within(root, options["--report"]);
            string backupRoot = Within(root, options["--backup"]);
            if (Inside(Path.Combine(root, "Assets"), reportPath) || Inside(Path.Combine(root, "Assets"), backupRoot)
                || Inside(Path.Combine(root, "Packages"), reportPath) || Inside(Path.Combine(root, "Packages"), backupRoot))
                throw new InvalidOperationException("Reports and backups must remain outside imported source folders.");

            var entries = new List<Entry>();
            foreach (string source in EnumerateSource(root)) entries.Add(Prepare(root, source));
            if (entries.Any(e => e.licenseNoticeInComments))
                throw new InvalidOperationException("A copyright/license notice was found in comments; preserve its required notice before applying.");
            var licenses = Directory.EnumerateFiles(Path.Combine(root, "Assets"), "*", SearchOption.AllDirectories)
                .Where(p => !p.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                    && (Path.GetFileName(p).Contains("license", StringComparison.OrdinalIgnoreCase)
                        || Path.GetFileName(p).Contains("ofl", StringComparison.OrdinalIgnoreCase)
                        || Path.GetFileName(p).Contains("notice", StringComparison.OrdinalIgnoreCase)
                        || Path.GetFileName(p).Contains("copying", StringComparison.OrdinalIgnoreCase)))
                .Select(p => new { path = Relative(root, p), sha256 = Hash(File.ReadAllBytes(p)) }).ToArray();
            if (mode == "apply")
            {
                foreach (Entry entry in entries)
                    if (Hash(File.ReadAllBytes(Path.Combine(root, entry.path))) != entry.beforeSha256)
                        throw new InvalidOperationException("Source changed during verification: " + entry.path);
                foreach (Entry entry in entries.Where(e => e.changed))
                {
                    string backup = Within(backupRoot, entry.path);
                    if (File.Exists(backup) && Hash(File.ReadAllBytes(backup)) != entry.beforeSha256)
                        throw new InvalidOperationException("Existing backup differs; it will not be overwritten: " + backup);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup));
                    if (!File.Exists(backup)) File.WriteAllBytes(backup, entry.Before);
                    entry.backupPath = Relative(root, backup);
                }
                WriteReport("prepared", mode, reportPath, entries, licenses);
                foreach (Entry entry in entries.Where(e => e.changed))
                {
                    string source = Path.Combine(root, entry.path);
                    if (Hash(File.ReadAllBytes(source)) != entry.beforeSha256)
                        throw new InvalidOperationException("Source changed before write: " + entry.path);
                    File.WriteAllBytes(source, entry.After);
                    if (Hash(File.ReadAllBytes(source)) != entry.afterSha256)
                        throw new InvalidOperationException("Written hash differs: " + entry.path);
                }
                foreach (var license in licenses)
                    if (Hash(File.ReadAllBytes(Path.Combine(root, license.path))) != license.sha256)
                        throw new InvalidOperationException("A license file changed: " + license.path);
            }
            WriteReport("passed", mode, reportPath, entries, licenses);
            Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", mode, files = entries.Count,
                changed = entries.Count(e => e.changed), commentSpans = entries.Sum(e => e.commentSpans),
                codeTokensIdentical = entries.All(e => e.codeTokensIdentical), report = reportPath }, JsonOptions));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void WriteReport(string status, string mode, string path, List<Entry> entries, object licenses)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, JsonSerializer.Serialize(new { status, mode, utc = DateTime.UtcNow.ToString("O"),
            scope = "Assets and embedded Packages source files only; generated caches, builds, tmp and license texts excluded",
            method = "Roslyn comment trivia across independently enabled conditional branches; shader lexical scan; only comment characters replaced with spaces; original newline bytes retained",
            files = entries.Count, changedFiles = entries.Count(e => e.changed),
            preservedLicenseFiles = licenses, entries }, JsonOptions), new UTF8Encoding(false));
    }

    private static IEnumerable<string> EnumerateSource(string root)
    {
        var files = new List<string>();
        foreach (string name in new[] { "Assets", "Packages" })
        {
            string path = Path.Combine(root, name);
            if (!Directory.Exists(path)) continue;
            var pending = new Stack<string>(); pending.Push(path);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                foreach (string child in Directory.EnumerateDirectories(directory))
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
                foreach (string file in Directory.EnumerateFiles(directory))
                    if (Extensions.Contains(Path.GetExtension(file)) && (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0)
                        files.Add(file);
            }
        }
        return files.OrderBy(p => Relative(root, p), StringComparer.Ordinal);
    }

    private static Entry Prepare(string root, string path)
    {
        byte[] beforeBytes = File.ReadAllBytes(path);
        (Encoding encoding, int preamble) = DetectEncoding(beforeBytes);
        string before = encoding.GetString(beforeBytes, preamble, beforeBytes.Length - preamble);
        bool csharp = Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase);
        int passes = 1;
        List<TextSpan> comments = csharp ? CSharpComments(before, out passes) : ShaderComments(before);
        string after = MaskComments(before, comments);
        string beforeTokens = csharp ? CSharpSignature(before) : ShaderSignature(before);
        string afterTokens = csharp ? CSharpSignature(after) : ShaderSignature(after);
        List<TextSpan> remaining = csharp ? CSharpComments(after, out _) : ShaderComments(after);
        if (beforeTokens != afterTokens || remaining.Count != 0 || Newlines(before) != Newlines(after))
            throw new InvalidOperationException("Comment/token/newline validation failed for " + path);
        byte[] payload = encoding.GetBytes(after);
        byte[] afterBytes = beforeBytes.Take(preamble).Concat(payload).ToArray();
        return new Entry { path = Relative(root, path), extension = Path.GetExtension(path),
            beforeSha256 = Hash(beforeBytes), afterSha256 = Hash(afterBytes),
            beforeCodeTokenSha256 = Hash(Encoding.UTF8.GetBytes(beforeTokens)),
            afterCodeTokenSha256 = Hash(Encoding.UTF8.GetBytes(afterTokens)),
            encoding = encoding.WebName + (preamble > 0 ? " with BOM" : " without BOM"),
            conditionalPasses = passes, commentSpans = comments.Count, commentCharacters = comments.Sum(s => s.Length),
            changed = !beforeBytes.SequenceEqual(afterBytes), codeTokensIdentical = true, lineEndingsIdentical = true,
            noCommentTriviaRemain = true, licenseNoticeInComments = comments.Any(s => HasNotice(before.Substring(s.Start, s.Length))),
            Before = beforeBytes, After = afterBytes };
    }

    private static bool HasNotice(string text) => new[] { "copyright", "permission is hereby", "redistribution",
        "all rights reserved", "licensed under", "license agreement" }.Any(s => text.Contains(s, StringComparison.OrdinalIgnoreCase));

    private static SyntaxNode Parse(string text, IEnumerable<string> symbols = null)
        => CSharpSyntaxTree.ParseText(text, symbols == null ? ParseOptions : ParseOptions.WithPreprocessorSymbols(symbols)).GetRoot();

    private static bool IsComment(SyntaxTrivia trivia) => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
        || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
        || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia);

    private static Rewrite ConditionalRewrite(string source)
    {
        if (source.Contains("__PROJECT_COMMENT_PASS_", StringComparison.Ordinal))
            throw new InvalidOperationException("Reserved comment audit preprocessor prefix appears in source.");
        var conditions = Parse(source).DescendantTrivia(descendIntoTrivia: true)
            .Select(t => t.GetStructure()).Select(n => n is IfDirectiveTriviaSyntax a ? a.Condition
                : n is ElifDirectiveTriviaSyntax b ? b.Condition : null).Where(n => n != null)
            .Select(n => n.Span).OrderBy(s => s.Start).ToArray();
        if (conditions.Length > 12) throw new InvalidOperationException("More than 12 conditional directives require a targeted audit.");
        var rewrite = new Rewrite(); var builder = new StringBuilder(); int cursor = 0;
        for (int index = 0; index < conditions.Length; index++)
        {
            TextSpan condition = conditions[index]; builder.Append(source, cursor, condition.Start - cursor);
            string symbol = "__PROJECT_COMMENT_PASS_" + index;
            rewrite.Conditions.Add((condition, builder.Length, symbol.Length)); builder.Append(symbol); cursor = condition.End;
        }
        builder.Append(source, cursor, source.Length - cursor); rewrite.Text = builder.ToString(); return rewrite;
    }

    private static List<TextSpan> CSharpComments(string source, out int passes)
    {
        var found = new List<TextSpan>();
        foreach (SyntaxTrivia trivia in Parse(source).DescendantTrivia(descendIntoTrivia: true))
            if (IsComment(trivia)) found.Add(trivia.FullSpan);
        Rewrite rewrite = ConditionalRewrite(source); passes = 1 << rewrite.Conditions.Count;
        for (int mask = 0; mask < passes; mask++)
        {
            IEnumerable<string> symbols = Enumerable.Range(0, rewrite.Conditions.Count)
                .Where(i => (mask & (1 << i)) != 0).Select(i => "__PROJECT_COMMENT_PASS_" + i);
            foreach (SyntaxTrivia trivia in Parse(rewrite.Text, symbols).DescendantTrivia(descendIntoTrivia: true))
                if (IsComment(trivia)) found.Add(TextSpan.FromBounds(rewrite.OriginalPosition(trivia.FullSpan.Start),
                    rewrite.OriginalPosition(trivia.FullSpan.End)));
        }
        return Merge(found);
    }

    private static string CSharpSignature(string source)
    {
        Rewrite rewrite = ConditionalRewrite(source); var builder = new StringBuilder();
        AppendTokens(builder, Parse(source));
        for (int mask = 0; mask < (1 << rewrite.Conditions.Count); mask++)
        {
            builder.Append("\nCONFIG:").Append(mask).Append('\n');
            IEnumerable<string> symbols = Enumerable.Range(0, rewrite.Conditions.Count)
                .Where(i => (mask & (1 << i)) != 0).Select(i => "__PROJECT_COMMENT_PASS_" + i);
            AppendTokens(builder, Parse(rewrite.Text, symbols));
        }
        return builder.ToString();
    }

    private static void AppendTokens(StringBuilder builder, SyntaxNode root)
    {
        foreach (SyntaxToken token in root.DescendantTokens(descendIntoTrivia: true))
        {
            if (token.Parent != null && token.Parent.AncestorsAndSelf().OfType<DocumentationCommentTriviaSyntax>().Any()) continue;
            builder.Append(token.RawKind).Append(':').Append(token.Text.Length).Append(':').Append(token.Text).Append('\n');
        }
    }

    private static List<TextSpan> ShaderComments(string source)
    {
        var spans = new List<TextSpan>();
        ScanShader(source, spans, null); return spans;
    }
    private static string ShaderSignature(string source)
    {
        var signature = new StringBuilder(); ScanShader(source, null, signature); return signature.ToString();
    }
    private static void ScanShader(string source, List<TextSpan> comments, StringBuilder signature)
    {
        int i = 0;
        while (i < source.Length)
        {
            int start = i; char c = source[i];
            if (c == '\r' || c == '\n') { signature?.Append(c); i++; continue; }
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                i += 2;
                while (i < source.Length)
                {
                    if (source[i] == '\\' && i + 1 < source.Length && (source[i + 1] == '\r' || source[i + 1] == '\n'))
                    {
                        i++; if (source[i] == '\r') i++; if (i < source.Length && source[i] == '\n') i++; continue;
                    }
                    if (source[i] == '\r' || source[i] == '\n') break;
                    i++;
                }
                comments?.Add(TextSpan.FromBounds(start, i));
                if (signature != null) signature.Append(Newlines(source.Substring(start, i - start)));
                continue;
            }
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                int end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) throw new InvalidOperationException("Unterminated shader block comment.");
                i = end + 2; comments?.Add(TextSpan.FromBounds(start, i));
                if (signature != null) signature.Append(Newlines(source.Substring(start, i - start)));
                continue;
            }
            if (c == '"' || c == '\'')
            {
                char quote = c; bool closed = false; i++;
                while (i < source.Length)
                {
                    if (source[i] == '\\' && i + 1 < source.Length) { i += 2; continue; }
                    if (source[i++] == quote) { closed = true; break; }
                }
                if (!closed) throw new InvalidOperationException("Unterminated shader string.");
            }
            else if (char.IsLetterOrDigit(c) || c == '_')
            {
                i++;
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_' || source[i] == '.')) i++;
            }
            else
            {
                i++;
                foreach (string op in new[] { ">>=", "<<=", "++", "--", "==", "!=", "<=", ">=", "&&", "||", "<<", ">>", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "##", "::", "->" })
                    if (start + op.Length <= source.Length && string.CompareOrdinal(source, start, op, 0, op.Length) == 0)
                    { i = start + op.Length; break; }
            }
            signature?.Append(i - start).Append(':').Append(source, start, i - start).Append('|');
        }
    }

    private static List<TextSpan> Merge(IEnumerable<TextSpan> spans)
    {
        var result = new List<TextSpan>();
        foreach (TextSpan span in spans.OrderBy(s => s.Start).ThenByDescending(s => s.Length))
            if (result.Count > 0 && span.Start <= result[^1].End)
                result[^1] = TextSpan.FromBounds(result[^1].Start, Math.Max(result[^1].End, span.End));
            else result.Add(span);
        return result;
    }
    private static string MaskComments(string source, List<TextSpan> spans)
    {
        char[] output = source.ToCharArray();
        foreach (TextSpan span in spans)
            for (int i = span.Start; i < span.End; i++) if (output[i] != '\r' && output[i] != '\n') output[i] = ' ';
        return new string(output);
    }
    private static (Encoding, int) DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return (new UTF8Encoding(false, true), 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return (new UnicodeEncoding(false, false, true), 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return (new UnicodeEncoding(true, false, true), 2);
        return (new UTF8Encoding(false, true), 0);
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string Newlines(string value) => new(value.Where(c => c == '\r' || c == '\n').ToArray());
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    private static bool Inside(string root, string path) => path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))
        + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static string Within(string root, string path)
    {
        string full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
        if (!Inside(root, full)) throw new InvalidOperationException("Path escapes workspace: " + full);
        return full;
    }

    private static void SelfTest()
    {
        string csharp = "// outside\r\nclass C { string u = \"https://host/a/*keep*/\"; char slash = '/'; "
            + "string v = @\"double\"\"//keep\"; string i = $\"literal //keep {1 /*remove*/ + 2}\"; }\r\n"
            + "#if false // directive comment\r\n/// <summary>inactive doc</summary>\r\nclass Hidden { /* remove hidden */ string u = \"//keep\"; }\r\n"
            + "#elif UNITY_EDITOR\r\n// editor only\r\nclass EditorOnly {}\r\n#else\r\n/* player only */ class PlayerOnly {}\r\n#endif\r\n"
            + "class Raw { string t = \"\"\"raw // string /* preserved */\"\"\"; }\r\n";
        var comments = CSharpComments(csharp, out int passes); string clean = MaskComments(csharp, comments);
        if (CSharpComments(clean, out _).Count != 0 || CSharpSignature(csharp) != CSharpSignature(clean)
            || !clean.Contains("https://host/a/*keep*/") || !clean.Contains("raw // string /* preserved */")
            || clean.Contains("remove hidden") || clean.Contains("editor only") || passes != 4)
            throw new InvalidOperationException("C# self-test failed.");
        string shader = "// header\r\n#define F(x) (x) /*block\r\nmore*/\r\n#include \"https://host/a//keep\"\r\n"
            + "float x = a/**/+/**/+b; // continued\\\r\ncomment tail\r\n#if X\r\nfloat y = 1;\r\n#endif\r\n";
        string shaderClean = MaskComments(shader, ShaderComments(shader));
        if (ShaderComments(shaderClean).Count != 0 || ShaderSignature(shader) != ShaderSignature(shaderClean)
            || !shaderClean.Contains("https://host/a//keep") || shaderClean.Contains("comment tail"))
            throw new InvalidOperationException("Shader self-test failed.");
        Console.WriteLine("PASS: C# active/inactive/constant-false/elif/doc/interpolated/raw/verbatim strings; shader strings, directives, operators, continued comments and newlines.");
    }
}
