# Number Cover Underscore Suffix Parsing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Release Number Cover 0.1.2 so filenames such as `MKMP-418_CH_SD` are normalized to `MKMP-418` without accepting ambiguous numeric underscore continuations.

**Architecture:** Extend only the terminal boundary in `NumberParser` so an identifier can end before an underscore followed by an ASCII letter. Keep all captures, canonicalization, provider coordination, DMM mapping, URL validation, caching, and rate limiting unchanged. Prove the boundary with focused red-green parser tests, then update patch-version metadata and deploy the exact verified DLL.

**Tech Stack:** C# 13, .NET 9, xUnit, Jellyfin 10.11.11, PowerShell, Git

---

## File Map

- Modify `Jellyfin.Plugin.NumberCover.Tests/NumberParserTests.cs`: parser success and ambiguity regressions.
- Modify `Jellyfin.Plugin.NumberCover/Services/NumberParser.cs`: alphabetic underscore-suffix boundary.
- Modify `Jellyfin.Plugin.NumberCover.Tests/ProjectVersionTests.cs`: required 0.1.2 version values.
- Modify `Jellyfin.Plugin.NumberCover/Jellyfin.Plugin.NumberCover.csproj`: package, assembly, and file versions.
- Modify `README.md`: 0.1.2 heading, suffix behavior, and package filename.
- Create release artifact outside Git at `outputs/NumberCover-v0.1.2.zip`.

### Task 1: Parse alphabetic underscore suffixes safely

**Files:**
- Modify: `Jellyfin.Plugin.NumberCover.Tests/NumberParserTests.cs`
- Modify: `Jellyfin.Plugin.NumberCover/Services/NumberParser.cs`

- [ ] **Step 1: Add the accepted suffix regression**

Add this row to `Parse_returns_a_canonical_number_for_a_bounded_supported_format`:

```csharp
[InlineData("MKMP-418_CH_SD", "MKMP-418")]
```

- [ ] **Step 2: Add the rejected numeric continuation regression**

Add this row to `Parse_returns_null_for_missing_or_non_bounded_formats`:

```csharp
[InlineData("ABC-123_456")]
```

- [ ] **Step 3: Run the focused test and verify the intended red state**

Run:

```powershell
dotnet test .\Jellyfin.Plugin.NumberCover.Tests\Jellyfin.Plugin.NumberCover.Tests.csproj --no-restore --filter "FullyQualifiedName~NumberParserTests"
```

Expected: one failure for `MKMP-418_CH_SD` returning `null`; `ABC-123_456` already returns `null`.

- [ ] **Step 4: Implement the minimal parser boundary**

Replace the `NumberPattern` expression in `NumberParser.cs` with:

```csharp
private static readonly Regex NumberPattern = new(
    @"\b(?<prefix>[A-Za-z]{2,8})[-_\s](?<number>[0-9]{2,6})(?:[Cc])?(?!-[0-9])(?=\b|_[A-Za-z])",
    RegexOptions.CultureInvariant);
```

The lookahead leaves the suffix unconsumed and permits only an underscore followed by an ASCII letter. It does not broaden the canonical prefix or number captures.

- [ ] **Step 5: Run the focused test and verify green**

Run:

```powershell
dotnet test .\Jellyfin.Plugin.NumberCover.Tests\Jellyfin.Plugin.NumberCover.Tests.csproj --no-restore --filter "FullyQualifiedName~NumberParserTests"
```

Expected: all `NumberParserTests` pass, including existing `IPZZ-034C`, `ABC-123X`, `ABC-123-456`, and `ABC-123C-456` cases.

- [ ] **Step 6: Commit the parser change**

```powershell
git add -- Jellyfin.Plugin.NumberCover.Tests/NumberParserTests.cs Jellyfin.Plugin.NumberCover/Services/NumberParser.cs
git commit -m "fix: parse alphabetic underscore copy suffixes"
```

### Task 2: Prepare the 0.1.2 release metadata

**Files:**
- Modify: `Jellyfin.Plugin.NumberCover.Tests/ProjectVersionTests.cs`
- Modify: `Jellyfin.Plugin.NumberCover/Jellyfin.Plugin.NumberCover.csproj`
- Modify: `README.md`

- [ ] **Step 1: Make the version test require 0.1.2**

Rename the version test and replace its expected values:

```csharp
[Fact]
public void Project_version_is_0_1_2()
{
    var project = XDocument.Parse(ReadProjectFile());
    var version = Assert.Single(
        project.Descendants(),
        element => element.Name.LocalName == "Version");
    var assemblyVersion = Assert.Single(
        project.Descendants(),
        element => element.Name.LocalName == "AssemblyVersion");
    var fileVersion = Assert.Single(
        project.Descendants(),
        element => element.Name.LocalName == "FileVersion");

    Assert.Equal("0.1.2", version.Value);
    Assert.Equal("0.1.2.0", assemblyVersion.Value);
    Assert.Equal("0.1.2.0", fileVersion.Value);
}
```

- [ ] **Step 2: Run the version test and verify red**

Run:

```powershell
dotnet test .\Jellyfin.Plugin.NumberCover.Tests\Jellyfin.Plugin.NumberCover.Tests.csproj --no-restore --filter "FullyQualifiedName~ProjectVersionTests"
```

