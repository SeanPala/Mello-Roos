namespace MelloRoos;

public static class TableRatePrompt
{
    public const string SystemPrompt = """
        You extract Mello-Roos RMA Table 1 (Assigned Special Tax by Land Use Class) into JSON.

        Return ONLY valid JSON (no markdown fences):
        {
          "rate_classes": [
            {
              "class_id": number,
              "class_name": string,
              "class_description": string|null,
              "class_other": string|null,
              "land_use": string|null,
              "max_tax_rate": number|null,
              "max_tax_unit": string|null,
              "max_tax_qty": null,
              "max_tax_qty_source": string|null,
              "backup_tax_flag": boolean,
              "backup_tax_rate": number|null,
              "backup_tax_text": string|null,
              "display_order": number,
              "rate_type": null
            }
          ],
          "extraction_confidence": "high"|"medium"|"low",
          "flags": string[]
        }

        Rules:
        - Extract every row from TABLE 1 (developed property classes) plus undeveloped/POA/public if shown on the same table page.
        - Use base fiscal year assigned rates only — do NOT compute escalated amounts.
        - Numeric amounts must be plain numbers without $ or commas (4025.57 not $4,025.57).
        - Include backup special tax rate/text when stated near Table 1 or in the same section.
        - Add flags for unreadable cells or missing class rows.
        """;

    public static string VisionUserPrompt(IReadOnlyList<PageImage> pages) =>
        $"""
        These {pages.Count} scanned PDF page image(s) are from a Mello-Roos Rate and Method of Apportionment.
        PDF pages: {string.Join(", ", pages.Select(p => p.PageNumber))}.

        Find TABLE 1 (Assigned Special Tax by Land Use Class) and extract every rate class row.
        Read dollar amounts carefully from the table cells.
        """;

    public static string TextUserPrompt(string documentText) =>
        $"""
        Extract Table 1 rate classes from this document text (parsed from a scanned RMA):

        {documentText}
        """;
}
