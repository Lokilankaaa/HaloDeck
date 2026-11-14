using System.Text;

namespace HandheldOpti.Core.Discovery;

public sealed class VdfNode
{
    public string? Value { get; set; }
    public Dictionary<string, VdfNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

    public VdfNode? Get(string key) => Children.TryGetValue(key, out var node) ? node : null;

    public string? GetString(string key) => Get(key)?.Value;
}

/// <summary>
/// Parses Valve KeyValues (VDF/ACF) files cleanly while handling escaped quotes,
/// escaped backslashes, comments, and nested blocks.
/// </summary>
public static class VdfParser
{
    public static VdfNode Parse(string content)
    {
        var tokens = Tokenize(content);
        var root = new VdfNode();
        int index = 0;
        ParseBlock(tokens, ref index, root);
        return root;
    }

    private static void ParseBlock(List<Token> tokens, ref int index, VdfNode target)
    {
        while (index < tokens.Count)
        {
            var token = tokens[index];
            if (token.Kind == TokenKind.CloseBrace)
            {
                index++;
                return;
            }

            if (token.Kind != TokenKind.String)
            {
                index++;
                continue;
            }

            string key = token.Text;
            index++;
            if (index >= tokens.Count)
            {
                break;
            }

            var next = tokens[index];
            if (next.Kind == TokenKind.OpenBrace)
            {
                index++;
                var child = target.Children.TryGetValue(key, out var existing) ? existing : new VdfNode();
                target.Children[key] = child;
                ParseBlock(tokens, ref index, child);
            }
            else if (next.Kind == TokenKind.String)
            {
                target.Children[key] = new VdfNode { Value = next.Text };
                index++;
            }
        }
    }

    private enum TokenKind
    {
        String,
        OpenBrace,
        CloseBrace
    }

    private readonly record struct Token(TokenKind Kind, string Text);

    private static List<Token> Tokenize(string content)
    {
        var tokens = new List<Token>();
        int i = 0;
        int len = content.Length;

        while (i < len)
        {
            char c = content[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            // Skip // comments
            if (c == '/' && i + 1 < len && content[i + 1] == '/')
            {
                i += 2;
                while (i < len && content[i] != '\n' && content[i] != '\r')
                {
                    i++;
                }
                continue;
            }

            if (c == '{')
            {
                tokens.Add(new Token(TokenKind.OpenBrace, "{"));
                i++;
                continue;
            }

            if (c == '}')
            {
                tokens.Add(new Token(TokenKind.CloseBrace, "}"));
                i++;
                continue;
            }

            if (c == '"')
            {
                i++;
                var sb = new StringBuilder();
                while (i < len)
                {
                    char ch = content[i];
                    if (ch == '\\' && i + 1 < len)
                    {
                        char escaped = content[i + 1];
                        sb.Append(escaped switch
                        {
                            '\\' => '\\',
                            '"' => '"',
                            'n' => '\n',
                            'r' => '\r',
                            't' => '\t',
                            _ => escaped
                        });
                        i += 2;
                        continue;
                    }
                    if (ch == '"')
                    {
                        i++;
                        break;
                    }
                    sb.Append(ch);
                    i++;
                }
                tokens.Add(new Token(TokenKind.String, sb.ToString()));
                continue;
            }

            // Unquoted token
            var bare = new StringBuilder();
            while (i < len && !char.IsWhiteSpace(content[i]) && content[i] != '{' && content[i] != '}' && content[i] != '"')
            {
                bare.Append(content[i]);
                i++;
            }
            tokens.Add(new Token(TokenKind.String, bare.ToString()));
        }

        return tokens;
    }
}
