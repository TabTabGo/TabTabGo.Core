using System.Runtime.CompilerServices;
using System.Text;
using TabTabGo.Core.Exceptions;
using TabTabGo.Core.ViewModels;

namespace TabTabGo.Core.Services.OData;

/// <summary>
/// Pulls custom function calls (for example <c>xmlExist(path,'value')</c>) out of an OData <c>$filter</c> string.
/// The rest of the filter is returned as text that standard OData can parse; no dangling <c>and</c>/<c>or</c> or
/// empty parentheses remain. Custom functions are only allowed on the <c>and</c> path: a custom function below an
/// <c>or</c> or a <c>not</c> cannot be expressed as a separate SQL fragment and is rejected with HTTP 400.
/// </summary>
internal static class ODataFilterTokenizer
{
    /// <summary>Longest filter, in characters, that is taken apart. A longer one is rejected with 400 (nothing legitimate is that long).</summary>
    internal const int MaxFilterLength = 4096;

    /// <summary>Deepest nesting of groups and <c>not</c>s that is accepted; the parser is recursive, so this bounds its stack.</summary>
    internal const int MaxDepth = 32;

    private static int _calls;

    /// <summary>How many times <see cref="Split"/> ran in this process (lets tests prove that standard filters bypass it).</summary>
    internal static int Calls => Volatile.Read(ref _calls);

    /// <summary>Result of <see cref="Split"/>.</summary>
    /// <param name="Filter">The filter without the custom functions; empty when nothing is left</param>
    /// <param name="CustomFilters">The custom function calls that were removed</param>
    internal sealed record Result(string Filter, IReadOnlyList<CustomOdataFiler> CustomFilters);

    /// <summary>
    /// Splits the filter.
    /// </summary>
    /// <param name="filter">Raw <c>$filter</c> value</param>
    /// <param name="customFunctions">Names of the custom functions, case-insensitive</param>
    /// <exception cref="ApiException">A custom function is used under <c>or</c> or <c>not</c> (400)</exception>
    internal static Result Split(string filter, IReadOnlyCollection<string> customFunctions)
    {
        Interlocked.Increment(ref _calls);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(customFunctions);
        if (filter.Length > MaxFilterLength)
        {
            throw Reject($"The filter is longer than {MaxFilterLength} characters.");
        }

        Node root;
        try
        {
            var parser = new Parser(filter, customFunctions);
            root = parser.ParseExpression();
            parser.EnsureEnd();
        }
        catch (InsufficientExecutionStackException)
        {
            // the depth limit is the normal guard; this is the backstop for a stack that is already nearly used up
            throw Reject("The filter is nested too deeply.");
        }

        var found = new List<CustomOdataFiler>();
        var rendered = Render(root, found, underOrOrNot: false);
        return new Result(rendered ?? string.Empty, found);
    }

