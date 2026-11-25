$ErrorActionPreference = 'Stop'

$repoUrl = 'https://github.com/v4deeqclaude-ui/ASTER-AI-Workstation.git'
$branch = 'feature/wpf-foundation-0.1'
$sourceRoot = Split-Path -Parent $PSScriptRoot
$workRoot = Join-Path $env:USERPROFILE 'ASTER-AI-Workstation-WPF-GitHub'

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Git was not found.'
}

if (Test-Path $workRoot) {
    throw "Destination folder already exists: $workRoot. Rename or remove it before retrying."
}

Write-Host 'Cloning the existing ASTER repository...' -ForegroundColor Yellow

git clone --branch main --single-branch $repoUrl $workRoot
if ($LASTEXITCODE -ne 0) {
    throw 'Repository clone failed. Check GitHub access.'
}

git -C $workRoot switch -c $branch
if ($LASTEXITCODE -ne 0) {
    throw 'Could not create the WPF branch.'
}

$destination = Join-Path $workRoot 'windows-wpf'
New-Item -ItemType Directory -Path $destination | Out-Null

Get-ChildItem -LiteralPath $sourceRoot -Force |
    Where-Object { $_.Name -notin @('.git', 'bin', 'obj', 'publish', 'artifacts') } |
    ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $destination -Recurse -Force
    }

Write-Host ''
Write-Host 'WPF project copied into windows-wpf.' -ForegroundColor Green
Write-Host 'The existing Electron files have not been changed.' -ForegroundColor Green
Write-Host "Local Git folder: $workRoot"
Write-Host "Branch: $branch"
Write-Host ''

$confirmation = Read-Host 'Type PUSH to publish this new branch to GitHub'
if ($confirmation -cne 'PUSH') {
    Write-Host 'Publishing cancelled. Local copy was kept.' -ForegroundColor Yellow
    exit 0
}

git -C $workRoot add -- windows-wpf
if ($LASTEXITCODE -ne 0) {
    throw 'Could not stage WPF files.'
}

git -C $workRoot commit -m 'Add WPF foundation 0.1'
if ($LASTEXITCODE -ne 0) {
    throw 'Commit failed. Check Git user name and email settings.'
}

git -C $workRoot push -u origin $branch
if ($LASTEXITCODE -ne 0) {
    throw 'Push failed. Local files and commit were kept.'
}

Write-Host "Published: https://github.com/v4deeqclaude-ui/ASTER-AI-Workstation/tree/$branch/windows-wpf" -ForegroundColor Green
