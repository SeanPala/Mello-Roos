# Mello-Roos rate extraction — how to run

PDF → LLM JSON → deterministic escalation → `[dbo].[Rate]` SQL INSERTs.

**Prototype:** review `extraction.json` before using SQL in production.

**Quick reference:** see [`README.md`](../README.md) for the two main command-line examples (short RMA vs long bond package).

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

## 3. Two ways to run `extract`

Same command for both paths. The tool chooses based on PDF size and whether you pass `--pages`.

### Short RMA — text-native, under ~50 pages

Example: **Fillmore CFD 8** — a standalone RMA with selectable text.

```bash
dotnet run --project src/MelloRoos.csproj -- extract \
  "Reference-Docs/City of Fillmore, CFD 8, RMA.pdf" \
  --debt-id 123 \
  --run-date 2026-08-18 \
  --save-json out/extraction.json \
  -o out/rates.sql
```

| | |
|---|---|
| **You need** | `GEMINI_API_KEY`, `--debt-id` |
| **Pipeline** | pdftotext → LLM JSON → escalate → SQL |
| **OCR?** | No (add `--force-ocr` only if scanned) |
| **Page range?** | No — reads the whole PDF |
| **Output** | `out/extraction.json` + `out/rates.sql` |

Sanity check: Zone 1 base $26,540/acre → **$37,905.66/acre** at run date 2026-08-18.

---

### Long bond package — 50+ pages, RMA in appendix

Example: **CFD 1, Series 2002** — 258-page scanned bond package; RMA is near the end.

```bash
dotnet run --project src/MelloRoos.csproj -- extract \
  "Reference-Docs/CFD 1, Series 2002 (1).pdf" \
  --debt-id 123 \
  --force-ocr \
  --save-json out/extraction.json \
  -o out/rates.sql
```

| | |
|---|---|
| **You need** | `GEMINI_API_KEY`, `--debt-id`, `--force-ocr` |
| **Pipeline** | TOC + offset (p. 1–15) → validate → binary search fallback → text LLM → vision Table 1 → escalate → SQL |
| **Page range?** | **No** — auto-discovery finds the RMA section |
| **Vision?** | Auto — gemini-2.0-flash, gpt-4o-mini, or claude-sonnet-4 on Table 1 at 400 DPI |
| **Output** | JSON + SQL; expect review flags on first run |

Optional IDP fallback when Table 1 OCR is garbled:

```bash
export LLAMA_CLOUD_API_KEY='...'

dotnet run --project src/MelloRoos.csproj -- extract \
  "Reference-Docs/CFD 1, Series 2002 (1).pdf" \
  --debt-id 123 --force-ocr --llamaparse \
  --save-json out/extraction.json -o out/rates.sql
```

Override auto-discovery if you know the pages:

```bash
dotnet run --project src/MelloRoos.csproj -- extract \
  "Reference-Docs/CFD 1, Series 2002 (1).pdf" \
  --debt-id 123 --force-ocr --no-auto-locate --pages 198-210 \
  --save-json out/extraction.json -o out/rates.sql
```

---

## 4. Commands

All commands run from repo root:

```bash
dotnet run --project src/MelloRoos.csproj -- <command> ...
```

### `extract` — full pipeline

PDF → text → LLM → (vision/IDP on large docs) → escalation → SQL

See **§3** for copy-paste examples (short RMA vs long bond package).

| Option | Purpose |
|--------|---------|
| `--provider gemini\|openai\|claude` | LLM provider (default: gemini) |
| `--model <name>` | Text LLM model (default: gemini-3.6-flash) |
| `--vision-provider <name>` | Vision provider: gemini, openai, or claude (default: same as `--provider`) |
| `--vision-model <name>` | Vision model (default: gemini-2.0-flash, gpt-4o-mini, or claude-sonnet-4-20250514) |
| `--json out/extraction.json` | Skip LLM; use reviewed JSON |
| `--save-text out/text.txt` | Save acquired PDF text |
| `--force` | Emit SQL even when review flags exist |
| `--force-ocr` | Always OCR (for scanned PDFs) |
| `--pages 1-30` | Explicit page range (skips auto-locate) |
| `--no-auto-locate` | Disable page discovery on large PDFs |
| `--vision-table` | Force vision on Table 1 (auto on large PDFs) |
| `--table-pages 199-205` | Page range for vision (default: auto-detected) |
| `--table-dpi 400` | DPI for table page images (default 400) |
| `--llamaparse` | Fall back to LlamaParse if vision incomplete |
| `--textract` | Fall back to AWS Textract if still incomplete |
| `--land-use-type 0` | Default `land_use_type` for all SQL rows |

