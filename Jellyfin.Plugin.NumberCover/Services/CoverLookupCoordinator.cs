using Jellyfin.Plugin.NumberCover.Domain;

namespace Jellyfin.Plugin.NumberCover.Services;

public sealed class CoverLookupCoordinator
{
    private readonly Func<string, CancellationToken, Task<CoverLookupResult>> _jableLookupAsync;
    private readonly Func<string, CancellationToken, Task<CoverLookupResult>> _dmmLookupAsync;

    public CoverLookupCoordinator(
        Func<string, CancellationToken, Task<CoverLookupResult>> jableLookupAsync,
        Func<string, CancellationToken, Task<CoverLookupResult>> dmmLookupAsync)
    {
        _jableLookupAsync = jableLookupAsync ?? throw new ArgumentNullException(nameof(jableLookupAsync));
        _dmmLookupAsync = dmmLookupAsync ?? throw new ArgumentNullException(nameof(dmmLookupAsync));
    }

    public async Task<CoverLookupCandidate?> LookupAsync(string number, CancellationToken cancellationToken)
    {
        var jableResult = await _jableLookupAsync(number, cancellationToken).ConfigureAwait(false);
        if (jableResult.Status == CoverLookupStatus.Success && jableResult.CoverUri is not null)
        {
            return new CoverLookupCandidate(jableResult.CoverUri, CoverSource.Jable);
        }

        if (jableResult.Status is not (CoverLookupStatus.NotFound or CoverLookupStatus.TemporaryFailure))
        {
            return null;
        }

        var dmmResult = await _dmmLookupAsync(number, cancellationToken).ConfigureAwait(false);
        return dmmResult.Status == CoverLookupStatus.Success && dmmResult.CoverUri is not null
            ? new CoverLookupCandidate(dmmResult.CoverUri, CoverSource.Dmm)
            : null;
    }
}
