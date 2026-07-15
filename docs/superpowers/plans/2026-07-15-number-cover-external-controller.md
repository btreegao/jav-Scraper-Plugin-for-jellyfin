# Number Cover External Configuration Controller Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Number Cover configuration page load and save its values reliably in Jellyfin Web 10.11.11 by replacing the non-executing inline script with an embedded external controller module.

**Architecture:** The HTML page declares Jellyfin's `data-controller="__plugin/NumberCoverConfigJs"` hook and contains only markup. A separate embedded ES module is registered as a second `PluginPageInfo`, instantiated by Jellyfin for the current page instance, and uses page-scoped selectors plus Jellyfin's configuration APIs.

**Tech Stack:** C# 13 / .NET 9, Jellyfin Server and Web 10.11.11 plugin APIs, JavaScript ES modules, xUnit.

---

## File map

- Modify `Jellyfin.Plugin.NumberCover.Tests/ConfigurationPageTests.cs`: source-level contract tests for the HTML controller hook, embedded resource registration, and controller behavior.
- Modify `Jellyfin.Plugin.NumberCover/Configuration/configPage.html`: configuration markup and Jellyfin controller declaration only.
- Create `Jellyfin.Plugin.NumberCover/Configuration/configPage.js`: page-scoped load/save controller.
- Modify `Jellyfin.Plugin.NumberCover/Plugin.cs`: register the JavaScript controller as a plugin web resource.
- Modify `Jellyfin.Plugin.NumberCover/Jellyfin.Plugin.NumberCover.csproj`: embed the JavaScript controller in the DLL.
- Update `outputs/NumberCover-v0.1.0.zip`: distributable package containing the verified Release DLL and README.

### Task 1: Lock the Jellyfin controller contract with failing tests

**Files:**
- Modify: `Jellyfin.Plugin.NumberCover.Tests/ConfigurationPageTests.cs`
- Test: `Jellyfin.Plugin.NumberCover.Tests/ConfigurationPageTests.cs`

- [ ] **Step 1: Replace the inline-script tests with controller contract tests**

Replace `ConfigurationPageTests.cs` with:

```csharp
using Xunit;

namespace Jellyfin.Plugin.NumberCover.Tests;

public class ConfigurationPageTests
{
    [Fact]
    public void Page_uses_the_embedded_external_controller()
    {
        var html = ReadPluginFile("Configuration", "configPage.html");

        Assert.Contains("data-controller=\"__plugin/NumberCoverConfigJs\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Jellyfin_form_components_are_declared()
    {
        var html = ReadPluginFile("Configuration", "configPage.html");

        Assert.Contains("data-require=\"emby-input,emby-button,emby-checkbox\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"checkboxContainer checkboxContainer-withDescription\"", html, StringComparison.Ordinal);
        Assert.Contains("type=\"checkbox\" is=\"emby-checkbox\"", html, StringComparison.Ordinal);
        Assert.Contains("type=\"number\" is=\"emby-input\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Plugin_registers_the_controller_resource()
    {
        var source = ReadPluginFile("Plugin.cs");

        Assert.Contains("Name = \"NumberCoverConfigJs\"", source, StringComparison.Ordinal);
        Assert.Contains("Configuration.configPage.js", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_embeds_the_controller_resource()
    {
        var project = ReadPluginFile("Jellyfin.Plugin.NumberCover.csproj");

        Assert.Contains("<EmbeddedResource Include=\"Configuration\\configPage.js\" />", project, StringComparison.Ordinal);
    }

    [Fact]
    public void Controller_is_page_scoped_and_loads_and_saves_configuration()
    {
        var script = ReadPluginFile("Configuration", "configPage.js");

        Assert.Contains("export default function NumberCoverConfigController(page)", script, StringComparison.Ordinal);
        Assert.Contains("page.querySelector('#EnableJableSource')", script, StringComparison.Ordinal);
        Assert.Contains("page.addEventListener('viewshow', loadConfiguration)", script, StringComparison.Ordinal);
        Assert.Contains("apiClient.getPluginConfiguration(pluginId)", script, StringComparison.Ordinal);
        Assert.Contains("apiClient.updatePluginConfiguration(pluginId, config)", script, StringComparison.Ordinal);
        Assert.DoesNotContain("document.getElementById(", script, StringComparison.Ordinal);
        Assert.DoesNotContain("document.querySelector(", script, StringComparison.Ordinal);
    }

    private static string ReadPluginFile(params string[] relativeParts)
    {
        var pathParts = new[]
        {
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "Jellyfin.Plugin.NumberCover",
        }.Concat(relativeParts).ToArray();
        var path = Path.GetFullPath(Path.Combine(pathParts));

        Assert.True(File.Exists(path), $"Plugin file not found at {path}");
        return File.ReadAllText(path);
    }
}
```

- [ ] **Step 2: Run the focused tests and verify the red state**

Run:

```powershell
dotnet test .\Jellyfin.Plugin.NumberCover.Tests\Jellyfin.Plugin.NumberCover.Tests.csproj --no-restore --filter FullyQualifiedName~ConfigurationPageTests
```

