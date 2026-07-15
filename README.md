# Number Cover for Jellyfin

Jellyfin 10.11.11 plugin that recognizes an identifier such as `ABC-123` in a movie name or path and, when explicitly enabled, offers a primary cover from the corresponding public Jable detail page.

The source is **disabled by default**. It never downloads videos, logs in, bypasses access controls, or replaces an existing primary image when **Only fill missing covers** is enabled (the default).

## Install

1. Close/restart Jellyfin before replacing plugin files.
2. Extract `NumberCover-v0.1.0.zip` into a new folder under Jellyfin's data `plugins` directory, for example `plugins/NumberCover_0.1.0`.
3. Restart Jellyfin and open **Dashboard > Plugins > Number Cover**.
4. Leave the safe defaults in place, or explicitly enable the Jable source for your personal library.
5. Refresh metadata/images for the target movie or library and choose the provider's offered primary image.

## Defaults

- Jable source: disabled
- Existing covers: retained
- Request cap: 12/minute
- Page timeout: 10 seconds

Only use this plugin for material you are authorized to access and in accordance with the source site's terms.
