# Windows smoke test — run before client handoff
# Usage:  .\scripts\test-windows.ps1
#         .\scripts\test-windows.ps1 -FullExtract   # also runs LLM extract (needs OPENAI_API_KEY)

param(
    [switch]$FullExtract
)

$ErrorActionPreference = "Stop"

if ($env:OS -ne "Windows_NT") {
    Write-Warning "This script is intended for Windows. Use GitHub Actions 'Windows smoke' workflow from macOS."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

function Step($Message) {
    Write-Host ""
    Write-Host "== $Message ==" -ForegroundColor Cyan
}

function Fail($Message) {
    Write-Host "FAIL: $Message" -ForegroundColor Red
    exit 1
}

Step "Build"
dotnet build MelloRoos.sln -c Release
if ($LASTEXITCODE -ne 0) { Fail "Build failed" }

Step "First-run setup + check-deps"
dotnet run --project src/MelloRoos.csproj -c Release -- check-deps
if ($LASTEXITCODE -ne 0) { Fail "check-deps failed — see INSTRUCTIONS.md Windows manual install" }

Step "pdftotext smoke (Fillmore CFD 8)"
$pdf = "Reference-Docs\City of Fillmore, CFD 8, RMA.pdf"
if (-not (Test-Path $pdf)) { Fail "Missing test PDF: $pdf" }

New-Item -ItemType Directory -Force -Path out | Out-Null
dotnet run --project src/MelloRoos.csproj -c Release -- text $pdf -o out/test-windows.txt
if ($LASTEXITCODE -ne 0) { Fail "text command failed" }

$chars = (Get-Item out/test-windows.txt).Length
Write-Host "Extracted $chars chars"
if ($chars -lt 1000) { Fail "pdftotext output too short ($chars chars)" }

Step "OCR smoke (scanned PDF, pages 1-2)"
$scanned = "Reference-Docs\CFD No. 2000-1, RMA (1).pdf"
if (-not (Test-Path $scanned)) { Fail "Missing scanned test PDF: $scanned" }

$ocrLog = (dotnet run --project src/MelloRoos.csproj -c Release -- text $scanned `
    --force-ocr --pages 1-2 -o out/test-ocr-forced.txt 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0) { Fail "OCR forced failed" }
if ($ocrLog -notmatch "Method: ocr-forced") { Fail "Expected ocr-forced in: $ocrLog" }

$fallbackLog = (dotnet run --project src/MelloRoos.csproj -c Release -- text $scanned `
    --pages 1-2 -o out/test-ocr-fallback.txt 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0) { Fail "OCR auto-fallback failed" }
if ($fallbackLog -notmatch "Method: ocr-fallback") { Fail "Expected ocr-fallback in: $fallbackLog" }

Write-Host "OCR OK (forced + auto-fallback)"

Step "escalate sample JSON (no LLM)"
dotnet run --project src/MelloRoos.csproj -c Release -- escalate src/rates.json `
    --debt-id 123 --run-date 2026-08-18 -o out/test-windows.sql
if ($LASTEXITCODE -ne 0) { Fail "escalate failed" }

if ($FullExtract) {
    Step "check-keys"
    dotnet run --project src/MelloRoos.csproj -c Release -- check-keys
    if ($LASTEXITCODE -ne 0) { Fail "check-keys failed" }

    if (-not $env:OPENAI_API_KEY -and -not $env:GEMINI_API_KEY) {
        Fail "Set OPENAI_API_KEY or GEMINI_API_KEY for -FullExtract"
    }

    Step "full extract (LLM)"
    $extractArgs = @(
        "run", "--project", "src/MelloRoos.csproj", "-c", "Release", "--",
        "extract", $pdf,
        "--debt-id", "123",
        "--run-date", "2026-08-18",
        "--save-json", "out/test-windows-extraction.json",
        "-o", "out/test-windows-extract.sql"
    )
    if ($env:GEMINI_API_KEY -and -not $env:OPENAI_API_KEY) {
        $extractArgs += @("--provider", "gemini")
    }
    dotnet @extractArgs
    if ($LASTEXITCODE -ne 0) { Fail "extract failed (exit $LASTEXITCODE) — may be review-required; check out/test-windows-extraction.json" }
}

Write-Host ""
Write-Host "All Windows smoke checks passed." -ForegroundColor Green
