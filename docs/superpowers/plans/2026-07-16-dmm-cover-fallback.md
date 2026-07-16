# Number Cover DMM Automatic Fallback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Upgrade Number Cover to 0.1.1 so every recognized identifier tries Jable first and automatically falls back to a validated deterministic DMM cover URL.

**Architecture:** Keep the stable Jellyfin provider name and saved configuration field, but compose Jable and DMM lookup services behind a source-aware coordinator. Both services share one cache and sliding-window limiter, use source-qualified cache keys, and expose only HTTPS URLs accepted by a separate final-download validator.

**Tech Stack:** C# 13, .NET 9, Jellyfin Server 10.11.11 plugin APIs, `HttpClient`, xUnit 2.9.3, PowerShell packaging and local Jellyfin deployment.

---

## File map

- Create `Jellyfin.Plugin.NumberCover/Services/DmmImageUri.cs`: deterministic identifier conversion and strict DMM URL validation.
- Create `Jellyfin.Plugin.NumberCover/Services/DmmCoverLookupService.cs`: rate-limited, cached DMM response validation.
- Create `Jellyfin.Plugin.NumberCover/Domain/CoverLookupCandidate.cs`: source-tagged successful lookup value.
- Create `Jellyfin.Plugin.NumberCover/Services/CoverLookupCoordinator.cs`: Jable-first/DMM-second fallback sequence.
- Create `Jellyfin.Plugin.NumberCover/Services/RemoteCoverUriValidator.cs`: final image-download allowlist shared by the provider and tests.
- Create `Jellyfin.Plugin.NumberCover/Services/CoverLookupPolicy.cs`: testable online-source and existing-cover gate.
- Modify `Jellyfin.Plugin.NumberCover/Services/JableCoverLookupService.cs`: accept a shared limiter and use a Jable-qualified cache key.
- Modify `Jellyfin.Plugin.NumberCover/Providers/JableImageProvider.cs`: compose both sources and download only validated URLs.
- Modify `Jellyfin.Plugin.NumberCover/Configuration/configPage.html`: describe the existing switch as enabling Jable plus DMM fallback.
- Modify `Jellyfin.Plugin.NumberCover/Jellyfin.Plugin.NumberCover.csproj`: advance assembly/package version to 0.1.1.
- Modify `README.md`: document fallback behavior, safety, installation, and operation.
- Create `Jellyfin.Plugin.NumberCover.Tests/DmmImageUriTests.cs`: mapping and URI-security tests.
- Create `Jellyfin.Plugin.NumberCover.Tests/DmmCoverLookupServiceTests.cs`: DMM status, media-type, cache, timeout, and cancellation tests.
- Create `Jellyfin.Plugin.NumberCover.Tests/CoverLookupCoordinatorTests.cs`: fallback-order and source-tag tests.
- Create `Jellyfin.Plugin.NumberCover.Tests/OnlineSourceCompositionTests.cs`: shared cache and global request-limit tests.
- Create `Jellyfin.Plugin.NumberCover.Tests/RemoteCoverUriValidatorTests.cs`: final download allowlist tests.
- Create `Jellyfin.Plugin.NumberCover.Tests/CoverLookupPolicyTests.cs`: disabled-source and only-fill-missing tests.
- Modify `Jellyfin.Plugin.NumberCover.Tests/ConfigurationPageTests.cs`: settings-label compatibility contract.
- Create `Jellyfin.Plugin.NumberCover.Tests/ProjectVersionTests.cs`: release version contract.
- Create `outputs/NumberCover-v0.1.1.zip`: verified distributable containing the Release DLL and README.

### Task 1: Define deterministic DMM URI mapping and validation

**Files:**
- Create: `Jellyfin.Plugin.NumberCover.Tests/DmmImageUriTests.cs`
- Create: `Jellyfin.Plugin.NumberCover/Services/DmmImageUri.cs`

- [ ] **Step 1: Write failing mapping and security tests**

Create `Jellyfin.Plugin.NumberCover.Tests/DmmImageUriTests.cs`:

```csharp
using Jellyfin.Plugin.NumberCover.Services;
using Xunit;

namespace Jellyfin.Plugin.NumberCover.Tests;

public class DmmImageUriTests
{
    [Theory]
    [InlineData("IPZZ-034", "ipzz00034")]
    [InlineData("ABC-12", "abc00012")]
    [InlineData("ABC-12345", "abc12345")]
    [InlineData("ABC-123456", "abc123456")]
    public void Build_maps_a_standalone_number_to_the_digital_cover(string number, string contentId)
    {
        var uri = DmmImageUri.Build(number);

        Assert.Equal(
            $"https://pics.dmm.co.jp/digital/video/{contentId}/{contentId}pl.jpg",
            uri.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("title IPZZ-034")]
    [InlineData("IPZZ-1")]
    public void Build_rejects_a_value_that_is_not_a_standalone_normalized_number(string number)
    {
        Assert.Throws<ArgumentException>(() => DmmImageUri.Build(number));
    }

    [Fact]
    public void IsAllowed_accepts_only_the_exact_deterministic_shape()
    {
        Assert.True(DmmImageUri.IsAllowed(
            new Uri("https://pics.dmm.co.jp/digital/video/ipzz00034/ipzz00034pl.jpg")));
    }

    [Theory]
    [InlineData("http://pics.dmm.co.jp/digital/video/ipzz00034/ipzz00034pl.jpg")]
    [InlineData("https://pics.dmm.co.jp.evil.test/digital/video/ipzz00034/ipzz00034pl.jpg")]
    [InlineData("https://user@pics.dmm.co.jp/digital/video/ipzz00034/ipzz00034pl.jpg")]
    [InlineData("https://pics.dmm.co.jp:444/digital/video/ipzz00034/ipzz00034pl.jpg")]
    [InlineData("https://pics.dmm.co.jp/mono/movie/ipzz00034/ipzz00034pl.jpg")]
    [InlineData("https://pics.dmm.co.jp/digital/video/ipzz00034/otherpl.jpg")]
    [InlineData("https://pics.dmm.co.jp/digital/video/ipzz00034/ipzz00034pl.jpg?next=https://evil.test")]
    public void IsAllowed_rejects_non_deterministic_or_unsafe_urls(string value)
    {
        Assert.False(DmmImageUri.IsAllowed(new Uri(value)));
    }
}
```

