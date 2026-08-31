# Create a zip for client handoff (no git, no build artifacts, no secrets).
param(
    [string]$OutZip = "MelloRoos-client.zip"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$exclude = @(
    "*/bin/*", "*/obj/*", "out/*", ".git/*", ".github/*", ".scratch/*",
    ".DS_Store", ".env", ".vs/*",
    "Reference-Docs/CFD 1, Series 2002 (1).pdf"
)

if (Test-Path $OutZip) { Remove-Item $OutZip -Force }

$items = Get-ChildItem -Force | Where-Object { $_.Name -ne $OutZip }
Compress-Archive -Path $items -DestinationPath $OutZip -CompressionLevel Optimal

$size = (Get-Item $OutZip).Length / 1MB
Write-Host "Created $OutZip ($([math]::Round($size, 1)) MB)"
Write-Host "Send via file share if over email size limit (~25 MB)."
