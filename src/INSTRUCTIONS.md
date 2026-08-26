# Mello-Roos rate extraction — how to run

PDF → LLM JSON → deterministic escalation → `[dbo].[Rate]` SQL INSERTs.

**Prototype:** review `extraction.json` before using SQL in production.

---

## 1. Install once

**Required**

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Poppler](https://poppler.freedesktop.org/) (`pdftotext`, `pdftoppm`, `pdfinfo`)
- [Tesseract OCR](https://github.com/tesseract-ocr/tesseract)

macOS:

```bash
brew install poppler tesseract
```

**LLM API key** (pick one provider):

```bash
export GEMINI_API_KEY='your-key-here'      # default provider
# export ANTHROPIC_API_KEY='your-key-here' # use --provider claude
# export OPENAI_API_KEY='sk-...'           # use --provider openai
```

Get a Gemini key: [Google AI Studio](https://aistudio.google.com/apikey)

Add the `export` line to `~/.zshrc` to persist it.

---

## 2. Build

From repo root:

```bash
dotnet build MelloRoos.sln
mkdir -p out
```

---

## 3. Quick start — extract an RMA

Works on text-native RMA PDFs (e.g. Fillmore CFD 8):

```bash
dotnet run --project src/MelloRoos.csproj -- extract \
  "Reference-Docs/City of Fillmore, CFD 8, RMA.pdf" \
  --debt-id 123 \
  --run-date 2026-08-18 \
  --save-json out/extraction.json \
  -o out/rates.sql
```

| Input | Meaning |
|-------|---------|
| `--debt-id` | Existing `[dbo].[Debt].debt_id` in your database |
| `--run-date` | Escalation date (default: today) |
| `--save-json` | Intermediate LLM output for human review |
| `-o` | SQL output file |

**Expected output:** `out/extraction.json` + `out/rates.sql`

Fillmore CFD 8 sanity check: Zone 1 base $26,540/acre → **$37,905.66/acre** at run date 2026-08-18.

---

## 4. Commands

All commands run from repo root:

```bash
dotnet run --project src/MelloRoos.csproj -- <command> ...
```

### `extract` — full pipeline

PDF → text → LLM → escalation → SQL

```bash
dotnet run --project src/MelloRoos.csproj -- extract "Reference-Docs/your-rma.pdf" \
  --debt-id 123 \
  --run-date 2026-08-18 \
  --save-json out/extraction.json \
  -o out/rates.sql
```

| Option | Purpose |
|--------|---------|
| `--provider gemini\|openai\|claude` | LLM provider (default: gemini) |
| `--model <name>` | Override model (default: gemini-3.6-flash) |
| `--json out/extraction.json` | Skip LLM; use reviewed JSON |
| `--save-text out/text.txt` | Save acquired PDF text |
| `--force` | Emit SQL even when review flags exist |
| `--force-ocr` | Always OCR (for scanned PDFs) |
| `--pages 1-30` | Limit OCR/extraction to page range |
| `--land-use-type 0` | Default `land_use_type` for all SQL rows |

### `locate` — find RMA pages in long bond packages

Use before `extract` on large scanned docs (e.g. 258-page Series 2002):

```bash
dotnet run --project src/MelloRoos.csproj -- locate \
  "Reference-Docs/CFD 1, Series 2002 (1).pdf" \
  --toc-loose \
  --toc-pages 1-40
```

Prints **PDF pages** to use with `--pages` in `extract`. Also shows listed TOC pages and page offset.

| Option | Purpose |
|--------|---------|
| `--toc-loose` | Fuzzy OCR-tolerant TOC matching |
| `--toc-pages 1-40` | PDF pages to OCR for table of contents |
| `--toc-min-score 35` | Lower = more permissive TOC matching (default floor 35 with `--toc-loose`) |
| `--page-offset 6` | Manual: PDF page = listed TOC page + N (Series 2002 ≈ 6) |
| `--no-auto-offset` | Disable auto offset detection |
| `--chunk-size 30` | Pages per chunk for keyword fallback scan |
| `--padding 2` | Extra pages before/after RMA section |
| `--max-span 35` | Max RMA section length if end unknown |
| `--json` | Output result as JSON |

Then run the suggested `extract` command from the output.

**If TOC fails on a bond package** (e.g. OCR garbles the index), use manual pages. For *CFD 1, Series 2002*, Appendix D RMA is around listed page 92; with offset 6 that is PDF pages ~98–130:

```bash
dotnet run --project src/MelloRoos.csproj -- extract \
  "Reference-Docs/CFD 1, Series 2002 (1).pdf" \
  --debt-id <ID> --force-ocr --pages 98-130 \
  --save-json out/extraction.json -o out/rates.sql
```

Or let keyword fallback finish (slower — scans the whole PDF in chunks).

### `text` — PDF text only (no LLM)

```bash
dotnet run --project src/MelloRoos.csproj -- text "Reference-Docs/your-rma.pdf" \
  -o out/text.txt
```

Add `--force-ocr --pages 1-25` for scanned sections.

### `escalate` — JSON → SQL only (no LLM)

After editing `extraction.json`:

```bash
dotnet run --project src/MelloRoos.csproj -- escalate out/extraction.json \
  --debt-id 123 \
  --run-date 2026-08-18 \
  -o out/rates.sql
```

---

## 5. Review workflow

1. Run `extract` with `--save-json out/extraction.json`
2. Open JSON and verify rates against the PDF
3. If SQL was blocked (flags or low confidence), edit JSON and re-run:

```bash
dotnet run --project src/MelloRoos.csproj -- extract "Reference-Docs/your-rma.pdf" \
  --debt-id 123 \
  --json out/extraction.json \
  --run-date 2026-08-18 \
  -o out/rates.sql
```

Or use `escalate` (step 4 above) — no PDF or LLM needed.

### What's in `extraction.json`

| Field | Purpose |
|-------|---------|
| `source` | CFD name, base fiscal year, escalation rules |
| `rate_classes[]` | One row per rate class (base-year amounts) |
| `one_time_taxes[]` | One-time levies (if any) |
| `extraction_confidence` | `high` / `medium` / `low` |
| `flags[]` | Issues requiring review |

Escalation is **not** in the JSON — it is computed deterministically from `source.escalation`.

---

## 6. PDF types

| Type | What to do |
|------|------------|
| **Text-native RMA** (most Fillmore/Casitas docs) | `extract` directly |
| **Scanned RMA** (≤20 pp) | `extract --force-ocr` |
| **Long bond package** (100+ pp) | `locate` first → `extract --force-ocr --pages <range>` |

Never OCR an entire 258-page bond package — always `locate` or set `--pages` manually.

---

## 7. LLM providers

| Provider | Env var | Flag |
|----------|---------|------|
| Gemini (default) | `GEMINI_API_KEY` | `--provider gemini` |
| Claude | `ANTHROPIC_API_KEY` | `--provider claude` |
| OpenAI | `OPENAI_API_KEY` | `--provider openai` |

Example with Claude:

```bash
dotnet run --project src/MelloRoos.csproj -- extract "Reference-Docs/your-rma.pdf" \
  --provider claude \
  --debt-id 123 \
  -o out/rates.sql
```

---

## 8. Prototype scope

**In scope**

- RMA rate table → `[dbo].[Rate]` INSERTs
- Deterministic escalation from RMA metadata
- Human review via JSON

**Out of scope**

- Creating `[dbo].[Debt]` rows
- Exhibit A / APN-to-zone lookup
- Disclosure report generation
- Automatic production load (SQL requires review)

---

## 9. Specs and sample data

| Path | Contents |
|------|----------|
| `src/rates.json` | Fillmore CFD 8 sample JSON |
| `Reference-Docs/` | Test PDF corpus |
| `.scratch/tax-rate-extraction/assets/extraction-pipeline-spec.md` | Pipeline spec |
| `.scratch/tax-rate-extraction/assets/rma-to-rate-mapping.md` | SQL column mapping |
