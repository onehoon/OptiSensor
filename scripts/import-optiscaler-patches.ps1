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
    if ($LASTEXITCODE -eq 0) {
        Invoke-Git checkout -B $SourceRef "origin/$SourceRef"
        Invoke-Git pull --ff-only origin $SourceRef
    }
    else {
        Invoke-Git checkout $SourceRef
    }

    git rev-parse --verify "origin/$BaseRef" *> $null
    if ($LASTEXITCODE -eq 0) {
        $base = "origin/$BaseRef"
    }
    else {
        $base = $BaseRef
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