- [ ] **Step 2: Run the focused tests and verify the red state**

Run:

```powershell
dotnet test .\Jellyfin.Plugin.NumberCover.Tests\Jellyfin.Plugin.NumberCover.Tests.csproj --no-restore --filter FullyQualifiedName~DmmImageUriTests
```

Expected: compilation fails because `DmmImageUri` does not exist.

- [ ] **Step 3: Implement the minimal deterministic URI component**

Create `Jellyfin.Plugin.NumberCover/Services/DmmImageUri.cs`:

```csharp
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.NumberCover.Services;

public static partial class DmmImageUri
{
    private static readonly Uri DigitalVideoBaseUri = new("https://pics.dmm.co.jp/digital/video/");

    public static Uri Build(string number)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);

        var normalized = NumberParser.Parse(number);
        if (normalized is null
            || !string.Equals(normalized, number.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The number must be a standalone normalized identifier.",
                nameof(number));
        }

        var separator = normalized.IndexOf("-", StringComparison.Ordinal);
        var prefix = normalized[..separator].ToLowerInvariant();
        var digits = normalized[(separator + 1)..].PadLeft(5, '0');
        var contentId = prefix + digits;

        return new Uri(DigitalVideoBaseUri, $"{contentId}/{contentId}pl.jpg");
    }

    public static bool IsAllowed(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, "pics.dmm.co.jp", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 4
            || !string.Equals(segments[0], "digital", StringComparison.Ordinal)
            || !string.Equals(segments[1], "video", StringComparison.Ordinal))
        {
            return false;
        }

        var contentId = segments[2];
        return ContentIdPattern().IsMatch(contentId)
            && string.Equals(segments[3], contentId + "pl.jpg", StringComparison.Ordinal);
    }

    [GeneratedRegex("^[a-z]{2,8}[0-9]{5,6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ContentIdPattern();
}
```

- [ ] **Step 4: Run the focused tests and verify the green state**

Run the Step 2 command again.

Expected: 15 tests pass and 0 fail.

- [ ] **Step 5: Commit the mapping component**

```powershell
git add -- Jellyfin.Plugin.NumberCover.Tests\DmmImageUriTests.cs Jellyfin.Plugin.NumberCover\Services\DmmImageUri.cs
git commit -m "feat: map identifiers to safe DMM cover URLs"
```

### Task 2: Implement cached DMM response validation

**Files:**
- Create: `Jellyfin.Plugin.NumberCover.Tests/DmmCoverLookupServiceTests.cs`
- Create: `Jellyfin.Plugin.NumberCover/Services/DmmCoverLookupService.cs`

- [ ] **Step 1: Write failing DMM lookup tests**

Create `Jellyfin.Plugin.NumberCover.Tests/DmmCoverLookupServiceTests.cs`:

```csharp
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Jellyfin.Plugin.NumberCover.Domain;
using Jellyfin.Plugin.NumberCover.Services;
using Xunit;

namespace Jellyfin.Plugin.NumberCover.Tests;

public class DmmCoverLookupServiceTests
{
    [Fact]
    public async Task LookupAsync_accepts_and_caches_an_http_200_image()
    {
        var calls = 0;
        var service = CreateService((_, _) =>
        {
            calls++;
            return Task.FromResult(ImageResponse());
        });

        var first = await service.LookupAsync("IPZZ-034", CancellationToken.None);
        var second = await service.LookupAsync("ipzz-034", CancellationToken.None);

        Assert.Equal(CoverLookupStatus.Success, first.Status);
        Assert.Equal("https://pics.dmm.co.jp/digital/video/ipzz00034/ipzz00034pl.jpg", first.CoverUri?.AbsoluteUri);
        Assert.Equal(CoverLookupStatus.Success, second.Status);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Found, "image/jpeg")]
    [InlineData(HttpStatusCode.NotFound, "image/jpeg")]
    [InlineData(HttpStatusCode.OK, "text/html")]
    public async Task LookupAsync_treats_redirect_missing_or_html_candidates_as_not_found(
        HttpStatusCode statusCode,
        string mediaType)
    {
        var service = CreateService((_, _) => Task.FromResult(Response(statusCode, mediaType)));

        var result = await service.LookupAsync("IPZZ-034", CancellationToken.None);

        Assert.Equal(CoverLookupStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task LookupAsync_treats_server_errors_as_temporary()
    {
        var service = CreateService((_, _) =>
            Task.FromResult(Response(HttpStatusCode.ServiceUnavailable, "text/html")));

        var result = await service.LookupAsync("IPZZ-034", CancellationToken.None);

        Assert.Equal(CoverLookupStatus.TemporaryFailure, result.Status);
    }

    [Fact]
    public async Task LookupAsync_treats_internal_timeout_cancellation_as_temporary()
    {
        var service = CreateService((_, _) => throw new OperationCanceledException());

        var result = await service.LookupAsync("IPZZ-034", CancellationToken.None);

        Assert.Equal(CoverLookupStatus.TemporaryFailure, result.Status);
    }

    [Fact]
    public async Task LookupAsync_propagates_caller_cancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var service = CreateService((_, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(ImageResponse());
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.LookupAsync("IPZZ-034", cancellationSource.Token));
    }

    private static DmmCoverLookupService CreateService(
        Func<Uri, CancellationToken, Task<HttpResponseMessage>> fetch) =>
        new(fetch, maxRequestsPerMinute: 12, timeoutSeconds: 10);

    private static HttpResponseMessage ImageResponse() =>
        Response(HttpStatusCode.OK, "image/jpeg");

    private static HttpResponseMessage Response(HttpStatusCode statusCode, string mediaType)
    {
        var content = new ByteArrayContent([0xFF, 0xD8, 0xFF]);
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return new HttpResponseMessage(statusCode) { Content = content };
    }
}
```

