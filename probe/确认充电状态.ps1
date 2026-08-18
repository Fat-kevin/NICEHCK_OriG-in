$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $scriptRoot 'ChargeStateProbe\ChargeStateProbe.csproj'

Write-Host 'Yuandao charge-state capture tool' -ForegroundColor Cyan
Write-Host 'Close the normal headset control app before continuing.' -ForegroundColor Yellow
Write-Host 'The native program will print the Chinese operation instructions.'
Write-Host ''

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnet)
{
    Write-Error 'dotnet was not found. Install the .NET 8 SDK first.'
    exit 1
}

& dotnet build "$project" --no-restore -c Debug '-p:Platform=x64'
if ($LASTEXITCODE -ne 0)
{
    Write-Error "The diagnostic project failed to build. Exit code: $LASTEXITCODE"
    exit $LASTEXITCODE
}

$executable = Join-Path $scriptRoot 'ChargeStateProbe\bin\x64\Debug\net8.0-windows10.0.19041.0\ChargeStateProbe.exe'
if (-not (Test-Path -LiteralPath $executable))
{
    Write-Error "The diagnostic executable was not found: $executable"
    exit 2
}

& $executable
exit $LASTEXITCODE
