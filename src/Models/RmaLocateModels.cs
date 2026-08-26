namespace MelloRoos.Models;

public sealed class PagedText
{
    public required int PageNumber { get; init; }
    public required string Text { get; init; }
}

public sealed class TocEntry
{
    public required string Title { get; init; }
    public required int PageNumber { get; init; }
    public required int Score { get; init; }
}

public sealed class RmaLocateResult
{
    public required string Method { get; init; }
    public required int StartPage { get; init; }
    public required int EndPage { get; init; }
    public required int TotalPages { get; init; }
    public int? ListedStartPage { get; init; }
    public int? ListedEndPage { get; init; }
    public int PageOffset { get; init; }
    public string? PageOffsetMethod { get; init; }
    public string? TocEntry { get; init; }
    public string? Notes { get; init; }
    public required string PagesArg { get; init; }
}

public sealed class RmaLocateOptions
{
    public required string PdfPath { get; init; }
    public int TocFirstPage { get; init; } = 1;
    public int TocLastPage { get; init; } = 25;
    public int ChunkSize { get; init; } = 30;
    public int Padding { get; init; } = 2;
    public int MaxSpan { get; init; } = 35;
    public bool ForceOcr { get; init; } = true;
    public int Dpi { get; init; } = 300;
    public string TesseractPsm { get; init; } = "6";
    public bool TocLoose { get; init; }
    public int TocMinScore { get; init; } = 35;
    public int? PageOffset { get; init; }
    public bool AutoPageOffset { get; init; } = true;
}
