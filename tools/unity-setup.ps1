<#
    Prepare a freshly created Unity project to consume this repository.

    The Windows counterpart of tools/unity-setup.mjs, for machines without
    Node installed - which is most machines with Unity on them, since Unity
    does not bring Node along.

    Unity Hub has to create the project itself (it writes ProjectSettings tied
    to your Editor version); this picks up from there and does the fiddly
    parts: adds both packages to Packages/manifest.json, copies the content
    pack into Assets/Resources, and drops in a smoke test.

    Run it from anywhere:

        powershell -ExecutionPolicy Bypass -File tools\unity-setup.ps1

    Written for Windows PowerShell 5.1 as well as PowerShell 7, so it avoids
    syntax only one of them has.

    One deliberate difference from the Node version: PowerShell's
    ConvertFrom-Json accepts a trailing comma where JSON.parse rejects it, so
    where the Node script refuses a hand-broken manifest, this one parses and
    rewrites it - turning a file Unity would not open into a valid one. Both
    outcomes are safe; neither leaves the project worse than it found it.
#>

$ErrorActionPreference = 'Stop'

$repoRoot     = Split-Path -Parent $PSScriptRoot
# Nested Join-Path rather than literal 'unity\UnityProject': a backslash is
# only a separator on Windows, and Windows PowerShell 5.1's Join-Path takes
# just two path arguments, so the multi-argument form is out too.
$projectDir   = Join-Path (Join-Path $repoRoot 'unity') 'UnityProject'
$manifestPath = Join-Path (Join-Path $projectDir 'Packages') 'manifest.json'
$contentSrc   = Join-Path (Join-Path $repoRoot 'content') 'starter.json'

$packages = [ordered]@{
    'com.aetherlight.core'  = 'file:../../Aetherlight.Core'
    'com.aetherlight.unity' = 'file:../../Aetherlight.Unity'
}

function Write-Ok   ($m) { Write-Host "  [ok] $m" }
function Write-Fail ($m, $hint) {
    Write-Host ""
    Write-Host "  [!] $m" -ForegroundColor Red
    if ($hint) { Write-Host ""; Write-Host $hint }
    Write-Host ""
    exit 1
}

# UTF-8 without a byte-order mark. Windows PowerShell's Out-File writes a BOM
# by default, and a BOM in front of a JSON document is exactly the kind of
# thing that works everywhere until it doesn't.
function Write-TextFile ($path, $text) {
    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($path, $text, $encoding)
}

# --- 0. the project must already exist ---------------------------------------

if (-not (Test-Path $projectDir)) {
    Write-Fail "No Unity project at unity\UnityProject" @"
  Unity Hub has to create it, because it writes ProjectSettings that are
  tied to your Editor version.

  In Unity Hub:
    Projects -> New project
    Editor version: 2021.3 LTS or newer
    Template:       2D
    Project name:   UnityProject
    Location:       $(Join-Path $repoRoot 'unity')

  Then quit the Editor and run this script again.
"@
}

if (-not (Test-Path $manifestPath)) {
    Write-Fail "unity\UnityProject exists but has no Packages\manifest.json" @"
  That folder does not look like a Unity project. If Unity Hub refused to
  create one because the directory was not empty, delete unity\UnityProject
  and let Hub create it fresh.
"@
}

# --- 1. packages --------------------------------------------------------------

$manifestRaw = Get-Content -Raw -Path $manifestPath
try {
    $manifest = $manifestRaw | ConvertFrom-Json
} catch {
    # Reached only for input PowerShell cannot make sense of at all - a
    # truncated file, say. A trailing comma does not land here; it parses and
    # is written back out valid.
    Write-Fail "Packages\manifest.json could not be parsed: $($_.Exception.Message)" @"
  Unity will refuse to open the project until this is fixed. If the file was
  edited by hand, check for an unclosed brace or quote.
"@
}

# Keep a copy. If anything below produces a file that will not parse, the
# original goes back rather than leaving a project that cannot be opened.
$backupPath = "$manifestPath.bak"
Write-TextFile $backupPath $manifestRaw

$deps = [ordered]@{}
if ($manifest.PSObject.Properties.Name -contains 'dependencies') {
    foreach ($p in $manifest.dependencies.PSObject.Properties) { $deps[$p.Name] = $p.Value }
}

$added = @()
$already = @()
foreach ($name in $packages.Keys) {
    if ($deps[$name] -eq $packages[$name]) { $already += $name }
    else { $deps[$name] = $packages[$name]; $added += $name }
}