- [ ] **Step 2: Run the focused tests and verify the red state**

Run:

```powershell
dotnet test .\Jellyfin.Plugin.NumberCover.Tests\Jellyfin.Plugin.NumberCover.Tests.csproj --no-restore --filter FullyQualifiedName~DmmCoverLookupServiceTests
```

Expected: compilation fails because `DmmCoverLookupService` does not exist.

- [ ] **Step 3: Implement the lookup service**

Create `Jellyfin.Plugin.NumberCover/Services/DmmCoverLookupService.cs`:

```csharp
using System.Net;
using Jellyfin.Plugin.NumberCover.Domain;

namespace Jellyfin.Plugin.NumberCover.Services;

public sealed class DmmCoverLookupService
{
    private const string CachePrefix = "dmm:";

    private readonly Func<Uri, CancellationToken, Task<HttpResponseMessage>> _fetchImageAsync;
    private readonly CoverLookupCache _cache;
    private readonly SlidingWindowRateLimiter _rateLimiter;
    private readonly TimeSpan _timeout;

    public DmmCoverLookupService(
        Func<Uri, CancellationToken, Task<HttpResponseMessage>> fetchImageAsync,
        int maxRequestsPerMinute,
        int timeoutSeconds,
        CoverLookupCache? cache = null,
        TimeProvider? timeProvider = null,
        SlidingWindowRateLimiter? rateLimiter = null)
    {
        _fetchImageAsync = fetchImageAsync ?? throw new ArgumentNullException(nameof(fetchImageAsync));
        _cache = cache ?? new CoverLookupCache(timeProvider);
        _rateLimiter = rateLimiter ?? new SlidingWindowRateLimiter(maxRequestsPerMinute, timeProvider);
        _timeout = TimeSpan.FromSeconds(timeoutSeconds > 0 ? timeoutSeconds : 10);
    }

    public async Task<CoverLookupResult> LookupAsync(string number, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        var cacheKey = CachePrefix + number;

        if (_cache.TryGet(cacheKey, out var cached))
        {
            return cached!;
        }

        try
        {
            await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(_timeout);
            var imageUri = DmmImageUri.Build(number);
            using var response = await _fetchImageAsync(imageUri, timeoutSource.Token).ConfigureAwait(false);
            var statusCode = (int)response.StatusCode;
            var isImage = response.Content.Headers.ContentType?.MediaType?
                .StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;

            var result = response.StatusCode == HttpStatusCode.OK && isImage
                ? CoverLookupResult.Success(imageUri)
                : statusCode is >= 300 and < 400 || response.StatusCode == HttpStatusCode.NotFound
                    || response.StatusCode == HttpStatusCode.OK
                    ? CoverLookupResult.NotFound()
                    : CoverLookupResult.TemporaryFailure();

            Trace.TraceInformation(
                "Number Cover DMM lookup for {0}: {1}",
                number,
                result.Status);
            _cache.Set(cacheKey, result);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            var result = CoverLookupResult.TemporaryFailure();
            Trace.TraceWarning("Number Cover DMM lookup timed out for {0}.", number);
            _cache.Set(cacheKey, result);
            return result;
        }
        catch (Exception exception)
        {
            var result = CoverLookupResult.TemporaryFailure();
            Trace.TraceWarning(
                "Number Cover DMM lookup failed for {0}: {1}",
                number,
                exception.GetType().Name);
            _cache.Set(cacheKey, result);
            return result;
        }
    }
}
```

- [ ] **Step 4: Run the focused tests and verify the green state**

Run the Step 2 command again.

Expected: 7 tests pass and 0 fail.

- [ ] **Step 5: Commit the DMM lookup service**

```powershell
git add -- Jellyfin.Plugin.NumberCover.Tests\DmmCoverLookupServiceTests.cs Jellyfin.Plugin.NumberCover\Services\DmmCoverLookupService.cs
git commit -m "feat: validate and cache DMM cover candidates"
```

### Task 3: Add source-aware fallback and shared request controls

**Files:**
- Create: `Jellyfin.Plugin.NumberCover/Domain/CoverLookupCandidate.cs`
- Create: `Jellyfin.Plugin.NumberCover/Services/CoverLookupCoordinator.cs`
- Modify: `Jellyfin.Plugin.NumberCover/Services/JableCoverLookupService.cs`
- Create: `Jellyfin.Plugin.NumberCover.Tests/CoverLookupCoordinatorTests.cs`
- Create: `Jellyfin.Plugin.NumberCover.Tests/OnlineSourceCompositionTests.cs`

- [ ] **Step 1: Write failing fallback-order tests**

Create `Jellyfin.Plugin.NumberCover.Tests/CoverLookupCoordinatorTests.cs`:

