namespace Snail.MCP.Figma.Tests;

/// <summary>Style guard over src, gen and tests: bodies explain themselves without inline comments, and strings are composed by interpolation, never by <c>+</c>. Exempt: anything inside string literals (generated JS, prompts) and a <c>+</c> joining two literals, which is only a line wrap.</summary>
public class CodeStyleConventionTests
{
    private sealed record Lexed(List<int> Comments, List<(int Start, int End)> Literals);

    [Fact]
    public void SourcesCarryNoInlineComments()
    {
        var offenders = Scan((file, text) => Lex(text).Comments
            .Select(position => Describe(file, text, position, "comment")));

        Assert.True(offenders.Count == 0, $"inline comments — move the why into a test name or an XML summary:\n{string.Join("\n", offenders)}");
    }

    [Fact]
    public void StringsAreComposedByInterpolationNotConcatenation()
    {
        var offenders = Scan((file, text) => Lex(text).Literals
            .Where(span => HasAdjacentPlus(text, span))
            .Select(span => Describe(file, text, span.Start, "string built with +")));

        Assert.True(offenders.Count == 0, $"string concatenation — use interpolation:\n{string.Join("\n", offenders)}");
    }

    private static List<string> Scan(Func<string, string, IEnumerable<string>> inspect)
    {
        var root = FindRepositoryRoot();
        var folders = new[] { "src", "gen", "tests" }
            .Select(folder => Path.Combine(root, folder))
            .Where(Directory.Exists);
        var files = folders
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            .Where(IsHandWritten);

        return [.. files.SelectMany(file => inspect(Path.GetRelativePath(root, file), File.ReadAllText(file)))];
    }

    private static bool IsHandWritten(string file)
    {
        var segments = file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return !segments.Contains("obj") && !segments.Contains("bin");
    }

    private static string Describe(string file, string text, int position, string label)
    {
        var line = text.AsSpan(0, position).Count('\n') + 1;

        return $"{file}:{line}: {label}";
    }

    private static Lexed Lex(string text)
    {
        var lexed = new Lexed([], []);
        var position = 0;

        LexCode(text, ref position, lexed, insideHole: false);

        return lexed;
    }

    private static void LexCode(string text, ref int position, Lexed lexed, bool insideHole)
    {
        var braceDepth = 0;

        while (position < text.Length)
        {
            var current = text[position];

            if (insideHole && current == '{')
            {
                braceDepth++;
            }

            if (insideHole && current == '}')
            {
                if (braceDepth == 0)
                {
                    return;
                }

                braceDepth--;
            }

            if (current == '/' && Peek(text, position + 1) == '/')
            {
                LexLineComment(text, ref position, lexed);
                continue;
            }

            if (current == '/' && Peek(text, position + 1) == '*')
            {
                LexBlockComment(text, ref position, lexed);
                continue;
            }

            if (current == '\'')
            {
                LexCharLiteral(text, ref position);
                continue;
            }

            if (IsStringStart(text, position, out var quotePosition))
            {
                LexStringLiteral(text, ref position, quotePosition, lexed);
                continue;
            }

            position++;
        }
    }

    private static void LexLineComment(string text, ref int position, Lexed lexed)
    {
        var isXmlDoc = Peek(text, position + 2) == '/';

        if (!isXmlDoc)
        {
            lexed.Comments.Add(position);
        }

        while (position < text.Length && text[position] != '\n')
        {
            position++;
        }
    }

    private static void LexBlockComment(string text, ref int position, Lexed lexed)
    {
        lexed.Comments.Add(position);

        var end = text.IndexOf("*/", position + 2, StringComparison.Ordinal);
        position = end < 0 ? text.Length : end + 2;
    }

    private static void LexCharLiteral(string text, ref int position)
    {
        position++;

        while (position < text.Length && text[position] != '\'')
        {
            position += text[position] == '\\' ? 2 : 1;
        }

        position++;
    }

    private static bool IsStringStart(string text, int position, out int quotePosition)
    {
        quotePosition = position;

        while (quotePosition < text.Length && (text[quotePosition] == '$' || text[quotePosition] == '@'))
        {
            quotePosition++;
        }

        return quotePosition < text.Length && text[quotePosition] == '"';
    }

    private static void LexStringLiteral(string text, ref int position, int quotePosition, Lexed lexed)
    {
        var start = position;
        var prefix = text[position..quotePosition];
        var quotes = QuoteRunLength(text, quotePosition);

        position = quotePosition + quotes;

        if (quotes >= 3)
        {
            LexRawTail(text, ref position, quotes);
        }
        else if (quotes == 1)
        {
            LexQuotedTail(text, ref position, prefix.Contains('$'), prefix.Contains('@'), lexed);
        }

        lexed.Literals.Add((start, position));
    }

    private static void LexQuotedTail(string text, ref int position, bool isInterpolated, bool isVerbatim, Lexed lexed)
    {
        while (position < text.Length)
        {
            var current = text[position];

            if (!isVerbatim && current == '\\')
            {
                position += 2;
                continue;
            }

            if (current == '"')
            {
                if (isVerbatim && Peek(text, position + 1) == '"')
                {
                    position += 2;
                    continue;
                }

                position++;
                return;
            }

            if (isInterpolated && current == '{' && Peek(text, position + 1) == '{')
            {
                position += 2;
                continue;
            }

            if (isInterpolated && current == '}' && Peek(text, position + 1) == '}')
            {
                position += 2;
                continue;
            }

            if (isInterpolated && current == '{')
            {
                position++;
                LexCode(text, ref position, lexed, insideHole: true);
                position++;
                continue;
            }

            position++;
        }
    }

    private static void LexRawTail(string text, ref int position, int quotes)
    {
        while (position < text.Length)
        {
            var run = QuoteRunLength(text, position);

            if (run >= quotes)
            {
                position += run;
                return;
            }

            position += Math.Max(1, run);
        }
    }

    private static int QuoteRunLength(string text, int position)
    {
        var length = 0;

        while (position + length < text.Length && text[position + length] == '"')
        {
            length++;
        }

        return length;
    }

    private static bool HasAdjacentPlus(string text, (int Start, int End) span)
    {
        var before = LastMeaningfulIndex(text, span.Start - 1);
        var after = FirstMeaningfulIndex(text, span.End);

        var composesOnLeft = before >= 0 && text[before] == '+' && Peek(text, before - 1) != '+'
            && !EndsLiteral(text, LastMeaningfulIndex(text, before - 1));
        var composesOnRight = after < text.Length && text[after] == '+'
            && Peek(text, after + 1) != '+' && Peek(text, after + 1) != '='
            && !StartsLiteral(text, FirstMeaningfulIndex(text, after + 1));

        return composesOnLeft || composesOnRight;
    }

    private static bool EndsLiteral(string text, int position) => position >= 0 && text[position] == '"';

    private static bool StartsLiteral(string text, int position) => Peek(text, position) is '"' or '$' or '@';

    private static int LastMeaningfulIndex(string text, int position)
    {
        while (position >= 0 && char.IsWhiteSpace(text[position]))
        {
            position--;
        }

        return position;
    }

    private static int FirstMeaningfulIndex(string text, int position)
    {
        while (position < text.Length && char.IsWhiteSpace(text[position]))
        {
            position++;
        }

        return position;
    }

    private static char Peek(string text, int position) =>
        position >= 0 && position < text.Length ? text[position] : '\0';

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Tools")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"src/Tools not found above {AppContext.BaseDirectory}");
    }
}
