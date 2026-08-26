using System.Globalization;
using System.Text.RegularExpressions;
using MelloRoos.Models;

namespace MelloRoos;

public static class EscalationService
{
    public static int ParseInitialRollYear(string baseFiscalYear)
    {
        var match = Regex.Match(baseFiscalYear.Trim(), @"(\d{4})");
        if (!match.Success)
            throw new ArgumentException($"Cannot parse base fiscal year: {baseFiscalYear}");

        return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    public static int CurrentRollYear(DateOnly runDate)
    {
        // Fiscal year starts July 1: on/after July 1 → FY = calendar year + 1 start label uses calendar year of July
        return runDate.Month >= 7 ? runDate.Year : runDate.Year - 1;
    }

    public static int CountEscalationYears(Escalation escalation, DateOnly runDate)
    {
        if (string.Equals(escalation.Type, "none", StringComparison.OrdinalIgnoreCase))
            return 0;

        if (string.IsNullOrWhiteSpace(escalation.Start))
            return 0;

        if (!DateOnly.TryParse(escalation.Start, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
            return 0;

        var years = 0;
        var anniversary = start;
        while (anniversary <= runDate)
        {
            years++;
            anniversary = anniversary.AddYears(1);
        }

        return years;
    }

    public static double Escalate(double baseRate, Escalation escalation, int years)
    {
        if (years <= 0)
            return baseRate;

        var type = escalation.Type.ToLowerInvariant();
        return type switch
        {
            "percent_annual" => baseRate * Math.Pow(1.0 + (escalation.Rate ?? 0.0), years),
            "multiplier_annual" => baseRate * Math.Pow(escalation.Multiplier ?? 1.0, years),
            "none" => baseRate,
            _ => baseRate
        };
    }

    public static List<EscalatedRateClass> Apply(ExtractionResult extraction, DateOnly runDate)
    {
        var initialRollYear = ParseInitialRollYear(extraction.Source.BaseFiscalYear);
        var currentRollYear = CurrentRollYear(runDate);
        var years = CountEscalationYears(extraction.Source.Escalation, runDate);

        var oneTimeKeys = extraction.OneTimeTaxes
            .Select(r => (r.ClassId, r.DisplayOrder))
            .ToHashSet();

        var all = extraction.RateClasses
            .Concat(extraction.OneTimeTaxes)
            .OrderBy(r => r.DisplayOrder)
            .ThenBy(r => r.ClassId)
            .ToList();

        var result = new List<EscalatedRateClass>();
        for (var i = 0; i < all.Count; i++)
        {
            var rc = all[i];
            if (rc.DisplayOrder <= 0)
                rc.DisplayOrder = i + 1;

            var isOneTime = oneTimeKeys.Contains((rc.ClassId, rc.DisplayOrder));
            double? currentMax = rc.MaxTaxRate;
            double? currentBackup = rc.BackupTaxRate;

            if (!isOneTime && rc.MaxTaxRate is not null)
                currentMax = Escalate(rc.MaxTaxRate.Value, extraction.Source.Escalation, years);

            if (rc.BackupTaxFlag && rc.BackupTaxRate is not null)
                currentBackup = Escalate(rc.BackupTaxRate.Value, extraction.Source.Escalation, years);

            result.Add(new EscalatedRateClass
            {
                RateClass = rc,
                InitialRollYear = initialRollYear,
                CurrentRollYear = currentRollYear,
                CurrentMaxTaxRate = currentMax,
                CurrentBackupTaxRate = currentBackup
            });
        }

        return result;
    }
}
