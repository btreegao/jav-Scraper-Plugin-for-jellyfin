# Number Cover Underscore Suffix Parsing Design

## Context

Number Cover currently recognizes normalized movie identifiers such as `MKMP-418` and the compact copy suffix form `IPZZ-034C`. A filename such as `MKMP-418_CH_SD` is not recognized because `_` is a word character, so the parser's required word boundary after the numeric component does not match.

The filename suffix describes the media copy and is not part of the movie identifier. Removing it should allow the existing Jable-first and DMM-fallback lookup flow to operate without changing either remote source.

## Goal

Recognize a canonical identifier before an underscore-delimited alphabetic media suffix:

- `MKMP-418_CH_SD` becomes `MKMP-418`.
- Existing `IPZZ-034C` behavior remains `IPZZ-034`.
- Existing prefix separators (`-`, `_`, and space) remain supported.

## Boundary Rule

After the numeric identifier and optional compact `C` suffix, the parser may finish at either:

1. the existing word boundary; or
2. an underscore whose following character is an ASCII letter.

The parser returns only the captured prefix and numeric component. It does not parse, validate, or return the media suffix.

An underscore followed directly by a digit remains invalid. For example, `ABC-123_456` must not be shortened to `ABC-123`, because it may represent a multi-part or otherwise ambiguous identifier.

## Implementation Scope

Only the identifier parser and its regression tests change. The Jable service, DMM URL mapping, cache, request limiter, provider policy, and configuration format remain unchanged.

The plugin patch version changes from `0.1.1` to `0.1.2`, including assembly/file version tests and the release package name.

## Data Flow

1. Jellyfin passes the movie name to `NumberParser.Parse`.
2. Unicode compatibility normalization runs as before.
3. The parser captures the identifier before a permitted alphabetic underscore suffix.
4. The canonical value `MKMP-418` enters the existing Jable-first lookup coordinator.
5. If Jable does not return a valid cover, the existing deterministic DMM fallback is attempted.

## Error Handling and Safety

- Numeric underscore continuations remain rejected.
- Arbitrary attached letters such as `ABC-123X` remain rejected.
- Hyphenated numeric continuations such as `ABC-123-456` and `ABC-123C-456` remain rejected.
- No remote URL validation or HTTP behavior is relaxed.

## Tests

Add parser regression cases that prove:

- `MKMP-418_CH_SD` returns `MKMP-418`.
- `ABC-123_456` returns `null`.
- Existing accepted and rejected parser cases continue to pass.
- Project version tests require `0.1.2` and assembly/file version `0.1.2.0`.

Run the complete Release test suite and require a warning-free, error-free Release build.

## Packaging and Acceptance

Create `NumberCover-v0.1.2.zip` containing only the plugin DLL and README. Install the matching DLL into the existing single Number Cover plugin directory, update its installed metadata version, restart Jellyfin, and verify that Jellyfin loads `0.1.2.0`.

For live acceptance, open `MKMP-418_CH_SD`, use **Modify images > Search**, and confirm that Number Cover returns a result for the canonical identifier. Applying the returned cover is permitted as part of the requested test.
