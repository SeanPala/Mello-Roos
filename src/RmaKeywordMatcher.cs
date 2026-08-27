using System.Text.RegularExpressions;

namespace MelloRoos;

/// <summary>Keyword heuristics for locating Mello-Roos RMA sections without a TOC.</summary>
internal static class RmaKeywordMatcher
{
    private const int SectionStartMinScore = 50;
    private const int SectionContinueMinScore = 15;

    public static bool IsSectionStart(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (RmaLocator.ScoreRmaContent(text) < SectionStartMinScore)
            return false;

        if (Regex.IsMatch(text, @"(?i)rate\s+and\s+method\s+of\s+apportionment|maximum\s+special\s+tax"))
            return true;

        if (Regex.IsMatch(text, @"(?i)\btable\s+1\b")
            && Regex.IsMatch(text, @"(?i)special\s+tax|apportionment|land\s+use"))
            return true;

        if (Regex.IsMatch(text, @"(?i)mello[\-\s]?roos")
            && Regex.IsMatch(text, @"(?i)special\s+tax|apportionment|community\s+facilities\s+district"))
            return true;

        if (Regex.IsMatch(text, @"(?i)section\s+c\b")
            && Regex.IsMatch(text, @"(?i)special\s+tax|apportionment|rate"))
            return true;

        return false;
    }

    public static bool IsSectionContinued(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return RmaLocator.ScoreRmaContent(text) >= SectionContinueMinScore;
    }

    public static bool IsHardSectionEnd(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (Regex.IsMatch(text, @"(?i)^exhibit\s+[a-z0-9\-]+", RegexOptions.Multiline))
            return true;

        if (Regex.IsMatch(text, @"(?i)^appendix\s+[a-z0-9]+\b", RegexOptions.Multiline)
            && !Regex.IsMatch(text, @"(?i)rate\s+and\s+method|apportionment\s+of\s+special|maximum\s+special\s+tax|\btable\s+1\b"))
        {
            return true;
        }

        var bondBoilerplate = Regex.IsMatch(text, @"(?i)indenture|official\s+statement|underwriter|book[\-\s]?entry");
        var rmaSignals = Regex.IsMatch(text, @"(?i)rate\s+and\s+method|special\s+tax|\btable\s+1\b|apportionment");
        return bondBoilerplate && !rmaSignals;
    }
}