```csharp
using Jellyfin.Plugin.NumberCover.Domain;
using Jellyfin.Plugin.NumberCover.Services;
using Xunit;

namespace Jellyfin.Plugin.NumberCover.Tests;

public class CoverLookupCoordinatorTests
{
    private static readonly Uri JableUri = new("https://assets-cdn.jable.tv/cover.jpg");
    private static readonly Uri DmmUri = new("https://pics.dmm.co.jp/digital/video/ipzz00034/ipzz00034pl.jpg");

    [Fact]
    public async Task LookupAsync_returns_Jable_without_calling_DMM()
    {
        var dmmCalls = 0;
        var coordinator = new CoverLookupCoordinator(
            (_, _) => Task.FromResult(CoverLookupResult.Success(JableUri)),
            (_, _) =>
            {
                dmmCalls++;
                return Task.FromResult(CoverLookupResult.Success(DmmUri));
            });

        var candidate = await coordinator.LookupAsync("IPZZ-034", CancellationToken.None);

        Assert.Equal(CoverSource.Jable, candidate?.Source);
        Assert.Equal(JableUri, candidate?.CoverUri);
        Assert.Equal(0, dmmCalls);
    }

    [Theory]
    [InlineData(CoverLookupStatus.NotFound)]
    [InlineData(CoverLookupStatus.TemporaryFailure)]
    public async Task LookupAsync_falls_back_to_DMM_for_every_Jable_non_success(CoverLookupStatus status)
    {
        var coordinator = new CoverLookupCoordinator(
            (_, _) => Task.FromResult(Result(status)),
            (_, _) => Task.FromResult(CoverLookupResult.Success(DmmUri)));

        var candidate = await coordinator.LookupAsync("IPZZ-034", CancellationToken.None);

        Assert.Equal(CoverSource.Dmm, candidate?.Source);
        Assert.Equal(DmmUri, candidate?.CoverUri);
    }

    [Fact]
    public async Task LookupAsync_returns_null_when_neither_source_succeeds()
    {
        var coordinator = new CoverLookupCoordinator(
            (_, _) => Task.FromResult(CoverLookupResult.NotFound()),
            (_, _) => Task.FromResult(CoverLookupResult.TemporaryFailure()));

        Assert.Null(await coordinator.LookupAsync("IPZZ-034", CancellationToken.None));
    }

    private static CoverLookupResult Result(CoverLookupStatus status) => status switch
    {
        CoverLookupStatus.NotFound => CoverLookupResult.NotFound(),
        CoverLookupStatus.TemporaryFailure => CoverLookupResult.TemporaryFailure(),
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}
```

Create `Jellyfin.Plugin.NumberCover.Tests/OnlineSourceCompositionTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using Jellyfin.Plugin.NumberCover.Domain;
using Jellyfin.Plugin.NumberCover.Services;
using Xunit;

namespace Jellyfin.Plugin.NumberCover.Tests;

public class OnlineSourceCompositionTests
{
    [Fact]
    public async Task Shared_cache_keeps_Jable_and_DMM_results_separate()
    {
        var cache = new CoverLookupCache();
        var jableCalls = 0;
        var dmmCalls = 0;
        var jable = new JableCoverLookupService(
            (_, _) => { jableCalls++; return Task.FromResult("<html></html>"); },
            12,
            10,
            cache);
        var dmm = new DmmCoverLookupService(
            (_, _) => { dmmCalls++; return Task.FromResult(ImageResponse()); },
            12,
            10,
            cache);

        Assert.Equal(CoverLookupStatus.NotFound, (await jable.LookupAsync("IPZZ-034", CancellationToken.None)).Status);
        Assert.Equal(CoverLookupStatus.Success, (await dmm.LookupAsync("IPZZ-034", CancellationToken.None)).Status);
        await jable.LookupAsync("ipzz-034", CancellationToken.None);
        await dmm.LookupAsync("ipzz-034", CancellationToken.None);

        Assert.Equal(1, jableCalls);
        Assert.Equal(1, dmmCalls);
    }

    [Fact]
    public async Task Shared_limiter_caps_the_combined_sources()
    {
        var timeProvider = new RateLimiterTestTimeProvider();
        var limiter = new SlidingWindowRateLimiter(1, timeProvider);
        var jable = new JableCoverLookupService(
            (_, _) => Task.FromResult("<meta property='og:image' content='https://jable.tv/cover.jpg'>"),
            1,
            10,
            timeProvider: timeProvider,
            rateLimiter: limiter);
        var dmm = new DmmCoverLookupService(
            (_, _) => Task.FromResult(ImageResponse()),
            1,
            10,
            timeProvider: timeProvider,
            rateLimiter: limiter);

        await jable.LookupAsync("ABC-123", CancellationToken.None);
        var waiting = dmm.LookupAsync("IPZZ-034", CancellationToken.None);
        Assert.False(waiting.IsCompleted);

        timeProvider.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(CoverLookupStatus.Success, (await waiting).Status);
    }

    private static HttpResponseMessage ImageResponse()
    {
        var content = new ByteArrayContent([0xFF, 0xD8, 0xFF]);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }
}
```

- [ ] **Step 2: Run the new tests and verify the red state**

Run:

```powershell
dotnet test .\Jellyfin.Plugin.NumberCover.Tests\Jellyfin.Plugin.NumberCover.Tests.csproj --no-restore --filter "FullyQualifiedName~CoverLookupCoordinatorTests|FullyQualifiedName~OnlineSourceCompositionTests"
```

Expected: compilation fails because the coordinator/source types and Jable `rateLimiter` parameter do not exist.

- [ ] **Step 3: Add the source-tagged candidate**

Create `Jellyfin.Plugin.NumberCover/Domain/CoverLookupCandidate.cs`:

```csharp
namespace Jellyfin.Plugin.NumberCover.Domain;

public enum CoverSource
{
    Jable,
    Dmm,
}

public sealed record CoverLookupCandidate(Uri CoverUri, CoverSource Source);
```

- [ ] **Step 4: Add the fallback coordinator**

Create `Jellyfin.Plugin.NumberCover/Services/CoverLookupCoordinator.cs`:

```csharp
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
        var jable = await _jableLookupAsync(number, cancellationToken).ConfigureAwait(false);
        if (jable.Status == CoverLookupStatus.Success && jable.CoverUri is not null)
        {
            return new CoverLookupCandidate(jable.CoverUri, CoverSource.Jable);
        }

        var dmm = await _dmmLookupAsync(number, cancellationToken).ConfigureAwait(false);
        return dmm.Status == CoverLookupStatus.Success && dmm.CoverUri is not null
            ? new CoverLookupCandidate(dmm.CoverUri, CoverSource.Dmm)
            : null;
    }
}
```

- [ ] **Step 5: Make Jable use the shared cache namespace and limiter**

In `JableCoverLookupService.cs`, add:

```csharp
private const string CachePrefix = "jable:";
```

Change the constructor tail and limiter assignment to:

```csharp
CoverLookupCache? cache = null,
TimeProvider? timeProvider = null,
SlidingWindowRateLimiter? rateLimiter = null)
{
    _fetchPageAsync = fetchPageAsync ?? throw new ArgumentNullException(nameof(fetchPageAsync));
    _cache = cache ?? new CoverLookupCache(timeProvider);
    _rateLimiter = rateLimiter ?? new SlidingWindowRateLimiter(maxRequestsPerMinute, timeProvider);
    _timeout = TimeSpan.FromSeconds(timeoutSeconds > 0 ? timeoutSeconds : 10);
}
```

