param(
    [Parameter(Mandatory = $true)]
    [string] $SourceRepo,

    [Parameter(Mandatory = $true)]
    [string] $SourceRef,

    [Parameter(Mandatory = $true)]
    [string] $BaseRef,

    [string] $OutputDir = "optiscaler/patches"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-Git {
    git @args
    if ($LASTEXITCODE -ne 0) {
        throw "git $args failed with exit code $LASTEXITCODE"
    }
}

function Resolve-GitCommit {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Ref
    )

    $output = git rev-parse --verify "$Ref^{commit}"
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0) {
        throw "Git ref '$Ref' could not be resolved to a commit (exit code $exitCode)."
    }

    $commit = ($output | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($commit)) {
        throw "Git ref '$Ref' resolved to an empty commit SHA."
    }

    return $commit
}

function Resolve-GitTree {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Ref
    )

    $output = git rev-parse --verify "$Ref^{tree}"
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0) {
        throw "Git ref '$Ref' could not be resolved to a tree (exit code $exitCode)."
    }

    $tree = ($output | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($tree)) {
        throw "Git ref '$Ref' resolved to an empty tree SHA."
    }

    return $tree
}

function Resolve-RemoteBranchOrRef {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Ref,

        [Parameter(Mandatory = $true)]
        [string] $Label
    )

    git ls-remote --exit-code --heads origin $Ref | Out-Null
    $lookupExitCode = $LASTEXITCODE

    if ($lookupExitCode -eq 0) {
        $revision = "origin/$Ref"
    }
    elseif ($lookupExitCode -eq 2) {
        $revision = $Ref
    }
    else {
        throw "Unable to look up $Label '$Ref' on origin (exit code $lookupExitCode)."
    }

    return Resolve-GitCommit $revision
}

