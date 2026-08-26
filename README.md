# Mello-Roos rate extraction pipeline

PDF-in → LLM JSON → deterministic escalation → `[dbo].[Rate]` SQL.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (or change `TargetFramework` in `src/MelloRoos.csproj`)
- [Poppler](https://poppler.freedesktop.org/) (`pdftotext`, `pdftoppm`, `pdfinfo`)
- [Tesseract OCR](https://github.com/tesseract-ocr/tesseract)
- `GEMINI_API_KEY` (or `GOOGLE_API_KEY`) for Gemini extraction — **default provider**
- `OPENAI_API_KEY` if using `--provider openai`

macOS:

```bash
brew install poppler tesseract
```

## Build

From repo root:

```bash
dotnet build MelloRoos.sln
```

## CLI

```bash
# Text only (pdftotext, OCR fallback if <1000 chars)
dotnet run --project src/MelloRoos.csproj -- text Reference-Docs/some-rma.pdf

# Full pipeline with Gemini (default)
export GEMINI_API_KEY=your-key-here
dotnet run --project src/MelloRoos.csproj -- extract Reference-Docs/some-rma.pdf \
  --debt-id 123 \
  --run-date 2026-08-18 \
  --save-json out/extraction.json \
  -o out/rates.sql

# OpenAI instead
export OPENAI_API_KEY=sk-...
dotnet run --project src/MelloRoos.csproj -- extract Reference-Docs/some-rma.pdf \
  --provider openai \
  --debt-id 123 \
  -o out/rates.sql

# Skip LLM — use reviewed JSON
dotnet run --project src/MelloRoos.csproj -- extract Reference-Docs/some-rma.pdf \
  --debt-id 123 \
  --json src/rates.json \
  --run-date 2026-08-18 \
  -o out/rates.sql

# Escalation + SQL only
dotnet run --project src/MelloRoos.csproj -- escalate src/rates.json \
  --debt-id 123 \
  --run-date 2026-08-18 \
  -o out/rates.sql
```

### OCR options

- `--force-ocr` — always OCR (skip pdftotext)
- `--pages 1-18` — limit OCR to page range (use for large scanned docs)
- `--dpi 300` — OCR resolution
- `--psm 6` — Tesseract page segmentation mode

### Review gate

If extraction confidence is `low` or `flags` is non-empty, SQL is **not** emitted unless you pass `--force`. Use `--save-json`, edit the JSON, then re-run with `--json`.

## Pipeline stages

1. **Text acquisition** — `pdftotext`; if &lt;1000 chars, OCR via `pdftoppm` + `tesseract`
2. **LLM extraction** — structured JSON per `.scratch/tax-rate-extraction/assets/extraction-pipeline-spec.md`
3. **Escalation** — deterministic from `source.escalation` (not LLM)
4. **SQL generation** — INSERTs per `.scratch/tax-rate-extraction/assets/rma-to-rate-mapping.md`

## Fixture

`src/rates.json` — Fillmore CFD 8 sample. Zone 1 base $26,540/acre at 2% annual from 2009-07-01 escalates to **$37,905.66** at run date 2026-08-18.

## Specs

- Pipeline: `.scratch/tax-rate-extraction/assets/extraction-pipeline-spec.md`
- SQL mapping: `.scratch/tax-rate-extraction/assets/rma-to-rate-mapping.md`
- PDF corpus: `Reference-Docs/`
