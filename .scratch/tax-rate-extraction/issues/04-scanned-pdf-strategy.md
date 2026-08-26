# Scanned PDF extraction strategy

Type: grilling
Status: resolved

## Question

Several Reference-Docs PDFs yield little or no text via `pdftotext` (likely scanned). Should we OCR them, manually transcribe rate tables, obtain digital copies elsewhere, or exclude them from v1? What quality bar is acceptable?

## Answer

**OCR and auto-extract** for all 6 scanned PDFs in Reference-Docs.

**Priority order** (per catalog):
1. Short RMAs first — Camarillo CFD 1, Fillmore CFD 1, Fillmore CFD 5 (use **2nd Amended**; skip 1st Amended once 2nd is extracted), CFD 2000-1
2. CFD 1 Series 2002 (258 pp) — selective OCR of the RMA/rate section only, not the full bond package

**Quality bar:** auto-extracted rate tables pass human review before generating load SQL; flag low-confidence OCR rows rather than silently inserting bad rates.

**Pipeline implication:** extraction ticket 07 should assume an OCR → structured parse → current-year rate computation → SQL load path, not manual transcription as the primary method.
