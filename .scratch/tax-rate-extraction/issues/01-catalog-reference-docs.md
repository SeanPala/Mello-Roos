# Catalog Reference-Docs RMA documents

Type: research
Status: resolved

## Question

For every PDF in `Reference-Docs/`, what is extractable (text vs scanned), what rate apportionment model does it use, where are the rate tables, and what dimensions (zone, land use class, acre, unit, sq ft, bond phase, etc.) apply?

## Answer

Cataloged all 11 PDFs: **5 text-native**, **6 scanned** (pdftotext yields ~1 char/page). Five distinct rate-model variants identified from extractable docs — land use class × unit/sq ft + bond phase (Casitas 2013-1), building sq ft + land acre 87/13 split (Fillmore 2), land use class + backup acre (Fillmore 3), annexation area × acre + one-time tax (Fillmore 6), zone × acre + zone bond allocation (Fillmore 8). Scanned docs (Camarillo CFD 1, Fillmore CFD 1 & 5, CFD 2000-1, Series 2002) need OCR before rate extraction.

Full per-document analysis: [research/01-catalog-reference-docs.md](../research/01-catalog-reference-docs.md)