At the start of `LookupAsync`, after argument validation, add:

```csharp
var cacheKey = CachePrefix + number;
```

Replace all three `_cache.Set(number, result)` calls with:

```csharp
_cache.Set(cacheKey, result);
```

Replace `_cache.TryGet(number, out var cached)` with:

```csharp
_cache.TryGet(cacheKey, out var cached)
```

- [ ] **Step 6: Run the focused and existing Jable tests**

Run:

```powershell
dotnet test .\Jellyfin.Plugin.NumberCover.Tests\Jellyfin.Plugin.NumberCover.Tests.csproj --no-restore --filter "FullyQualifiedName~CoverLookupCoordinatorTests|FullyQualifiedName~OnlineSourceCompositionTests|FullyQualifiedName~JableCoverLookupServiceTests"
```

Expected: 10 tests pass and 0 fail.

- [ ] **Step 7: Commit fallback composition**

```powershell
git add -- Jellyfin.Plugin.NumberCover\Domain\CoverLookupCandidate.cs Jellyfin.Plugin.NumberCover\Services\CoverLookupCoordinator.cs Jellyfin.Plugin.NumberCover\Services\JableCoverLookupService.cs Jellyfin.Plugin.NumberCover.Tests\CoverLookupCoordinatorTests.cs Jellyfin.Plugin.NumberCover.Tests\OnlineSourceCompositionTests.cs
git commit -m "feat: coordinate Jable and DMM fallback"
```

### Task 4: Wire both sources into the Jellyfin image provider

**Files:**
- Create: `Jellyfin.Plugin.NumberCover.Tests/RemoteCoverUriValidatorTests.cs`
- Create: `Jellyfin.Plugin.NumberCover.Tests/CoverLookupPolicyTests.cs`
- Create: `Jellyfin.Plugin.NumberCover/Services/RemoteCoverUriValidator.cs`
- Create: `Jellyfin.Plugin.NumberCover/Services/CoverLookupPolicy.cs`
- Modify: `Jellyfin.Plugin.NumberCover/Providers/JableImageProvider.cs`

- [ ] **Step 1: Write failing final-download allowlist tests**

Create `Jellyfin.Plugin.NumberCover.Tests/RemoteCoverUriValidatorTests.cs`:

```csharp
using Jellyfin.Plugin.NumberCover.Services;
using Xunit;

namespace Jellyfin.Plugin.NumberCover.Tests;

public class RemoteCoverUriValidatorTests
{
    [Theory]
    [InlineData("https://assets-cdn.jable.tv/contents/preview.jpg")]
    [InlineData("https://jable.tv/covers/abc-123.jpg")]
    [InlineData("https://pics.dmm.co.jp/digital/video/ipzz00034/ipzz00034pl.jpg")]
    public void IsAllowed_accepts_the_supported_image_hosts(string value)
    {
        Assert.True(RemoteCoverUriValidator.IsAllowed(new Uri(value)));
    }

    [Theory]
    [InlineData("https://example.test/cover.jpg")]
    [InlineData("https://pics.dmm.co.jp.evil.test/digital/video/ipzz00034/ipzz00034pl.jpg")]
    [InlineData("https://pics.dmm.co.jp/mono/movie/ipzz00034/ipzz00034pl.jpg")]
    [InlineData("http://jable.tv/cover.jpg")]
    public void IsAllowed_rejects_external_or_unsafe_urls(string value)
    {
        Assert.False(RemoteCoverUriValidator.IsAllowed(new Uri(value)));
    }
}
```

Create `Jellyfin.Plugin.NumberCover.Tests/CoverLookupPolicyTests.cs`:

```csharp
using Jellyfin.Plugin.NumberCover.Services;
using Xunit;

namespace Jellyfin.Plugin.NumberCover.Tests;

public class CoverLookupPolicyTests
{
    [Theory]
    [InlineData(false, true, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, true)]
    public void ShouldLookup_enforces_online_source_and_existing_cover_settings(
        bool onlineSourcesEnabled,
        bool onlyFillMissing,
        bool hasPrimaryImage,
        bool expected)
    {
        Assert.Equal(
            expected,
            CoverLookupPolicy.ShouldLookup(onlineSourcesEnabled, onlyFillMissing, hasPrimaryImage));
    }
}
```

- [ ] **Step 2: Run the validator tests and verify the red state**

Run:

```powershell
dotnet test .\Jellyfin.Plugin.NumberCover.Tests\Jellyfin.Plugin.NumberCover.Tests.csproj --no-restore --filter "FullyQualifiedName~RemoteCoverUriValidatorTests|FullyQualifiedName~CoverLookupPolicyTests"
```

Expected: compilation fails because `RemoteCoverUriValidator` and `CoverLookupPolicy` do not exist.

- [ ] **Step 3: Implement the shared allowlist**

Create `Jellyfin.Plugin.NumberCover/Services/RemoteCoverUriValidator.cs`:

```csharp
namespace Jellyfin.Plugin.NumberCover.Services;

public static class RemoteCoverUriValidator
{
    public static bool IsAllowed(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return JableCoverLookupService.IsJableHost(uri) || DmmImageUri.IsAllowed(uri);
    }
}
```

Create `Jellyfin.Plugin.NumberCover/Services/CoverLookupPolicy.cs`:

```csharp
namespace Jellyfin.Plugin.NumberCover.Services;

public static class CoverLookupPolicy
{
    public static bool ShouldLookup(
        bool onlineSourcesEnabled,
        bool onlyFillMissing,
        bool hasPrimaryImage) =>
        onlineSourcesEnabled && (!onlyFillMissing || !hasPrimaryImage);
}
```

- [ ] **Step 4: Run the validator tests and verify the green state**

Run the Step 2 command again.

Expected: 11 tests pass and 0 fail.

