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
    'docs/Windows桌面宠物产品需求文档_PRD.md',
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
    $fullPath = Join-Path $projectRoot $relativePath
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        Add-ValidationFailure "Required file is missing: $relativePath"
    }
}

foreach ($relativePath in $requiredDirectories) {
    $fullPath = Join-Path $projectRoot $relativePath
    if (-not (Test-Path -LiteralPath $fullPath -PathType Container)) {
        Add-ValidationFailure "Required directory is missing: $relativePath"
    }
}

if ($failures.Count -eq 0) {
    Write-ValidationPass 'Required project structure is present.'
}

$versionPath = Join-Path $projectRoot 'VERSION'
if (Test-Path -LiteralPath $versionPath -PathType Leaf) {
    $version = [System.IO.File]::ReadAllText($versionPath).Trim()
    if ($version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
        Add-ValidationFailure "VERSION is not valid SemVer: $version"
    }
    else {
        Write-ValidationPass "VERSION is valid SemVer: $version"
        $changelogPath = Join-Path $projectRoot 'CHANGELOG.md'
        if (Test-Path -LiteralPath $changelogPath -PathType Leaf) {
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
    if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
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
        $relativePath = [System.IO.Path]::GetRelativePath($projectRoot, $file.FullName)
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
    $relativePath = [System.IO.Path]::GetRelativePath($projectRoot, $file.FullName)
    Add-ValidationFailure "Potential secret file must not be committed: $relativePath"
}

if ($forbiddenSecretFiles.Count -eq 0) {
    Write-ValidationPass 'No forbidden secret file names were found.'
}

$unexpectedBuildFiles = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'build') -Recurse -Force -File -ErrorAction SilentlyContinue | Where-Object {
    $_.Name -ne '.gitkeep'
}

foreach ($file in $unexpectedBuildFiles) {
    $relativePath = [System.IO.Path]::GetRelativePath($projectRoot, $file.FullName)
    Add-ValidationFailure "Temporary build output must not be committed: $relativePath"
}

if ($failures.Count -gt 0) {
    Write-Output "Validation failed with $($failures.Count) error(s)."
    exit 1
}

Write-Output 'Project validation completed successfully.'
exit 0
