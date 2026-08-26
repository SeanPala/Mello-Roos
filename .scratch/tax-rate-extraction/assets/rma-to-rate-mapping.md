# RMA rate dimensions → `[dbo].[Rate]` mapping

Prototype for ticket [06 SQL schema shape for rate dimensions](../issues/06-sql-schema-shape.md).

## Core rule

**One `[dbo].[Rate]` row per discrete rate-class entry extracted from the RMA** — not one row per parcel. Parcels match rows at disclosure time using classification already in the DB (zone, land use class, acreage, unit count, etc.).

**Caller supplies:** `debt_id` (parent `[dbo].[Debt]` row must exist before INSERT).

**Automation computes at load time:** `current_roll_year`, `current_max_tax_rate`, `current_backup_tax_rate` by applying the RMA's escalation from `initial_roll_year` to the run date (ticket 02).

## Column population (all variants)

| Column | Source |
|--------|--------|
| `debt_id` | Caller input — ties rates to the CFD/debt record |
| `display_order` | Row order in RMA table (1, 2, 3…) |
| `class_id` | Numeric id from RMA (land use class #, zone #, annexation area #); use `0` for synthetic rows (building/land split) |
| `class_name` | Short label: `"Class 3"`, `"Zone 2"`, `"Annexation Area 5"`, `"Building"`, `"One-Time Annexation"` |
| `class_description` | Full RMA description + dimension text (≤250 chars; truncate with ellipsis if needed) |
| `class_other` | Secondary key: bond phase, sq ft tier shorthand, `"Original Property"`, etc. |
| `land_use` | Category text: `"Residential"`, `"Commercial"`, `"Non-Residential"`, `"Undeveloped"`, `"POA/Public"` |
| `land_use_type` | Caller/default enum — not derivable from RMA alone |
| `initial_roll_year` | Base fiscal year start from rate table header (e.g. `2013` for FY 2013-14) |
| `max_tax_rate` | Base-year rate amount from RMA table |
| `max_tax_unit` | `"per unit"`, `"per acre"`, `"per sq ft"` |
| `max_tax_qty` | Usually `1` for per-unit; leave NULL for acre/sq ft (qty comes from parcel) |
| `max_tax_qty_source` | `"unit count"`, `"acreage"`, `"building sq ft"`, `"non-residential floor area"` |
| `current_roll_year` | Fiscal year at automation run time |
| `current_max_tax_rate` | Escalated rate for `current_roll_year` |
| `max_tax_text` | Disclosure string, e.g. `"$2,093.00 per unit — Single Family Detached, ≥43,560 sq ft"` |
| `backup_tax_flag` | `1` when RMA defines backup acre levy applicable to this class; else `0` |
| `backup_tax_rate` / `current_backup_tax_rate` | Base and escalated backup $/acre when flag set |
| `backup_tax_text` | Disclosure text for backup levy when flag set |
| `rate_type` | Default/`NULL` for annual max rates; **TBD enum** for one-time annexation rows (CFD 6) |
| Audit columns | DB defaults |

## Escalation formulas (apply at load, store result only)

| RMA pattern | Formula | Example docs |
|-------------|---------|--------------|
| 2% annual, July 1 | `current = base × 1.02^years` | Casitas 2013-1, Fillmore 3, Fillmore 8 |
| 102% annual | `current = base × 1.02^years` | Fillmore 2 |
| None stated | `current = base` | Fillmore 6 |

`years` = count of July 1 escalation anniversaries from base fiscal year through run date.

## Per-variant row shapes

### A — Land use class × unit/sq ft + bond phase (Casitas 2013-1)

**Rows:** one per Table 1 cell — if both bond-phase columns populated, **one row per phase** (same `class_id`, different `class_other`).

| Field | Example (Class 1, after 2nd bond) |
|-------|-----------------------------------|
| `class_id` | `1` |
| `class_name` | `"Class 1"` |
| `class_description` | `"Single Family Detached Unit, parcel ≥43,560 sq ft"` |
| `class_other` | `"After 2nd Bond Issue"` |
| `land_use` | `"Residential"` |
| `max_tax_unit` | `"per unit"` |
| `max_tax_qty_source` | `"unit count"` |
| `max_tax_rate` | `2093.00` (base year — use column matching active bond phase) |
| `backup_tax_flag` | `0` |

Classes 7–8 (commercial/industrial): `max_tax_unit` = `"per sq ft"`, `max_tax_qty_source` = `"non-residential floor area"`.

### B — Building sq ft + land acre (Fillmore 2)

**Rows:** **two rows** for the CFD — not per parcel.

| Row | `class_name` | `max_tax_unit` | `max_tax_qty_source` | Base rate |
|-----|--------------|----------------|----------------------|-----------|
| 1 | `"Building"` | `"per sq ft"` | `"building sq ft"` | `$0.60` |
| 2 | `"Land"` | `"per acre"` | `"acreage"` | `$8,773.00` |

`class_id` = `0` for both. The 87/13 apportionment split is **not stored** (proportionate levy — out of scope). Exempt parcels (UNOCAL) are **not** rate rows — handled at parcel level.

### C — Land use class + backup acre (Fillmore 3)

**Rows:** one per Table 1 class (6 rows) + optionally one row for undeveloped/POA/public flat acre.

| Field | Example (Class 1 residential) |
|-------|-------------------------------|
| `class_id` | `1` |
| `class_description` | `"Residential, floor area >3,000 sq ft"` |
| `class_other` | `">3000 sq ft"` |
| `max_tax_unit` | `"per unit"` |
| `max_tax_rate` | `3493.00` |
| `backup_tax_flag` | `1` |
| `backup_tax_rate` | `17503.00` |
| `backup_tax_text` | `"Backup special tax $17,503.00 per acre (FY 2005-06 base)"` |

Class 6 (non-residential): `max_tax_unit` = `"per acre"`, same backup fields.

Undeveloped/POA/public ($/acre only): separate row(s), `land_use` = `"Undeveloped"` / `"POA/Public"`, no backup flag (backup applies to developed classes).

### D — Annexation area × acre + one-time tax (Fillmore 6)

**Annual rows:** one per Table 2 annexation area (8 rows) + one for original property.

| Field | Example (Annexation Area 3) |
|-------|----------------------------|
| `class_id` | `3` |
| `class_name` | `"Annexation Area 3"` |
| `class_other` | `"Annexation Area Property"` |
| `max_tax_unit` | `"per acre"` |
| `max_tax_rate` | `2116.00` |

Original property row: `class_name` = `"Original CFD Property"`, `max_tax_rate` = `13913.00`.

**One-time row:** additional row per annexation area (or one generic row):

| Field | Value |
|-------|-------|
| `class_name` | `"One-Time Annexation Tax"` |
| `class_description` | `"Catch-up levy at annexation (sum of max annual taxes from FY 2008-09 through year before annual levy begins)"` |
| `max_tax_rate` | NULL or computed if formula inputs known |
| `max_tax_text` | Full formula text from RMA Section C.2 |
| `rate_type` | **TBD** — needs `rate_type` enum from existing system |

Table 1 (reductions to original) and Table 4 (exempt acres) are **adjustment rules**, not standalone rate rows — capture in `max_tax_text` on original-property row or omit from `[Rate]`.

### E — Zone × acre (Fillmore 8)

**Rows:** one per Table 1 zone (5 rows).

| Field | Example (Zone 1) |
|-------|------------------|
| `class_id` | `1` |
| `class_name` | `"Zone 1"` |
| `class_description` | `"The Stop (TSAF, LLC)"` |
| `max_tax_unit` | `"per acre"` |
| `max_tax_rate` | `26540.00` |

POA/public per zone (Table 2): same zone ids, `land_use` = `"POA/Public"`, typically same $/acre as zone row.

## Example INSERT (Casitas Class 1, after 2nd bond)

```sql
-- debt_id supplied by caller; escalation precomputed for current fiscal year
INSERT INTO [dbo].[Rate] (
    debt_id, display_order, class_id, class_name, class_description, class_other,
    land_use, initial_roll_year, max_tax_rate, max_tax_unit, max_tax_qty, max_tax_qty_source,
    current_roll_year, current_max_tax_rate, max_tax_text,
    backup_tax_flag
) VALUES (
    @debt_id, 1, 1, 'Class 1',
    'Single Family Detached Unit, parcel >= 43,560 sq ft',
    'After 2nd Bond Issue',
    'Residential', 2013, 2093.00000, 'per unit', 1, 'unit count',
    2026, 2654.00000,  -- example: 2% escalated from 2013-14 base
    '$2,654.00 per unit — Single Family Detached, >=43,560 sq ft (Casitas CFD 2013-1, Table 1)',
    0
);
```

## Open gaps (not blocking mapping spec)

1. **`rate_type` enum** — needed for one-time annexation rows (Variant D).
2. **`land_use_type` enum** — not in provided DDL; caller sets or defaults.
3. **`[dbo].[Debt]` DDL** — debt-level metadata (CFD name, agency) lives there, not in `[Rate]`.
4. **Exhibit A APN lists** — out of scope (ticket 09); zone/class assignment stays in existing parcel data upstream of rate lookup.

## Scanned / unknown variants

OCR'd docs should classify into A–E where possible. New structures get new `class_other` conventions documented in the extraction run output — no schema change required if one-row-per-class rule holds.