    /// <summary>
    /// True when the filter calls one of the functions (name, optional spaces, an opening parenthesis), outside string literals
    /// and not as the tail of a longer name. A single linear scan: no recursion, so it is safe on any input.
    /// </summary>
    /// <param name="filter">Raw <c>$filter</c> value</param>
    /// <param name="customFunctions">Function names, case-insensitive</param>
    internal static bool ContainsCustomFunction(string filter, IReadOnlyCollection<string> customFunctions)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(customFunctions);
        var i = 0;
        while (i < filter.Length)
        {
            var c = filter[i];
            if (c == '\'' || c == '"')
            {
                i++;
                while (i < filter.Length)
                {
                    if (filter[i] == c)
                    {
                        if (i + 1 < filter.Length && filter[i + 1] == c)
                        {
                            i += 2;
                            continue;
                        }

                        break;
                    }

                    i++;
                }

                i++;
                continue;
            }

            if (char.IsLetter(c) && (i == 0 || !(char.IsLetterOrDigit(filter[i - 1]) || filter[i - 1] is '_' or '.' or '/')))
            {
                var end = i;
                while (end < filter.Length && (char.IsLetterOrDigit(filter[end]) || filter[end] == '_'))
                {
                    end++;
                }

                var word = filter.AsSpan(i, end - i);
                var next = end;
                while (next < filter.Length && char.IsWhiteSpace(filter[next]))
                {
                    next++;
                }

                if (next < filter.Length && filter[next] == '(')
                {
                    foreach (var name in customFunctions)
                    {
                        if (word.Equals(name, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }

                i = end;
                continue;
            }

            i++;
        }

        return false;
    }

    private static ApiException Reject(string message) =>
        new($"Invalid $filter. {message}", statusCode: System.Net.HttpStatusCode.BadRequest, code: "InvalidFilter");

    /// <summary>Marks the depth rejection so the group back-tracking in the parser lets it through instead of retrying.</summary>
    private const string TooDeepKey = "tooDeep";

    private abstract record Node;

    private sealed record Atom(string Text) : Node;

    private sealed record Custom(string Name, IReadOnlyList<string> Arguments) : Node;

    private sealed record Group(Node Inner) : Node;

    private sealed record Not(Node Inner) : Node;

    private sealed record And(IReadOnlyList<Node> Items) : Node;

    private sealed record Or(IReadOnlyList<Node> Items) : Node;

    private static string? Render(Node node, List<CustomOdataFiler> found, bool underOrOrNot)
    {
        switch (node)
        {
            case Atom atom:
                return atom.Text;
            case Custom custom:
                if (underOrOrNot)
                {
                    throw new ApiException(
                        $"The custom function '{custom.Name}' cannot be combined with 'or' or 'not'. Use 'and'.",
                        statusCode: System.Net.HttpStatusCode.BadRequest,
                        code: "InvalidFilter");
                }

                found.Add(new CustomOdataFiler { FunctionName = custom.Name, Parameters = custom.Arguments.ToList() });
                return null;
            case Group group:
            {
                var inner = Render(group.Inner, found, underOrOrNot);
                return inner == null ? null : $"({inner})";
            }
            case Not not:
            {
                var inner = Render(not.Inner, found, underOrOrNot: true);
                return inner == null ? null : (not.Inner is Group ? $"not {inner}" : $"not ({inner})");
            }
            case And and:
            {
                var parts = and.Items.Select(i => Render(i, found, underOrOrNot)).Where(p => p != null).ToList();
                return parts.Count == 0 ? null : string.Join(" and ", parts);
            }
            case Or or:
            {
                // anything under an 'or' must stay in the OData filter
                var parts = or.Items.Select(i => Render(i, found, underOrOrNot: true)).Where(p => p != null).ToList();
                return parts.Count == 0 ? null : string.Join(" or ", parts);
            }
            default:
                throw new InvalidOperationException("Unknown node.");
        }
    }

    private sealed class Parser(string text, IReadOnlyCollection<string> customFunctions)
    {
        private int _position;
        private int _depth;

        internal Node ParseExpression()
        {
            var items = new List<Node> { ParseTerm() };
            while (TryConsumeKeyword("or"))
            {
                items.Add(ParseTerm());
            }

            return items.Count == 1 ? items[0] : new Or(items);
        }

        internal void EnsureEnd()
        {
            SkipWhitespace();
            if (_position < text.Length)
            {
                throw Invalid($"Unexpected '{text[_position]}' at position {_position}.");
            }
        }

        private Node ParseTerm()
        {
            var items = new List<Node> { ParseFactor() };
            while (TryConsumeKeyword("and"))
            {
                items.Add(ParseFactor());
            }

            return items.Count == 1 ? items[0] : new And(items);
        }

        // a boolean group is "( expr )" followed by the end, ')' , 'and' or 'or'; anything else is part of a larger condition
        private Group? TryParseGroup()
        {
            try
            {
                _position++;
                var inner = ParseExpression();
                SkipWhitespace();
                if (_position >= text.Length || text[_position] != ')')
                {
                    return null;
                }

                _position++;
                SkipWhitespace();
                var closesCondition = _position >= text.Length || text[_position] == ')' || NextWordIs("and") || NextWordIs("or");
                return closesCondition ? new Group(inner) : null;
            }
            catch (ApiException ex) when (!ex.Data.Contains(TooDeepKey))
            {
                return null;
            }
        }

        private Node ParseFactor()
        {
            if (++_depth > MaxDepth)
            {
                var tooDeep = Reject($"The filter is nested more than {MaxDepth} levels deep.");
                tooDeep.Data[TooDeepKey] = true;
                throw tooDeep;
            }

            RuntimeHelpers.EnsureSufficientExecutionStack();
            try
            {
                return ParseFactorCore();
            }
            finally
            {
                _depth--;
            }
        }

        private Node ParseFactorCore()
        {
            SkipWhitespace();
            if (TryConsumeKeyword("not"))
            {
                return new Not(ParseFactor());
            }

            SkipWhitespace();
            if (_position < text.Length && text[_position] == '(')
            {
                var start = _position;
                var group = TryParseGroup();
                if (group != null)
                {
                    return group;
                }

                // "(A add 1) gt 2": the parenthesis is an operand of a comparison, not a boolean group
                _position = start;
            }

            return ParseAtom();
        }

        // an atom runs until a top-level 'and' / 'or' / ')' ; parentheses and quotes inside it are balanced
        private Node ParseAtom()
        {
            SkipWhitespace();
            if (NextWordIs("and") || NextWordIs("or"))
            {
                throw Invalid($"Expected a condition at position {_position}.");
            }

            var start = _position;
            var depth = 0;
            while (_position < text.Length)
            {
                var c = text[_position];
                if (c == '\'' || c == '"')
                {
                    SkipQuoted(c);
                    continue;
                }

                if (c == '(')
                {
                    depth++;
                }
                else if (c == ')')
                {
                    if (depth == 0)
                    {
                        break;
                    }

                    depth--;
                }
                else if (depth == 0 && char.IsWhiteSpace(c) && !char.IsWhiteSpace(text[_position - 1])
                         && (NextWordIs("and") || NextWordIs("or")))
                {
                    // only the first blank of a run asks: NextWordIs skips the whole run itself, so asking at each blank is quadratic
                    break;
                }

                _position++;
            }

            if (depth > 0)
            {
                throw Invalid("Missing ')'.");
            }

            var atomText = text[start.._position].Trim();
            if (atomText.Length == 0)
            {
                throw Invalid($"Expected a condition at position {start}.");
            }

            return (Node?)TryAsCustom(atomText) ?? new Atom(atomText);
        }

        private Custom? TryAsCustom(string atomText)
        {
            var open = atomText.IndexOf('(');
            if (open <= 0 || !atomText.EndsWith(')'))
            {
                return null;
            }

            var name = atomText[..open].Trim();
            if (!customFunctions.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                return null;
            }

            var arguments = SplitArguments(atomText[(open + 1)..^1]);
            return new Custom(name, arguments);
        }

        private static List<string> SplitArguments(string arguments)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            var depth = 0;
            char? quote = null;
            foreach (var c in arguments)
            {
                if (quote != null)
                {
                    current.Append(c);
                    if (c == quote)
                    {
                        quote = null;
                    }

                    continue;
                }

                switch (c)
                {
                    case '\'':
                    case '"':
                        quote = c;
                        current.Append(c);
                        break;
                    case '(':
                        depth++;
                        current.Append(c);
                        break;
                    case ')':
                        depth--;
                        current.Append(c);
                        break;
                    case ',' when depth == 0:
                        result.Add(current.ToString().Trim());
                        current.Clear();
                        break;
                    default:
                        current.Append(c);
                        break;
                }
            }

            result.Add(current.ToString().Trim());
            return result;
        }

        private void SkipQuoted(char quote)
        {
            _position++; // opening quote
            while (_position < text.Length)
            {
                if (text[_position] == quote)
                {
                    // a doubled quote is an escaped quote
                    if (_position + 1 < text.Length && text[_position + 1] == quote)
                    {
                        _position += 2;
                        continue;
                    }

                    _position++;
                    return;
                }

                _position++;
            }

            throw Invalid("Unterminated string literal.");
        }

        private bool NextWordIs(string word)
        {
            var i = _position;
            while (i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i++;
            }

            return string.Compare(text, i, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0
                   && (i + word.Length == text.Length
                       || char.IsWhiteSpace(text[i + word.Length]) || text[i + word.Length] == '(');
        }

        private bool TryConsumeKeyword(string word)
        {
            SkipWhitespace();
            if (string.Compare(text, _position, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) != 0)
            {
                return false;
            }

            var end = _position + word.Length;
            if (end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] != '(')
            {
                return false;
            }

            _position = end;
            return true;
        }

        private void SkipWhitespace()
        {
            while (_position < text.Length && char.IsWhiteSpace(text[_position]))
            {
                _position++;
            }
        }

        private static ApiException Invalid(string message) =>
            new($"Invalid $filter. {message}", statusCode: System.Net.HttpStatusCode.BadRequest, code: "InvalidFilter");
    }
}
