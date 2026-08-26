# Reference-Docs PDF catalog

Research ticket: [01-catalog-reference-docs](../issues/01-catalog-reference-docs.md)  
Date: 2026-08-12  
Corpus: 11 PDFs in `Reference-Docs/`

## Summary

| Metric | Count |
|--------|------:|
| Total PDFs | 11 |
| Text-native (extractable) | 5 |
| Scanned / image-only | 6 |
| Distinct rate-model variants (from text-native docs) | 5 |

**Classification rule:** `pdftotext` character count ≥ 1,000 → text-native; otherwise scanned. Scanned docs yield ~1 char/page (page numbers only); `pdffonts` shows no embedded fonts.

---

## Master index

| # | File | Pages | Chars | Type | CFD / Agency |
|---|------|------:|------:|------|--------------|
| 1 | `Casitas Municipal Water District, CFD 2013-1, RMA (1).pdf` | 11 | 30,298 | text-native | Casitas MWD CFD 2013-1 (Ojai) |
| 2 | `City of Fillmore, CFD 2, RMA.pdf` | 8 | 17,129 | text-native | Fillmore CFD 2 (Balden Towne Plaza) |
| 3 | `City of Fillmore, CFD 3, RMA.pdf` | 10 | 27,211 | text-native | Fillmore CFD 3 (River Oaks) |
| 4 | `City of Fillmore, CFD 6, RMA.pdf` | 7 | 17,127 | text-native | Fillmore CFD 6 (Sespe Creek / River St) |
| 5 | `City of Fillmore, CFD 8, RMA.pdf` | 19 | 43,982 | text-native | Fillmore CFD 8 (Fillmore Business Park) |
| 6 | `CFD 1, Series 2002 (1).pdf` | 258 | 258 | scanned | Unknown (Series 2002 bond/RMA package) |
| 7 | `CFD No. 2000-1, RMA (1).pdf` | 6 | 48 | scanned | CFD 2000-1 (agency unclear; title = "Admin Report 2010") |
| 8 | `City of Camarillo, CFD 1, RMA (Amended) (1).pdf` | 18 | 18 | scanned | Camarillo CFD 1 (Amended) |
| 9 | `City of Fillmore, CFD 1, RMA (1).pdf` | 11 | 11 | scanned | Fillmore CFD 1 |
| 10 | `City of Fillmore, CFD 5, IA B RMA (1st Amended).pdf` | 13 | 13 | scanned | Fillmore CFD 5, Improvement Area B (1st Amended) |
| 11 | `City of Fillmore, CFD 5, IA B RMA (2nd Amended).pdf` | 17 | 17 | scanned | Fillmore CFD 5, Improvement Area B (2nd Amended) |

---

## Rate model variants (text-native docs)

Five structurally distinct apportionment models appear in the extractable corpus:

| Variant | Docs | Key dimensions | Apportionment logic |
|---------|------|----------------|---------------------|
| **A — Land use class × unit/sq ft + bond phase** | Casitas 2013-1 | Land use class (1–8), parcel sq ft tiers, unit count, non-residential floor area (sq ft); prior/after 2nd bond issue | Proportionate levy on developed property up to max; undeveloped exempt |
| **B — Building sq ft + land acre (87/13 split)** | Fillmore 2 | Building floor area (sq ft), acreage; exempt parcel (UNOCAL) | Annual special tax split 87% buildings / 13% land; each parcel = building rate × sq ft + land rate × acres |
| **C — Land use class × residential floor area + backup acre** | Fillmore 3 | Residential floor area tiers (5 classes), non-residential (per acre); undeveloped/public/POA (per acre) | Staged: 100% assigned tax → proportionate undeveloped → backup acre levy on developed → POA → public |
| **D — Annexation area × acre + one-time tax** | Fillmore 6 | Original vs annexation area (8 areas), acreage; POA/public per area | Proportionate annual per acre by annexation area; one-time catch-up tax at annexation; original acre rate reduced as areas annex |
| **E — Zone × acre + zone bond allocation** | Fillmore 8 | Zone (1–5), acreage; POA/public per zone | Per-zone special tax requirement; proportionate levy within zone; bond debt allocated by zone |

Scanned docs (6) may add more variants once OCR'd — especially Camarillo CFD 1, Fillmore CFD 1, and CFD 5 (Improvement Area B).

---

## Text-native document details

### 1. Casitas MWD CFD 2013-1 (Ojai)

| Field | Value |
|-------|-------|
| **Issuing agency** | Casitas Municipal Water District |
| **CFD identity** | Community Facilities District No. 2013-1 (Ojai) |
| **Base fiscal year** | 2013-14 |
| **Rate model** | Variant A — land use class with unit/sq ft maxima + proportionate levy |
| **Rate tables** | **Table 1** (Section C, pp. 5–6): 8 land use classes |
| **Dimensions** | Land use class; parcel square footage tiers (43,560+ / 22,000–43,560 / 10,000–22,000 / <10,000); per-unit (residential); per sq ft non-residential floor area (commercial/industrial); bond issue phase (prior vs after 2nd bond issue) |
| **Escalation** | 2% annual increase on max special tax, July 1 each year from 2014 |
| **One-time taxes** | None (prepayment formulas in Section H) |
| **Bond-phase splits** | Yes — "Prior to 2nd Bond Issue" vs "After 2nd Bond Issue" columns in Table 1 |
| **Undeveloped vs developed** | Undeveloped, POA, and public property exempt (Section E); only developed property taxed |
| **Zones / annexation** | None — single district boundary |

