namespace RecipeParser.Domain.Discovery;

public static class DiscoveryUrlNormalizer
{
    public static string? NormalizeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
            return null;

        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = NormalizeDomain(uri.Host),
            Query = string.Empty,
            Fragment = string.Empty
        };

        var path = builder.Path.TrimEnd('/');
        builder.Path = string.IsNullOrWhiteSpace(path) ? "/" : path;
        return builder.Uri.AbsoluteUri.TrimEnd('/');
    }

    public static string? DomainFromUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
            return null;

        return NormalizeDomain(uri.Host);
    }

    public static string? NormalizeDomain(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var domain = value.Trim().ToLowerInvariant();
        if (domain.StartsWith("www.", StringComparison.Ordinal))
            domain = domain[4..];

        return domain.Length == 0 ? null : domain;
    }
}
