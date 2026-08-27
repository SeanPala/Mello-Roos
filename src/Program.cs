using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.RegularExpressions;
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
var visionTableOption = new Option<bool>("--vision-table", () => false, "Force vision on Table 1 (auto-enabled for large PDFs via auto-locate)");
var tablePagesOption = new Option<string?>("--table-pages", "PDF pages for Table 1 vision (default: same as --pages, or auto-detect TABLE 1)");
var tableDpiOption = new Option<int>("--table-dpi", () => PdfPageImages.TableDpi, "DPI for table page images");
var visionModelOption = new Option<string?>("--vision-model", "Vision model for Table 1 (default per --vision-provider)");
var visionProviderOption = new Option<string?>("--vision-provider", "Vision provider: gemini, openai, or claude (default: same as --provider)");
var llamaparseOption = new Option<bool>("--llamaparse", () => false, "Fall back to LlamaParse if vision table extraction is incomplete");
var textractOption = new Option<bool>("--textract", () => false, "Fall back to AWS Textract if vision (and LlamaParse) are incomplete");
var noAutoLocateOption = new Option<bool>("--no-auto-locate", () => false, "Disable TOC/chunk discovery on large PDFs (requires --pages)");

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
extractCommand.AddOption(visionTableOption);
extractCommand.AddOption(tablePagesOption);
extractCommand.AddOption(tableDpiOption);
extractCommand.AddOption(visionProviderOption);
extractCommand.AddOption(visionModelOption);
extractCommand.AddOption(llamaparseOption);
extractCommand.AddOption(textractOption);
extractCommand.AddOption(noAutoLocateOption);

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
    var visionTable = parse.GetValueForOption(visionTableOption);
    var tablePages = parse.GetValueForOption(tablePagesOption);
    var tableDpi = parse.GetValueForOption(tableDpiOption);
    var visionProviderStr = parse.GetValueForOption(visionProviderOption);
    var (visionProvider, visionModel) = ResolveVision(provider, visionProviderStr, parse.GetValueForOption(visionModelOption));
    var llamaparse = parse.GetValueForOption(llamaparseOption);
    var textract = parse.GetValueForOption(textractOption);
    var noAutoLocate = parse.GetValueForOption(noAutoLocateOption);

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
        VisionProvider = visionProvider,
        VisionModel = visionModel,
        LandUseType = landUseType,
        VisionTable = visionTable,
        TablePages = tablePages,
        TableDpi = tableDpi,
        LlamaParseFallback = llamaparse,
        TextractFallback = textract,
        AutoLocate = !noAutoLocate
    });

    if (result.Discovery is not null)
    {
        Console.Error.WriteLine($"Auto-locate: PDF pages {result.Discovery.ExtractFirst}-{result.Discovery.ExtractLast} ({result.Discovery.Method}, score {result.Discovery.ConfidenceScore})");
        if (result.Discovery.TableFirst is int tf && result.Discovery.TableLast is int tl)
            Console.Error.WriteLine($"Table 1 window: {tf}-{tl}");
        Console.Error.WriteLine($"Notes: {result.Discovery.Notes}");
    }

    if (result.TextResult is not null)
        Console.Error.WriteLine($"Text: {result.TextResult.Method}, {result.TextResult.CharCount} chars");

    Console.Error.WriteLine(
        $"Provider: text={providerStr}/{model}, vision={VisionLlmClient.ProviderLabel(visionProvider)}/{visionModel}");

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

