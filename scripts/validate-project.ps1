[CmdletBinding()]
param(
    [switch]$CI
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$failures = [System.Collections.Generic.List[string]]::new()
$excludedScanRoots = @(
    (Join-Path $projectRoot '.git'),
    # build/ contains both .staging/ (real intermediates, git-ignored)
    # and historical portable/ folders; it is not committable per
    # PROJECT_SPEC §2, so the validator skips it. Individual cleanups
    # of staged build outputs are still the responsibility of the
    # packaging adapters.
    (Join-Path $projectRoot 'build'),
    (Join-Path $projectRoot 'node_modules'),
    (Join-Path $projectRoot '.cache')
)

function Test-ExcludedScanPath {
    param([Parameter(Mandatory)][string]$Path)
    foreach ($excludedRoot in $excludedScanRoots) {
        if ($Path.Equals($excludedRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
            $Path.StartsWith("$excludedRoot$([System.IO.Path]::DirectorySeparatorChar)", [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }
    return $false
}

function Add-ValidationFailure {
    param([Parameter(Mandatory)][string]$Message)
    $failures.Add($Message)
    Write-Output "[FAIL] $Message"
}

function Write-ValidationPass {
    param([Parameter(Mandatory)][string]$Message)
    Write-Output "[PASS] $Message"
}

$requiredFiles = @(
    'AGENTS.md',
    'README.md',
    'VERSION',
    'CHANGELOG.md',
    '.gitattributes',
    'assets/README.md',
    'docs/PROJECT_SPEC.md',
    # PRD lives under docs/ with a CJK leaf; we use a wildcard so the
    # check works under both pwsh 7 and Windows PowerShell 5.1 (where
    # CJK path literals in script source get mangled by the GBK
    # console codepage).
    'docs/*_PRD.md',
    'CLAUDE.md',
    'GEMINI.md',
    '.github/copilot-instructions.md',
    '.cursor/rules/project-governance.mdc',
    '.windsurfrules',
    '.clinerules',
    '.roo/rules/01-project-governance.md',
    '.continue/rules/project-governance.md'
)

$requiredDirectories = @(
    'src',
    'tests/unit',
    'tests/integration',
    'tests/e2e',
    'tests/fixtures',
    'docs',
    'assets/icons',
    'assets/pets',
    'config',
    'scripts',
    'packaging',
    'build',
    'dist/portable',
    'dist/installer',
    'dist/checksums',
    '.github/workflows'
)

foreach ($relativePath in $requiredFiles) {
    # Walk the path component-by-component with Get-ChildItem -Literal.
    # Get-ChildItem reads the OS Unicode layer directly, sidestepping
    # the PS 5.1 source-file GBK decoding that breaks literal CJK
    # path components inside this script file. Wildcards in the final
    # segment are expanded by the OS, so `docs/*_PRD.md` matches
    # `docs/Windows桌面宠物产品需求文档_PRD.md` regardless of locale.
    $parts  = $relativePath -split '[\\/]'
    $cursor = $projectRoot
    $ok = $true
    for ($i = 0; $i -lt $parts.Count; $i++) {
        $part = $parts[$i]
        $isLast = ($i -eq $parts.Count - 1)
        $next = if ($cursor -is [string]) { Join-Path $cursor $part } else { Join-Path $cursor.FullName $part }
        if ($isLast) {
            # Last segment: if it contains wildcard chars, expand via the OS.
            if ($part -match '[\*\?]') {
                $parent = if ($cursor -is [string]) { $cursor } else { $cursor.FullName }
                $items = Get-ChildItem -LiteralPath $parent -ErrorAction SilentlyContinue |
                    Where-Object { $_.Name -like $part }
                if ($null -eq $items -or @($items).Count -eq 0) { $ok = $false }
                break
            }
            if (-not [System.IO.File]::Exists($next)) { $ok = $false }
            break
        }
        if (-not [System.IO.Directory]::Exists($next)) { $ok = $false; break }
        $cursor = Get-Item -LiteralPath $next -ErrorAction SilentlyContinue
        if ($null -eq $cursor) { $ok = $false; break }
    }
    if (-not $ok) {
        Add-ValidationFailure "Required file is missing: $relativePath"
    }
}

foreach ($relativePath in $requiredDirectories) {
    $fullPath = Join-Path $projectRoot $relativePath
    if (-not [System.IO.Directory]::Exists($fullPath)) {
        Add-ValidationFailure "Required directory is missing: $relativePath"
    }
}

if ($failures.Count -eq 0) {
    Write-ValidationPass 'Required project structure is present.'
}

$versionPath = Join-Path $projectRoot 'VERSION'
if ([System.IO.File]::Exists($versionPath)) {
    $version = [System.IO.File]::ReadAllText($versionPath).Trim()
    if ($version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
        Add-ValidationFailure "VERSION is not valid SemVer: $version"
    }
    else {
        Write-ValidationPass "VERSION is valid SemVer: $version"
        $changelogPath = Join-Path $projectRoot 'CHANGELOG.md'
        if ([System.IO.File]::Exists($changelogPath)) {
            $changelog = [System.IO.File]::ReadAllText($changelogPath)
            if ($changelog -notmatch [regex]::Escape("## [$version]")) {
                Add-ValidationFailure "CHANGELOG.md has no entry for VERSION $version."
            }
            else {
                Write-ValidationPass 'CHANGELOG.md contains the current version.'
            }
        }
    }
}

$aiEntryFiles = @(
    'CLAUDE.md',
    'GEMINI.md',
    '.github/copilot-instructions.md',
    '.cursor/rules/project-governance.mdc',
    '.windsurfrules',
    '.clinerules',
    '.roo/rules/01-project-governance.md',
    '.continue/rules/project-governance.md'
)

foreach ($relativePath in $aiEntryFiles) {
    $fullPath = Join-Path $projectRoot $relativePath
    if ([System.IO.File]::Exists($fullPath)) {
        $content = [System.IO.File]::ReadAllText($fullPath)
        if ($content -notmatch 'AGENTS\.md') {
            Add-ValidationFailure "AI compatibility entry does not reference AGENTS.md: $relativePath"
        }
    }
}

if (-not ($failures | Where-Object { $_ -like 'AI compatibility entry*' })) {
    Write-ValidationPass 'AI compatibility entries reference the canonical AGENTS.md.'
}

$strictUtf8 = [System.Text.UTF8Encoding]::new($false, $true)
$textNames = @('VERSION', '.gitignore', '.gitattributes', '.windsurfrules', '.clinerules')
$textExtensions = @('.md', '.mdc', '.txt', '.json', '.yml', '.yaml', '.ps1', '.toml')
$textFiles = Get-ChildItem -LiteralPath $projectRoot -Recurse -Force -File | Where-Object {
    -not (Test-ExcludedScanPath -Path $_.FullName) -and
    ($textNames -contains $_.Name -or $textExtensions -contains $_.Extension.ToLowerInvariant())
}

foreach ($file in $textFiles) {
    try {
        $null = $strictUtf8.GetString([System.IO.File]::ReadAllBytes($file.FullName))
    }
    catch {
        # PS 5.1 ships .NET 4.5.2 and lacks Path.GetRelativePath; do a
        # safe substring of the project root instead.
        $relativePath = $file.FullName
        if ($relativePath.StartsWith($projectRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            $relativePath = $relativePath.Substring($projectRoot.Length).TrimStart('\', '/')
        }
        Add-ValidationFailure "Text file is not valid UTF-8: $relativePath"
    }
}

if (-not ($failures | Where-Object { $_ -like 'Text file is not valid UTF-8*' })) {
    Write-ValidationPass "Validated UTF-8 encoding for $($textFiles.Count) text files."
}

$forbiddenSecretFiles = Get-ChildItem -LiteralPath $projectRoot -Recurse -Force -File | Where-Object {
    -not (Test-ExcludedScanPath -Path $_.FullName) -and
    (($_.Name -match '^\.env(?:\..+)?$' -and $_.Name -ne '.env.example') -or
    $_.Extension.ToLowerInvariant() -in @('.key', '.pem', '.pfx', '.p12'))
}

foreach ($file in $forbiddenSecretFiles) {
    $relativePath = $file.FullName
    if ($relativePath.StartsWith($projectRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        $relativePath = $relativePath.Substring($projectRoot.Length).TrimStart('\', '/')
    }
    Add-ValidationFailure "Potential secret file must not be committed: $relativePath"
}

if ($forbiddenSecretFiles.Count -eq 0) {
    Write-ValidationPass 'No forbidden secret file names were found.'
}

# 0.1.0: build/ is fully excluded by .gitignore + the excludedScanRoots
# list above, so we skip the historical "unexpected build files" check.
# If you ever need to verify that build/ is empty pre-build, reintroduce
# the check with a `-SkipBuildScan` switch.
Write-ValidationPass 'build/ is git-ignored; contents are not validated as committable files.'

if ($failures.Count -gt 0) {
    Write-Output "Validation failed with $($failures.Count) error(s)."
    exit 1
}

Write-Output 'Project validation completed successfully.'
exit 0
