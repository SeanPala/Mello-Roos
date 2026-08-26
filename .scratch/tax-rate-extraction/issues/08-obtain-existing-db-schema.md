# Obtain existing database schema

Type: task
Status: resolved

## Question

The load target is an existing database (ticket 03). What are the table names, columns, constraints, and any enums/types used for Mello-Roos / special tax / CFD rate data? Provide DDL, ERD, or a schema dump so ticket 06 can map RMA rate dimensions to existing columns.

## Answer

**Target table:** `[dbo].[Rate]` — SQL Server (T-SQL).

**DDL saved at:** [assets/dbo.Rate.ddl.sql](../assets/dbo.Rate.ddl.sql)

**Parent FK:** `debt_id` → `[dbo].[Debt].[DebtId]` — each rate row belongs to a debt/CFD record. Automation must receive or derive `debt_id` for the PDF being processed (not extracted from the RMA itself).

**Columns relevant to RMA extraction:**

| Column | Type | Likely RMA mapping |
|--------|------|-------------------|
| `class_id` | int NOT NULL | Land use class number, zone number, or annexation area id from RMA tables |
| `class_name` | varchar(100) NOT NULL | Short label (e.g. "Zone 1", "Class 3", "Annexation Area 2") |
| `class_description` | varchar(250) | Full RMA description (e.g. "Single Family Detached Unit ≥43,560 sq ft") |
| `class_other` | varchar(100) | Secondary classifier (bond phase, parcel sq ft tier, etc.) |
| `land_use` | varchar(100) | Land use category text from RMA |
| `land_use_type` | int | Enum/code — meaning TBD (not in provided DDL) |
| `initial_roll_year` | numeric(4,0) | Base fiscal year from RMA rate table header |
| `max_tax_rate` | numeric(18,5) | Base-year maximum rate amount from RMA |
| `max_tax_unit` | varchar(100) | Unit of measure: "per unit", "per acre", "per sq ft", etc. |
| `max_tax_qty` | numeric(18,5) | Quantity multiplier if rate applies per qty (e.g. unit count = 1) |
| `max_tax_qty_source` | varchar(50) | Where qty comes from (e.g. "unit count", "acreage", "sq ft") |
| `current_roll_year` | numeric(4,0) | Current fiscal year (2025 or 2026) |
| `current_max_tax_rate` | numeric(18,5) | Escalated rate for current year (ticket 02: precompute at load time) |
| `max_tax_text` | varchar(500) | Human-readable rate string for disclosure |
| `backup_tax_flag` | bit NOT NULL | 1 when RMA defines a backup acre levy (e.g. Fillmore CFD 3) |
| `backup_tax_rate` | numeric(18,5) | Base-year backup rate |
| `current_backup_tax_rate` | numeric(18,5) | Current-year backup rate |
| `backup_tax_text` | varchar(1000) | Backup rate disclosure text |

**Columns likely set by defaults / caller, not RMA:** `rate_type`, `display_order`, `nost_type_id` (default 1), audit columns.

**Not yet provided:** `[dbo].[Debt]` DDL — may hold CFD name, agency, document metadata. Needed to know what debt-level fields automation populates vs assumes pre-existing.
