# Mello-Roos rate extraction — client guide (Windows)

Extract rate tables from a standalone **Rate and Method of Apportionment (RMA)** PDF → review JSON → SQL for `[dbo].[Rate]`.

**Typical input:** short RMA PDF (under ~20 pages), text-native or scanned.

**Prototype:** always review `out/extraction.json` before using SQL in production.

---

## Receiving the project

Place the project in a folder on your PC (for example `C:\Projects\Mello-Roos`). Run all commands below from that folder — the one that contains `MelloRoos.sln`.

---

## Quick start checklist

1. Install [.NET 10 SDK](https://dotnet.microsoft.com/download)
2. Set your API key in PowerShell ([below](#api-keys-windows-powershell))
3. Open **new** PowerShell in the project folder (the one containing `MelloRoos.sln`)
4. Run `check-deps` (installs PDF tools on first run)
5. Run `extract` on your RMA PDF
6. Review JSON → run `escalate` for SQL

---

## Step 0 — Install once

### .NET 10 SDK

Download and install: https://dotnet.microsoft.com/download

Verify:

```powershell
dotnet --version
```

### PDF tools (Poppler + Tesseract)

**Automatic:** on first command, the tool installs `pdftotext` and `tesseract` via `winget` (or downloads Poppler if `winget` is unavailable). You may see an admin prompt once.

```powershell
dotnet build MelloRoos.sln
mkdir out -Force
dotnet run --project src/MelloRoos.csproj -- check-deps
```

All four tools should report **OK**: `pdftotext`, `pdftoppm`, `pdfinfo`, `tesseract`.

### Manual install (if automatic setup fails)

Use when `check-deps` fails (no `winget`, UAC blocked, offline PC, corporate policy).

**Poppler** (`pdftotext`, `pdftoppm`, `pdfinfo`):

```powershell
winget install --id oschwartz10612.Poppler -e `
  --accept-package-agreements --accept-source-agreements
```

Or download `Release-*.zip` from [poppler-windows releases](https://github.com/oschwartz10612/poppler-windows/releases), extract to `C:\Tools\poppler`, and add `Library\bin` to your user PATH (Settings → Environment Variables).

**Tesseract** (required for scanned PDFs):

```powershell
winget install --id tesseract-ocr.tesseract -e `
  --accept-package-agreements --accept-source-agreements
```

Or install from [UB Mannheim Tesseract](https://github.com/UB-Mannheim/tesseract/wiki) with **“Add to PATH”** checked.

**Verify in a new PowerShell window:**

```powershell
pdftotext -v
tesseract --version
dotnet run --project src/MelloRoos.csproj -- check-deps
```

---

## API keys (Windows PowerShell)

You need an **OpenAI API key** (or Gemini/Claude). ChatGPT Plus is **not** API access — billing is separate at https://platform.openai.com.

### Persist (recommended)

Survives reboots. Run once, then **close and reopen PowerShell**:

```powershell
[System.Environment]::SetEnvironmentVariable("OPENAI_API_KEY", "sk-proj-REPLACE-WITH-YOUR-KEY", "User")

# Optional — if check-keys shows only one allowed model:
[System.Environment]::SetEnvironmentVariable("OPENAI_MODEL", "gpt-5", "User")

# Alternative providers (uncomment one):
# [System.Environment]::SetEnvironmentVariable("GEMINI_API_KEY", "AIza...", "User")
# [System.Environment]::SetEnvironmentVariable("ANTHROPIC_API_KEY", "sk-ant-...", "User")
```

### Current session only (temporary)

Lost when you close the window:

```powershell
$env:OPENAI_API_KEY = "sk-proj-REPLACE-WITH-YOUR-KEY"
```

### Verify

```powershell
if ($env:OPENAI_API_KEY) { "OPENAI_API_KEY is set" } else { "NOT set — reopen PowerShell if you just set User env vars" }

dotnet run --project src/MelloRoos.csproj -- check-keys
```

**Security:** never email API keys, commit them to files, or share your screen while keys are visible.

---

## Step 1 — Extract rates from an RMA PDF

Replace the PDF path and `--debt-id` with your values:

```powershell
dotnet run --project src/MelloRoos.csproj -- extract `
  "C:\path\to\your-rma.pdf" `
  --debt-id 123 `
  --run-date 2026-08-18 `
  --save-json out/extraction.json `
  -o out/rates.sql
```

**Example** (included sample PDF):

```powershell
dotnet run --project src/MelloRoos.csproj -- extract `
  "Reference-Docs\City of Fillmore, CFD 8, RMA.pdf" `
  --debt-id 123 `
  --run-date 2026-08-18 `
  --save-json out/extraction.json `
  -o out/rates.sql
```

**Helper script:**

```powershell
.\scripts\extract.ps1 -Pdf "C:\path\to\your-rma.pdf" -DebtId 123 -RunDate "2026-08-18"
```

| Flag | Required? | Purpose |
|------|-----------|---------|
| `--debt-id` | **Yes** | Existing `[dbo].[Debt].debt_id` |
| `--save-json` | Recommended | Save JSON for review |
| `-o` | Recommended | SQL output file |
| `--run-date` | Optional | Escalation date (default: today) |
| `--force-ocr` | Scanned PDFs | Force OCR (see below) |
| `--provider gemini` | Optional | Use Gemini instead of OpenAI |

**Sanity check (Fillmore CFD 8):** Zone 1 base $26,540/acre → **$37,905.66/acre** at run date 2026-08-18.

---

## Text PDFs vs scanned PDFs (OCR)

The pipeline always tries **`pdftotext` first**. OCR is part of the same `extract` command — no separate tool.

```
  RMA PDF
    → pdftotext (fast, text-native PDFs)
    → if too little text OR --force-ocr:
         pdftoppm (render pages) → Tesseract OCR
    → LLM extracts rates → JSON → escalate → SQL
```

| PDF type | What to run | Log shows |
|----------|-------------|-----------|
| **Text-native** (selectable text) | `extract` only | `Method: pdftotext` |
| **Scanned** (image-only) | `extract --force-ocr` | `Method: ocr-forced` |
| **Scanned, forgot `--force-ocr`** | `extract` only | `Method: ocr-fallback` (auto if pdftotext &lt; 1,000 chars) |

**Scanned RMA:**

```powershell
dotnet run --project src/MelloRoos.csproj -- extract `
  "C:\path\to\scanned-rma.pdf" `
  --debt-id 123 `
  --force-ocr `
  --save-json out/extraction.json `
  -o out/rates.sql
```

**Test OCR without LLM** (debug):

```powershell
dotnet run --project src/MelloRoos.csproj -- text `
  "Reference-Docs\CFD No. 2000-1, RMA (1).pdf" `
  --force-ocr --pages 1-2 -o out/ocr-test.txt
```

stderr should show `Method: ocr-forced, chars: ...`. Tesseract must be OK in `check-deps`.

---

## Step 2 — Review JSON

Open `out\extraction.json`. Verify:

- `source.cfd_name`, `source.base_fiscal_year`, `source.escalation`
- Each `rate_classes[]` row: `class_id`, `class_name`, `max_tax_rate`, `max_tax_unit`

If the tool printed **Review required**, JSON was saved but **SQL was not written**. Edit the JSON, then Step 3.

---

## Step 3 — Generate SQL from reviewed JSON

```powershell
dotnet run --project src/MelloRoos.csproj -- escalate out/extraction.json `
  --debt-id 123 `
  --run-date 2026-08-18 `
  -o out/rates.sql
```

No PDF or LLM needed — escalation is deterministic from JSON.

---

## Verify your setup

```powershell
.\scripts\test-windows.ps1
```

Runs build, tool install, pdftotext smoke, **OCR smoke**, and sample escalation (no API key required for basic test).

With API key, full extract test:

```powershell
$env:OPENAI_API_KEY = "sk-proj-..."   # or set User env var and reopen PowerShell
.\scripts\test-windows.ps1 -FullExtract
```

---

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| `OPENAI_API_KEY is required` | Set User env var; **reopen PowerShell** |
| `insufficient_quota` / 429 | Add API billing at platform.openai.com, or `--provider gemini` |
| `Review required` | Edit JSON → `escalate` |
| Empty / garbled extraction | Add `--force-ocr` for scanned PDFs |
| `pdftotext` not recognized | [Manual install](#manual-install-if-automatic-setup-fails); new PowerShell window |
| OCR fails / `tesseract` missing | Install Tesseract; run `check-deps` |
| `winget not found` | Install [App Installer](https://apps.microsoft.com/detail/9nblggh4nns1) from Microsoft Store |

```powershell
dotnet run --project src/MelloRoos.csproj -- check-deps
dotnet run --project src/MelloRoos.csproj -- check-keys
```

---

## What's in `extraction.json`

| Field | Purpose |
|-------|---------|
| `source` | CFD name, agency, base fiscal year, escalation rules |
| `rate_classes[]` | One row per rate class (base-year amounts) |
| `extraction_confidence` | `high` / `medium` / `low` |
| `flags[]` | Issues needing human review |

Escalation amounts are computed at `--run-date` from `source.escalation` — not stored in JSON.

---

## Other commands

```powershell
dotnet run --project src/MelloRoos.csproj -- <command> ...
```

| Command | Purpose |
|---------|---------|
| `extract` | Full pipeline: PDF → LLM → JSON → SQL |
| `escalate` | Reviewed JSON → SQL only |
| `text` | PDF → text file (debug pdftotext/OCR) |
| `check-deps` | Verify/install Poppler + Tesseract |
| `check-keys` | Verify API key and model access |

---

## For the developer — packaging for client

Create a zip (~10 MB, email-friendly):

```bash
./scripts/package-for-client.sh
```

```powershell
.\scripts\package-for-client.ps1
```

**Include in handoff:** `MelloRoos-client.zip` and this file (`INSTRUCTIONS.md`). Client creates their own API key at platform.openai.com — do **not** send your key.

---

## macOS (development only)

```bash
brew install poppler tesseract
export OPENAI_API_KEY='sk-...'
dotnet run --project src/MelloRoos.csproj -- extract \
  "Reference-Docs/City of Fillmore, CFD 8, RMA.pdf" \
  --debt-id 123 --save-json out/extraction.json -o out/rates.sql
```

---

## Advanced: large bond packages (unusual)

Not needed for typical short RMAs. Bond-package options (`--force-ocr`, auto-locate on 50+ page PDFs) apply only if you encounter a full bond indenture with the RMA in an appendix.

---

## Specs

| Path | Contents |
|------|----------|
| `src/rates.json` | Sample JSON (Fillmore CFD 8) |
| `Reference-Docs/` | Sample PDFs for testing |
| `scripts/extract.ps1` | PowerShell wrapper |
| `scripts/test-windows.ps1` | Setup verification |
