using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

internal static class SecretIdentifierRecognition
{
    private static readonly string[] Suffixes = ["TokenName", "Name", "Path", "Url", "Protector"];

    public static bool IsIdentifier(string name, string value)
    {
        // Require a literal that spells the declaration's descriptive name. A suffix
        // alone never exempts random credentials, JWTs or connection-string payloads.
        if (value.Any(character => !char.IsLetter(character) && character is not ('.' or '-' or '_')))
            return false;
        var terminal = value[(value.LastIndexOf('.') + 1)..];
        var normalizedValue = Normalize(terminal);
        if (Normalize(name) == normalizedValue) return true;
        return Suffixes.Any(suffix => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
            Normalize(name[..^suffix.Length]) == normalizedValue);
    }

    private static string Normalize(string text) =>
        new(text.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
