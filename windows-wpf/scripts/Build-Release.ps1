$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\ASTER.Workstation.App\ASTER.Workstation.App.csproj'
$output = Join-Path $root 'publish\win-x64'

Set-Location $root
Write-Host 'Публикация ASTER AI Workstation для Windows x64...' -ForegroundColor DarkYellow
& dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $output
if ($LASTEXITCODE -ne 0) { throw 'Публикация завершилась с ошибкой.' }
Write-Host "Готово. Папка приложения: $output" -ForegroundColor Green