Expected: failure because the project still contains `0.1.1` and `0.1.1.0`.

- [ ] **Step 3: Update project version properties**

Set the three properties in `Jellyfin.Plugin.NumberCover.csproj` to:

```xml
<Version>0.1.2</Version>
<AssemblyVersion>0.1.2.0</AssemblyVersion>
<FileVersion>0.1.2.0</FileVersion>
```

- [ ] **Step 4: Update README release text**

Change the heading and package filename to 0.1.2:

```markdown
# Number Cover 0.1.2
```

```markdown
1. Extract `NumberCover-v0.1.2.zip` into one dedicated Number Cover folder under Jellyfin's `plugins` directory, then restart Jellyfin. Do not keep duplicate Number Cover DLLs in other plugin folders.
```

Add this bullet under **How it works**:

```markdown
- Alphabetic copy suffixes after an underscore are ignored, so `MKMP-418_CH_SD` is normalized to `MKMP-418`; numeric continuations such as `ABC-123_456` remain rejected.
```

- [ ] **Step 5: Run focused version and documentation tests**

Run:

```powershell
dotnet test .\Jellyfin.Plugin.NumberCover.Tests\Jellyfin.Plugin.NumberCover.Tests.csproj --no-restore --filter "FullyQualifiedName~ProjectVersionTests|FullyQualifiedName~NumberParserTests"
```

Expected: all selected tests pass.

- [ ] **Step 6: Commit release metadata**

```powershell
git add -- Jellyfin.Plugin.NumberCover.Tests/ProjectVersionTests.cs Jellyfin.Plugin.NumberCover/Jellyfin.Plugin.NumberCover.csproj README.md
git commit -m "docs: prepare Number Cover 0.1.2"
```

### Task 3: Verify, package, install, and accept the release

**Files:**
- Build output: `Jellyfin.Plugin.NumberCover/bin/Release/net9.0/Jellyfin.Plugin.NumberCover.dll`
- Create: `outputs/NumberCover-v0.1.2.zip`
- Update during approved deployment: `C:\Users\Administrator\AppData\Local\jellyfin\plugins\NumberCover-v0.1.0\Jellyfin.Plugin.NumberCover.dll`
- Update during approved deployment: `C:\Users\Administrator\AppData\Local\jellyfin\plugins\NumberCover-v0.1.0\meta.json`

- [ ] **Step 1: Run the complete Release test suite**

Run:

```powershell
dotnet test .\Jellyfin.Plugin.NumberCover.Tests\Jellyfin.Plugin.NumberCover.Tests.csproj -c Release --no-restore
```

Expected: all tests pass with zero failures.

- [ ] **Step 2: Produce a warning-free Release build**

Run:

```powershell
dotnet build .\Jellyfin.Plugin.NumberCover\Jellyfin.Plugin.NumberCover.csproj -c Release --no-restore
```

Expected: build succeeds with 0 warnings and 0 errors.

- [ ] **Step 3: Create and inspect the release ZIP**

Run from the feature worktree, using the repository-level `outputs` directory:

```powershell
$dll = Resolve-Path '.\Jellyfin.Plugin.NumberCover\bin\Release\net9.0\Jellyfin.Plugin.NumberCover.dll'
$readme = Resolve-Path '.\README.md'
$zip = 'C:\Users\Administrator\Documents\Codex\2026-07-15\plugin-computer-use-openai-bundled-play-2\outputs\NumberCover-v0.1.2.zip'
Compress-Archive -LiteralPath @($dll.Path, $readme.Path) -DestinationPath $zip -Force
Get-FileHash -Algorithm SHA256 -LiteralPath $dll.Path, $zip
```

Expected: the ZIP contains only `Jellyfin.Plugin.NumberCover.dll` and `README.md`.

- [ ] **Step 4: Install the exact DLL and metadata version**

After explicit system-write approval, stop Jellyfin, copy the verified DLL to the existing single plugin directory, set the installed `meta.json` version to `0.1.2.0`, and restart `D:\Program Files\Jellyfin\Server\jellyfin.exe` with a hidden window. Preserve the existing plugin configuration XML.

Verify all of these signals:

```text
Installed DLL SHA-256 equals the built DLL SHA-256.
GET http://127.0.0.1:8096/System/Info/Public returns HTTP 200.
The latest Jellyfin log loads Number Cover assembly 0.1.2.0.
The latest Jellyfin log loads Number Cover plugin 0.1.2.0.
```

- [ ] **Step 5: Run live acceptance for MKMP-418_CH_SD**

In the logged-in Jellyfin UI:

1. Open the existing `MKMP-418_CH_SD` item.
2. Choose **More > Modify images > Search**.
3. Confirm at least one Number Cover result is returned for canonical identifier `MKMP-418`.
4. Confirm the source label is `Number Cover (Jable)` or `Number Cover (DMM fallback)`.
5. Apply the returned image and verify the primary-image tag or displayed dimensions change.

- [ ] **Step 6: Run final consistency checks**

Verify:

```text
All tests pass.
Build has 0 warnings and 0 errors.
Git diff check is clean.
Feature worktree contains no uncommitted tracked changes.
Built DLL, ZIP DLL, and installed DLL SHA-256 values match.
Jellyfin is serving HTTP 200 with Number Cover 0.1.2.0 loaded.
```
