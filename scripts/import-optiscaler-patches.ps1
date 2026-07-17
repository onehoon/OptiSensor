param(
    [Parameter(Mandatory = $true)]
    [string] $SourceRepo,

    [Parameter(Mandatory = $true)]
    [string] $SourceRef,

    [Parameter(Mandatory = $true)]
    [string] $BaseRef,

    [string] $OutputDir = "optiscaler/patches"
)

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

    $commit = (git rev-parse --verify "$Ref^{commit}").Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Git ref '$Ref' could not be resolved to a commit (exit code $LASTEXITCODE)."
    }

    return $commit
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$workRoot = Join-Path $repoRoot ".tmp"
$cloneDir = Join-Path $workRoot "OptiScaler-patch-import"
$resolvedOutputDir = Join-Path $repoRoot $OutputDir

if ($SourceRepo -match "^https?://") {
    $repoUrl = $SourceRepo
}
else {
    $repoUrl = "https://github.com/$SourceRepo.git"
}

if (Test-Path -LiteralPath $cloneDir) {
    Remove-Item -LiteralPath $cloneDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $workRoot, $resolvedOutputDir | Out-Null

Invoke-Git clone $repoUrl $cloneDir
Push-Location $cloneDir
try {
    Invoke-Git fetch origin --tags --prune

    git ls-remote --exit-code --heads origin $SourceRef | Out-Null
    $sourceBranchLookupExitCode = $LASTEXITCODE
    if ($sourceBranchLookupExitCode -eq 0) {
        $sourceRevision = "origin/$SourceRef"
    }
    elseif ($sourceBranchLookupExitCode -eq 2) {
        $sourceRevision = $SourceRef
    }
    else {
        throw "Unable to look up source branch '$SourceRef' on origin (exit code $sourceBranchLookupExitCode)."
    }

    $expectedSourceCommit = Resolve-GitCommit $sourceRevision
    Invoke-Git checkout --detach $expectedSourceCommit
    $checkedOutCommit = Resolve-GitCommit "HEAD"
    if ($checkedOutCommit -ne $expectedSourceCommit) {
        throw "Checked out commit '$checkedOutCommit' does not match requested source ref '$SourceRef' ('$expectedSourceCommit')."
    }

    git show-ref --verify --quiet "refs/remotes/origin/$BaseRef"
    $baseBranchLookupExitCode = $LASTEXITCODE
    if ($baseBranchLookupExitCode -eq 0) {
        $base = Resolve-GitCommit "origin/$BaseRef"
    }
    elseif ($baseBranchLookupExitCode -eq 1) {
        $base = Resolve-GitCommit $BaseRef
    }
    else {
        throw "Unable to look up base branch '$BaseRef' on origin (exit code $baseBranchLookupExitCode)."
    }

    Get-ChildItem $resolvedOutputDir -Filter "*.patch" -ErrorAction SilentlyContinue | Remove-Item -Force

    Invoke-Git format-patch "$base..HEAD" -o $resolvedOutputDir
}
finally {
    Pop-Location
}

$patches = Get-ChildItem $resolvedOutputDir -Filter "*.patch" | Sort-Object Name
if ($patches.Count -eq 0) {
    throw "No patches were generated. Check SourceRef '$SourceRef' and BaseRef '$BaseRef'."
}

Write-Host "Generated patches:"
$patches | ForEach-Object { Write-Host " - $($_.FullName)" }
