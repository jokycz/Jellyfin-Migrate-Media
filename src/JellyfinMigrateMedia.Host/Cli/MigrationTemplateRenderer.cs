namespace JellyfinMigrateMedia.Host.Cli;

internal static class MigrationTemplateRenderer
{
    public static string Render(
        string template,
        string movieName,
        string originalName,
        int? year,
        string extensionNoDot)
    {
        template ??= "";

        var firstChar = GetFirstChar(movieName);
        var y = year?.ToString() ?? "";

        // Very small templating system for current needs.
        return template
            .Replace("{MovieName[0]}", firstChar, StringComparison.OrdinalIgnoreCase)
            .Replace("{MovieName}", movieName ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{OriginalName}", originalName ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{Year}", y, StringComparison.OrdinalIgnoreCase)
            .Replace("{Extension}", extensionNoDot ?? "", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetFirstChar(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return "_";

        var trimmed = s.Trim();
        var ch = trimmed[0];
        return ch.ToString();
    }
}

