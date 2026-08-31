namespace MelloRoos;

/// <summary>Tracks OpenAI API availability for the current process (e.g. insufficient_quota).</summary>
public static class OpenAiAvailability
{
    private static int _quotaExhausted;

    public static bool IsDisabled => Interlocked.CompareExchange(ref _quotaExhausted, 0, 0) != 0;

    public static void MarkQuotaExhausted()
    {
        Interlocked.Exchange(ref _quotaExhausted, 1);
    }

    public static bool IsQuotaError(Exception ex) =>
        ex.ToString().Contains("insufficient_quota", StringComparison.OrdinalIgnoreCase)
        || ex.ToString().Contains("exceeded your current quota", StringComparison.OrdinalIgnoreCase);
}
