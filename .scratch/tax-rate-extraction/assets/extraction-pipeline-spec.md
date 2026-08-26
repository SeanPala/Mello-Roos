# PDF-in → SQL-out extraction pipeline

Spec for ticket [07 Extraction pipeline approach](../issues/07-extraction-pipeline-approach.md).

## Decision

**Hybrid pipeline:** text extraction (native or OCR) → **LLM structured extraction** to intermediate JSON → **deterministic escalation + SQL generation** → **human review** → output T-SQL INSERTs for `[dbo].[Rate]`.

Not manual INSERT scripts (doesn't scale). Not pure rule-based table parsing (5 rate-model variants + inconsistent table layouts). Not LLM-all-the-way to SQL (escalation math and column mapping should be deterministic).

## Pipeline stages

```
PDF + debt_id (caller)
    │
    ▼
┌─────────────────────┐
│ 1. Text acquisition │  pdftotext if ≥1,000 chars; else OCR
└─────────┬───────────┘
          ▼
┌─────────────────────┐
│ 2. LLM extraction   │  → intermediate JSON (rate_classes[])
└─────────┬───────────┘
          ▼
┌─────────────────────┐
│ 3. Escalation       │  base year → current_roll_year rates (deterministic)
└─────────┬───────────┘
          ▼
┌─────────────────────┐
│ 4. SQL generation   │  INSERT INTO [dbo].[Rate] (...) per mapping spec
└─────────┬───────────┘
          ▼
┌─────────────────────┐
│ 5. Human review     │  reviewer approves/edits JSON before SQL is final
└─────────┬───────────┘
          ▼
    T-SQL output file
```

## Stage 1 — Text acquisition

| Input type | Method | Notes |
|------------|--------|-------|
| Text-native PDF | `pdftotext` | 5 docs in Reference-Docs corpus |
| Scanned PDF | OCR | 6 docs; full OCR for ≤18 pp; selective page range for Series 2002 (258 pp) |
| Threshold | ≥1,000 chars from pdftotext → skip OCR | Per catalog classification rule |

**OCR tooling:** **Tesseract** (via `ocrmypdf` or `tesseract` + page images) as default — local, no API cost, sufficient for test corpus. Cloud OCR (Textract, Document AI) as optional upgrade if Tesseract quality is poor on Camarillo/large docs.

## Stage 2 — LLM structured extraction

Send extracted text (truncate intelligently — prioritize Section C, rate tables, escalation clauses) to an LLM with a **fixed JSON schema** aligned to [rma-to-rate-mapping.md](./rma-to-rate-mapping.md):

```json
{
  "source": {
    "cfd_name": "City of Fillmore Community Facilities District No. 8",
    "agency": "City of Fillmore",
    "base_fiscal_year": "2008-09",
    "escalation": { "type": "percent_annual", "rate": 0.02, "start": "2009-07-01" }
  },
  "rate_classes": [
    {
      "class_id": 1,
      "class_name": "Zone 1",
      "class_description": "The Stop (TSAF, LLC)",
      "class_other": null,
      "land_use": null,
      "max_tax_rate": 26540.00,
      "max_tax_unit": "per acre",
      "max_tax_qty_source": "acreage",
      "backup_tax_flag": false,
      "backup_tax_rate": null,
      "display_order": 1
    }
  ],
  "one_time_taxes": [],
  "extraction_confidence": "high",
  "flags": []
}
```

LLM also classifies the document into variant A–E (or `unknown`) to validate row shape expectations.

**Low-confidence rows** get a `flags` entry; human review must acknowledge before SQL emit.

## Stage 3 — Escalation (deterministic, not LLM)

Apply formulas from RMA metadata — never ask the LLM to do compound interest:

| `escalation.type` | Formula |
|-------------------|---------|
| `percent_annual` | `current = base × (1 + rate)^years` |
| `multiplier_annual` | `current = base × multiplier^years` (Fillmore 2: 1.02) |
| `none` | `current = base` |

`years` = July 1 anniversaries from base fiscal year through run date.  
Output populates `current_roll_year`, `current_max_tax_rate`, `current_backup_tax_rate`.

## Stage 4 — SQL generation (deterministic template)

Parameterized T-SQL INSERT template using `@debt_id` from caller:

- Column mapping per [rma-to-rate-mapping.md](./rma-to-rate-mapping.md)
- `max_tax_text` / `backup_tax_text` built from formatted strings
- Defaults for audit columns, `backup_tax_flag`, `nost_type_id`
- One INSERT per `rate_classes[]` entry (+ one-time rows when present)

No LLM in this stage — reduces hallucinated column values.

## Stage 5 — Human review

Reviewer sees:
- Side-by-side: source PDF page(s) vs extracted JSON vs generated SQL
- Flags for low-confidence OCR or ambiguous table cells
- Edit JSON directly; re-run stages 3–4 after edits

Matches ticket 04 quality bar: **no SQL reaches production without review.**

## Caller inputs (required per run)

| Input | Source |
|-------|--------|
| `pdf` | User upload |
| `debt_id` | Caller — existing `[dbo].[Debt]` row |
| `run_date` | Optional; defaults to today (drives escalation) |
| `bond_phase` | Optional; when RMA has prior/after bond columns (Variant A) |

## Test corpus validation

Run all 11 Reference-Docs PDFs through pipeline; compare output row counts and sample rates against manual reads in [01-catalog-reference-docs.md](../research/01-catalog-reference-docs.md).

## Out of pipeline scope

- Creating or updating `[dbo].[Debt]` rows
- Exhibit A / APN-to-zone extraction (out of scope — ticket 09)
- Proportionate levy calculation
- Automatic bond-phase detection (caller specifies when needed)

## Implementation handoff

Map destination is satisfied — build order:

1. Text acquisition module (pdftotext + OCR wrapper)
2. JSON schema + LLM extraction prompt (version-controlled)
3. Escalation module
4. SQL template generator
5. Review UI or CLI diff workflow
6. Reference-Docs regression suite
