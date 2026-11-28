namespace HaloDeck.Core.Services;

/// <summary>
/// Pure-logic helper for removing legacy Windows On-Screen Keyboard ("osk") tokens from
/// HKCU\Software\Microsoft\Windows NT\CurrentVersion\Accessibility\Configuration
/// while preserving any other enabled accessibility utilities (e.g. narrator, magnifier).
/// </summary>
public static class AccessibilityOskStateCleaner
{
    public static string RemoveOskFromConfiguration(string? rawConfig, out bool changed)
    {
        if (string.IsNullOrEmpty(rawConfig))
        {
            changed = false;
            return string.Empty;
        }

        string[] parts = rawConfig.Split(',', StringSplitOptions.None);
        var kept = new List<string>(parts.Length);
        bool removedAny = false;

        foreach (string part in parts)
        {
            string trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            if (string.Equals(trimmed, "osk", StringComparison.OrdinalIgnoreCase))
            {
                removedAny = true;
                continue;
            }

            kept.Add(trimmed);
        }

        if (!removedAny)
        {
            changed = false;
            return rawConfig;
        }

        changed = true;
        return string.Join(",", kept);
    }
}
