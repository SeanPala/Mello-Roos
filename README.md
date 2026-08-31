# Mello-Roos rate extraction

Extract rate tables from a standalone **Rate and Method of Apportionment (RMA)** PDF → review JSON → SQL INSERTs for `[dbo].[Rate]`.

**Typical input:** a short RMA PDF (under ~50 pages) like Fillmore CFD 8 — not a multi-hundred-page bond indenture.

Full client guide: [`INSTRUCTIONS.md`](INSTRUCTIONS.md)

**Hand off to client:** run `./scripts/package-for-client.sh` → zip (~10 MB) → email or file share.

---

## Quick start (4 steps)

Works on **macOS and Windows**. Windows-specific install: [`docs/windows-setup.md`](docs/windows-setup.md).

### 1. Install once

**macOS:**

```bash
brew install poppler tesseract    # pdftotext + OCR
dotnet build MelloRoos.sln
mkdir -p out
```

**Windows (PowerShell):**

```powershell
# Install .NET 10 SDK only — Poppler + Tesseract install automatically on first run
dotnet build MelloRoos.sln
mkdir out -Force
dotnet run --project src/MelloRoos.csproj -- extract `
  "Reference-Docs\City of Fillmore, CFD 8, RMA.pdf" `
  --debt-id 123 --save-json out/extraction.json -o out/rates.sql
```

Details: [`docs/windows-setup.md`](docs/windows-setup.md)

Set an LLM API key (pick one):

**macOS / bash:**

```bash
export OPENAI_API_KEY='sk-...'     # default
# export GEMINI_API_KEY='...'      # use with --provider gemini
# add to ~/.zshrc to persist
```

**Windows PowerShell (persist — recommended):**

```powershell
[System.Environment]::SetEnvironmentVariable("OPENAI_API_KEY", "sk-proj-...", "User")
# [System.Environment]::SetEnvironmentVariable("GEMINI_API_KEY", "AIza...", "User")
```

Close and reopen PowerShell, then verify:

```powershell
dotnet run --project src/MelloRoos.csproj -- check-keys
```

Full options (OpenAI model override, Gemini, Claude, session-only): [`INSTRUCTIONS.md`](INSTRUCTIONS.md#api-keys-windows-powershell)

### 2. Run extract

**macOS / Linux (bash):**

```bash
dotnet run --project src/MelloRoos.csproj -- extract \
  "Reference-Docs/City of Fillmore, CFD 8, RMA.pdf" \
  --debt-id 123 \
  --run-date 2026-08-18 \
  --save-json out/extraction.json \
  -o out/rates.sql
```

**Windows (PowerShell):**

```powershell
dotnet run --project src/MelloRoos.csproj -- extract `
  "Reference-Docs\City of Fillmore, CFD 8, RMA.pdf" `
  --debt-id 123 `
  --run-date 2026-08-18 `
  --save-json out/extraction.json `
  -o out/rates.sql
```

Or use the helper script: `.\scripts\extract.ps1 -Pdf "..." -DebtId 123`

**What this does:** reads the PDF text → LLM extracts rates and escalation rules → computes escalated amounts → writes JSON + SQL.

**No extra flags needed** if the PDF has selectable text (most standalone RMAs).

### 3. Review the JSON

Open `out/extraction.json` and check rate classes and amounts against the PDF.

If the tool printed `Review required`, SQL was **not** written — fix the JSON first (step 4).

**Sanity check (Fillmore CFD 8):** Zone 1 base $26,540/acre → **$37,905.66/acre** at run date 2026-08-18.

### 4. Re-run for SQL (after edits, if needed)

```bash
dotnet run --project src/MelloRoos.csproj -- escalate out/extraction.json \
  --debt-id 123 \
  --run-date 2026-08-18 \
  -o out/rates.sql
```

Or pass the reviewed JSON back through extract:

```bash
dotnet run --project src/MelloRoos.csproj -- extract \
  --json out/extraction.json \
  --debt-id 123 \
  --run-date 2026-08-18 \
  -o out/rates.sql
```

---

## Scanned RMA (no selectable text)

Add `--force-ocr` — everything else stays the same:

```bash
dotnet run --project src/MelloRoos.csproj -- extract \
  "Reference-Docs/your-scanned-rma.pdf" \
  --debt-id 123 \
  --force-ocr \
  --save-json out/extraction.json \
  -o out/rates.sql
```

---

## Required flags

| Flag | Required? | Purpose |
|------|-----------|---------|
| `--debt-id` | **Yes** | Existing `[dbo].[Debt].debt_id` for the SQL INSERTs |
| `--save-json` | Recommended | Save JSON so you can review before using SQL |
| `-o out/rates.sql` | Recommended | SQL output file |
| `--run-date` | Optional | Escalation date (default: today) |

---

## Troubleshooting

| Problem | Fix |
|---------|-----|
| `OPENAI_API_KEY is required` | `[System.Environment]::SetEnvironmentVariable("OPENAI_API_KEY", "sk-...", "User")` then reopen PowerShell |
| `insufficient_quota` / 429 | Add API billing at platform.openai.com, or `--provider gemini` |
| `Review required` | Edit `out/extraction.json`, then run `escalate` |
| Garbled / empty text | Add `--force-ocr` |
| Wrong model for your key | `dotnet run --project src/MelloRoos.csproj -- check-keys` |

ChatGPT Plus is **not** API access — the API is billed separately at platform.openai.com.

---

## Other commands

| Command | When to use |
|---------|-------------|
| `escalate` | JSON already reviewed → SQL only (no PDF, no LLM) |
| `text` | Debug: PDF → text file, no LLM |
| `check-keys` | Verify API keys and model access |
| `check-deps` | Verify pdftotext / tesseract on PATH |

Advanced (large bond packages, vision, IDP): see [`INSTRUCTIONS.md`](INSTRUCTIONS.md) § Advanced.

**Windows desktop:** [`docs/windows-setup.md`](docs/windows-setup.md)

---

## Specs

- Runbook: [`INSTRUCTIONS.md`](INSTRUCTIONS.md)
- Sample JSON: `src/rates.json` (Fillmore CFD 8)
- Test PDFs: `Reference-Docs/`