**Large-doc auto-pipeline (PDF > 50 pages, no `--pages`):** TOC + offset (p. 1–15) → validate → binary search fallback → text LLM → vision Table 1 → optional IDP.

### `table-extract` — Table 1 only (optional IDP fallbacks)

For scanned bond packages where OCR garbles Table 1. Runs **Gemini vision** on page images first; optionally falls back to **LlamaParse** or **AWS Textract**.

```bash
# Vision only (pages 199–205 = Table 1 region for Series 2002)
dotnet run --project src/MelloRoos.csproj -- table-extract \
  "Reference-Docs/CFD 1, Series 2002 (1).pdf" \
  --pages 199-205 \
  -o out/table-rates.json

# Merge into an existing extraction.json
dotnet run --project src/MelloRoos.csproj -- table-extract \
  "Reference-Docs/CFD 1, Series 2002 (1).pdf" \
  --pages 199-205 \
  --merge-json out/extraction.json \
  -o out/extraction.json
```

| Option | Purpose |
|--------|---------|
| `--pages 199-205` | PDF pages containing Table 1 |
| `--table-dpi 400` | Image resolution (default 400) |
| `--llamaparse` | Fall back to LlamaParse if vision misses rates |
| `--textract` | Fall back to AWS Textract if still incomplete |
| `--no-vision` | Skip vision (use with `--llamaparse` or `--textract`) |
| `--merge-json` | Patch `rate_classes` into existing extraction JSON |

**Env vars for fallbacks:**

```bash
export LLAMA_CLOUD_API_KEY='...'   # --llamaparse
export AWS_ACCESS_KEY_ID='...'     # --textract
export AWS_SECRET_ACCESS_KEY='...'
export AWS_REGION='us-west-2'
```

**Integrated into `extract`** — on large PDFs this runs automatically; no extra flags needed beyond `--llamaparse` / `--textract` for IDP fallbacks.

### `analyze` — same pipeline, SQL output (no review gate)

Uses the identical discovery + vision/IDP pipeline as `extract`, but skips the JSON review gate and writes SQL directly.

```bash
dotnet run --project src/MelloRoos.csproj -- analyze \
  "Reference-Docs/CFD 1, Series 2002 (1).pdf" \
  --debt-id 123 --force-ocr \
  --llamaparse \
  -o out/rates.sql
```

### `locate` — preview discovery (TOC + offset → binary search fallback)

Dry-run the page discovery step without running extraction:

```bash
dotnet run --project src/MelloRoos.csproj -- locate \
  "Reference-Docs/CFD 1, Series 2002 (1).pdf" \
  --toc-loose --force-ocr
```

Prints discovered PDF pages. When the TOC lists an appendix with `D-1`-style refs, the locator **OCRs the back third in 12-page chunks** and searches page **body text** for the appendix title — not footer page numbers. If that misses, **Gemini vision** classifies ~8 page images (requires `GEMINI_API_KEY`).

| Option | Purpose |
|--------|---------|
| `--toc-loose` | Fuzzy OCR-tolerant TOC matching |
| `--toc-pages 1-15` | PDF pages to OCR for table of contents |
| `--toc-min-score 35` | Lower = more permissive TOC matching (default floor 35 with `--toc-loose`) |
| `--page-offset 6` | Manual: PDF page = listed TOC page + N (Series 2002 ≈ 6) |
| `--no-auto-offset` | Disable auto offset detection |
| `--padding 2` | Extra pages before/after RMA section |
| `--max-span 35` | Max RMA section length if end unknown |
| `--json` | Output result as JSON |

Then run `extract` (see §3) — or pass `--no-auto-locate --pages N-M` if you want to override.

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

## 6. Which PDF type?

| PDF | Pages | Command |
|-----|-------|---------|
| **Short RMA** (Fillmore, Casitas) | &lt; 50, text-native | `extract` — no `--force-ocr`, no `--pages` |
| **Scanned RMA** | &lt; 50 | `extract --force-ocr` |
| **Long bond package** (Series 2002) | 50+ | `extract --force-ocr` — auto-discovery + vision |

Never OCR an entire 258-page bond package without auto-locate or explicit `--pages`.

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
