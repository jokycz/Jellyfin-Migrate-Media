namespace JellyfinMigrateMedia.Host.Cli;

internal static class ConsolePrompts
{
    public static string Prompt(string label, string current)
    {
        Console.Write($"{label}{(string.IsNullOrWhiteSpace(current) ? "" : $" [{current}]")}: ");
        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input))
            return current;
        return input.Trim();
    }

    public static bool PromptBool(string label, bool current)
    {
        Console.Write($"{label} [{(current ? "Y/n" : "y/N")}]: ");
        var input = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(input))
            return current;

        return input.Equals("y", StringComparison.OrdinalIgnoreCase)
               || input.Equals("yes", StringComparison.OrdinalIgnoreCase)
               || input.Equals("true", StringComparison.OrdinalIgnoreCase)
               || input.Equals("1", StringComparison.OrdinalIgnoreCase);
    }
}

