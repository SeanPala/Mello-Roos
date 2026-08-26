# Database consumer and engine

Type: grilling
Status: resolved

## Question

Who or what will query this database, and on what engine should it live (PostgreSQL, SQLite, other)? The consumer (disclosure PDF, parcel lookup by APN, internal reporting, seed data for an app) drives whether we need normalized rate tables, document versioning, or fiscal-year history.

## Answer

**Consumer:** parcel disclosure — showing applicable Mello-Roos special tax rates on a property.

**Integration model:** rates load into an **existing database** outside this repo. Deliverable is **SQL INSERT/UPDATE statements (or equivalent load queries)** that populate the existing tables, not a greenfield schema in Mello-Roos.

**Engine:** portable/regular SQL — no engine-specific features required; queries should work against whatever the existing DB uses.

**Implication for downstream tickets:** ticket 06 (schema shape) becomes "map RMA rate dimensions onto the existing table columns" rather than inventing new tables. **Blocker:** we need the existing table definitions (DDL or ERD) before schema mapping or write queries can be drafted. Add as a task ticket if not already available.
