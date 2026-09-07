[CmdletBinding()]
param(
    [switch]$CI
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path

& (Join-Path $PSScriptRoot 'validate-project.ps1') -CI:$CI
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'test-release-evidence.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Recurse -File -ErrorAction SilentlyContinue | Where-Object {
    $_.Name -ne '.gitkeep'
})

if ($sourceFiles.Count -eq 0) {
    Write-Output '[SKIP] No application source exists in this bootstrap snapshot.'
    Write-Output '[INFO] Runtime, integration, E2E, and packaging tests are not applicable yet.'
    exit 0
}

$testRunnerFound = $false

if (Test-Path -LiteralPath (Join-Path $projectRoot 'pyproject.toml')) {
    $testRunnerFound = $true
    & python -m pytest
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
elseif (Test-Path -LiteralPath (Join-Path $projectRoot 'package.json')) {
    $testRunnerFound = $true
    & npm test -- --runInBand
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
elseif (Test-Path -LiteralPath (Join-Path $projectRoot 'src\AiPet.sln') -PathType Leaf) {
    $testRunnerFound = $true
    $solution = Get-Item -LiteralPath (Join-Path $projectRoot 'src\AiPet.sln')
    $isolatedDotnetRoot = Join-Path $projectRoot 'build\dotnet-user'
    $isolatedNugetRoot = Join-Path $isolatedDotnetRoot 'NuGet'
    $isolatedLocalData = Join-Path $isolatedDotnetRoot 'AppData\Local'
    [System.IO.Directory]::CreateDirectory($isolatedNugetRoot) | Out-Null
    [System.IO.Directory]::CreateDirectory($isolatedLocalData) | Out-Null
    [System.IO.File]::Copy(
        (Join-Path $projectRoot 'NuGet.Config'),
        (Join-Path $isolatedNugetRoot 'NuGet.Config'),
        $true)
    $env:APPDATA = $isolatedDotnetRoot
    $env:LOCALAPPDATA = $isolatedLocalData

    Write-Output "[INFO ] restoring: dotnet restore $($solution.FullName) with isolated user configuration"
    & dotnet restore $solution.FullName --configfile (Join-Path $projectRoot 'NuGet.Config') --ignore-failed-sources --nologo -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & dotnet build $solution.FullName --configuration Release --no-restore --nologo -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Write-Output "[INFO ] running: dotnet test $($solution.FullName) --configuration Release --no-restore"
    & dotnet test $solution.FullName --configuration Release --no-restore --nologo -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
elseif (Test-Path -LiteralPath (Join-Path $projectRoot 'CMakeLists.txt')) {
    $testRunnerFound = $true
    $testBuildDirectory = Join-Path $projectRoot 'build/tests'
    & cmake -S $projectRoot -B $testBuildDirectory -DBUILD_TESTING=ON
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & cmake --build $testBuildDirectory --config Release
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & ctest --test-dir $testBuildDirectory -C Release --output-on-failure
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if (-not $testRunnerFound) {
    Write-Error 'Application source exists, but scripts/test.ps1 has no configured test runner for the selected stack.'
    exit 1
}

Write-Output 'All configured automated tests completed successfully.'
exit 0
