namespace Jellyfin.Plugin.NumberCover.Services;

public static class RemoteCoverUriValidator
{
    public static bool IsAllowed(Uri? uri) =>
        uri is not null &&
        (JableCoverLookupService.IsJableHost(uri) || DmmImageUri.IsAllowed(uri));
}