var tableExtractCommand = new Command("table-extract", "Extract Table 1 rate_classes via vision (optional LlamaParse/Textract fallbacks)");
var tablePdfArg = new Argument<string>("pdf", "Path to RMA PDF");
var tablePagesArg = new Option<string?>("--pages", "PDF page range containing Table 1; auto-discovered on large bond PDFs if omitted");
var tableOutOption = new Option<string?>(["-o", "--output"], "Write rate_classes JSON to file");
var tableMergeJsonOption = new Option<string?>("--merge-json", "Merge results into existing extraction.json");
var tableProviderOption = new Option<string?>("--vision-provider", "Vision provider: gemini, openai, or claude (default: gemini)");
var tableModelOption = new Option<string?>("--vision-model", "Vision model for Table 1 (default per provider)");
var tableVisionOption = new Option<bool>("--vision", () => true, "Use vision on Table 1 (default)");
var tableNoVisionOption = new Option<bool>("--no-vision", () => false, "Skip vision table extraction");
var tableLlamaOption = new Option<bool>("--llamaparse", () => false, "Fall back to LlamaParse if vision is incomplete");
var tableTextractOption = new Option<bool>("--textract", () => false, "Fall back to AWS Textract if vision/LlamaParse incomplete");
var tableExtractDpiOption = new Option<int>("--table-dpi", () => PdfPageImages.TableDpi, "DPI for page images");

tableExtractCommand.AddArgument(tablePdfArg);
tableExtractCommand.AddOption(tablePagesArg);
tableExtractCommand.AddOption(tableOutOption);
tableExtractCommand.AddOption(tableMergeJsonOption);
tableExtractCommand.AddOption(tableProviderOption);
tableExtractCommand.AddOption(tableModelOption);
tableExtractCommand.AddOption(tableVisionOption);
tableExtractCommand.AddOption(tableNoVisionOption);
tableExtractCommand.AddOption(tableLlamaOption);
tableExtractCommand.AddOption(tableTextractOption);
tableExtractCommand.AddOption(tableExtractDpiOption);

