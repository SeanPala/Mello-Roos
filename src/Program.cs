using System.CommandLine;
using System.CommandLine.Invocation;
using MelloRoos;
using MelloRoos.Models;

var root = new RootCommand("Mello-Roos RMA rate extraction pipeline");

var textCommand = new Command("text", "Extract text from a PDF (pdftotext with OCR fallback)");
var pdfArg = new Argument<string>("pdf", "Path to RMA PDF");
var textOutOption = new Option<string?>(["-o", "--output"], "Write extracted text to file");
var forceOcrOption = new Option<bool>("--force-ocr", "Skip pdftotext and run OCR");
var pagesOption = new Option<string?>("--pages", "Page range for OCR, e.g. 1-18");
var dpiOption = new Option<int>("--dpi", () => TextAcquisition.DefaultDpi, "OCR DPI");
var psmOption = new Option<string>("--psm", () => TextAcquisition.DefaultPsm, "Tesseract PSM mode");

textCommand.AddArgument(pdfArg);
textCommand.AddOption(textOutOption);
textCommand.AddOption(forceOcrOption);
textCommand.AddOption(pagesOption);
textCommand.AddOption(dpiOption);
textCommand.AddOption(psmOption);

textCommand.SetHandler((string pdf, string? output, bool forceOcr, string? pages, int dpi, string psm) =>
{
    var (first, last) = Pipeline.ParsePageRange(pages);
    var result = TextAcquisition.Acquire(pdf, new TextAcquisitionOptions
    {
        ForceOcr = forceOcr,
        FirstPage = first,
        LastPage = last,
        Dpi = dpi,
        TesseractPsm = psm
    });

    Console.Error.WriteLine($"Method: {result.Method}, chars: {result.CharCount}, pages: {result.PageCount?.ToString() ?? "unknown"}");

    if (output is not null)
        File.WriteAllText(output, result.Text);
    else
        Console.Write(result.Text);
}, pdfArg, textOutOption, forceOcrOption, pagesOption, dpiOption, psmOption);

var extractCommand = new Command("extract", "Full pipeline: PDF → LLM JSON → escalation → SQL");
var extractPdfArg = new Argument<string>("pdf", "Path to RMA PDF");
var debtIdOption = new Option<int>("--debt-id", "Existing [dbo].[Debt].debt_id") { IsRequired = true };
var jsonOption = new Option<string?>("--json", "Skip LLM; load intermediate JSON from file");
var runDateOption = new Option<string?>("--run-date", "Escalation run date (yyyy-MM-dd); default today");
var saveTextOption = new Option<string?>("--save-text", "Save acquired text to file");
var saveJsonOption = new Option<string?>("--save-json", "Save LLM extraction JSON to file");
var sqlOutOption = new Option<string?>(["-o", "--output"], "Write SQL INSERTs to file");
var forceOption = new Option<bool>("--force", "Emit SQL even when review is required");
var extractForceOcrOption = new Option<bool>("--force-ocr", "Skip pdftotext and run OCR");
var extractPagesOption = new Option<string?>("--pages", "Page range for OCR, e.g. 1-18");
var extractDpiOption = new Option<int>("--dpi", () => TextAcquisition.DefaultDpi, "OCR DPI");
var extractPsmOption = new Option<string>("--psm", () => TextAcquisition.DefaultPsm, "Tesseract PSM mode");
var providerOption = new Option<string>("--provider", () => "gemini", "LLM provider: gemini, openai, or claude");
var modelOption = new Option<string?>("--model", "Model name (default: gemini-3.6-flash, gpt-4o-mini, or claude-sonnet-4-20250514)");
var landUseTypeOption = new Option<int>("--land-use-type", () => 0, "Default land_use_type for all rows");

extractCommand.AddArgument(extractPdfArg);
extractCommand.AddOption(debtIdOption);
extractCommand.AddOption(jsonOption);
extractCommand.AddOption(runDateOption);
extractCommand.AddOption(saveTextOption);
extractCommand.AddOption(saveJsonOption);
extractCommand.AddOption(sqlOutOption);
extractCommand.AddOption(forceOption);
extractCommand.AddOption(extractForceOcrOption);
extractCommand.AddOption(extractPagesOption);
extractCommand.AddOption(extractDpiOption);
extractCommand.AddOption(extractPsmOption);
extractCommand.AddOption(providerOption);
extractCommand.AddOption(modelOption);
extractCommand.AddOption(landUseTypeOption);

