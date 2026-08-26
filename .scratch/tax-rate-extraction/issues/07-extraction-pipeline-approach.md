# Extraction pipeline approach

Type: grilling
Status: resolved

## Question

How should rates move from RMA PDFs to SQL — manual INSERT scripts, semi-automated table parsing, LLM-assisted extraction with human review, or a hybrid? What is the best path for this corpus?

## Answer

**Hybrid: OCR/pdftotext → LLM structured JSON extraction → deterministic escalation + SQL templating → human review → T-SQL output.**

- **Not manual INSERTs** — 5 rate-model variants and 11 test PDFs make hand-authoring unsustainable.
- **Not pure rule-based parsing** — table layouts differ too much across CFDs; rules would be fragile per-variant.
- **Not LLM-to-SQL directly** — escalation math and `[dbo].[Rate]` column mapping stay deterministic to avoid hallucinated values.

**OCR default:** Tesseract (local) for scanned docs; pdftotext for text-native. Cloud OCR optional upgrade.

**Human review gate** before final SQL (ticket 04).

**Full spec:** [assets/extraction-pipeline-spec.md](../assets/extraction-pipeline-spec.md)

**Caller still provides:** `debt_id`, optional `bond_phase` (Variant A), optional `run_date`.
