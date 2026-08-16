namespace Cooldown;

internal static class Vdf
{
    public static Dictionary<string, object> Parse(string text)
    {
        var tokens = Tokenize(text);
        var index = 0;

        Dictionary<string, object> ParseObject()
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            while (index < tokens.Count)
            {
                var token = tokens[index];
                if (token == "}")
                {
                    index++;
                    break;
                }
                var key = token;
                index++;
                if (index >= tokens.Count) break;
                var next = tokens[index];
                if (next == "{")
                {
                    index++;
                    result[key] = ParseObject();
                }
                else
                {
                    result[key] = next;
                    index++;
                }
            }
            return result;
        }

        if (tokens.Count > 0 && tokens[0] != "{")
        {
            var key = tokens[0];
            index = 1;
            if (index < tokens.Count && tokens[index] == "{")
            {
                index++;
                return new Dictionary<string, object> { [key] = ParseObject() };
            }
        }
        if (tokens.Count > 0 && tokens[0] == "{")
        {
            index = 1;
            return ParseObject();
        }
        return ParseObject();
    }

    public static string Get(Dictionary<string, object> node, string key)
    {
        return node.TryGetValue(key, out var value) ? value?.ToString() ?? "" : "";
    }

    public static Dictionary<string, object>? Child(Dictionary<string, object> node, string key)
    {
        return node.TryGetValue(key, out var value) ? value as Dictionary<string, object> : null;
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '\\' && i + 1 < text.Length)
                {
                    current.Append(text[i + 1]);
                    i++;
                    continue;
                }
                if (c == '"')
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    inQuotes = false;
                }
                else current.Append(c);
                continue;
            }
            if (c == '"')
            {
                inQuotes = true;
                continue;
            }
            if (c is '{' or '}')
            {
                tokens.Add(c.ToString());
                continue;
            }
        }
        return tokens;
    }
}