- [ ] **Step 5: Replace the provider implementation with source composition**

Replace `Jellyfin.Plugin.NumberCover/Providers/JableImageProvider.cs` with:

```csharp
using System.Net.Http;
using Jellyfin.Plugin.NumberCover.Domain;
using Jellyfin.Plugin.NumberCover.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.NumberCover.Providers;

public sealed class JableImageProvider : IRemoteImageProvider, IHasOrder
{
    private static readonly HttpClient CoverHttpClient = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
    });

    private readonly CoverLookupCoordinator _lookupService;

    public JableImageProvider()
    {
        var configuration = Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();
        var timeProvider = TimeProvider.System;
        var cache = new CoverLookupCache(timeProvider);
        var rateLimiter = new SlidingWindowRateLimiter(configuration.MaxRequestsPerMinute, timeProvider);
        var jable = new JableCoverLookupService(
            FetchPageAsync,
            configuration.MaxRequestsPerMinute,
            configuration.TimeoutSeconds,
            cache,
            timeProvider,
            rateLimiter);
        var dmm = new DmmCoverLookupService(
            FetchDmmHeadersAsync,
            configuration.MaxRequestsPerMinute,
            configuration.TimeoutSeconds,
            cache,
            timeProvider,
            rateLimiter);
        _lookupService = new CoverLookupCoordinator(jable.LookupAsync, dmm.LookupAsync);
    }

    public string Name => "Number Cover (Jable)";

    public int Order => 0;

    public bool Supports(BaseItem item) => item is Movie;

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item) =>
        item is Movie ? [ImageType.Primary] : [];

    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        var configuration = Plugin.Instance?.Configuration;
        if (configuration is null || item is not Movie)
        {
            return [];
        }

        if (!CoverLookupPolicy.ShouldLookup(
                configuration.EnableJableSource,
                configuration.OnlyFillMissing,
                item.HasImage(ImageType.Primary)))
        {
            return [];
        }

        var number = NumberParser.Parse(item.Name) ?? NumberParser.Parse(item.Path);
        if (number is null)
        {
            return [];
        }

        var candidate = await _lookupService.LookupAsync(number, cancellationToken).ConfigureAwait(false);
        return candidate is not null
            ?
            [
                new RemoteImageInfo
                {
                    ProviderName = candidate.Source == CoverSource.Dmm
                        ? "Number Cover (DMM fallback)"
                        : Name,
                    Type = ImageType.Primary,
                    Url = candidate.CoverUri.AbsoluteUri,
                },
            ]
            : [];
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !RemoteCoverUriValidator.IsAllowed(uri))
        {
            throw new ArgumentException("Only validated Number Cover HTTPS image URLs are allowed.", nameof(url));
        }

        return CoverHttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private async Task<string> FetchPageAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!JableCoverLookupService.IsJableHost(uri))
        {
            throw new ArgumentException("Only Jable HTTPS detail pages are allowed.", nameof(uri));
        }

        using var response = await CoverHttpClient
            .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task<HttpResponseMessage> FetchDmmHeadersAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!DmmImageUri.IsAllowed(uri))
        {
            throw new ArgumentException("Only deterministic DMM HTTPS image URLs are allowed.", nameof(uri));
        }

        return CoverHttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
}
```

- [ ] **Step 6: Run the complete test suite and compile the provider**

Run:

```powershell
dotnet test .\Jellyfin.Plugin.NumberCover.sln -c Release --no-restore
```

Expected: all tests pass, 0 fail, and the plugin provider compiles for `net9.0`.

- [ ] **Step 7: Commit the provider integration**

```powershell
git add -- Jellyfin.Plugin.NumberCover.Tests\RemoteCoverUriValidatorTests.cs Jellyfin.Plugin.NumberCover.Tests\CoverLookupPolicyTests.cs Jellyfin.Plugin.NumberCover\Services\RemoteCoverUriValidator.cs Jellyfin.Plugin.NumberCover\Services\CoverLookupPolicy.cs Jellyfin.Plugin.NumberCover\Providers\JableImageProvider.cs
git commit -m "feat: wire automatic DMM fallback into Jellyfin"
```

### Task 5: Update the compatible setting, version, and documentation

**Files:**
- Modify: `Jellyfin.Plugin.NumberCover.Tests/ConfigurationPageTests.cs`
- Create: `Jellyfin.Plugin.NumberCover.Tests/ProjectVersionTests.cs`
- Modify: `Jellyfin.Plugin.NumberCover/Configuration/configPage.html`
- Modify: `Jellyfin.Plugin.NumberCover/Jellyfin.Plugin.NumberCover.csproj`
- Modify: `README.md`

- [ ] **Step 1: Add failing UI-label and project-version tests**

Add this test to `ConfigurationPageTests.cs`:

```csharp
[Fact]
public void Page_explains_Jable_first_with_automatic_DMM_fallback()
{
    var html = ReadPluginFile("Configuration", "configPage.html");

    Assert.Contains("Enable online cover sources", html, StringComparison.Ordinal);
    Assert.Contains("Jable is tried first, with automatic DMM fallback", html, StringComparison.Ordinal);
    Assert.DoesNotContain("Enable Jable source", html, StringComparison.Ordinal);
}
```

Create `Jellyfin.Plugin.NumberCover.Tests/ProjectVersionTests.cs`:

```csharp
using System.Xml.Linq;
using Xunit;

namespace Jellyfin.Plugin.NumberCover.Tests;

public class ProjectVersionTests
{
    [Fact]
    public void Plugin_project_targets_release_0_1_1()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "Jellyfin.Plugin.NumberCover",
            "Jellyfin.Plugin.NumberCover.csproj"));
        var project = XDocument.Load(path);

        Assert.Equal("0.1.1", project.Descendants("Version").Single().Value);
        Assert.Equal("0.1.1.0", project.Descendants("AssemblyVersion").Single().Value);
        Assert.Equal("0.1.1.0", project.Descendants("FileVersion").Single().Value);
    }
}
```

- [ ] **Step 2: Run the focused tests and verify the red state**

