# SQL schema shape for rate dimensions

Type: prototype
Status: resolved

## Question

Given scoped rate data and a target consumer, what relational schema (tables, keys, enums) best models the rate dimensions seen across Reference-Docs — without overfitting to one CFD's layout?

## Answer

**No new tables.** All five RMA variants map onto existing `[dbo].[Rate]` using **one row per rate-class entry** from the RMA tables, with multi-dimensional keys encoded in `class_id` / `class_name` / `class_description` / `class_other` / `land_use`.

**Full mapping spec:** [assets/rma-to-rate-mapping.md](../assets/rma-to-rate-mapping.md)

**Variant summary:**

| Variant | Rows per CFD | Key encoding |
|---------|-------------|--------------|
| A — Land use + bond phase | 1 row per table cell (×2 if both bond phases loaded) | `class_id` + `class_other` = bond phase |
| B — Building + land | 2 rows (`Building`, `Land`) | `max_tax_unit` distinguishes components |
| C — Assigned + backup | 1 row per Table 1 class | `backup_tax_flag` + backup columns on developed classes |
| D — Annexation + one-time | 1 row per annexation area + original + one-time row | `rate_type` TBD for one-time |
| E — Zone × acre | 1 row per zone | `class_id` = zone number |

**Escalation:** computed at load time into `current_max_tax_rate` / `current_backup_tax_rate`; base year in `initial_roll_year`, base amount in `max_tax_rate`.

**Gaps for implementation:** `rate_type` enum (one-time taxes), `land_use_type` enum, `[dbo].[Debt]` DDL for debt-level fields.
