[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'src\UsageTrackerWidget.App\UsageTrackerWidget.App.csproj'
$output = Join-Path $repositoryRoot "artifacts\publish\$Runtime"

if (Test-Path $output) {
    Remove-Item -Path $output -Recurse -Force
}

dotnet publish $project `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --output $output `
    --nologo

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$executable = Join-Path $output 'UsageTrackerWidget.exe'
if (-not (Test-Path $executable)) {
    throw "Published executable was not found at $executable."
}

Write-Output "Published $executable"
