# What rates belong in the database?

Type: grilling
Status: resolved

## Question

When we say "applicable tax rates," what exactly should SQL rows represent? Options include: maximum special tax by classification only; base-year max plus escalation rules; one-time annexation taxes; undeveloped vs developed splits; bond-issue-dependent rate phases. What can be computed at query time vs must be stored?

## Answer

**Store current-fiscal-year applicable rates, not a full escalation history and not proportionate-levy math.**

Rows should represent the rate a parcel would be disclosed against *right now* — precomputed for the current fiscal year from each RMA's base tables and escalation rules. We do **not** need every historical or future year's rate in the database; re-run the load (or a refresh job) when a new fiscal year starts.

**In scope for stored data:**
- Current-year rate amounts with their classification dimensions (land use class, zone, annexation area, parcel size tier, bond phase, etc.) as defined in each RMA's tables
- One-time special taxes where the RMA defines them (e.g. Fillmore CFD 6 annexation catch-up levy)
- Enough metadata to trace each rate back to source document, CFD, and fiscal year

**Out of scope for stored data:**
- Proportionate apportionment against live debt service (actual levy ≤ max)
- Full escalation formula history — apply escalation at load time to produce the current year's figure; store the result, not every intermediate year

**Open dependency:** exact column mapping depends on the existing DB table structure (see ticket 03).
