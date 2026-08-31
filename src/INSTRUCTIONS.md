# Mello-Roos rate extraction — how to run

PDF → LLM JSON → deterministic escalation → `[dbo].[Rate]` SQL INSERTs.

**Prototype:** always review `extraction.json` before using SQL in production.

**Expected input:** standalone RMA PDFs (typically under 50 pages). Long bond indentures with buried appendices are supported but not the normal workflow — see [Advanced](#advanced-large-bond-packages).

---

## The simple workflow

This is what you run day to day.

```
  RMA PDF  →  pdftotext  →  LLM  →  extraction.json  →  review  →  rates.sql
```

### Step 0 — Install once

#### Windows (client machines)

**Automatic (default):** install [.NET 10 SDK](https://dotnet.microsoft.com/download) only. Poppler and Tesseract install on **first run** via `winget` (or a direct download if `winget` is unavailable). No manual steps required in the normal case.

**API key** — set in PowerShell (see [API keys (Windows PowerShell)](#api-keys-windows-powershell) below).

```powershell
dotnet build MelloRoos.sln
mkdir out -Force
dotnet run --project src/MelloRoos.csproj -- check-deps   # triggers first-run install if needed
dotnet run --project src/MelloRoos.csproj -- check-keys
```

##### Windows manual install (if automatic setup fails)

Use this when first-run setup fails (no `winget`, UAC blocked, corporate policy, offline machine, etc.).

**1. Poppler** (`pdftotext`, `pdftoppm`, `pdfinfo`) — pick one:

*Option A — winget (recommended):*

```powershell
winget install --id oschwartz10612.Poppler -e `
  --accept-package-agreements --accept-source-agreements
```

*Option B — zip download:*

1. Download **`Release-*.zip`** from [poppler-windows releases](https://github.com/oschwartz10612/poppler-windows/releases)
2. Extract to `C:\Tools\poppler` (or any permanent folder)
3. Add **`Library\bin`** to your user PATH, e.g. `C:\Tools\poppler\Library\bin`
   - Settings → System → About → Advanced system settings → Environment Variables → User `Path` → New

**2. Tesseract** (only needed for scanned PDFs with `--force-ocr`) — pick one:

*Option A — winget:*

```powershell
winget install --id tesseract-ocr.tesseract -e `
  --accept-package-agreements --accept-source-agreements
```

If that package is unavailable, try: `winget install --id UB-Mannheim.TesseractOCR -e`

*Option B — installer:*

1. Download from [UB Mannheim Tesseract](https://github.com/UB-Mannheim/tesseract/wiki)
2. Run installer; check **“Add to PATH”**
3. Default folder: `C:\Program Files\Tesseract-OCR`

**3. Verify** — open a **new** PowerShell window:

```powershell
pdftotext -v
pdfinfo -v
tesseract --version
dotnet run --project src/MelloRoos.csproj -- check-deps
```

All four tools should report OK. If `check-deps` still fails, confirm PATH in a fresh terminal (not the one used for install).

Also see [`docs/windows-setup.md`](../docs/windows-setup.md).

#### API keys (Windows PowerShell)

**Recommended — persist for your Windows user account** (survives reboots; available in new terminals):

```powershell
# OpenAI (default provider)
[System.Environment]::SetEnvironmentVariable("OPENAI_API_KEY", "sk-proj-...", "User")

# Optional: force a specific model if check-keys shows a narrow allowlist
[System.Environment]::SetEnvironmentVariable("OPENAI_MODEL", "gpt-5", "User")

# Gemini (use with --provider gemini)
# [System.Environment]::SetEnvironmentVariable("GEMINI_API_KEY", "AIza...", "User")

# Claude (use with --provider claude)
# [System.Environment]::SetEnvironmentVariable("ANTHROPIC_API_KEY", "sk-ant-...", "User")
```

**Important:** close and reopen PowerShell (or sign out/in) after setting user variables. Existing windows keep the old environment.

**Current session only** (temporary — lost when you close the window):

```powershell
$env:OPENAI_API_KEY = "sk-proj-..."
# $env:GEMINI_API_KEY = "AIza..."
# $env:ANTHROPIC_API_KEY = "sk-ant-..."
```

**Verify:**

```powershell
# Should print "set" — not the key value
if ($env:OPENAI_API_KEY) { "OPENAI_API_KEY is set" } else { "OPENAI_API_KEY is NOT set" }

dotnet run --project src/MelloRoos.csproj -- check-keys
```

**Do not** commit keys to git or paste them into command history in shared logs. Prefer user-level env vars over hard-coding in scripts.

#### macOS (development)

```bash
brew install poppler tesseract
dotnet build MelloRoos.sln
mkdir -p out
export OPENAI_API_KEY='sk-...'   # add to ~/.zshrc to persist
```

### Step 1 — Extract

Run from the **repo root**. Replace the PDF path and `--debt-id`.

**macOS / bash:**

```bash
dotnet run --project src/MelloRoos.csproj -- extract \
  "Reference-Docs/City of Fillmore, CFD 8, RMA.pdf" \
  --debt-id 123 \
  --run-date 2026-08-18 \
  --save-json out/extraction.json \
  -o out/rates.sql
```

**Windows PowerShell:**

```powershell
dotnet run --project src/MelloRoos.csproj -- extract `
  "Reference-Docs\City of Fillmore, CFD 8, RMA.pdf" `
  --debt-id 123 `
  --run-date 2026-08-18 `
  --save-json out/extraction.json `
  -o out/rates.sql
```

Helper: `.\scripts\extract.ps1 -Pdf "path\to\rma.pdf" -DebtId 123`

| | |
|---|---|
| **Input** | Standalone RMA PDF with selectable text |
| **Pipeline** | `pdftotext` → LLM JSON → escalate → SQL |
| **Flags needed** | `--debt-id`, `--save-json`, `-o` |
| **OCR?** | No |
| **Output** | `out/extraction.json` + `out/rates.sql` (if no review flags) |

**Sanity check:** Fillmore CFD 8 Zone 1 base $26,540/acre → **$37,905.66/acre** at run date 2026-08-18.

### Step 2 — Review JSON

Open `out/extraction.json`. Check:

- `source.cfd_name`, `source.base_fiscal_year`, `source.escalation`
- Every `rate_classes[]` row: `class_id`, `class_name`, `max_tax_rate`, `max_tax_unit`

If the tool printed **Review required**, it saved JSON but **did not write SQL**. Fix the JSON, then go to step 3.

### Step 3 — Generate SQL from reviewed JSON

```bash
dotnet run --project src/MelloRoos.csproj -- escalate out/extraction.json \
  --debt-id 123 \
  --run-date 2026-08-18 \
  -o out/rates.sql
```

No PDF or LLM needed — escalation is deterministic from the JSON.

Alternative (same result):

```bash
dotnet run --project src/MelloRoos.csproj -- extract \
  --json out/extraction.json \
  --debt-id 123 \
  --run-date 2026-08-18 \
  -o out/rates.sql
```

---

## Scanned RMA

If the PDF has no selectable text (image-only scan), add **`--force-ocr`**. Nothing else changes:

```bash
dotnet run --project src/MelloRoos.csproj -- extract \
  "Reference-Docs/your-scanned-rma.pdf" \
  --debt-id 123 \
  --force-ocr \
  --save-json out/extraction.json \
  -o out/rates.sql
```

Pipeline becomes: render pages → Tesseract OCR → LLM → escalate → SQL.

---

## Required and common flags

| Flag | Required? | Purpose |
|------|-----------|---------|
| `--debt-id <id>` | **Yes** | `[dbo].[Debt].debt_id` for SQL INSERTs |
| `--save-json <path>` | Strongly recommended | Save JSON for review |
| `-o <path>` | Recommended | SQL output file |
| `--run-date YYYY-MM-DD` | Optional | Escalation as-of date (default: today) |
| `--force-ocr` | If scanned | OCR instead of pdftotext |
| `--provider gemini\|openai\|claude` | Optional | LLM provider (default: openai) |
| `--json <path>` | Step 3 only | Skip LLM; use reviewed JSON |
| `--force` | Avoid in prod | Emit SQL even when review flags exist |

You do **not** need `--pages`, `--vision-table`, `--llamaparse`, or `--textract` for typical short RMAs.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---------|--------------|-----|
| `OPENAI_API_KEY is required` | Key not exported | See [API keys (Windows PowerShell)](#api-keys-windows-powershell); reopen terminal after setting |
| `insufficient_quota` / HTTP 429 | No API credits | Add billing at platform.openai.com, or `--provider gemini` |
| `model_not_found` | Wrong model for your project | Run `check-keys`; set `OPENAI_MODEL` to an allowed model |
| `Review required` | Flags or low confidence | Edit JSON → `escalate` |
| Empty / garbled extraction | Scanned PDF, pdftotext failed | Add `--force-ocr` |
| ChatGPT Plus but API fails | Plus ≠ API | API billed separately at platform.openai.com |
| **`pdftotext` not recognized (Windows)** | Auto-install failed or PATH stale | [Manual install](#windows-manual-install-if-automatic-setup-fails); open new PowerShell |
| **`winget not found`** | App Installer missing | Install [App Installer](https://apps.microsoft.com/detail/9nblggh4nns1) from Microsoft Store, or use manual zip/installer steps |

```bash
dotnet run --project src/MelloRoos.csproj -- check-keys
```

---

## What's in `extraction.json`

| Field | Purpose |
|-------|---------|
| `source` | CFD name, agency, base fiscal year, escalation rules |
| `rate_classes[]` | One row per land-use / rate class (base-year amounts) |
| `one_time_taxes[]` | One-time levies (if any) |
| `extraction_confidence` | `high` / `medium` / `low` |
| `flags[]` | Issues requiring human review |

Escalation amounts are **not** stored in JSON — they are computed deterministically from `source.escalation` at `--run-date`.

---

## Other commands (simple workflow)

All commands from repo root:

```bash
dotnet run --project src/MelloRoos.csproj -- <command> ...
```

### `escalate` — JSON → SQL (no LLM, no PDF)

Use after reviewing/editing JSON:

```bash
dotnet run --project src/MelloRoos.csproj -- escalate out/extraction.json \
  --debt-id 123 \
  --run-date 2026-08-18 \
  -o out/rates.sql
```

### `text` — PDF → text only (debug)

```bash
dotnet run --project src/MelloRoos.csproj -- text "Reference-Docs/your-rma.pdf" \
  -o out/text.txt
```

Add `--force-ocr` for scanned PDFs.

### `check-deps` — verify system tools (run first on Windows)

```bash
dotnet run --project src/MelloRoos.csproj -- check-deps
```

Confirms `pdftotext`, `pdftoppm`, `pdfinfo`, and `tesseract` are on PATH.

### `check-keys` — verify API setup

```bash
dotnet run --project src/MelloRoos.csproj -- check-keys
```

Lists allowed OpenAI models, probes API quota, checks Gemini/LlamaParse keys.

---

## LLM providers

| Provider | Env var | Flag |
|----------|---------|------|
| OpenAI (default) | `OPENAI_API_KEY` | `--provider openai` |
| Gemini | `GEMINI_API_KEY` | `--provider gemini` |
| Claude | `ANTHROPIC_API_KEY` | `--provider claude` |

Example with Gemini:

```bash
dotnet run --project src/MelloRoos.csproj -- extract \
  "Reference-Docs/City of Fillmore, CFD 8, RMA.pdf" \
  --provider gemini \
  --debt-id 123 \
  --save-json out/extraction.json \
  -o out/rates.sql
```

---

## Prototype scope

**In scope**

- Standalone RMA rate tables → `[dbo].[Rate]` INSERTs
- Deterministic escalation from RMA metadata
- Human review via JSON

**Out of scope**

- Creating `[dbo].[Debt]` rows
- Exhibit A / APN-to-zone lookup
- Disclosure report generation
- Automatic production load

---

## Advanced: large bond packages

> **Not the typical workflow.** Use this only for 50+ page bond indentures where the RMA is buried in an appendix (e.g. CFD 1, Series 2002, 258 pages).

The tool auto-discovers the RMA section, OCRs it, runs vision on Table 1, and optionally falls back to LlamaParse or AWS Textract. Expect review flags on first run.

```bash
dotnet run --project src/MelloRoos.csproj -- extract \
  "Reference-Docs/CFD 1, Series 2002 (1).pdf" \
  --debt-id 123 \
  --force-ocr \
  --save-json out/extraction.json \
  -o out/rates.sql
```

| Option | Purpose |
|--------|---------|
| `--pages N-M` | Skip auto-discovery; explicit page range |
| `--no-auto-locate` | Disable TOC/discovery |
| `--vision-table` | Force Table 1 vision (auto on large PDFs) |
| `--table-pages 199-205` | Page range for Table 1 |
| `--no-llamaparse` | Disable LlamaParse fallback |
| `--textract` | Fall back to AWS Textract |

**Extra env vars (large docs only):**

```bash
export LLAMA_CLOUD_API_KEY='...'
export AWS_ACCESS_KEY_ID='...'
export AWS_SECRET_ACCESS_KEY='...'
export AWS_REGION='us-west-2'
```

### `locate` — preview page discovery (large docs only)

```bash
dotnet run --project src/MelloRoos.csproj -- locate \
  "Reference-Docs/CFD 1, Series 2002 (1).pdf" \
  --force-ocr --toc-loose
```

### `table-extract` — Table 1 only (large docs only)

```bash
dotnet run --project src/MelloRoos.csproj -- table-extract \
  "Reference-Docs/CFD 1, Series 2002 (1).pdf" \
  --pages 199-205 \
  -o out/table-rates.json
```

### `analyze` — extract without review gate (large docs only)

Same pipeline as `extract` for large PDFs but writes SQL directly without the review gate.

---

## Verify Windows before client handoff

You can validate the Windows path **without a local Windows PC** using GitHub Actions, or on a Windows VM / client machine with the script below.

### Option A — GitHub Actions (from macOS)

Push the repo, then open **Actions → Windows smoke → Run workflow**, or push to `main` and check the workflow result.

The CI job on `windows-latest`:

1. Builds the solution
2. Runs first-run PDF tool setup (`check-deps`)
3. Runs `pdftotext` on Fillmore CFD 8 (no API key)
4. Runs `escalate` on `src/rates.json` (no API key)

Optional full extract (needs secret): add `OPENAI_API_KEY` as a repository secret and uncomment the extract step in `.github/workflows/windows-smoke.yml`.

### Option B — Windows machine or VM

From PowerShell in the repo root:

```powershell
.\scripts\test-windows.ps1
```

This runs build, dependency install/check, pdftotext smoke, and sample JSON escalation. Pass `-FullExtract` to also run LLM extract (requires `OPENAI_API_KEY`).

### Client handoff checklist

- [ ] `.\scripts\test-windows.ps1` exits 0 (or GitHub Actions Windows smoke is green)
- [ ] `check-deps` shows all four tools OK
- [ ] `check-keys` shows API key + quota OK
- [ ] Sample extract on one RMA PDF produces `out/extraction.json`
- [ ] Client has INSTRUCTIONS.md and knows the review → `escalate` workflow

---

## Specs and sample data

| Path | Contents |
|------|----------|
| `src/rates.json` | Fillmore CFD 8 sample JSON |
| `Reference-Docs/` | Test PDF corpus |
| `.scratch/tax-rate-extraction/assets/extraction-pipeline-spec.md` | Pipeline spec |
| `.scratch/tax-rate-extraction/assets/rma-to-rate-mapping.md` | SQL column mapping |
