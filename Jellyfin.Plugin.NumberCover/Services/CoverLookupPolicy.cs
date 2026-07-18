namespace Jellyfin.Plugin.NumberCover.Services;

public static class CoverLookupPolicy
{
    public static bool ShouldLookup(bool onlineSourcesEnabled, bool onlyFillMissing, bool hasPrimaryImage) =>
        onlineSourcesEnabled && (!onlyFillMissing || !hasPrimaryImage);
}
