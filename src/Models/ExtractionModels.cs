using System.Text.Json.Serialization;

namespace MelloRoos.Models;

public sealed class Escalation
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "none";

    [JsonPropertyName("rate")]
    public double? Rate { get; set; }

    [JsonPropertyName("multiplier")]
    public double? Multiplier { get; set; }

    [JsonPropertyName("start")]
    public string? Start { get; set; }
}

public sealed class Source
{
    [JsonPropertyName("cfd_name")]
    public string CfdName { get; set; } = "";

    [JsonPropertyName("agency")]
    public string Agency { get; set; } = "";

    [JsonPropertyName("base_fiscal_year")]
    public string BaseFiscalYear { get; set; } = "";

    [JsonPropertyName("variant")]
    public string Variant { get; set; } = "unknown";

    [JsonPropertyName("escalation")]
    public Escalation Escalation { get; set; } = new();
}

public sealed class RateClass
{
    [JsonPropertyName("class_id")]
    public int ClassId { get; set; }

    [JsonPropertyName("class_name")]
    public string ClassName { get; set; } = "";

    [JsonPropertyName("class_description")]
    public string? ClassDescription { get; set; }

    [JsonPropertyName("class_other")]
    public string? ClassOther { get; set; }

    [JsonPropertyName("land_use")]
    public string? LandUse { get; set; }

    [JsonPropertyName("max_tax_rate")]
    public double? MaxTaxRate { get; set; }

    [JsonPropertyName("max_tax_unit")]
    public string? MaxTaxUnit { get; set; }

    [JsonPropertyName("max_tax_qty")]
    public double? MaxTaxQty { get; set; }

    [JsonPropertyName("max_tax_qty_source")]
    public string? MaxTaxQtySource { get; set; }

    [JsonPropertyName("backup_tax_flag")]
    public bool BackupTaxFlag { get; set; }

    [JsonPropertyName("backup_tax_rate")]
    public double? BackupTaxRate { get; set; }

    [JsonPropertyName("backup_tax_text")]
    public string? BackupTaxText { get; set; }

    [JsonPropertyName("display_order")]
    public int DisplayOrder { get; set; } = 1;

    [JsonPropertyName("rate_type")]
    public int? RateType { get; set; }
}

public sealed class ExtractionResult
{
    [JsonPropertyName("source")]
    public Source Source { get; set; } = new();

    [JsonPropertyName("rate_classes")]
    public List<RateClass> RateClasses { get; set; } = [];

    [JsonPropertyName("one_time_taxes")]
    public List<RateClass> OneTimeTaxes { get; set; } = [];

    [JsonPropertyName("extraction_confidence")]
    public string ExtractionConfidence { get; set; } = "medium";

    [JsonPropertyName("flags")]
    public List<string> Flags { get; set; } = [];
}

public sealed class EscalatedRateClass
{
    public required RateClass RateClass { get; init; }
    public int InitialRollYear { get; init; }
    public int CurrentRollYear { get; init; }
    public double? CurrentMaxTaxRate { get; init; }
    public double? CurrentBackupTaxRate { get; init; }
}

public sealed class TextAcquisitionResult
{
    public required string Text { get; init; }
    public required string Method { get; init; }
    public int CharCount { get; init; }
    public int? PageCount { get; init; }
}

public sealed class TextAcquisitionOptions
{
    public bool ForceOcr { get; init; }
    public int? FirstPage { get; init; }
    public int? LastPage { get; init; }
    public int Dpi { get; init; } = 300;
    public string TesseractPsm { get; init; } = "6";
}
