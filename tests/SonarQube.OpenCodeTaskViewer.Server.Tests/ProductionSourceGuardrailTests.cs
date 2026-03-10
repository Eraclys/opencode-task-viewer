using System.Text;
using System.Text.RegularExpressions;

namespace SonarQube.OpenCodeTaskViewer.Server.Tests;

public sealed class ProductionSourceGuardrailTests
{
    static readonly Regex ObjectKeywordPattern = new(@"\bobject\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    static readonly Regex JsonObjectPattern = new(@"\bJsonObject\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void ProductionSource_DoesNotUseObjectKeyword() => AssertNoViolations(ObjectKeywordPattern, "object");

    [Fact]
    public void ProductionSource_DoesNotUseJsonObject() => AssertNoViolations(JsonObjectPattern, "JsonObject");

    static void AssertNoViolations(Regex pattern, string label)
    {
        var sourceRoot = FindProductionSourceRoot();
        var repoRoot = Directory.GetParent(Directory.GetParent(sourceRoot)!.FullName)!.FullName;

        var violations = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => FindViolations(path, repoRoot, pattern))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Found banned `{label}` usage(s) in production source:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    static IEnumerable<string> FindViolations(string filePath, string repoRoot, Regex pattern)
    {
        var sanitized = SanitizeSource(File.ReadAllText(filePath));
        var relativePath = Path.GetRelativePath(repoRoot, filePath).Replace('\\', '/');
        var lines = sanitized.Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            if (pattern.IsMatch(lines[index]))
                yield return $"{relativePath}:{index + 1}";
        }
    }

    static string FindProductionSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "SonarQube.OpenCodeTaskViewer.Server");

            if (Directory.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate src/SonarQube.OpenCodeTaskViewer.Server from test output directory.");
    }

    static string SanitizeSource(string source)
    {
        var builder = new StringBuilder(source.Length);
        var state = TokenState.Code;
        var rawStringQuoteCount = 0;

        for (var index = 0; index < source.Length; index++)
        {
            var current = source[index];
            var next = index + 1 < source.Length ? source[index + 1] : '\0';

            switch (state)
            {
                case TokenState.Code:
                    if (TryStartRawString(
                            source,
                            index,
                            out var rawPrefixLength,
                            out rawStringQuoteCount))
                    {
                        AppendSanitized(
                            builder,
                            source,
                            index,
                            rawPrefixLength);

                        index += rawPrefixLength - 1;
                        state = TokenState.RawString;

                        break;
                    }

                    if (current == '/' &&
                        next == '/')
                    {
                        AppendSanitized(
                            builder,
                            source,
                            index,
                            2);

                        index++;
                        state = TokenState.LineComment;

                        break;
                    }

                    if (current == '/' &&
                        next == '*')
                    {
                        AppendSanitized(
                            builder,
                            source,
                            index,
                            2);

                        index++;
                        state = TokenState.BlockComment;

                        break;
                    }

                    if (IsVerbatimStringStart(source, index, out var verbatimPrefixLength))
                    {
                        AppendSanitized(
                            builder,
                            source,
                            index,
                            verbatimPrefixLength);

                        index += verbatimPrefixLength - 1;
                        state = TokenState.VerbatimString;

                        break;
                    }

                    if (IsRegularStringStart(source, index, out var stringPrefixLength))
                    {
                        AppendSanitized(
                            builder,
                            source,
                            index,
                            stringPrefixLength);

                        index += stringPrefixLength - 1;
                        state = TokenState.String;

                        break;
                    }

                    if (current == '\'')
                    {
                        AppendSanitized(builder, current);
                        state = TokenState.Character;

                        break;
                    }

                    builder.Append(current);

                    break;

                case TokenState.LineComment:
                    AppendSanitized(builder, current);

                    if (current == '\n')
                        state = TokenState.Code;

                    break;

                case TokenState.BlockComment:
                    AppendSanitized(builder, current);

                    if (current == '*' &&
                        next == '/')
                    {
                        AppendSanitized(builder, next);
                        index++;
                        state = TokenState.Code;
                    }

                    break;

                case TokenState.String:
                    AppendSanitized(builder, current);

                    if (current == '\\' &&
                        next != '\0')
                    {
                        AppendSanitized(builder, next);
                        index++;

                        break;
                    }

                    if (current == '"')
                        state = TokenState.Code;

                    break;

                case TokenState.VerbatimString:
                    AppendSanitized(builder, current);

                    if (current == '"' &&
                        next == '"')
                    {
                        AppendSanitized(builder, next);
                        index++;

                        break;
                    }

                    if (current == '"')
                        state = TokenState.Code;

                    break;

                case TokenState.Character:
                    AppendSanitized(builder, current);

                    if (current == '\\' &&
                        next != '\0')
                    {
                        AppendSanitized(builder, next);
                        index++;

                        break;
                    }

                    if (current == '\'')
                        state = TokenState.Code;

                    break;

                case TokenState.RawString:
                    if (current == '"' &&
                        CountConsecutive(source, index, '"') >= rawStringQuoteCount)
                    {
                        AppendSanitized(
                            builder,
                            source,
                            index,
                            rawStringQuoteCount);

                        index += rawStringQuoteCount - 1;
                        state = TokenState.Code;

                        break;
                    }

                    AppendSanitized(builder, current);

                    break;
            }
        }

        return builder.ToString();
    }

    static bool TryStartRawString(
        string source,
        int index,
        out int prefixLength,
        out int quoteCount)
    {
        var cursor = index;

        while (cursor < source.Length &&
               source[cursor] == '$')
        {
            cursor++;
        }

        quoteCount = CountConsecutive(source, cursor, '"');
        prefixLength = cursor - index + quoteCount;

        return quoteCount >= 3;
    }

    static bool IsVerbatimStringStart(string source, int index, out int prefixLength)
    {
        prefixLength = 0;

        if (Matches(source, index, "@\"") ||
            Matches(source, index, "$@\"") ||
            Matches(source, index, "@$\""))
        {
            prefixLength = source[index] == '@' ? 2 : 3;

            return true;
        }

        return false;
    }

    static bool IsRegularStringStart(string source, int index, out int prefixLength)
    {
        prefixLength = 0;

        if (Matches(source, index, "\""))
        {
            prefixLength = 1;

            return true;
        }

        if (Matches(source, index, "$\""))
        {
            prefixLength = 2;

            return true;
        }

        return false;
    }

    static int CountConsecutive(string source, int index, char value)
    {
        var count = 0;

        while (index + count < source.Length &&
               source[index + count] == value)
        {
            count++;
        }

        return count;
    }

    static bool Matches(string source, int index, string value)
    {
        if (index + value.Length > source.Length)
            return false;

        for (var offset = 0; offset < value.Length; offset++)
        {
            if (source[index + offset] != value[offset])
                return false;
        }

        return true;
    }

    static void AppendSanitized(
        StringBuilder builder,
        string source,
        int start,
        int length)
    {
        for (var offset = 0; offset < length; offset++)
        {
            AppendSanitized(builder, source[start + offset]);
        }
    }

    static void AppendSanitized(StringBuilder builder, char value) => builder.Append(value is '\r' or '\n' ? value : ' ');

    enum TokenState
    {
        Code,
        LineComment,
        BlockComment,
        String,
        VerbatimString,
        Character,
        RawString
    }
}
