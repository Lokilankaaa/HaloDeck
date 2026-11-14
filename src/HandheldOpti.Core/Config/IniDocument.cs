using System.Text;

namespace HandheldOpti.Core.Config;

/// <summary>
/// Comment-preserving INI parser and writer tailored for OptiScaler.ini.
/// Preserves existing comments, blank lines, and unknown keys. If a target key exists
/// in a commented-out template form (e.g. ";Dx12Upscaler=auto"), it is uncommented and updated in place.
/// </summary>
public sealed class IniDocument
{
    private readonly List<string> _lines;

    public IniDocument(IEnumerable<string>? lines = null)
    {
        _lines = lines is null ? new List<string>() : new List<string>(lines);
    }

    public static IniDocument Load(string path)
    {
        if (!File.Exists(path))
        {
            return new IniDocument();
        }
        return Parse(File.ReadAllText(path, Encoding.UTF8));
    }

    public static IniDocument Parse(string content)
    {
        var split = content.Replace("\r\n", "\n").Split('\n');
        return new IniDocument(split);
    }

    public string? Get(string section, string key)
    {
        string? currentSection = null;
        foreach (var raw in _lines)
        {
            var trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith('#'))
            {
                continue;
            }

            if (trimmed.StartsWith('[') && trimmed.EndsWith(']') && trimmed.Length >= 2)
            {
                currentSection = trimmed[1..^1].Trim();
                continue;
            }

            if (!string.Equals(currentSection, section, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int eqIdx = trimmed.IndexOf('=');
            if (eqIdx <= 0)
            {
                continue;
            }

            var candidateKey = trimmed[..eqIdx].Trim();
            if (string.Equals(candidateKey, key, StringComparison.OrdinalIgnoreCase))
            {
                return StripInlineComment(trimmed[(eqIdx + 1)..]).Trim();
            }
        }

        return null;
    }

    public void Set(string section, string key, string value)
    {
        string? currentSection = null;
        int sectionStartIndex = -1;
        int sectionInsertIndex = -1;
        int commentedMatchIndex = -1;

        for (int i = 0; i < _lines.Count; i++)
        {
            var raw = _lines[i];
            var trimmed = raw.Trim();

            if (trimmed.StartsWith('[') && trimmed.EndsWith(']') && trimmed.Length >= 2)
            {
                if (sectionStartIndex != -1 && sectionInsertIndex == -1)
                {
                    sectionInsertIndex = i;
                }

                currentSection = trimmed[1..^1].Trim();
                if (string.Equals(currentSection, section, StringComparison.OrdinalIgnoreCase))
                {
                    sectionStartIndex = i;
                }
                continue;
            }

            if (!string.Equals(currentSection, section, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed.StartsWith(';') || trimmed.StartsWith('#'))
            {
                var uncommented = trimmed[1..].TrimStart();
                int eqC = uncommented.IndexOf('=');
                if (eqC > 0)
                {
                    var cKey = uncommented[..eqC].Trim();
                    if (string.Equals(cKey, key, StringComparison.OrdinalIgnoreCase) && commentedMatchIndex == -1)
                    {
                        commentedMatchIndex = i;
                    }
                }
                continue;
            }

            int eqIdx = trimmed.IndexOf('=');
            if (eqIdx > 0)
            {
                var candidateKey = trimmed[..eqIdx].Trim();
                if (string.Equals(candidateKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    _lines[i] = $"{key}={value}";
                    return;
                }
            }
        }

        if (commentedMatchIndex != -1)
        {
            _lines[commentedMatchIndex] = $"{key}={value}";
            return;
        }

        if (sectionStartIndex != -1)
        {
            int insertAt = sectionInsertIndex != -1 ? sectionInsertIndex : _lines.Count;
            // Insert before trailing blank lines of the section
            while (insertAt > sectionStartIndex + 1 && string.IsNullOrWhiteSpace(_lines[insertAt - 1]))
            {
                insertAt--;
            }
            _lines.Insert(insertAt, $"{key}={value}");
            return;
        }

        if (_lines.Count > 0 && !string.IsNullOrWhiteSpace(_lines[^1]))
        {
            _lines.Add(string.Empty);
        }
        _lines.Add($"[{section}]");
        _lines.Add($"{key}={value}");
    }

    public string ToIniString()
    {
        return string.Join("\r\n", _lines) + "\r\n";
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(path, ToIniString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string StripInlineComment(string rawValue)
    {
        // Only strip ';' comments preceded by whitespace so hex values or paths remain intact
        for (int i = 0; i < rawValue.Length; i++)
        {
            if (rawValue[i] == ';' && (i == 0 || char.IsWhiteSpace(rawValue[i - 1])))
            {
                return rawValue[..i];
            }
        }
        return rawValue;
    }
}
