using System.Text;
using Amazon;
using Amazon.Textract;
using Amazon.Textract.Model;

namespace MelloRoos;

public static class TextractClient
{
    public static async Task<string> ExtractTablesAsync(
        IReadOnlyList<PageImage> pages,
        CancellationToken ct = default)
    {
        var region = Environment.GetEnvironmentVariable("AWS_REGION")
            ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION")
            ?? "us-west-2";

        using var client = new AmazonTextractClient(RegionEndpoint.GetBySystemName(region));
        var sb = new StringBuilder();

        foreach (var page in pages)
        {
            Console.Error.WriteLine($"  Textract page {page.PageNumber}...");
            var response = await client.AnalyzeDocumentAsync(new AnalyzeDocumentRequest
            {
                Document = new Document { Bytes = new MemoryStream(page.Bytes) },
                FeatureTypes = ["TABLES"]
            }, ct);

            sb.AppendLine($"<<<PAGE {page.PageNumber}>>>");
            sb.AppendLine(FormatTables(response));
            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }

    private static string FormatTables(AnalyzeDocumentResponse response)
    {
        var blocks = response.Blocks ?? [];
        var blockMap = blocks.ToDictionary(b => b.Id);
        var tables = blocks.Where(b => b.BlockType == BlockType.TABLE).ToList();

        if (tables.Count == 0)
            return "[no tables detected]";

        var sb = new StringBuilder();
        foreach (var table in tables)
        {
            sb.AppendLine("TABLE:");
            foreach (var row in GetTableRows(table, blockMap))
                sb.AppendLine(string.Join(" | ", row));
            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }

    private static IEnumerable<List<string>> GetTableRows(Block table, Dictionary<string, Block> blockMap)
    {
        if (table.Relationships is null)
            yield break;

        var cellIds = table.Relationships
            .Where(r => r.Type == RelationshipType.CHILD)
            .SelectMany(r => r.Ids)
            .ToList();

        var cells = cellIds
            .Where(blockMap.ContainsKey)
            .Select(id => blockMap[id])
            .Where(b => b.BlockType == BlockType.CELL)
            .OrderBy(c => c.RowIndex ?? 0)
            .ThenBy(c => c.ColumnIndex ?? 0)
            .ToList();

        var rowIndex = 0;
        List<string>? currentRow = null;

        foreach (var cell in cells)
        {
            var cellRow = cell.RowIndex ?? 0;
            if (currentRow is null || cellRow != rowIndex)
            {
                if (currentRow is not null)
                    yield return currentRow;
                currentRow = [];
                rowIndex = cellRow;
            }

            currentRow.Add(GetCellText(cell, blockMap));
        }

        if (currentRow is not null)
            yield return currentRow;
    }

    private static string GetCellText(Block cell, Dictionary<string, Block> blockMap)
    {
        if (cell.Relationships is null)
            return "";

        var words = cell.Relationships
            .Where(r => r.Type == RelationshipType.CHILD)
            .SelectMany(r => r.Ids)
            .Where(blockMap.ContainsKey)
            .Select(id => blockMap[id])
            .Where(b => b.BlockType == BlockType.WORD)
            .Select(w => w.Text ?? "")
            .Where(t => t.Length > 0);

        return string.Join(" ", words);
    }
}