extractCommand.SetHandler(async (InvocationContext ctx) =>
{
    var parse = ctx.ParseResult;
    var pdf = parse.GetValueForArgument(extractPdfArg);
    var debtId = parse.GetValueForOption(debtIdOption);
    var jsonPath = parse.GetValueForOption(jsonOption);
    var runDateStr = parse.GetValueForOption(runDateOption);
    var saveText = parse.GetValueForOption(saveTextOption);
    var saveJson = parse.GetValueForOption(saveJsonOption);
    var sqlOut = parse.GetValueForOption(sqlOutOption);
    var force = parse.GetValueForOption(forceOption);
    var forceOcr = parse.GetValueForOption(extractForceOcrOption);
    var pages = parse.GetValueForOption(extractPagesOption);
    var dpi = parse.GetValueForOption(extractDpiOption);
    var psm = parse.GetValueForOption(extractPsmOption) ?? TextAcquisition.DefaultPsm;
    var providerStr = parse.GetValueForOption(providerOption) ?? "gemini";
    var provider = LlmExtractor.ParseProvider(providerStr);
    var model = parse.GetValueForOption(modelOption) ?? LlmExtractor.DefaultModel(provider);
    var landUseType = parse.GetValueForOption(landUseTypeOption);

    var runDate = ParseRunDate(runDateStr);
    var (first, last) = Pipeline.ParsePageRange(pages);

    var result = await Pipeline.RunExtractAsync(new PipelineOptions
    {
        PdfPath = pdf,
        DebtId = debtId,
        RunDate = runDate,
        JsonPath = jsonPath,
        SaveTextPath = saveText,
        SaveJsonPath = saveJson,
        SqlOutputPath = sqlOut,
        Force = force,
        ForceOcr = forceOcr,
        FirstPage = first,
        LastPage = last,
        Dpi = dpi,
        TesseractPsm = psm,
        LlmProvider = provider,
        LlmModel = model,
        LandUseType = landUseType
    });

    if (result.TextResult is not null)
        Console.Error.WriteLine($"Text: {result.TextResult.Method}, {result.TextResult.CharCount} chars");

    Console.Error.WriteLine($"Provider: {providerStr}, model: {model}");

    Console.Error.WriteLine($"Confidence: {result.Extraction.ExtractionConfidence}, flags: {result.Extraction.Flags.Count}");
    foreach (var flag in result.Extraction.Flags)
        Console.Error.WriteLine($"  flag: {flag}");

    if (result.ReviewRequired)
    {
        Console.Error.WriteLine("Review required — use --save-json to edit, then re-run with --json. Or pass --force to emit SQL anyway.");
        ctx.ExitCode = 2;
        return;
    }

    Console.Error.WriteLine($"Escalated {result.Escalated.Count} rows for debt_id={debtId}, run_date={runDate:yyyy-MM-dd}");

    if (sqlOut is null)
        Console.Write(result.Sql);
    else
        Console.Error.WriteLine($"SQL written to {sqlOut}");
});

var escalateCommand = new Command("escalate", "Deterministic escalation + SQL from JSON (no LLM)");
var jsonArg = new Argument<string>("json", "Intermediate extraction JSON");
var escalateDebtIdOption = new Option<int>("--debt-id", "Existing [dbo].[Debt].debt_id") { IsRequired = true };
var escalateRunDateOption = new Option<string?>("--run-date", "Escalation run date (yyyy-MM-dd); default today");
var escalateSqlOutOption = new Option<string?>(["-o", "--output"], "Write SQL INSERTs to file");
var escalateLandUseTypeOption = new Option<int>("--land-use-type", () => 0, "Default land_use_type for all rows");

escalateCommand.AddArgument(jsonArg);
escalateCommand.AddOption(escalateDebtIdOption);
escalateCommand.AddOption(escalateRunDateOption);
escalateCommand.AddOption(escalateSqlOutOption);
escalateCommand.AddOption(escalateLandUseTypeOption);

escalateCommand.SetHandler((string jsonPath, int debtId, string? runDateStr, string? sqlOut, int landUseType) =>
{
    var runDate = ParseRunDate(runDateStr);
    var extraction = LlmExtractor.LoadFromJsonFile(jsonPath);
    var escalated = EscalationService.Apply(extraction, runDate);
    var sql = SqlGenerator.Generate(debtId, escalated, landUseType);

    if (sqlOut is null)
        Console.Write(sql);
    else
    {
        File.WriteAllText(sqlOut, sql);
        Console.Error.WriteLine($"SQL written to {sqlOut} ({escalated.Count} rows)");
    }
}, jsonArg, escalateDebtIdOption, escalateRunDateOption, escalateSqlOutOption, escalateLandUseTypeOption);

root.AddCommand(textCommand);
root.AddCommand(extractCommand);
root.AddCommand(escalateCommand);

