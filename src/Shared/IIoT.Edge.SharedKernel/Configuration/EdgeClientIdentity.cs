namespace IIoT.Edge.SharedKernel.Configuration;

public static class EdgeClientIdentity
{
    public static string NormalizeClientCode(string? clientCode)
    {
        if (string.IsNullOrWhiteSpace(clientCode))
        {
            throw new ArgumentException("ClientCode is required.", nameof(clientCode));
        }

        var normalized = clientCode.Trim().ToUpperInvariant();
        if (normalized.Length > 128
            || normalized is "." or ".."
            || normalized.Contains("..", StringComparison.Ordinal)
            || normalized.Any(static character =>
                char.IsControl(character)
                || character is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|'))
        {
            throw new ArgumentException("ClientCode contains unsupported characters.", nameof(clientCode));
        }

        return normalized;
    }

    public static bool EqualsClientCode(string? left, string? right)
    {
        try
        {
            return string.Equals(
                NormalizeClientCode(left),
                NormalizeClientCode(right),
                StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