Run:

```powershell
dotnet test .\Jellyfin.Plugin.NumberCover.Tests\Jellyfin.Plugin.NumberCover.Tests.csproj --no-restore --filter "FullyQualifiedName~ConfigurationPageTests.Page_explains|FullyQualifiedName~ProjectVersionTests"
```

Expected: 2 tests fail because the page still says Jable-only and the project is version 0.1.0.

- [ ] **Step 3: Update the existing compatible settings label**

In `configPage.html`, keep the input ID and name `EnableJableSource` unchanged, but replace its visible text with:

```html
<span>Enable online cover sources</span>
</label>
<div class="fieldDescription">Jable is tried first, with automatic DMM fallback when Jable does not return a cover.</div>
```

Do not rename `PluginConfiguration.EnableJableSource` or the JavaScript `EnableJableSource` property.

- [ ] **Step 4: Advance project version metadata**

Change the version properties in `Jellyfin.Plugin.NumberCover.csproj` to:

```xml
<Version>0.1.1</Version>
<AssemblyVersion>0.1.1.0</AssemblyVersion>
<FileVersion>0.1.1.0</FileVersion>
```

- [ ] **Step 5: Replace README with the 0.1.1 operating guide**

Replace `README.md` with:

```markdown
# Number Cover for Jellyfin

Jellyfin 10.11.11 plugin that recognizes an identifier such as `IPZZ-034` in a movie name or path and offers a primary cover when online sources are enabled.

The plugin tries the public Jable detail page first. If Jable is blocked, unavailable, or has no valid image, it automatically checks the deterministic DMM/FANZA digital cover URL. A DMM result is accepted only when the exact HTTPS host and path return HTTP 200 with an image media type; redirects and HTML pages are rejected.

The online sources are disabled by default. The plugin does not download videos, log in, reuse browser cookies, or bypass access controls. It retains an existing primary image when **Only fill missing covers** is enabled.

## Install

1. Stop Jellyfin before replacing the plugin DLL.
2. Extract `NumberCover-v0.1.1.zip` into one Number Cover folder under Jellyfin's data `plugins` directory. Do not keep two Number Cover DLLs in separate plugin folders.
3. Restart Jellyfin and open **Dashboard > Plugins > Number Cover**.
4. Enable **Online cover sources (Jable + DMM fallback)**.
5. Keep **Only fill missing covers** enabled to preserve current covers, or disable it when you explicitly want to replace them.
6. For one movie, open **Edit images > Search**, choose the Number Cover result, and save it. For a library, run **Scan library** or **Refresh metadata** with image replacement options that match your intent.

## Identifier mapping example

`IPZZ-034` is normalized to DMM content ID `ipzz00034` and checked at:

`https://pics.dmm.co.jp/digital/video/ipzz00034/ipzz00034pl.jpg`

Not every identifier is guaranteed to exist at DMM; an invalid candidate is returned as no image.

## Defaults

- Online sources: disabled
- Existing covers: retained
- Combined request cap: 12/minute
- Request timeout: 10 seconds

Only use this plugin for material you are authorized to access and in accordance with each source site's terms.
```

- [ ] **Step 6: Run focused tests and the full Release suite**

Run:

```powershell
dotnet test .\Jellyfin.Plugin.NumberCover.Tests\Jellyfin.Plugin.NumberCover.Tests.csproj --no-restore --filter "FullyQualifiedName~ConfigurationPageTests|FullyQualifiedName~PluginConfigurationTests|FullyQualifiedName~ProjectVersionTests"
dotnet test .\Jellyfin.Plugin.NumberCover.sln -c Release --no-restore
```

Expected: both commands pass with 0 failures. `PluginConfigurationTests` confirms the serialized field and safe default remain unchanged.

- [ ] **Step 7: Commit release metadata and documentation**

```powershell
git add -- Jellyfin.Plugin.NumberCover.Tests\ConfigurationPageTests.cs Jellyfin.Plugin.NumberCover.Tests\ProjectVersionTests.cs Jellyfin.Plugin.NumberCover\Configuration\configPage.html Jellyfin.Plugin.NumberCover\Jellyfin.Plugin.NumberCover.csproj README.md
git commit -m "docs: prepare Number Cover 0.1.1"
```

### Task 6: Verify, package, install, and perform live acceptance

**Files:**
- Verify: `Jellyfin.Plugin.NumberCover.sln`
- Build: `Jellyfin.Plugin.NumberCover/bin/Release/net9.0/Jellyfin.Plugin.NumberCover.dll`
- Create: `outputs/NumberCover-v0.1.1.zip`
- Install: `%LOCALAPPDATA%/jellyfin/plugins/NumberCover-v0.1.0/Jellyfin.Plugin.NumberCover.dll`
- Modify during approved deployment: the single Jellyfin Number Cover configuration XML containing `EnableJableSource`

- [ ] **Step 1: Run final clean verification**

Run:

```powershell
dotnet test .\Jellyfin.Plugin.NumberCover.sln -c Release --no-restore
dotnet build .\Jellyfin.Plugin.NumberCover\Jellyfin.Plugin.NumberCover.csproj -c Release --no-restore
git diff --check
git status --short
```

Expected: all tests pass; build reports 0 warnings and 0 errors; `git diff --check` prints nothing; working tree is clean except the implementation plan if it has not yet been committed.

- [ ] **Step 2: Build and inspect the 0.1.1 distribution ZIP**

Run from the feature worktree root:

```powershell
$staging = Join-Path (Get-Location) 'artifacts\NumberCover-v0.1.1'
$outputDir = (Resolve-Path '..\..\..\outputs').Path
$zip = Join-Path $outputDir 'NumberCover-v0.1.1.zip'

if (Test-Path -LiteralPath $staging) {
    Remove-Item -LiteralPath $staging -Recurse -Force
}

