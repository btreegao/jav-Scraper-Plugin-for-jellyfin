# Number Cover DMM Automatic Fallback Design

**Date:** 2026-07-16

**Target release:** 0.1.1

**Target platform:** Jellyfin 10.11.11

## Context

Number Cover 0.1.0 recognizes identifiers such as `IPZZ-034` and asks Jable for a primary movie image. The Jable page exists and is accessible in an interactive browser, but ordinary server-side HTTP requests receive HTTP 403. Consequently, Jellyfin cannot reliably obtain the page's `og:image` value through the current plugin.

For `IPZZ-034`, the deterministic DMM/FANZA digital image URL `https://pics.dmm.co.jp/digital/video/ipzz00034/ipzz00034pl.jpg` was verified to return HTTP 200 and a JPEG. Invalid deterministic candidates redirect to an HTML page, which can be rejected without following redirects.

## Goals

- Keep Jable as the first source.
- Automatically try a deterministic DMM image candidate whenever Jable does not produce an image.
- Apply the fallback to every identifier accepted by the existing number parser.
- Preserve existing plugin configuration and Jellyfin provider selection across the upgrade.
- Validate every remote URL and response before exposing it to Jellyfin.
- Package, install, and live-test version 0.1.1 with `IPZZ-034`.

## Non-goals

- Bypassing Jable access controls, reusing browser cookies, or automating a logged-in browser.
- Scraping DMM pages or performing a DMM text search.
- Guaranteeing that every recognized identifier has a DMM image. The fallback is best-effort and returns no image when the deterministic candidate is invalid.
- Replacing existing covers when `OnlyFillMissing` is enabled.

## Architecture

The existing Jellyfin remote image provider remains the orchestration point. Its provider `Name` remains `Number Cover (Jable)` so existing Jellyfin fetcher configuration is not invalidated.

A new `DmmCoverLookupService` is responsible only for:

1. converting a normalized identifier to a deterministic DMM content ID;
2. constructing the permitted DMM image URL;
3. validating the response without downloading the complete image during lookup; and
4. returning the existing success, not-found, or temporary-failure result model.

The provider executes this sequence:

1. Stop immediately when online sources are disabled.
2. Stop immediately when `OnlyFillMissing` is enabled and the movie already has a primary image.
3. Parse the identifier from the movie name using the existing parser.
4. Ask Jable for an image.
5. Return the Jable image when lookup succeeds.
6. On Jable not-found or temporary failure, ask DMM for a deterministic image.
7. Return the DMM image when validation succeeds; otherwise return no remote images.

Cancellation is propagated immediately and never converted into a fallback or cached failure.

## Identifier and URL Mapping

The parser continues to accept 2-8 letters, a hyphen, underscore, or space separator, and 2-6 digits. DMM conversion uses the parsed components rather than reparsing arbitrary text:

- lowercase the letter prefix;
- preserve the numeric text;
- left-pad the numeric text with zeroes to a minimum width of five;
- never truncate a numeric component longer than five characters; and
- concatenate prefix and padded numeric text.

Examples:

| Input | DMM content ID | Candidate URL |
| --- | --- | --- |
| `IPZZ-034` | `ipzz00034` | `https://pics.dmm.co.jp/digital/video/ipzz00034/ipzz00034pl.jpg` |
| `ABC-12` | `abc00012` | `https://pics.dmm.co.jp/digital/video/abc00012/abc00012pl.jpg` |
| `ABC-12345` | `abc12345` | `https://pics.dmm.co.jp/digital/video/abc12345/abc12345pl.jpg` |
| `ABC-123456` | `abc123456` | `https://pics.dmm.co.jp/digital/video/abc123456/abc123456pl.jpg` |

## Network Validation and Security

All requests use the configured timeout and the caller's cancellation token. Redirect following is disabled.

Jable behavior remains constrained to HTTPS Jable page and asset hosts. Extracted image URLs must be HTTPS and must belong to `jable.tv` or one of its subdomains.

DMM lookup permits only:

- HTTPS;
- exact host `pics.dmm.co.jp`;
- no user information or non-default port; and
- path `/digital/video/{contentId}/{contentId}pl.jpg`, where both content IDs equal the locally derived value.

The DMM validation request uses response-headers mode and accepts only HTTP 200 with a media type beginning with `image/`. Redirects, HTML responses, authentication challenges, and all other statuses are rejected. A redirect from an invalid candidate is treated as not-found, not followed.

The provider's final image-download method independently repeats URL validation. It accepts the current permitted Jable asset URLs and the exact deterministic DMM URL shape. This defense prevents an unexpected or cached URL from turning the provider into a general-purpose HTTP proxy.

## Rate Limiting, Cache, and Failures

Jable and DMM share one sliding-window request limiter. `MaxRequestsPerMinute` therefore remains a global plugin limit even when a fallback is attempted. A request that is cancelled while waiting for the limiter exits without making a network call.

Cache entries are source-qualified, for example `jable:IPZZ-034` and `dmm:IPZZ-034`, so one source's failure cannot hide another source's success. Durations are:

- successful image: 30 days;
- confirmed not-found: 7 days; and
- temporary/network failure: 1 hour.

Jable HTTP 403, timeout, transport failure, missing metadata, or invalid image URL does not prevent the current request from trying DMM. DMM timeout or transport failure ends the lookup with no image and is logged as a temporary failure. Expected not-found results use concise debug/information logging; unexpected failures include enough source and identifier context for diagnosis without logging cookies or credentials.

## Configuration and Compatibility

The serialized `EnableJableSource` property is retained to preserve existing configuration files. Its settings-page label and description change to explain that it enables online sources, consisting of Jable plus automatic DMM fallback. No new fallback switch is added.

The provider's stable `Name` remains unchanged. A returned `RemoteImageInfo.ProviderName` identifies whether the candidate came from Jable or `Number Cover (DMM fallback)` for display and diagnosis.

The plugin code does not silently override the saved enable setting. During deployment on the user's machine, the online-source setting will be enabled explicitly, then saved through Jellyfin before live verification. Other existing settings remain unchanged.

## Tests

Automated tests will cover:

- `IPZZ-034` mapping to `ipzz00034` and the expected URL;
- left-padding below five digits and preserving five- or six-digit values;
- rejection of malformed identifiers and unsupported external hosts;
- acceptance of a DMM HTTP 200 image response;
- rejection of DMM redirects, HTML, non-200 responses, and lookalike hosts;
- Jable success returning immediately without a DMM request;
- Jable not-found and temporary failure both invoking DMM;
- disabled online sources making no network requests;
- `OnlyFillMissing` preserving an existing primary image;
- cancellation propagation;
- source-qualified cache behavior and cache durations;
- shared request-limit behavior; and
- final image-download allowlisting for both sources.

All existing tests must remain green. The Release build must complete with zero warnings and zero errors.

## Packaging and Live Acceptance

The assembly and manifest version are advanced to 0.1.1. The release artifact is `NumberCover-v0.1.1.zip`.

Deployment acceptance consists of:

1. installing the 0.1.1 DLL into the Jellyfin plugin directory;
2. restarting Jellyfin and confirming that version 0.1.1 loads without plugin errors;
3. enabling the online-source setting while preserving the other settings;
4. confirming that a remote image search for `IPZZ-034` returns the validated DMM JPEG when Jable is blocked; and
5. refreshing the item image and confirming Jellyfin stores and displays the cover.

If the final live image refresh cannot be tied to an existing `IPZZ-034` library item, direct plugin lookup and image-download behavior will still be verified and the remaining Jellyfin UI action will be reported explicitly rather than claimed complete.
