using Jellyfin.Plugin.NumberCover.Domain;

namespace Jellyfin.Plugin.NumberCover.Services;

public sealed class CoverLookupCoordinator
{
    private readonly (CoverSource Source, Func<string, CancellationToken, Task<CoverLookupResult>> Lookup)[] _sources;

    public CoverLookupCoordinator(
        Func<string, CancellationToken, Task<CoverLookupResult>> r18LookupAsync,
        Func<string, CancellationToken, Task<CoverLookupResult>> javDbLookupAsync,
        Func<string, CancellationToken, Task<CoverLookupResult>> dmmLookupAsync)
    {
        ArgumentNullException.ThrowIfNull(r18LookupAsync);
        ArgumentNullException.ThrowIfNull(javDbLookupAsync);
        ArgumentNullException.ThrowIfNull(dmmLookupAsync);
        _sources =
        [
            (CoverSource.R18, r18LookupAsync),
            (CoverSource.JavDb, javDbLookupAsync),
            (CoverSource.Dmm, dmmLookupAsync),
        ];
    }

    public async Task<CoverLookupCandidate?> LookupAsync(string number, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var (source, lookup) in _sources)
        {
            var result = await lookup(number, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (result.Status == CoverLookupStatus.Success && result.CoverUri is not null)
            {
                return new CoverLookupCandidate(result.CoverUri, source);
            }
        }
        return null;
    }
}