**Table 1 rates (FY 2013-14, prior to 2nd bond issue):**

| Class | Description | Dimension | Prior 2nd bond | After 2nd bond |
|------:|-------------|-----------|---------------:|---------------:|
| 1 | Single family detached | ≥43,560 sq ft | $345/unit | $2,093/unit |
| 2 | Single family detached | 22,000–43,560 sq ft | $203/unit | $1,235/unit |
| 3 | Single family detached | 10,000–22,000 sq ft | $122/unit | $741/unit |
| 4 | Single family detached | <10,000 sq ft | $79/unit | $480/unit |
| 5 | Condominium unit | per unit | $67/unit | $407/unit |
| 6 | Multifamily attached | per unit | $57/unit | $349/unit |
| 7 | Commercial | non-residential floor area | $0.050/sq ft | $0.303/sq ft |
| 8 | Industrial | non-residential floor area | $0.026/sq ft | $0.159/sq ft |

---

### 2. City of Fillmore CFD 2 (Balden Towne Plaza)

| Field | Value |
|-------|-------|
| **Issuing agency** | City of Fillmore |
| **CFD identity** | Community Facilities District No. 2 (Balden Towne Plaza Public Improvements) |
| **Base fiscal year** | Through FY 1995-96 (max rates); annual report shows FY 2004/05 applied rates |
| **Rate model** | Variant B — building sq ft + land acre dual-rate |
| **Rate tables** | No numbered table in RMA; **SPECIAL TAX RATES** appendix (annual report) lists applied vs maximum rates |
| **Dimensions** | Building floor area (sq ft), acreage; exempt UNOCAL parcel (legal description in RMA) |
| **Escalation** | 102% compounding annual on both max building rate ($0.60/sq ft base) and max land rate ($8,773/acre base) from FY 1996-97 onward |
| **One-time taxes** | None |
| **Bond-phase splits** | No |
| **Undeveloped vs developed** | No explicit split — all parcels in CFD boundary taxed by formula |
| **Zones / annexation** | None |

**Max rates (base, through FY 1995-96):** $0.60/sq ft building; $8,773/acre land.  
**Applied rates (FY 2004/05):** $0.7171/sq ft building; $1,094.24/acre land (10.44% of max land rate).  
**Apportionment:** 87% of annual special tax on buildings (capped at max building rate × total floor area); remainder on land proportionate by acre.

---

### 3. City of Fillmore CFD 3 (River Oaks)

| Field | Value |
|-------|-------|
| **Issuing agency** | City of Fillmore |
| **CFD identity** | Community Facilities District No. 3 (River Oaks) |
| **Base fiscal year** | 2005-06 |
| **Rate model** | Variant C — assigned land use class + backup per acre |
| **Rate tables** | **Table 1** (Section C.1.b, Appendix B): 6 land use classes |
| **Dimensions** | Residential floor area tiers (5 classes); non-residential (per acre); backup tax ($/acre); undeveloped/POA/public (per acre) |
| **Escalation** | 2% annual on assigned, backup, and undeveloped/POA/public maxima from July 1, 2006 |
| **One-time taxes** | None |
| **Bond-phase splits** | No |
| **Undeveloped vs developed** | Yes — staged apportionment (Section D): developed assigned → undeveloped proportionate → backup on developed → POA → public |
| **Zones / annexation** | None; up to 3.74 acres POA/public exempt |

**Table 1 rates (FY 2005-06):**

| Class | Description | Residential floor area | Assigned tax |
|------:|-------------|------------------------|-------------:|
| 1 | Residential | >3,000 sq ft | $3,493/unit |
| 2 | Residential | 2,800–3,000 sq ft | $3,333/unit |
| 3 | Residential | 2,600–2,799 sq ft | $3,227/unit |
| 4 | Residential | 2,400–2,599 sq ft | $3,050/unit |
| 5 | Residential | <2,400 sq ft | $1,438/unit |
| 6 | Non-residential | per acre | $17,503/acre |

**Backup special tax:** $17,503/acre (same as undeveloped/POA/public max).

---

### 4. City of Fillmore CFD 6 (Sespe Creek and River Street Improvements)

| Field | Value |
|-------|-------|
| **Issuing agency** | City of Fillmore |
| **CFD identity** | Community Facilities District No. 6 (Sespe Creek and River Street Improvements) |
| **Base fiscal year** | 2008-09 |
| **Rate model** | Variant D — annexation area × acre + one-time catch-up |
| **Rate tables** | **Table 1** (reductions to original property), **Table 2** (annexation area annual max), **Table 3** (POA/public by area), **Table 4** (exempt public acres by area) |
| **Dimensions** | Original CFD property vs annexation area (1–8), acreage |
| **Escalation** | None stated — fixed $/acre rates |
| **One-time taxes** | Yes — catch-up levy at annexation equal to sum of max annual taxes for FY 2008-09 through year before annual levy begins (Section C.2) |
| **Bond-phase splits** | No |
| **Undeveloped vs developed** | "Developable property" (original + annexed); POA and taxable public taxed in later apportionment steps |
| **Zones / annexation** | 8 future annexation areas (Exhibit A boundary map); original property = Parcel 2 of Parcel Map 05-03 |

