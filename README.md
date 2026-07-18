# Number Cover 0.1.1

Number Cover is a Jellyfin 10.11.11 plugin that recognizes identifiers such as `IPZZ-034` in a movie name or path and finds a primary cover. Jable is tried first; the fallback is deterministic DMM/FANZA cover lookup.

Online sources are disabled by default. The plugin does not download videos, require login, use browser cookies, or bypass access controls. Use it only where you have authorization and follow the applicable source-site terms.

## How it works

- `IPZZ-034` maps to `ipzz00034` and the exact DMM image URL `https://pics.dmm.co.jp/digital/video/ipzz00034/ipzz00034pl.jpg`.
- DMM results are accepted only from the exact HTTPS host and canonical path, and only when the response is HTTP 200 with an image content type. Redirects and HTML are rejected.
- Jable is tried first, followed by the deterministic DMM/FANZA fallback when Jable does not return a cover.
- Not every identifier is guaranteed to have a matching cover.
- **Only fill missing** preserves an existing primary cover.

Default settings are: online sources off, retain existing covers, combined request limit 12 per minute, and a 10-second timeout.

## Install and use

1. Extract `NumberCover-v0.1.1.zip` into one dedicated Number Cover folder under Jellyfin's `plugins` directory, then restart Jellyfin. Do not keep duplicate Number Cover DLLs in other plugin folders.
2. Open **Dashboard > Plugins > Number Cover** and enable online cover sources.
3. For one item, use **Edit images > Search**. For a batch, run a library scan or refresh.

Choose settings and sources only for content you are authorized to access, and comply with the source sites' terms.
