param(
  [string]$OutputZip = "artifacts/packages/site.zip"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

# Ensure output dirs exist
$outDir = Split-Path -Parent $OutputZip
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }
$backupDir = "artifacts/backups"
if (-not (Test-Path $backupDir)) { New-Item -ItemType Directory -Force -Path $backupDir | Out-Null }

# Create zip from sample-site contents
Compress-Archive -Path "sample-site/*" -DestinationPath $OutputZip -Force

Write-Host "Created $OutputZip"
