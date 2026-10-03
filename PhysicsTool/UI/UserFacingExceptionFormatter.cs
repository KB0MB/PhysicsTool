using System.Text.RegularExpressions;

namespace HKCLTool;

internal static partial class UserFacingExceptionFormatter
{
    public static string Format(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var messages = new List<string>();
        for (var current = exception; current != null; current = current.InnerException)
        {
            var message = RedactLocalPaths(current.Message).Trim();
            if (message.Length == 0)
                continue;
            if (!messages.Contains(message, StringComparer.Ordinal))
                messages.Add(message);
        }

        if (messages.Count == 0)
            messages.Add("PhysicsTool hit an unexpected error.");

        return string.Join(Environment.NewLine + Environment.NewLine, messages) +
            Environment.NewLine + Environment.NewLine +
            $"Error type: {exception.GetType().Name}";
    }

    private static string RedactLocalPaths(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        text = WindowsDrivePath().Replace(text, match => Path.GetFileName(match.Value.TrimEnd('\\', '/', '\'', '"')));
        return UncPath().Replace(text, match => Path.GetFileName(match.Value.TrimEnd('\\', '/', '\'', '"')));
    }

    [GeneratedRegex(@"[A-Za-z]:[\\/][^\r\n'""<>|]*", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsDrivePath();

    [GeneratedRegex(@"\\\\[^\r\n'""<>|]+", RegexOptions.CultureInvariant)]
    private static partial Regex UncPath();
}