tableExtractCommand.SetHandler(async (InvocationContext ctx) =>
{
    var parse = ctx.ParseResult;
    var pdf = parse.GetValueForArgument(tablePdfArg);
    var pages = parse.GetValueForOption(tablePagesArg);
    var output = parse.GetValueForOption(tableOutOption);
    var mergeJson = parse.GetValueForOption(tableMergeJsonOption);
    var visionProviderStr = parse.GetValueForOption(tableProviderOption);
    var (visionProvider, model) = ResolveVision(LlmProvider.Gemini, visionProviderStr, parse.GetValueForOption(tableModelOption));
    var useVision = parse.GetValueForOption(tableVisionOption) && !parse.GetValueForOption(tableNoVisionOption);
    var llamaparse = parse.GetValueForOption(tableLlamaOption);
    var textract = parse.GetValueForOption(tableTextractOption);
    var tableDpi = parse.GetValueForOption(tableExtractDpiOption);

    int first;
    int last;

    if (!string.IsNullOrWhiteSpace(pages))
    {
        var parsed = Pipeline.ParsePageRange(pages);
        if (parsed.first is null || parsed.last is null)
            throw new ArgumentException($"Invalid --pages: {pages}");
        first = parsed.first.Value;
        last = parsed.last.Value;
    }
    else
    {
        var pageCount = TextAcquisition.GetPageCount(pdf);
        if (pageCount is int total && total > TextAcquisition.LargePdfPageThreshold)
        {
            var discovery = RmaDiscoveryService.Discover(new RmaDiscoveryOptions { PdfPath = pdf, ForceOcr = true });
            if (discovery.TableFirst is int tf && discovery.TableLast is int tl)
            {
                first = tf;
                last = tl;
            }
            else
            {
                first = discovery.ExtractFirst;
                last = discovery.ExtractLast;
            }

            Console.Error.WriteLine($"Auto-locate: using pages {first}-{last} ({discovery.Method})");
        }
        else
        {
            first = 1;
            last = pageCount ?? 1;
        }
    }

    if (!useVision && !llamaparse && !textract)
        throw new ArgumentException("Enable at least one of --vision, --llamaparse, or --textract.");

    var result = await TableExtractionService.ExtractAsync(new TableExtractionOptions
    {
        PdfPath = pdf,
        FirstPage = first,
        LastPage = last,
        Dpi = tableDpi,
        VisionProvider = visionProvider,
        Model = model,
        UseVision = useVision,
        UseLlamaParse = llamaparse,
        UseTextract = textract
    });

    Console.Error.WriteLine($"Method: {result.Method}, confidence: {result.ExtractionConfidence}, classes: {result.RateClasses.Count}");
    foreach (var flag in result.Flags)
        Console.Error.WriteLine($"  flag: {flag}");

    if (!string.IsNullOrWhiteSpace(mergeJson))
    {
        var extraction = LlmExtractor.LoadFromJsonFile(mergeJson!);
        RateClassMerger.Merge(extraction, result);
        var mergedJson = System.Text.Json.JsonSerializer.Serialize(extraction, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var mergeOut = output ?? mergeJson!;
        await File.WriteAllTextAsync(mergeOut, mergedJson);
        Console.Error.WriteLine($"Merged into {mergeOut}");
        return;
    }

    var payload = new
    {
        rate_classes = result.RateClasses,
        extraction_confidence = result.ExtractionConfidence,
        flags = result.Flags,
        table_extraction_method = result.Method
    };
    var json = System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    if (output is null)
        Console.Write(json);
    else
    {
        await File.WriteAllTextAsync(output, json);
        Console.Error.WriteLine($"Written to {output}");
    }
});

var analyzeCommand = new Command("analyze", "Long docs: same pipeline as extract, emits SQL directly (no review gate)");
var analyzePdfArg = new Argument<string>("pdf", "Path to bond package or RMA PDF");
var analyzeDebtIdOption = new Option<int>("--debt-id", "Existing [dbo].[Debt].debt_id") { IsRequired = true };
var analyzeRunDateOption = new Option<string?>("--run-date", "Escalation run date (yyyy-MM-dd); default today");
var analyzeSaveTextOption = new Option<string?>("--save-text", "Save acquired text to file");
var analyzeSaveJsonOption = new Option<string?>("--save-json", "Save extraction JSON to file");
var analyzeSqlOutOption = new Option<string?>(["-o", "--output"], "Write SQL to file");
var analyzeForceOcrOption = new Option<bool>("--force-ocr", "Skip pdftotext and run OCR");
var analyzePagesOption = new Option<string?>("--pages", "Explicit page range (skips auto-discovery)");
var analyzeDpiOption = new Option<int>("--dpi", () => TextAcquisition.DefaultDpi, "OCR DPI");
var analyzePsmOption = new Option<string>("--psm", () => TextAcquisition.DefaultPsm, "Tesseract PSM mode");
var analyzeProviderOption = new Option<string>("--provider", () => "gemini", "LLM provider for text extraction");
var analyzeModelOption = new Option<string?>("--model", "Text LLM model (default: gemini-3.6-flash)");
var analyzeVisionProviderOption = new Option<string?>("--vision-provider", "Vision provider: gemini, openai, or claude (default: same as --provider)");
var analyzeVisionModelOption = new Option<string?>("--vision-model", "Vision model for Table 1 (default per provider)");
var analyzeLlamaOption = new Option<bool>("--llamaparse", () => false, "Fall back to LlamaParse for Table 1");
var analyzeTextractOption = new Option<bool>("--textract", () => false, "Fall back to AWS Textract for Table 1");
var analyzeLandUseTypeOption = new Option<int>("--land-use-type", () => 0, "Default land_use_type for all rows");

analyzeCommand.AddArgument(analyzePdfArg);
analyzeCommand.AddOption(analyzeDebtIdOption);
analyzeCommand.AddOption(analyzeRunDateOption);
analyzeCommand.AddOption(analyzeSaveTextOption);
analyzeCommand.AddOption(analyzeSaveJsonOption);
analyzeCommand.AddOption(analyzeSqlOutOption);
analyzeCommand.AddOption(analyzeForceOcrOption);
analyzeCommand.AddOption(analyzePagesOption);
analyzeCommand.AddOption(analyzeDpiOption);
analyzeCommand.AddOption(analyzePsmOption);
analyzeCommand.AddOption(analyzeProviderOption);
analyzeCommand.AddOption(analyzeModelOption);
analyzeCommand.AddOption(analyzeVisionProviderOption);
analyzeCommand.AddOption(analyzeVisionModelOption);
analyzeCommand.AddOption(analyzeLlamaOption);
analyzeCommand.AddOption(analyzeTextractOption);
analyzeCommand.AddOption(analyzeLandUseTypeOption);

analyzeCommand.SetHandler(async (InvocationContext ctx) =>
{
    var parse = ctx.ParseResult;
    var pdf = parse.GetValueForArgument(analyzePdfArg);
    var debtId = parse.GetValueForOption(analyzeDebtIdOption);
    var runDateStr = parse.GetValueForOption(analyzeRunDateOption);
    var saveText = parse.GetValueForOption(analyzeSaveTextOption);
    var saveJson = parse.GetValueForOption(analyzeSaveJsonOption);
    var sqlOut = parse.GetValueForOption(analyzeSqlOutOption);
    var forceOcr = parse.GetValueForOption(analyzeForceOcrOption);
    var pages = parse.GetValueForOption(analyzePagesOption);
    var dpi = parse.GetValueForOption(analyzeDpiOption);
    var psm = parse.GetValueForOption(analyzePsmOption) ?? TextAcquisition.DefaultPsm;
    var providerStr = parse.GetValueForOption(analyzeProviderOption) ?? "gemini";
    var provider = LlmExtractor.ParseProvider(providerStr);
    var model = parse.GetValueForOption(analyzeModelOption) ?? LlmExtractor.DefaultModel(provider);
    var visionProviderStr = parse.GetValueForOption(analyzeVisionProviderOption);
    var (visionProvider, visionModel) = ResolveVision(provider, visionProviderStr, parse.GetValueForOption(analyzeVisionModelOption));
    var llamaparse = parse.GetValueForOption(analyzeLlamaOption);
    var textract = parse.GetValueForOption(analyzeTextractOption);
    var landUseType = parse.GetValueForOption(analyzeLandUseTypeOption);

    var (first, last) = Pipeline.ParsePageRange(pages);

    var result = await DocumentAnalyzer.RunAsync(new AnalyzeOptions
    {
        PdfPath = pdf,
        DebtId = debtId,
        RunDate = ParseRunDate(runDateStr),
        LandUseType = landUseType,
        ForceOcr = forceOcr,
        FirstPage = first,
        LastPage = last,
        Dpi = dpi,
        TesseractPsm = psm,
        LlmProvider = provider,
        LlmModel = model,
        VisionProvider = visionProvider,
        VisionModel = visionModel,
        LlamaParseFallback = llamaparse,
        TextractFallback = textract,
        SaveTextPath = saveText,
        SaveJsonPath = saveJson,
        SqlOutputPath = sqlOut
    });

    if (result.Discovery is not null)
    {
        Console.Error.WriteLine($"Discovery: PDF pages {result.Discovery.ExtractFirst}-{result.Discovery.ExtractLast} ({result.Discovery.Method})");
        Console.Error.WriteLine($"Notes: {result.Discovery.Notes}");
    }

    Console.Error.WriteLine(
        $"Text model: {model}, vision: {VisionLlmClient.ProviderLabel(visionProvider)}/{visionModel}");
    Console.Error.WriteLine("Review SQL before production load.");

    if (sqlOut is null)
        Console.Write(result.Sql);
    else
        Console.Error.WriteLine($"SQL written to {sqlOut}");
});

root.AddCommand(textCommand);
root.AddCommand(extractCommand);
root.AddCommand(tableExtractCommand);
root.AddCommand(analyzeCommand);
root.AddCommand(escalateCommand);

var locateCommand = new Command("locate", "Find Mello-Roos RMA page range in a long PDF (TOC first, keyword fallback)");
var locatePdfArg = new Argument<string>("pdf", "Path to bond package or RMA PDF");
var tocPagesOption = new Option<string>("--toc-pages", () => "1-15", "Page range to scan for table of contents");
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
    var tocPages = parse.GetValueForOption(tocPagesOption) ?? "1-15";
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
        TocLastPage = tocLast ?? RmaLocateOptions.DefaultTocLastPage,
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
    Console.WriteLine($"    --debt-id <ID> --force-ocr \\");
    Console.WriteLine("    --save-json out/extraction.json -o out/rates.sql");
    Console.WriteLine("  (auto-locates pages + vision table on large PDFs; or pass --pages explicitly)");
});

