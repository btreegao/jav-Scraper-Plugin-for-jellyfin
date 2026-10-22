namespace Jellyfin.Plugin.NumberCover.Domain;

public enum CoverSource
{
    Dmm = 1,
    JavDb,
    R18,
}

public sealed record CoverLookupCandidate(Uri CoverUri, CoverSource Source);
