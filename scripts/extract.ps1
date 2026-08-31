param(
    [Parameter(Mandatory = $true)]
    [string]$Pdf,

    [Parameter(Mandatory = $true)]
    [int]$DebtId,

    [string]$RunDate = (Get-Date -Format "yyyy-MM-dd"),

    [string]$OutJson = "out/extraction.json",

    [string]$OutSql = "out/rates.sql",

    [string]$Provider = "",

    [switch]$ForceOcr
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

if (-not (Test-Path "out")) {
    New-Item -ItemType Directory -Path "out" | Out-Null
}

$args = @(
    "run", "--project", "src/MelloRoos.csproj", "--",
    "extract", $Pdf,
    "--debt-id", $DebtId,
    "--run-date", $RunDate,
    "--save-json", $OutJson,
    "-o", $OutSql
)

if ($Provider) {
    $args += @("--provider", $Provider)
}

if ($ForceOcr) {
    $args += "--force-ocr"
}

Write-Host "Running: dotnet $($args -join ' ')" -ForegroundColor Cyan
dotnet @args
