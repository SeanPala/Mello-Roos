# Parcel and APN lookup scope

Type: grilling
Status: resolved

## Question

For PDF-in → SQL-out v1, should the automation extract Exhibit A boundary/APN-to-zone mappings from RMAs (e.g. Fillmore CFD 8 lists APNs per zone), or only rate tables — leaving parcel classification to data already in the existing DB?

## Answer

**Rate tables only.** v1 extracts RMA rate tables → `[dbo].[Rate]` rows. No Exhibit A / APN-to-zone extraction.

**No schema hook for Exhibit A.** The only provided DDL is `[dbo].[Rate]` (ticket 08). Rate rows carry classification dimensions (`class_id`, `class_name`, etc.) — zone numbers, land use classes, annexation areas — but there is no table for APN lists, boundary maps, or parcel→class assignment.

**Parcel classification is upstream.** The sample disclosure report (`Reference-Docs/71E442A71EADF072.pdf`) shows the consumer model: APN in → which CFDs apply → Current Levy / Maximum Tax Rate out. The report describes parcels as "assigned a maximum special tax… based on development status, property use, and/or size of improvements" — classification happens before rate lookup. Some CFDs show "Insufficient data to produce NOST" when rate/class data is missing upstream. This automation populates the rate catalog; disclosure joins parcel attributes (zone, land use, acreage, unit count, etc.) to `[Rate]` rows at query time.

**Exempt parcels:** ignored in v1 (e.g. Fillmore CFD 2 UNOCAL) — handled at parcel level, not in rate extraction.

**Exhibit A extraction:** out of scope for this effort — same boundary as a full parcel/APN geospatial database. Zone-based CFDs (Variant E, e.g. Fillmore CFD 8) rely on parcel zone assignment maintained outside this pipeline.
