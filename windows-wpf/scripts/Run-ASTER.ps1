$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\ASTER.Workstation.App\ASTER.Workstation.App.csproj'

Write-Host ''
Write-Host 'ASTER AI WORKSTATION 0.1' -ForegroundColor Yellow
Write-Host 'Checking .NET SDK...' -ForegroundColor Gray

try {
    $sdk = & dotnet --version
    if ($LASTEXITCODE -ne 0) { throw '.NET SDK not found.' }
    Write-Host "Found .NET SDK: $sdk" -ForegroundColor Green
}
catch {
    Write-Host '.NET SDK 10 is not available.' -ForegroundColor Red
    Write-Host 'Install .NET 10 SDK for Windows x64:'
    Write-Host 'https://dotnet.microsoft.com/download/dotnet/10.0'
    Read-Host 'Press Enter to exit' | Out-Null
    exit 1
}

Set-Location $root
Write-Host 'Restoring packages and starting ASTER...' -ForegroundColor Gray
& dotnet run --project $project

if ($LASTEXITCODE -ne 0) {
    Write-Host 'ASTER could not start. Send me the complete error above.' -ForegroundColor Red
    Read-Host 'Press Enter to exit' | Out-Null
    exit $LASTEXITCODE
}
