# Tax rate extraction — wayfinder map

Status: complete — route clear

## Destination

A decided extraction approach and SQL generation spec for a **PDF-in → SQL-out** automation: given one RMA PDF, produce the load queries for current-fiscal-year applicable Mello-Roos special tax rates into an existing database. `Reference-Docs/` validates the pipeline; no multi-document versioning logic.

## Notes

- **Domain:** Mello-Roos Community Facilities District (CFD) Rate and Method of Apportionment (RMA) documents for Ventura County agencies (Fillmore, Camarillo, Casitas MWD, etc.).
- **Corpus:** 11 PDFs in `Reference-Docs/` as test inputs (as of charting).
- **Workflow:** one PDF per run; treat that document as authoritative — no supersession across versions.
- **Skills every session should consult:** `/domain-modeling`, `/grilling`; `/research` for document cataloging and external references.
- **Standing preference:** Plan first — this map decides *what* to extract and *how* to model it in SQL; implementation is out of scope until the route is clear.
- **Broader process:** The California Tax Disclosure Report (`Reference-Docs/71E442A71EADF072.pdf`) is the **end product** — today filled by hand from the existing DB plus supplemental docs (Exhibit A, parcel data, etc.). This effort automates only the **RMA rate-table → `[dbo].[Rate]`** step; APN lookup, parcel classification, and report generation remain upstream/downstream of this pipeline.

## Decisions so far

- [01 Catalog Reference-Docs RMA documents](issues/01-catalog-reference-docs.md) — 11 PDFs: 5 text-native / 6 scanned; five rate-model variants (land-use+bond-phase, sqft+acre split, assigned+backup acre, annexation+one-time, zone+acre); scanned docs need OCR.
- [02 What rates belong in the database?](issues/02-what-rates-belong-in-db.md) — Store **current-fiscal-year applicable rates** (precomputed at load time); include classification dimensions and one-time taxes; exclude proportionate-levy math and full escalation history.
- [03 Database consumer and engine](issues/03-database-consumer-and-engine.md) — Parcel disclosure; load into **existing DB** via SQL write queries; portable SQL.
- [04 Scanned PDF extraction strategy](issues/04-scanned-pdf-strategy.md) — **OCR + auto-extract** all 6 scanned docs; human review before load; selective OCR for Series 2002 bond package.
- [05 Amendment and versioning strategy](issues/05-amendment-versioning.md) — **No supersession**; one PDF in → SQL out; each doc is sole source of truth for its run; Reference-Docs is test corpus only.
- [08 Obtain existing database schema](issues/08-obtain-existing-db-schema.md) — Target is SQL Server `[dbo].[Rate]` (FK to `[dbo].[Debt]`); DDL at `assets/dbo.Rate.ddl.sql`; one row per rate class with base + current-year columns and optional backup tax fields.
- [06 SQL schema shape for rate dimensions](issues/06-sql-schema-shape.md) — One `[Rate]` row per RMA rate-class entry; variants A–E mapped via class_* columns + backup fields; mapping spec at `assets/rma-to-rate-mapping.md`.
- [07 Extraction pipeline approach](issues/07-extraction-pipeline-approach.md) — Hybrid: pdftotext/OCR → LLM JSON extraction → deterministic escalation + SQL templates → human review; spec at `assets/extraction-pipeline-spec.md`.
- [09 Parcel and APN lookup scope](issues/09-parcel-apn-lookup-scope.md) — **Rate tables only** into `[dbo].[Rate]`; no Exhibit A / APN extraction (no schema for it); parcel→class assignment is upstream of this automation; exempt parcels ignored in v1.

## Not yet specified

<!-- route clear — nothing left to decide before implementation -->

## Out of scope

- Building a full Ventura County parcel/APN geospatial database.
- **Exhibit A boundary/APN-to-zone extraction** from RMA PDFs ([09 Parcel and APN lookup scope](issues/09-parcel-apn-lookup-scope.md)) — parcel classification (zone, annexation area, exemptions) lives in existing parcel data, not this pipeline.
- Computing actual levied special taxes from live debt-service requirements (proportionate apportionment against outstanding bonds).
- CFDs not represented in `Reference-Docs/`.
- RMA amendment supersession, effective-date reconciliation, or picking a canonical version when multiple PDFs exist for the same CFD.