New-Item -ItemType Directory -Path $staging | Out-Null
Copy-Item -LiteralPath '.\Jellyfin.Plugin.NumberCover\bin\Release\net9.0\Jellyfin.Plugin.NumberCover.dll' -Destination $staging
Copy-Item -LiteralPath '.\README.md' -Destination $staging
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::OpenRead($zip).Entries | Select-Object FullName, Length
```

Expected: the ZIP root contains only `Jellyfin.Plugin.NumberCover.dll` and `README.md`.

- [ ] **Step 3: Verify the real `IPZZ-034` DMM response before deployment**

Run with network approval:

```powershell
$uri = 'https://pics.dmm.co.jp/digital/video/ipzz00034/ipzz00034pl.jpg'
$response = Invoke-WebRequest -Uri $uri -Method Get -MaximumRedirection 0 -UseBasicParsing
$response.StatusCode
$response.Headers['Content-Type']
$response.Content.Length
```

Expected: HTTP 200, an `image/jpeg` content type, and a non-zero body length. A different result blocks live acceptance but does not invalidate unit tests.

- [ ] **Step 4: Install the verified DLL and enable the existing online-source setting**

Run with approval because this stops Jellyfin and writes to its data directory:

```powershell
$source = (Resolve-Path '.\Jellyfin.Plugin.NumberCover\bin\Release\net9.0\Jellyfin.Plugin.NumberCover.dll').Path
$target = 'C:\Users\Administrator\AppData\Local\jellyfin\plugins\NumberCover-v0.1.0\Jellyfin.Plugin.NumberCover.dll'
$jellyfinRoot = 'C:\Users\Administrator\AppData\Local\jellyfin'
$server = 'D:\Program Files\Jellyfin\Server\jellyfin.exe'

$configMatches = @(rg -l --glob '*.xml' '<EnableJableSource>' $jellyfinRoot)
if ($configMatches.Count -ne 1) {
    throw "Expected exactly one Number Cover configuration XML, found $($configMatches.Count)."
}

Get-Process -Name jellyfin -ErrorAction SilentlyContinue | Stop-Process -Force
for ($attempt = 0; $attempt -lt 20 -and (Get-Process -Name jellyfin -ErrorAction SilentlyContinue); $attempt++) {
    Start-Sleep -Milliseconds 300
}

if (Get-Process -Name jellyfin -ErrorAction SilentlyContinue) {
    throw 'Jellyfin did not stop.'
}

Copy-Item -LiteralPath $source -Destination $target -Force
[xml]$configuration = Get-Content -LiteralPath $configMatches[0] -Raw
$enableNode = $configuration.SelectSingleNode('//EnableJableSource')
if ($null -eq $enableNode) {
    throw 'EnableJableSource was not found in the Number Cover configuration.'
}

$enableNode.InnerText = 'true'
$configuration.Save($configMatches[0])

if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne
    (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash) {
    throw 'Installed DLL does not match the verified Release DLL.'
}

Start-Process -FilePath $server -WorkingDirectory (Split-Path -Parent $server) -WindowStyle Hidden
```

Expected: exactly one existing configuration file is modified, no duplicate plugin folder is created, and installed/source DLL hashes match.

- [ ] **Step 5: Verify Jellyfin startup, version, configuration, and logs**

Run with local-server approval:

```powershell
$ready = $false
for ($attempt = 0; $attempt -lt 30; $attempt++) {
    try {
        $response = Invoke-WebRequest -Uri 'http://127.0.0.1:8096/System/Info/Public' -UseBasicParsing -TimeoutSec 2
        if ($response.StatusCode -eq 200) {
            $ready = $true
            break
        }
    } catch {
        Start-Sleep -Seconds 1
    }
}

if (-not $ready) {
    throw 'Jellyfin did not become ready after restart.'
}

[Reflection.AssemblyName]::GetAssemblyName($target).Version
[xml]$savedConfiguration = Get-Content -LiteralPath $configMatches[0] -Raw
$savedConfiguration.SelectSingleNode('//EnableJableSource').InnerText
$latestLog = Get-ChildItem -LiteralPath (Join-Path $jellyfinRoot 'log') -File |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
rg -n 'Number Cover|NumberCover|Error.*plugin' $latestLog.FullName
```

Expected: endpoint returns HTTP 200, assembly version is `0.1.1.0`, the setting is `true`, logs show Number Cover 0.1.1 loading, and there is no Number Cover startup exception.

- [ ] **Step 6: Perform Jellyfin UI acceptance for `IPZZ-034`**

Use the already authenticated local Jellyfin web session:

1. Open **Dashboard > Plugins > Number Cover** and confirm the page says **Enable online cover sources**, the checkbox is enabled, and the other saved values are unchanged.
2. Search the Jellyfin library for `IPZZ-034`.
3. If the item exists, open **Edit images > Search**, confirm a result labeled **Number Cover (DMM fallback)** appears, select it, save, and visually confirm the primary image is displayed.
4. If the item already has a primary image and **Only fill missing covers** is enabled, temporarily disable only that setting for this explicit single-item test, restore it afterward, and verify the saved value returned to its original state.
5. If no `IPZZ-034` item exists, do not create or import media. Record the successful deterministic mapping test, real HTTP 200 JPEG check, installed DLL hash/version, and plugin startup as the available acceptance evidence, and report that the final library-image click remains untested.

- [ ] **Step 7: Record final evidence before claiming completion**

Run:

```powershell
git log --oneline --decorate -6
git status --short
Get-FileHash -LiteralPath '.\Jellyfin.Plugin.NumberCover\bin\Release\net9.0\Jellyfin.Plugin.NumberCover.dll' -Algorithm SHA256
Get-FileHash -LiteralPath 'C:\Users\Administrator\AppData\Local\jellyfin\plugins\NumberCover-v0.1.0\Jellyfin.Plugin.NumberCover.dll' -Algorithm SHA256
Get-FileHash -LiteralPath '..\..\..\outputs\NumberCover-v0.1.1.zip' -Algorithm SHA256
```

Expected: feature commits are present, the worktree has no uncommitted plugin changes, source and installed DLL hashes are identical, and the ZIP hash is recorded. Report the exact test total and whether the final Jellyfin library image was visually confirmed.