var locateCommand = new Command("locate", "Find Mello-Roos RMA page range in a long PDF (TOC first, keyword fallback)");
var locatePdfArg = new Argument<string>("pdf", "Path to bond package or RMA PDF");
var tocPagesOption = new Option<string>("--toc-pages", () => "1-25", "Page range to scan for table of contents");
var chunkSizeOption = new Option<int>("--chunk-size", () => 30, "Pages per chunk when scanning without TOC");
var paddingOption = new Option<int>("--padding", () => 2, "Extra pages to include before/after RMA section");
var maxSpanOption = new Option<int>("--max-span", () => 35, "Max RMA section length if end page unknown");
var locateForceOcrOption = new Option<bool>("--force-ocr", () => true, "OCR scanned pages (default true)");
var locateDpiOption = new Option<int>("--dpi", () => TextAcquisition.DefaultDpi, "OCR DPI");
var locatePsmOption = new Option<string>("--psm", () => TextAcquisition.DefaultPsm, "Tesseract PSM mode");
var locateJsonOption = new Option<bool>("--json", () => false, "Output result as JSON");
var tocLooseOption = new Option<bool>("--toc-loose", () => false, "Fuzzy TOC matching (OCR-tolerant, appendix/exhibit entries, raw keyword search)");
var tocMinScoreOption = new Option<int>("--toc-min-score", () => 35, "Minimum match score for TOC RMA entry (lower = more permissive)");
var pageOffsetOption = new Option<int?>("--page-offset", "Manual offset: PDF page = listed TOC page + N");
var noAutoOffsetOption = new Option<bool>("--no-auto-offset", () => false, "Disable auto-detection of preliminary-page offset");

locateCommand.AddArgument(locatePdfArg);
locateCommand.AddOption(tocPagesOption);
locateCommand.AddOption(chunkSizeOption);
locateCommand.AddOption(paddingOption);
locateCommand.AddOption(maxSpanOption);
locateCommand.AddOption(locateForceOcrOption);
locateCommand.AddOption(locateDpiOption);
locateCommand.AddOption(locatePsmOption);
locateCommand.AddOption(locateJsonOption);
locateCommand.AddOption(tocLooseOption);
locateCommand.AddOption(tocMinScoreOption);
locateCommand.AddOption(pageOffsetOption);
locateCommand.AddOption(noAutoOffsetOption);

locateCommand.SetHandler((InvocationContext ctx) =>
{
    var parse = ctx.ParseResult;
    var pdf = parse.GetValueForArgument(locatePdfArg);
    var tocPages = parse.GetValueForOption(tocPagesOption) ?? "1-25";
    var chunkSize = parse.GetValueForOption(chunkSizeOption);
    var padding = parse.GetValueForOption(paddingOption);
    var maxSpan = parse.GetValueForOption(maxSpanOption);
    var forceOcr = parse.GetValueForOption(locateForceOcrOption);
    var dpi = parse.GetValueForOption(locateDpiOption);
    var psm = parse.GetValueForOption(locatePsmOption) ?? TextAcquisition.DefaultPsm;
    var asJson = parse.GetValueForOption(locateJsonOption);
    var tocLoose = parse.GetValueForOption(tocLooseOption);
    var tocMinScore = parse.GetValueForOption(tocMinScoreOption);
    var pageOffset = parse.GetValueForOption(pageOffsetOption);
    var noAutoOffset = parse.GetValueForOption(noAutoOffsetOption);

    var (tocFirst, tocLast) = Pipeline.ParsePageRange(tocPages);
    var result = RmaLocator.Locate(pdf, new RmaLocateOptions
    {
        PdfPath = pdf,
        TocFirstPage = tocFirst ?? 1,
        TocLastPage = tocLast ?? 25,
        ChunkSize = chunkSize,
        Padding = padding,
        MaxSpan = maxSpan,
        ForceOcr = forceOcr,
        Dpi = dpi,
        TesseractPsm = psm,
        TocLoose = tocLoose,
        TocMinScore = tocMinScore,
        PageOffset = pageOffset,
        AutoPageOffset = !noAutoOffset
    });

    if (asJson)
    {
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return;
    }

    Console.WriteLine($"Method:       {result.Method}");
    Console.WriteLine($"Total pages:  {result.TotalPages}");
    if (result.ListedStartPage is not null)
        Console.WriteLine($"Listed pages: {result.ListedStartPage}-{result.ListedEndPage} (TOC numbering)");
    Console.WriteLine($"Page offset:  {result.PageOffset} ({result.PageOffsetMethod})");
    Console.WriteLine($"PDF pages:    {result.StartPage}-{result.EndPage} (use with --pages)");
    if (result.TocEntry is not null)
        Console.WriteLine($"TOC entry:    {result.TocEntry}");
    if (result.Notes is not null)
        Console.WriteLine($"Notes:        {result.Notes}");
    Console.WriteLine();
    Console.WriteLine("Suggested extract command:");
    Console.WriteLine($"  dotnet run --project src/MelloRoos.csproj -- extract \"{pdf}\" \\");
    Console.WriteLine($"    --debt-id <ID> --force-ocr --pages {result.PagesArg} \\");
    Console.WriteLine("    --save-json out/extraction.json -o out/rates.sql");
});

root.AddCommand(locateCommand);

return await root.InvokeAsync(args);

static DateOnly ParseRunDate(string? runDateStr)
{
    if (string.IsNullOrWhiteSpace(runDateStr))
        return DateOnly.FromDateTime(DateTime.Today);

    if (!DateOnly.TryParse(runDateStr, out var runDate))
        throw new ArgumentException($"Invalid --run-date: {runDateStr}");

    return runDate;
}