function Assert-RepositoryContainedPath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RepoRoot,

        [Parameter(Mandatory = $true)]
        [string] $CandidatePath
    )

    $separators = @([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $repoRootFull = ([IO.Path]::GetFullPath($RepoRoot)).TrimEnd($separators)

    if ([IO.Path]::IsPathRooted($CandidatePath)) {
        $targetFull = [IO.Path]::GetFullPath($CandidatePath)
    }
    else {
        $targetFull = [IO.Path]::GetFullPath((Join-Path $repoRootFull $CandidatePath))
    }

    $targetFull = $targetFull.TrimEnd($separators)

    if ($targetFull -eq $repoRootFull) {
        throw "OutputDir must not be the repository root itself.`nOutputDir: $CandidatePath`nResolved target: $targetFull`nRepository root: $repoRootFull"
    }

    # Anchor the prefix comparison with a trailing separator so a sibling directory
    # that merely shares the repo root as a string prefix (e.g. "...\OptiSensor2\x")
    # is not mistaken for being inside the repository.
    $repoRootWithSeparator = $repoRootFull + [IO.Path]::DirectorySeparatorChar
    if (-not $targetFull.StartsWith($repoRootWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
        throw "OutputDir must resolve to a path inside the repository.`nOutputDir: $CandidatePath`nResolved target: $targetFull`nRepository root: $repoRootFull"
    }

    return $targetFull
}

$repoRoot = ([IO.Path]::GetFullPath((Resolve-Path (Join-Path $PSScriptRoot "..")).Path)).TrimEnd(
    [IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)

# Containment must be validated before any network access or filesystem mutation.
$targetDir = Assert-RepositoryContainedPath -RepoRoot $repoRoot -CandidatePath $OutputDir

$runId = "$PID-$([guid]::NewGuid().ToString('N'))"
$runRoot = Join-Path $repoRoot ".tmp/OptiScaler-patch-import-$runId"
$sourceDir = Join-Path $runRoot "source"
$generatedDir = Join-Path $runRoot "generated"
$smokeDir = Join-Path $runRoot "smoke"

New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
New-Item -ItemType Directory -Force -Path $generatedDir | Out-Null

try {
    if ($SourceRepo -match "^https?://") {
        $repoUrl = $SourceRepo
    }
    else {
        $repoUrl = "https://github.com/$SourceRepo.git"
    }

    Invoke-Git clone $repoUrl $sourceDir

    $sourceCommit = $null
    $baseCommit = $null
    $commitCount = 0
    $rangeSpec = $null

    Push-Location $sourceDir
    try {
        Invoke-Git fetch origin --tags --prune

        $sourceCommit = Resolve-RemoteBranchOrRef -Ref $SourceRef -Label "SourceRef"
        Invoke-Git checkout --detach $sourceCommit
        $checkedOutCommit = Resolve-GitCommit "HEAD"
        if ($checkedOutCommit -ne $sourceCommit) {
            throw "Checked-out commit '$checkedOutCommit' does not match requested SourceRef '$SourceRef' ('$sourceCommit')."
        }

        $baseCommit = Resolve-RemoteBranchOrRef -Ref $BaseRef -Label "BaseRef"

        git merge-base --is-ancestor $baseCommit $sourceCommit
        $ancestorExitCode = $LASTEXITCODE
        if ($ancestorExitCode -eq 1) {
            throw "BaseRef is not an ancestor of SourceRef. Existing patch stack was not modified.`nBaseRef: $BaseRef ($baseCommit)`nSourceRef: $SourceRef ($sourceCommit)"
        }
        elseif ($ancestorExitCode -ne 0) {
            throw "git merge-base --is-ancestor failed with exit code $ancestorExitCode."
        }

        $rangeSpec = "$baseCommit..$sourceCommit"
        $countOutput = git rev-list --count $rangeSpec
        $countExitCode = $LASTEXITCODE
        if ($countExitCode -ne 0) {
            throw "git rev-list --count '$rangeSpec' failed with exit code $countExitCode."
        }
        $commitCount = [int](($countOutput | Out-String).Trim())
        if ($commitCount -le 0) {
            throw "No commits exist in patch range '$rangeSpec'. Existing patch stack was not modified."
        }

        Invoke-Git format-patch $rangeSpec -o $generatedDir
    }
    finally {
        Pop-Location
    }

    $generatedPatches = @(Get-ChildItem -LiteralPath $generatedDir -Filter "*.patch" -File | Sort-Object Name)
    if ($generatedPatches.Count -eq 0) {
        throw "No patches were generated for range '$BaseRef..$SourceRef'. Existing patch stack was not modified."
    }

    foreach ($patch in $generatedPatches) {
        if ($patch.Length -le 0) {
            throw "Generated patch file '$($patch.Name)' is empty. Existing patch stack was not modified."
        }
    }

    Write-Host "Generated $($generatedPatches.Count) patch(es) in range '$BaseRef..$SourceRef' ($commitCount commit(s)):"
    $generatedPatches | ForEach-Object { Write-Host " - $($_.Name)" }

    Push-Location $sourceDir
    try {
        Invoke-Git worktree add --detach $smokeDir $baseCommit
    }
    finally {
        Pop-Location
    }

    $expectedTree = $null

    Push-Location $smokeDir
    try {
        Invoke-Git config user.name "optiscaler-patch-import"
        Invoke-Git config user.email "optiscaler-patch-import@local"

        foreach ($patch in $generatedPatches) {
            git am $patch.FullName
            if ($LASTEXITCODE -ne 0) {
                $failedPatchName = $patch.Name
                git am --abort | Out-Null
                throw "Smoke 'git am' failed applying patch '$failedPatchName'. Existing patch stack was not modified."
            }
        }

        $expectedTree = Resolve-GitTree $sourceCommit
        $actualTree = Resolve-GitTree "HEAD"

        if ($actualTree -ne $expectedTree) {
            throw "Generated patch stack applied successfully but produced a different tree than SourceRef. Existing patch stack was not modified."
        }
    }
    finally {
        Pop-Location
    }

    Write-Host "Smoke apply succeeded: patches applied cleanly on BaseRef and match SourceRef tree ($expectedTree)."

    $targetParent = Split-Path -Path $targetDir -Parent
    if (-not (Test-Path -LiteralPath $targetParent)) {
        New-Item -ItemType Directory -Force -Path $targetParent | Out-Null
    }

    $replacementDir = Join-Path $targetParent (".patches-new-" + [guid]::NewGuid().ToString('N'))
    $backupDir = Join-Path $targetParent (".patches-backup-" + [guid]::NewGuid().ToString('N'))

    New-Item -ItemType Directory -Force -Path $replacementDir | Out-Null

    if (Test-Path -LiteralPath $targetDir) {
        Get-ChildItem -LiteralPath $targetDir -Force |
            Where-Object { -not ($_.Extension -eq ".patch" -and -not $_.PSIsContainer) } |
            ForEach-Object {
                Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $replacementDir $_.Name) -Recurse -Force
            }
    }

    foreach ($patch in $generatedPatches) {
        Copy-Item -LiteralPath $patch.FullName -Destination (Join-Path $replacementDir $patch.Name) -Force
    }

    $targetExisted = Test-Path -LiteralPath $targetDir
    $backupCreated = $false
    $replacementInstalled = $false

    try {
        if ($targetExisted) {
            Move-Item -LiteralPath $targetDir -Destination $backupDir
            $backupCreated = $true
        }

        Move-Item -LiteralPath $replacementDir -Destination $targetDir
        $replacementInstalled = $true

        $installedPatches = @(Get-ChildItem -LiteralPath $targetDir -Filter "*.patch" -File | Sort-Object Name)
        $expectedNames = @($generatedPatches | ForEach-Object { $_.Name } | Sort-Object)
        $installedNames = @($installedPatches | ForEach-Object { $_.Name } | Sort-Object)
        $nameDiff = @(Compare-Object -ReferenceObject $expectedNames -DifferenceObject $installedNames)

        if ($installedPatches.Count -ne $generatedPatches.Count -or $nameDiff.Count -ne 0) {
            throw "Installed patch files do not match the generated patch set after replacement."
        }

        if ($backupCreated) {
            Remove-Item -LiteralPath $backupDir -Recurse -Force
        }
    }
    catch {
        $replacementError = $_

        try {
            if ($replacementInstalled -and (Test-Path -LiteralPath $targetDir)) {
                Remove-Item -LiteralPath $targetDir -Recurse -Force
            }
            elseif ((-not $targetExisted) -and (Test-Path -LiteralPath $replacementDir)) {
                Remove-Item -LiteralPath $replacementDir -Recurse -Force
            }

            if ($backupCreated) {
                Move-Item -LiteralPath $backupDir -Destination $targetDir
            }
        }
        catch {
            throw "Patch stack replacement failed AND automatic rollback failed. Manual recovery required.`nBackup preserved at: $backupDir`nOriginal error: $($replacementError.Exception.Message)`nRollback error: $($_.Exception.Message)"
        }

        throw $replacementError
    }

    Write-Host ""
    Write-Host "Source repository: $SourceRepo"
    Write-Host "SourceRef: $SourceRef"
    Write-Host "Resolved source commit: $sourceCommit"
    Write-Host "BaseRef: $BaseRef"
    Write-Host "Resolved base commit: $baseCommit"
    Write-Host "Commit count: $commitCount"
    Write-Host "Generated patch count: $($generatedPatches.Count)"
    Write-Host "Smoke apply result: success (tree $expectedTree)"
    Write-Host "Output directory: $targetDir"
    Write-Host "Generated patch files:"
    $generatedPatches | ForEach-Object { Write-Host " - $($_.Name)" }
    Write-Host ""
    Write-Host "Patch stack replacement completed successfully."
}
finally {
    if (Test-Path -LiteralPath $runRoot) {
        try {
            Remove-Item -LiteralPath $runRoot -Recurse -Force
        }
        catch {
            Write-Warning "Failed to clean up temporary run directory '$runRoot': $($_.Exception.Message)"
        }
    }
}