# Sorted, so re-running produces no diff.
$sorted = [ordered]@{}
foreach ($name in ($deps.Keys | Sort-Object)) { $sorted[$name] = $deps[$name] }

$out = [ordered]@{}
foreach ($p in $manifest.PSObject.Properties) {
    if ($p.Name -ne 'dependencies') { $out[$p.Name] = $p.Value }
}
$out['dependencies'] = $sorted

Write-TextFile $manifestPath (($out | ConvertTo-Json -Depth 20) + "`n")

# Read it back. PowerShell's JSON writer differs between versions, so trusting
# it without checking is how you find out on someone else's machine.
try {
    $check = (Get-Content -Raw -Path $manifestPath) | ConvertFrom-Json
    $names = $check.dependencies.PSObject.Properties.Name
    foreach ($name in $packages.Keys) {
        if ($names -notcontains $name) { throw "dependency '$name' missing after write" }
    }
} catch {
    Write-TextFile $manifestPath $manifestRaw
    Write-Fail "Wrote manifest.json but it did not read back correctly: $($_.Exception.Message)" @"
  The original has been restored from the backup, so the project is unchanged.
  Add these two lines inside the "dependencies" block by hand instead:

    "com.aetherlight.core": "file:../../Aetherlight.Core",
    "com.aetherlight.unity": "file:../../Aetherlight.Unity",
"@
}

Remove-Item $backupPath -ErrorAction SilentlyContinue
if ($added.Count   -gt 0) { Write-Ok ("added to manifest.json: "     + ($added   -join ', ')) }
if ($already.Count -gt 0) { Write-Ok ("already in manifest.json: "   + ($already -join ', ')) }

# --- 2. content ---------------------------------------------------------------

if (-not (Test-Path $contentSrc)) {
    Write-Fail "content\starter.json is missing" "  Check that the clone is complete."
}

$resourcesDir = Join-Path (Join-Path $projectDir 'Assets') 'Resources'
New-Item -ItemType Directory -Force -Path $resourcesDir | Out-Null
Copy-Item -Force -Path $contentSrc -Destination (Join-Path $resourcesDir 'starter.json')
Write-Ok "copied content\starter.json -> Assets\Resources\starter.json"

# --- 3. smoke test ------------------------------------------------------------

$smokeTestPath = Join-Path (Join-Path $projectDir 'Assets') 'CoreSmokeTest.cs'
if (Test-Path $smokeTestPath) {
    Write-Ok "Assets\CoreSmokeTest.cs already present, left alone"
} else {
    $smokeTest = @'
using UnityEngine;
using Aetherlight.Core;
using Aetherlight.Domain;

/// <summary>
/// Proves the rules core works inside Unity: package resolution, JSON parsing,
/// content validation, and class derivation from bound motes.
///
/// Put this on any GameObject and press Play. Written by tools/unity-setup.ps1;
/// delete it once the project is real.
/// </summary>
public class CoreSmokeTest : MonoBehaviour
{
    void Start()
    {
        var asset = Resources.Load<TextAsset>("starter");
        if (asset == null)
        {
            Debug.LogError("starter.json not found in Assets/Resources. Re-run tools/unity-setup.ps1.");
            return;
        }

        var registry = ContentRegistry.From(ContentLoader.LoadPack(asset.text));
        registry.AssertValid();

        var def = registry.ActorDef("rell");
        if (def == null)
        {
            Debug.LogError("Actor 'rell' is missing from the content pack.");
            return;
        }

        var rell = Actors.Create(def, 12, registry);
        rell.Motes.Add(new MoteInstance("boulder"));
        rell.Motes.Add(new MoteInstance("cinder"));

        // Terra plus pyre resolves to the dual class. If this prints
        // "ashwarden", the whole chain underneath it works.
        Debug.Log($"class: {Actors.DeriveClass(rell, registry)?.Id}");
        Debug.Log($"maxHp: {Actors.DeriveStats(rell, registry).MaxHp}");
        Debug.Log($"items in pack: {registry.Items.Count}");
    }
}
'@
    Write-TextFile $smokeTestPath $smokeTest
    Write-Ok "wrote Assets\CoreSmokeTest.cs"
}

Write-Host @"

  Next, in Unity:

    1. Open the project from Unity Hub.
    2. Watch the Console (Window -> General -> Console).
         clean               -> both packages compiled
         "Safe Mode?"        -> a compile error; click Ignore and read the Console
         "package not found" -> the project is not at unity\UnityProject
    3. GameObject -> Create Empty, drag CoreSmokeTest.cs onto it, press Play.

  Passing looks like:

    class: ashwarden
    items in pack: 8

  If anything errors, send the full Console text.

"@