**Key rates:**

| Category | Rate |
|----------|-----:|
| Original CFD No. 6 property | $13,913/acre (reduced per Table 1 as areas annex) |
| Annexation areas 1–8 | $828–$7,390/acre (Table 2) |
| POA / public (by area) | Same as Table 2/3 per annexation area |

**Table 1 reductions (original property, per acre annexed):** Area 1 $847 … Area 8 $1,715.

---

### 5. City of Fillmore CFD 8 (Fillmore Business Park)

| Field | Value |
|-------|-------|
| **Issuing agency** | City of Fillmore |
| **CFD identity** | Community Facilities District No. 8 (Fillmore Business Park) |
| **Base fiscal year** | 2008-09 |
| **Rate model** | Variant E — zone × acre with zone-specific bond allocation |
| **Rate tables** | **Table 1** (Section C.1.a): max special tax by zone; **Table 2** (Section E): exempt acres by zone |
| **Dimensions** | Zone (1–5), acreage; POA/public per zone |
| **Escalation** | 2% annual on max special tax from July 1, 2009 |
| **One-time taxes** | None |
| **Bond-phase splits** | Zone bond allocation (Outstanding Zone N Bonds = Non-Prepayment Principal × Zone N Allocation) |
| **Undeveloped vs developed** | Zone property taxed; POA/public in steps 2–3 per zone |
| **Zones / annexation** | 5 zones (Exhibit A boundary map with APNs and zone numbers) |

**Table 1 rates (FY 2008-09, per acre):**

| Zone | Description | Max special tax |
|-----:|-------------|----------------:|
| 1 | The Stop (TSAF, LLC) | $26,540/acre |
| 2 | The Stop (Karasiuk) | $17,782/acre |
| 3 | Perry Ranch Project | $8,000/acre |
| 4 | Sespe Creek Properties (Maxwell) | $18,182/acre |
| 5 | KDF/Coe Family | $16,931/acre |

**Exhibit A** lists assessor parcel numbers per zone (8 APNs total).

---

## Scanned documents

All six yield no usable text via `pdftotext`. Producer metadata confirms scanner/MFP origin (Xerox, C3765dnf, e-Financial eConvert, EISI PDF Writer).

| File | Pages | OCR recommendation |
|------|------:|--------------------|
| `CFD 1, Series 2002 (1).pdf` | 258 | **Selective OCR** — full doc is likely bond indenture + RMA + exhibits; OCR entire 258 pp is costly. OCR RMA section and rate tables only (~first 20–30 pp if structured like other CFDs). |
| `CFD No. 2000-1, RMA (1).pdf` | 6 | **Full OCR** — small; title suggests admin report not full RMA; verify content after OCR. |
| `City of Camarillo, CFD 1, RMA (Amended) (1).pdf` | 18 | **Full OCR** — standard RMA length; high priority (Camarillo is a major agency in corpus). |
| `City of Fillmore, CFD 1, RMA (1).pdf` | 11 | **Full OCR** — standard RMA length. |
| `City of Fillmore, CFD 5, IA B RMA (1st Amended).pdf` | 13 | **Full OCR** — use 2nd amended as authoritative if both cover same CFD; compare after OCR. |
| `City of Fillmore, CFD 5, IA B RMA (2nd Amended).pdf` | 17 | **Full OCR** — treat as current version for Fillmore CFD 5 IA B; supersedes 1st amended. |

**Manual entry fallback:** For rate-table extraction only, manual transcription of Table 1–N pages may be faster than OCR QA for the 6-page and 11-page docs.

---

## Cross-cutting observations

1. **Escalation patterns:** 2% annual (Casitas, Fillmore 3, 8) vs 102% annual (Fillmore 2) vs none (Fillmore 6).
2. **Proportionate levy:** All text-native docs allow levying below maximum when debt service + admin is less than total max capacity.
3. **Exemptions:** POA and public property commonly exempt or capped (acre allotments vary by CFD).
4. **Prepayment:** All text-native docs include prepayment formulas (full and/or partial).
5. **Boundary maps:** Fillmore 6 (Exhibit A), Fillmore 8 (Exhibit A with APNs/zones) embed parcel/zone geography in PDF — geospatial extraction is a separate concern from rate tables.
6. **Amendment precedence:** Fillmore CFD 5 has two amended versions (1st and 2nd); only 2nd amended should be used once OCR'd.

---

## Extraction method

```bash
# Classification
pdftotext "file.pdf" - | wc -c
pdfinfo "file.pdf" | grep Pages

# Threshold: chars >= 1000 → text-native
```

Text extracts for the 5 text-native docs are saved under `.scratch/tax-rate-extraction/research/text-extracts/`.
