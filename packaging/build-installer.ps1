[CmdletBinding()]
param(
    [string]$Version,
    [string]$Configuration = 'Release',
    [string]$OutputDirectory,
    [string]$StagingDirectory
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$versionFile = (Get-Content -LiteralPath (Join-Path $projectRoot 'VERSION') -Raw).Trim()

if ([string]::IsNullOrEmpty($Version)) { $Version = $versionFile }
elseif ($Version -ne $versionFile) {
    throw "Requested version $Version does not match VERSION $versionFile."
}

if ([string]::IsNullOrEmpty($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot 'dist\installer'
}
if (-not (Test-Path -LiteralPath $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
}

# This adapter publishes a self-contained payload and compiles a genuine
# interactive installer with Inno Setup 6. It fails honestly when ISCC is
# unavailable; a staging ZIP is never accepted as an installer.

$runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + "-$PID"
if ([string]::IsNullOrWhiteSpace($StagingDirectory)) {
    $StagingDirectory = Join-Path $projectRoot "build\$Version\installer-$runId\win-x64"
}
$buildDir = [System.IO.Path]::GetFullPath($StagingDirectory)
$buildRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'build'))
$buildRootPrefix = $buildRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $buildDir.StartsWith($buildRootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Installer staging must stay under the repository build directory: $buildDir"
}
$appCsproj = Join-Path $projectRoot 'src\AiPet.App\AiPet.App.csproj'

if (Test-Path -LiteralPath $buildDir) {
    throw "Refusing to reuse installer staging directory: $buildDir"
}
New-Item -ItemType Directory -Path $buildDir | Out-Null

Write-Output "[INFO ] dotnet publish (installer staging) -> $buildDir"
$publishLog = & dotnet publish $appCsproj -c $Configuration -r win-x64 --self-contained true -o $buildDir -p:UseAppHost=true -p:NuGetAudit=false -p:RestoreIgnoreFailedSources=true -nologo
$publishExit = $LASTEXITCODE
$publishLog | Select-Object -First 10 | ForEach-Object { Write-Output $_ }
if ($publishExit -ne 0) { throw "dotnet publish failed with exit code $publishExit" }

$assetRoot = Join-Path $projectRoot 'assets\pets'
if (Test-Path -LiteralPath $assetRoot) {
    $assetDst = Join-Path $buildDir 'assets\pets'
    New-Item -ItemType Directory -Path $assetDst -Force | Out-Null
    Write-Output "[INFO ] copying assets/pets -> $assetDst"
    Copy-Item -Path (Join-Path $assetRoot '*') -Destination $assetDst -Recurse -Force
} else {
    throw "Pet assets are missing: $assetRoot"
}

$manualSource = Join-Path $projectRoot 'docs\USER_MANUAL.html'
if (-not (Test-Path -LiteralPath $manualSource -PathType Leaf)) {
    throw "Offline user manual is missing: $manualSource"
}
$manualDirectory = Join-Path $buildDir 'docs'
New-Item -ItemType Directory -Path $manualDirectory -Force | Out-Null
Copy-Item -LiteralPath $manualSource -Destination (Join-Path $manualDirectory 'USER_MANUAL.html') -Force

$isccPath = $null
if (-not [string]::IsNullOrWhiteSpace($env:AIPET_INNO) -and (Test-Path -LiteralPath $env:AIPET_INNO -PathType Leaf)) {
    $isccPath = $env:AIPET_INNO
} else {
    $iscc = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($iscc) { $isccPath = $iscc.Source }

    # Inno Setup's installer is commonly installed outside PATH. Probe the
    # standard per-machine locations after honoring AIPET_INNO/PATH so a
    # normal developer machine can run the repository packaging entry point
    # without a one-off shell environment change.
    if (-not $isccPath) {
        $standardInnoRoots = @()
        if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
            $standardInnoRoots += ${env:ProgramFiles(x86)}
        }
        if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
            $standardInnoRoots += $env:ProgramFiles
        }
        foreach ($innoRoot in ($standardInnoRoots | Select-Object -Unique)) {
            $candidate = Join-Path $innoRoot 'Inno Setup 6\ISCC.exe'
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                $isccPath = $candidate
                Write-Output "[INFO ] detected Inno Setup compiler: $isccPath"
                break
            }
        }
    }
}
if (-not $isccPath) {
    throw 'Inno Setup 6 is required to build setup.exe. Install it or set AIPET_INNO to the full ISCC.exe path. No staging ZIP will be treated as an installer.'
}

$issPath = Join-Path $PSScriptRoot 'windows-ai-desktop-pet.iss'
if (-not (Test-Path -LiteralPath $issPath -PathType Leaf)) {
    throw "Inno Setup definition is missing: $issPath"
}

$compilerArguments = @(
    "/DMyAppVersion=$Version",
    "/DSourceDir=$buildDir",
    "/DOutputDir=$OutputDirectory"
)
if (-not [string]::IsNullOrWhiteSpace($env:AIPET_SIGN_CERT_PATH)) {
    # Inno invokes the same repository signer for both Setup and its generated
    # uninstaller. $q and $f are Inno placeholders and must remain literal.
    $signScript = Join-Path $projectRoot 'scripts\sign.ps1'
    $signCommand = 'pwsh.exe -NoProfile -File $q' + $signScript + '$q -Path $f'
    $compilerArguments += "--signtool=aipet=$signCommand"
    $compilerArguments += '/DSignBuild=1'
}
$compilerArguments += $issPath
& $isccPath @compilerArguments
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE" }

$setupPath = Join-Path $OutputDirectory "windows-ai-desktop-pet-v$Version-setup.exe"
if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf)) {
    throw "Expected installer was not generated: $setupPath"
}
Write-Output "[OK   ] installer asset: $setupPath"
