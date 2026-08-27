# Mello-Roos rate extraction pipeline

Extract Mello-Roos **Rate and Method of Apportionment (RMA)** rate tables from PDFs → review JSON → deterministic escalation → `[dbo].[Rate]` SQL INSERTs.

---

## Setup (once)

```bash
brew install poppler tesseract          # macOS — pdftotext, pdftoppm, tesseract
dotnet build MelloRoos.sln
mkdir -p out
export GEMINI_API_KEY='your-key-here'   # https://aistudio.google.com/apikey
```

---

## Two ways to run

Use the same `extract` command for both. The tool picks the path based on PDF size.

### 1. Short RMA (typical — under ~50 pages, text-native)

Standalone RMA PDFs like Fillmore or Casitas. No page hunting, no OCR needed if the PDF has selectable text.

```bash
dotnet run --project src/MelloRoos.csproj -- extract \
  "Reference-Docs/City of Fillmore, CFD 8, RMA.pdf" \
  --debt-id 123 \
  --run-date 2026-08-18 \
  --save-json out/extraction.json \
  -o out/rates.sql
```

| What happens | |
|---|---|
| Text | `pdftotext` reads the whole PDF |
| LLM | Extracts rate classes + escalation rules → JSON |
| Escalation | Computed deterministically from JSON (not LLM) |
| Output | `out/extraction.json` + `out/rates.sql` |

**Sanity check:** Fillmore CFD 8 Zone 1 → base $26,540/acre escalates to **$37,905.66/acre** at 2026-08-18.

For a **scanned** short RMA, add `--force-ocr`:

```bash
dotnet run --project src/MelloRoos.csproj -- extract \
  "Reference-Docs/your-scanned-rma.pdf" \
  --debt-id 123 --force-ocr \
  --save-json out/extraction.json -o out/rates.sql
```

---

### 2. Long bond package (50+ pages, RMA buried in appendix)

Bond indentures where the RMA is an appendix at the back (e.g. *CFD 1, Series 2002*, 258 pages). **Do not pass `--pages`** — auto-discovery finds the RMA section.

```bash
dotnet run --project src/MelloRoos.csproj -- extract \
  "Reference-Docs/CFD 1, Series 2002 (1).pdf" \
  --debt-id 123 \
  --force-ocr \
  --save-json out/extraction.json \
  -o out/rates.sql
```

| What happens | |
|---|---|
| TOC + offset | OCR pages 1–15, find RMA in TOC, map listed page → PDF page |
| Validate | Reject TOC hit if probe pages don't look like an RMA |
| Binary search | Body-text chunk scan; vision fallback if `GEMINI_API_KEY` set |
| Text LLM | Escalation metadata + rate structure from discovered section |
| Vision table | Gemini, OpenAI, or Claude vision reads Table 1 images at 400 DPI |
| Output | `out/extraction.json` + `out/rates.sql` (review flags expected) |

Add **`--llamaparse`** or **`--textract`** if vision misses garbled Table 1 rates:

```bash
export LLAMA_CLOUD_API_KEY='...'   # optional IDP fallback

dotnet run --project src/MelloRoos.csproj -- extract \
  "Reference-Docs/CFD 1, Series 2002 (1).pdf" \
  --debt-id 123 --force-ocr --llamaparse \
  --save-json out/extraction.json -o out/rates.sql
```

**Preview discovery only** (no extraction):

```bash
dotnet run --project src/MelloRoos.csproj -- locate \
  "Reference-Docs/CFD 1, Series 2002 (1).pdf" --force-ocr --toc-loose
```

**Override** when you know the exact pages:

```bash
dotnet run --project src/MelloRoos.csproj -- extract \
  "Reference-Docs/CFD 1, Series 2002 (1).pdf" \
  --debt-id 123 --force-ocr --no-auto-locate --pages 198-210 \
  --save-json out/extraction.json -o out/rates.sql
```

---

## Review workflow

1. Run `extract` with `--save-json out/extraction.json`
2. Check JSON rates against the PDF
3. If SQL was blocked (flags or low confidence), edit JSON and re-run:

```bash
dotnet run --project src/MelloRoos.csproj -- escalate out/extraction.json \
  --debt-id 123 --run-date 2026-08-18 -o out/rates.sql
```

Or pass `--force` on `extract` to emit SQL despite flags (not recommended for production).

---

## Other commands

| Command | Use |
|---------|-----|
| `text` | PDF → text only (no LLM) |
| `escalate` | Reviewed JSON → SQL (no LLM, no PDF) |
| `table-extract` | Table 1 rates only via vision/IDP |
| `analyze` | Same as `extract` for long docs, skips JSON review gate |
| `locate` | Dry-run page discovery |
| `toc-smoke` | TocParser regression (no PDF) |

Full option reference: [`src/INSTRUCTIONS.md`](src/INSTRUCTIONS.md)

---

## Key flags

| Flag | When |
|------|------|
| `--debt-id` | Required — existing `[dbo].[Debt].debt_id` |
| `--run-date` | Escalation date (default: today) |
| `--save-json` | Save intermediate JSON for review |
| `-o` | SQL output file |
| `--force-ocr` | Scanned PDFs |
| `--force` | Emit SQL even when review flags exist |
| `--llamaparse` / `--textract` | IDP fallback for garbled Table 1 |
| `--no-auto-locate --pages N-M` | Skip discovery; use explicit page range |

---

## Pipeline

1. **Text acquisition** — `pdftotext`; OCR fallback via `pdftoppm` + `tesseract`
2. **LLM extraction** — structured JSON (`source`, `rate_classes`, escalation rules)
3. **Vision / IDP** — Table 1 rates on large scanned docs (auto)
4. **Escalation** — deterministic from `source.escalation`
5. **SQL** — INSERTs per `rma-to-rate-mapping.md`

## Specs

- Runbook: [`src/INSTRUCTIONS.md`](src/INSTRUCTIONS.md)
- Pipeline spec: `.scratch/tax-rate-extraction/assets/extraction-pipeline-spec.md`
- SQL mapping: `.scratch/tax-rate-extraction/assets/rma-to-rate-mapping.md`
- Sample JSON: `src/rates.json` (Fillmore CFD 8)
- Test PDFs: `Reference-Docs/`