root.AddCommand(locateCommand);

var tocSmokeCommand = new Command("toc-smoke", "Run TocParser regression checks (no PDF/OCR)");
tocSmokeCommand.SetHandler(() =>
{
    const int totalPages = 258;
    var series2002Toc = """
        TABLE OF CONTENTS
        Special Tax Fund ........................................ 49
        Administrative Expense Account .......................... 52
        Apportionment of Special Tax ............................ 55
        Appendix D Rate and Method of Apportionment of Special Tax .......... 92
        Appendix E Form of Approving Legal Opinion .............. 120
        """;

    var entries = TocParser.Parse(series2002Toc, loose: true, totalPages);
    var best = TocParser.FindBestRmaEntry(entries, minScore: 35, totalPages);

    if (best is null)
        throw new InvalidOperationException("Expected Appendix D RMA entry at listed page 92.");

    if (best.PageNumber != 92)
        throw new InvalidOperationException($"Expected listed page 92, got {best.PageNumber} ({best.Title}).");

    if (!Regex.IsMatch(best.Title, @"(?i)appendix\s+d\s+rate\s+and\s+method"))
        throw new InvalidOperationException($"Expected Appendix D title, got: {best.Title}");

    if (TocParser.LooksLikeRmaTocTitle("Special Tax Fund"))
        throw new InvalidOperationException("Bond boilerplate 'Special Tax Fund' must not look like RMA.");

    if (TocParser.LooksLikeRmaTocTitle("Apportionment of Special Tax"))
        throw new InvalidOperationException("Standalone 'Apportionment of Special Tax' must not look like RMA.");

    if (TocParser.IsValidListedPage(2002, totalPages, "Series 2002"))
        throw new InvalidOperationException("Series year 2002 must not pass as a listed page.");

    var garbledOcrToc = """
        TABLE OF CONTENTS
        APPENDIX D RATE AND METHOD OF APPORTIONMENT OF SPECIAL TAX .........0.00000eD>l
        APPENDIX E INFORMATION CONCERNING THE DEPOSITORY TRUST COMPANY ......... E-1
        """;

    if (!TocParser.ContainsRmaAppendixTitle(garbledOcrToc))
        throw new InvalidOperationException("Garbled OCR TOC must still detect RMA appendix title.");

    var garbledTitle = TocParser.FindRmaAppendixTitle(garbledOcrToc);
    if (garbledTitle is null || !Regex.IsMatch(garbledTitle, @"(?i)appendix\s+d"))
        throw new InvalidOperationException($"Expected Appendix D from garbled OCR, got: {garbledTitle}");

    Console.Error.WriteLine($"toc-smoke OK: listed p.{best.PageNumber}, score {best.Score}, title: {best.Title}");
});

root.AddCommand(tocSmokeCommand);

return await root.InvokeAsync(args);

static DateOnly ParseRunDate(string? runDateStr)
{
    if (string.IsNullOrWhiteSpace(runDateStr))
        return DateOnly.FromDateTime(DateTime.Today);

    if (!DateOnly.TryParse(runDateStr, out var runDate))
        throw new ArgumentException($"Invalid --run-date: {runDateStr}");

    return runDate;
}

static (LlmProvider provider, string model) ResolveVision(
    LlmProvider textProvider,
    string? visionProviderStr,
    string? visionModelStr)
{
    var visionProvider = string.IsNullOrWhiteSpace(visionProviderStr)
        ? textProvider
        : LlmExtractor.ParseProvider(visionProviderStr);

    var visionModel = string.IsNullOrWhiteSpace(visionModelStr)
        ? LlmExtractor.DefaultVisionModelFor(visionProvider)
        : visionModelStr;

    return (visionProvider, visionModel);
}
