namespace Jellyfin.Plugin.NumberCover.Services;

public static class RemoteCoverUriValidator
{
    public static bool IsAllowed(Uri? uri) =>
        uri is not null &&
        (DmmImageUri.IsAllowed(uri)
            || JavDbCoverLookupService.IsImageUriAllowed(uri) || R18CoverLookupService.IsImageUriAllowed(uri));
}
