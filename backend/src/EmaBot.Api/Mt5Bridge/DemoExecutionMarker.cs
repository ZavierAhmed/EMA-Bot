using System.Text.RegularExpressions;

namespace EmaBot.Api.Mt5Bridge;

// Exness stores only the first 31 characters of an MT5 comment, so every NEW
// correlation marker must fit within that budget or broker-side evidence can
// never match it exactly.  Legacy 36-character markers are never rewritten;
// legacy executions recover through native exact tickets instead.
public static partial class DemoExecutionMarker
{
    public const int BrokerSafeMaxLength = 31;
    public const int MinimumHexCharacters = 24;
    public const int MaximumPrefixLength = BrokerSafeMaxLength - 1 - MinimumHexCharacters;

    public static bool IsPrefixValid(string prefix) => prefix.Length is >= 1 and <= MaximumPrefixLength && HexPrefixRegex().IsMatch(prefix);

    public static string Generate(string correlationPrefix, Guid clientExecutionId)
    {
        if (!IsPrefixValid(correlationPrefix)) throw new ArgumentException($"The correlation prefix must be 1-{MaximumPrefixLength} alphanumeric characters so the marker fits {BrokerSafeMaxLength} broker-safe characters.");
        var hex = clientExecutionId.ToString("N");
        var hexLength = Math.Min(BrokerSafeMaxLength - correlationPrefix.Length - 1, hex.Length);
        if (hexLength < MinimumHexCharacters) throw new ArgumentException("The correlation prefix does not retain sufficient GUID entropy.");
        return $"{correlationPrefix}-{hex[..hexLength]}";
    }

    // Stored markers from before E11.5B2 can be 36 characters long.  Exness
    // persists the first 31 characters, so bounded history lookup must send
    // that physical broker comment while the database retains the logical ID.
    public static string BrokerMarker(string persistedMarker) =>
        persistedMarker.Length <= BrokerSafeMaxLength ? persistedMarker : persistedMarker[..BrokerSafeMaxLength];

    public static bool MatchesPersistedMarker(string persistedMarker, string brokerMarker) =>
        string.Equals(BrokerMarker(persistedMarker), brokerMarker, StringComparison.Ordinal);

    [GeneratedRegex("^[0-9A-Za-z]+$")]
    private static partial Regex HexPrefixRegex();
}
