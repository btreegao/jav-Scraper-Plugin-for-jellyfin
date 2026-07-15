namespace Jellyfin.Plugin.NumberCover.Domain;

public enum CoverLookupStatus
{
    Success,
    NotFound,
    TemporaryFailure,
}

public sealed class CoverLookupResult
{
    private CoverLookupResult(CoverLookupStatus status, Uri? coverUri)
    {
        Status = status;
        CoverUri = coverUri;
    }

    public CoverLookupStatus Status { get; }

    public Uri? CoverUri { get; }

    public static CoverLookupResult Success(Uri coverUri)
    {
        ArgumentNullException.ThrowIfNull(coverUri);

        if (!coverUri.IsAbsoluteUri ||
            !string.Equals(coverUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(coverUri.Host))
        {
            throw new ArgumentException("The cover URI must be an absolute HTTPS URI.", nameof(coverUri));
        }

        return new CoverLookupResult(CoverLookupStatus.Success, coverUri);
    }

    public static CoverLookupResult NotFound() => new(CoverLookupStatus.NotFound, null);

    public static CoverLookupResult TemporaryFailure() => new(CoverLookupStatus.TemporaryFailure, null);
}
