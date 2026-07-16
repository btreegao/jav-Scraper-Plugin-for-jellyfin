namespace Jellyfin.Plugin.NumberCover.Domain;

public enum CoverSource
{
    Jable,
    Dmm,
}

public sealed record CoverLookupCandidate(Uri CoverUri, CoverSource Source);