Expected: FAIL because the HTML has no `data-controller`, `configPage.js` does not exist, and neither `Plugin.cs` nor the project file registers the JS resource.

- [ ] **Step 3: Commit the failing contract tests**

```powershell
git add -- Jellyfin.Plugin.NumberCover.Tests\ConfigurationPageTests.cs
git commit -m "test: define external config controller contract"
```

### Task 2: Implement the embedded external controller

**Files:**
- Modify: `Jellyfin.Plugin.NumberCover/Configuration/configPage.html`
- Create: `Jellyfin.Plugin.NumberCover/Configuration/configPage.js`
- Modify: `Jellyfin.Plugin.NumberCover/Plugin.cs`
- Modify: `Jellyfin.Plugin.NumberCover/Jellyfin.Plugin.NumberCover.csproj`
- Test: `Jellyfin.Plugin.NumberCover.Tests/ConfigurationPageTests.cs`

- [ ] **Step 1: Convert the HTML page to markup-only controller host**

Add the controller attribute to the root page element:

```html
<div id="NumberCoverConfigPage"
     data-role="page"
     class="page type-interior pluginConfigurationPage"
     data-require="emby-input,emby-button,emby-checkbox"
     data-controller="__plugin/NumberCoverConfigJs">
```

Delete the complete `<script>...</script>` block. Keep the existing form, error area, checkboxes, number inputs, and Save button unchanged.

- [ ] **Step 2: Add the page-scoped ES module controller**

Create `Jellyfin.Plugin.NumberCover/Configuration/configPage.js` with:

```javascript
const pluginId = 'c9c7b9d7-8eab-4f1b-8bf4-5ddc3f5ae9a7';
const defaultConfig = {
    EnableJableSource: false,
    OnlyFillMissing: true,
    MaxRequestsPerMinute: 12,
    TimeoutSeconds: 10
};

function withDefaults(config) {
    return Object.assign({}, defaultConfig, config || {});
}

function integerOrDefault(value, fallback) {
    const parsed = Number.parseInt(value, 10);
    return Number.isFinite(parsed) ? parsed : fallback;
}

export default function NumberCoverConfigController(page) {
    const apiClient = window.ApiClient;
    const dashboard = window.Dashboard;
    const form = page.querySelector('#NumberCoverConfigForm');
    const errorMessage = page.querySelector('#NumberCoverConfigError');

    function showError(message) {
        errorMessage.textContent = message;
        errorMessage.style.display = 'block';
    }

    function clearError() {
        errorMessage.textContent = '';
        errorMessage.style.display = 'none';
    }

    function loadConfiguration() {
        dashboard.showLoadingMsg();
        apiClient.getPluginConfiguration(pluginId).then(function (config) {
            config = withDefaults(config);
            page.querySelector('#EnableJableSource').checked = config.EnableJableSource;
            page.querySelector('#OnlyFillMissing').checked = config.OnlyFillMissing;
            page.querySelector('#MaxRequestsPerMinute').value = config.MaxRequestsPerMinute;
            page.querySelector('#TimeoutSeconds').value = config.TimeoutSeconds;
            clearError();
        }).catch(function (error) {
            console.error('Number Cover configuration load failed.', error);
            showError('Unable to load configuration.');
        }).then(function () {
            dashboard.hideLoadingMsg();
        });
    }

    page.addEventListener('viewshow', loadConfiguration);

    form.addEventListener('submit', function (event) {
        event.preventDefault();
        dashboard.showLoadingMsg();
        apiClient.getPluginConfiguration(pluginId).then(function (config) {
            config = withDefaults(config);
            config.EnableJableSource = page.querySelector('#EnableJableSource').checked;
            config.OnlyFillMissing = page.querySelector('#OnlyFillMissing').checked;
            config.MaxRequestsPerMinute = integerOrDefault(
                page.querySelector('#MaxRequestsPerMinute').value,
                defaultConfig.MaxRequestsPerMinute);
            config.TimeoutSeconds = integerOrDefault(
                page.querySelector('#TimeoutSeconds').value,
                defaultConfig.TimeoutSeconds);
            return apiClient.updatePluginConfiguration(pluginId, config);
        }).then(function (result) {
            clearError();
            dashboard.processPluginConfigurationUpdateResult(result);
        }).catch(function (error) {
            console.error('Number Cover configuration save failed.', error);
            showError('Unable to save configuration.');
        }).then(function () {
            dashboard.hideLoadingMsg();
        });
    });
}
```

- [ ] **Step 3: Register the controller with Jellyfin**

Change `Plugin.GetPages()` to return both resources:

```csharp
public IEnumerable<PluginPageInfo> GetPages()
{
    return
    [
        new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html",
        },
        new PluginPageInfo
        {
            Name = "NumberCoverConfigJs",
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.js",
        },
    ];
}
```

- [ ] **Step 4: Embed the controller module in the DLL**

Change the resource item group in `Jellyfin.Plugin.NumberCover.csproj` to:

```xml
<ItemGroup>
  <EmbeddedResource Include="Configuration\configPage.html" />
  <EmbeddedResource Include="Configuration\configPage.js" />
</ItemGroup>
```

- [ ] **Step 5: Run the focused tests and verify the green state**

Run:

```powershell
dotnet test .\Jellyfin.Plugin.NumberCover.Tests\Jellyfin.Plugin.NumberCover.Tests.csproj --no-restore --filter FullyQualifiedName~ConfigurationPageTests
```

Expected: PASS, 5 tests passed and 0 failed.

- [ ] **Step 6: Commit the implementation**

```powershell
git add -- Jellyfin.Plugin.NumberCover\Configuration\configPage.html Jellyfin.Plugin.NumberCover\Configuration\configPage.js Jellyfin.Plugin.NumberCover\Plugin.cs Jellyfin.Plugin.NumberCover\Jellyfin.Plugin.NumberCover.csproj
git commit -m "fix: load plugin settings through external controller"
```

### Task 3: Verify, package, install, and perform live acceptance

**Files:**
- Verify: `Jellyfin.Plugin.NumberCover.sln`
- Build: `Jellyfin.Plugin.NumberCover/bin/Release/net9.0/Jellyfin.Plugin.NumberCover.dll`
- Update: `outputs/NumberCover-v0.1.0.zip`
- Install: `%LOCALAPPDATA%/jellyfin/plugins/NumberCover-v0.1.0/Jellyfin.Plugin.NumberCover.dll`

- [ ] **Step 1: Run the complete automated test suite**

Run:

```powershell
dotnet test .\Jellyfin.Plugin.NumberCover.sln --no-restore
```

Expected: all tests pass with 0 failed.

- [ ] **Step 2: Build the Release plugin**

Run:

```powershell
dotnet build .\Jellyfin.Plugin.NumberCover\Jellyfin.Plugin.NumberCover.csproj -c Release --no-restore
```

Expected: Build succeeded with 0 errors; the DLL exists at `Jellyfin.Plugin.NumberCover/bin/Release/net9.0/Jellyfin.Plugin.NumberCover.dll`.

- [ ] **Step 3: Rebuild the distribution ZIP**

Run from the worktree root:

```powershell
$staging = Join-Path (Get-Location) 'artifacts\NumberCover-v0.1.0'
$outputDir = (Resolve-Path '..\..\..\outputs').Path
$zip = Join-Path $outputDir 'NumberCover-v0.1.0.zip'
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

Expected: the archive listing contains only `Jellyfin.Plugin.NumberCover.dll` and `README.md` at the ZIP root.

- [ ] **Step 4: Install the verified DLL and restart Jellyfin**

Run with approval because this stops Jellyfin and writes outside the workspace:

```powershell
$source = (Resolve-Path '.\Jellyfin.Plugin.NumberCover\bin\Release\net9.0\Jellyfin.Plugin.NumberCover.dll').Path
$target = 'C:\Users\Administrator\AppData\Local\jellyfin\plugins\NumberCover-v0.1.0\Jellyfin.Plugin.NumberCover.dll'
$server = 'D:\Program Files\Jellyfin\Server\jellyfin.exe'
$copied = $false

for ($attempt = 0; $attempt -lt 20; $attempt++) {
    Get-Process -Name jellyfin -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 300
    if (Get-Process -Name jellyfin -ErrorAction SilentlyContinue) {
        continue
    }

    try {
        Copy-Item -LiteralPath $source -Destination $target -Force -ErrorAction Stop
        $copied = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -eq
            (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
        if ($copied) {
            break
        }
    } catch {
        Start-Sleep -Milliseconds 300
    }
}

if (-not $copied) {
    throw 'Unable to install a byte-identical Number Cover DLL.'
}

Start-Process -FilePath $server -WorkingDirectory (Split-Path -Parent $server) -WindowStyle Hidden
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

Get-FileHash -LiteralPath $source -Algorithm SHA256
Get-FileHash -LiteralPath $target -Algorithm SHA256
```

The target DLL is:

```text
C:\Users\Administrator\AppData\Local\jellyfin\plugins\NumberCover-v0.1.0\Jellyfin.Plugin.NumberCover.dll
```

Expected: source and installed SHA-256 hashes are identical, then the Jellyfin public system endpoint returns HTTP 200.

- [ ] **Step 5: Verify the live configuration page in Jellyfin Web 10.11.11**

Open the Number Cover configuration page with a cache-busting query value and verify the visible current page shows:

```text
Enable Jable source: unchecked
Only fill missing covers: checked
Maximum requests per minute: 12
Timeout (seconds): 10
```

Also verify there is no “Unable to load configuration” message and no failed request for `NumberCoverConfigJs`.

- [ ] **Step 6: Preserve the current server configuration**

Do not toggle Enable Jable source and do not submit changed values. A save/reload test is optional and must use the same values or values explicitly requested by the user.

- [ ] **Step 7: Record final verification evidence**

Run `git status --short`, report the test count, Release build result, DLL hash match, Jellyfin startup response, and the four visible configuration values. Do not claim completion unless each result was observed in this run.
