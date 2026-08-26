# Amendment and versioning strategy

Type: grilling
Status: resolved

## Question

When multiple RMA versions exist for the same CFD (e.g. Fillmore CFD 5 — 1st and 2nd amended), how should the database represent authority, effective dates, and supersession?

## Answer

**No supersession or amendment reconciliation.** The automation treats each uploaded PDF as the sole source of truth for that run.

**Workflow model:** user provides one PDF → automation extracts rates and emits SQL load queries. `Reference-Docs/` is a **test corpus** for validating that pipeline, not a canonical multi-version registry.

**Implications:**
- Both Fillmore CFD 5 amended PDFs can remain in the test set; each is processed independently when supplied as input.
- No effective-date columns, no "latest wins" logic, no cross-document deduplication by CFD identity.
- Extract whatever rate tables and metadata appear in the **current document only** (including its stated base fiscal year and escalation rules, applied to current year per ticket 02).
- Overrides the CFD 5 "prefer 2nd Amended" note in ticket 04 — that applied only if picking a canonical version; here the user picks the file.
