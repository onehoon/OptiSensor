<#
Builds OptiScaler.dll locally the same way the "Build OptiScaler 0.9" GitHub
Actions workflow does: clone optiscaler/OptiScaler, apply this repo's
release/0.9 patch stack, and build OptiScaler.vcxproj with MSBuild.

Usage:
  .\scripts\build-optiscaler-0.9-dll.ps1
  .\scripts\build-optiscaler-0.9-dll.ps1 -OptiScalerRef v1.2.3
  .\scripts\build-optiscaler-0.9-dll.ps1 -NoPause
#>

param(
    [string] $OptiScalerRef = "release/0.9",
    [switch] $NoPause
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ---- Fixed per-branch configuration ---------------------------------------
$OptiScalerRepo   = "optiscaler/OptiScaler"
$PatchSourceBranch = "release/0.9"
$OutputDir        = "C:\GoogleDrive\00. OptiScaler\06. OptiSensor\release-0.9"
$CacheDirName     = ".work/OptiScaler-0.9"

function Write-Step {
    param([string] $Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-Git {
    git @args
    if ($LASTEXITCODE -ne 0) {
        throw "git $args failed with exit code $LASTEXITCODE"
    }
}

function Resolve-GitCommit {
    param([Parameter(Mandatory = $true)] [string] $Revision)

    $output = git rev-parse --verify "$Revision^{commit}"
    if ($LASTEXITCODE -ne 0) {
        throw "Git ref '$Revision' could not be resolved to a commit (exit code $LASTEXITCODE)."
    }
    return ($output | Out-String).Trim()
}

function Resolve-MSBuildPath {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path -LiteralPath $vswhere)) {
        throw "vswhere.exe not found at '$vswhere'. Install Visual Studio (or Build Tools) with the 'Desktop development with C++' workload."
    }

    $installPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
    if ([string]::IsNullOrWhiteSpace($installPath)) {
        $installPath = & $vswhere -latest -prerelease -products * -requires Microsoft.Component.MSBuild -property installationPath
    }
    if ([string]::IsNullOrWhiteSpace($installPath)) {
        throw "Could not locate a Visual Studio installation with MSBuild via vswhere."
    }

    $msbuildPath = Join-Path $installPath.Trim() "MSBuild\Current\Bin\MSBuild.exe"
    if (-not (Test-Path -LiteralPath $msbuildPath)) {
        throw "MSBuild.exe not found at expected path '$msbuildPath'."
    }

    return $msbuildPath
}

$exitCode = 0
$buildTimestamp = Get-Date -Format "yyyyMMddHHmm"
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

try {
    $repoRoot = ([IO.Path]::GetFullPath((Resolve-Path (Join-Path $PSScriptRoot "..")).Path)).TrimEnd(
        [IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)

    Write-Step "Validating output folder"
    if (-not (Test-Path -LiteralPath $OutputDir)) {
        throw "Output folder does not exist: '$OutputDir'. Create it first (it is intentionally not auto-created since it lives on a synced drive)."
    }
    Write-Host "    $OutputDir"

    Write-Step "Resolving this repo's patch stack (branch '$PatchSourceBranch')"
    Invoke-Git -C $repoRoot rev-parse --verify "$PatchSourceBranch^{commit}" | Out-Null
    $patchNames = git -C $repoRoot ls-tree -r --name-only $PatchSourceBranch -- optiscaler/patches |
        Where-Object { $_ -like "*.patch" } |
        Sort-Object
    if (-not $patchNames -or $patchNames.Count -eq 0) {
        throw "No *.patch files found under optiscaler/patches on branch '$PatchSourceBranch'."
    }
    Write-Host "    Found $($patchNames.Count) patch(es):"
    $patchNames | ForEach-Object { Write-Host "      - $_" }

    Write-Step "Resolving MSBuild"
    $msbuild = Resolve-MSBuildPath
    Write-Host "    $msbuild"

    $cacheDir = Join-Path $repoRoot $CacheDirName

    if (-not (Test-Path -LiteralPath (Join-Path $cacheDir ".git"))) {
        Write-Step "Cloning $OptiScalerRepo (first run, this will take a while)"
        $parentDir = Split-Path -Path $cacheDir -Parent
        if (-not (Test-Path -LiteralPath $parentDir)) {
            New-Item -ItemType Directory -Force -Path $parentDir | Out-Null
        }
        Invoke-Git clone "https://github.com/$OptiScalerRepo.git" $cacheDir
    }
    else {
        Write-Step "Reusing cached OptiScaler clone"
        Write-Host "    $cacheDir"
    }

    Push-Location $cacheDir
    try {
        $rebaseApplyPath = Join-Path $cacheDir ".git\rebase-apply"
        if (Test-Path -LiteralPath $rebaseApplyPath) {
            Write-Step "Aborting leftover 'git am' state from a previous run"
            git am --abort
        }

        Write-Step "Fetching $OptiScalerRepo"
        Invoke-Git fetch origin --tags --prune

        Write-Step "Resolving OptiScaler ref '$OptiScalerRef'"
        git ls-remote --exit-code --heads origin $OptiScalerRef | Out-Null
        $branchLookupExitCode = $LASTEXITCODE
        if ($branchLookupExitCode -eq 0) {
            $revision = "origin/$OptiScalerRef"
        }
        elseif ($branchLookupExitCode -eq 2) {
            $revision = $OptiScalerRef
        }
        else {
            throw "Unable to query OptiScaler ref '$OptiScalerRef' (exit code $branchLookupExitCode)."
        }
        $expectedCommit = Resolve-GitCommit $revision
        Write-Host "    $OptiScalerRef -> $expectedCommit"

        Write-Step "Checking out $expectedCommit and cleaning stale build state"
        Invoke-Git checkout --detach $expectedCommit
        $checkedOutCommit = Resolve-GitCommit "HEAD"
        if ($checkedOutCommit -ne $expectedCommit) {
            throw "Checked-out OptiScaler commit '$checkedOutCommit' does not match requested ref '$OptiScalerRef' ('$expectedCommit')."
        }
        Invoke-Git reset --hard $expectedCommit
        # Single -f intentionally does not recurse into already-initialized
        # submodules (git treats them as nested repos), so this clears stray
        # build output / previous patch state without forcing a submodule
        # re-clone on every run.
        Invoke-Git clean -fdx

        Write-Step "Syncing submodules"
        Invoke-Git submodule sync --recursive
        Invoke-Git submodule update --init --recursive

        Write-Step "Applying $($patchNames.Count) patch(es) from this repo"
        git config user.name "OptiSensor Local Build"
        git config user.email "optisensor-local-build@example.invalid"

        $tempPatchDir = Join-Path ([IO.Path]::GetTempPath()) ("optisensor-patch-" + [guid]::NewGuid().ToString("N"))
        New-Item -ItemType Directory -Force -Path $tempPatchDir | Out-Null
        try {
            foreach ($name in $patchNames) {
                $leaf = Split-Path -Path $name -Leaf
                $tempPatchPath = Join-Path $tempPatchDir $leaf
                # Out-File would join lines with the OS newline (CRLF) and can
                # add a UTF-8 BOM depending on PowerShell version; git am needs
                # byte-exact LF line endings and no BOM, so write manually.
                $patchLines = git -C $repoRoot show "${PatchSourceBranch}:${name}"
                $patchContent = ($patchLines -join "`n") + "`n"
                [IO.File]::WriteAllText($tempPatchPath, $patchContent, (New-Object Text.UTF8Encoding($false)))
                Write-Host "    Applying $leaf"
                git am $tempPatchPath
                if ($LASTEXITCODE -ne 0) {
                    git am --abort | Out-Null
                    throw "Failed to apply '$leaf'. Patch conflicts are not auto-resolved; refresh the patch stack instead."
                }
            }
        }
        finally {
            Remove-Item -LiteralPath $tempPatchDir -Recurse -Force -ErrorAction SilentlyContinue
        }

        Write-Step "Building OptiScaler.vcxproj (Release|x64)"
        $vcxproj = Join-Path $cacheDir "OptiScaler\OptiScaler.vcxproj"
        & $msbuild $vcxproj /m /verbosity:minimal `
            /p:Configuration=Release `
            /p:Platform=x64 `
            /p:SolutionDir="$cacheDir\" `
            /p:PostBuildEventUseInBuild=false
        if ($LASTEXITCODE -ne 0) {
            throw "MSBuild failed with exit code $LASTEXITCODE."
        }

        Write-Step "Collecting OptiScaler.dll"
        $dll = Get-ChildItem $cacheDir -Recurse -Filter "OptiScaler.dll" |
            Where-Object { $_.FullName -match "\\x64\\" -and $_.FullName -match "\\Release\\" } |
            Select-Object -First 1
        if (-not $dll) {
            throw "OptiScaler.dll not found after build."
        }

        $destName = "OptiScaler-$buildTimestamp.dll"
        $destPath = Join-Path $OutputDir $destName
        Copy-Item -LiteralPath $dll.FullName -Destination $destPath -Force

        $stopwatch.Stop()
        Write-Host ""
        Write-Host "==> Build succeeded in $($stopwatch.Elapsed.ToString('mm\m\ ss\s'))" -ForegroundColor Green
        Write-Host "    OptiScaler commit: $expectedCommit"
        Write-Host "    Output: $destPath"
    }
    finally {
        Pop-Location
    }
}
catch {
    $exitCode = 1
    Write-Host ""
    Write-Host "==> Build FAILED: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.ScriptStackTrace) {
        Write-Host $_.ScriptStackTrace
    }
}
finally {
    if (-not $NoPause) {
        Write-Host ""
        Read-Host "Press Enter to close this window" | Out-Null
    }
}

exit $exitCode
