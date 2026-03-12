using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace TradingIntelligence.Infrastructure.Helpers;

public static class TickerExtractor
{
    // Matches 1–5 uppercase letters, optionally preceded by $ sign
    private static readonly Regex TickerPattern =
        new(@"\$([A-Z]{1,5})\b|\b([A-Z]{2,5})\b",
            RegexOptions.Compiled);

    // Common false positives — words that look like tickers but aren't
    private static readonly HashSet<string> Blacklist = new(StringComparer.OrdinalIgnoreCase)
    {
        // Common English words that are all caps in titles
        "A", "I", "THE", "AND", "FOR", "ARE", "BUT", "NOT", "YOU", "ALL",
        "CAN", "HER", "WAS", "ONE", "OUR", "OUT", "DAY", "GET", "HAS",
        "HIM", "HIS", "HOW", "ITS", "MAY", "NEW", "NOW", "OLD", "SEE",
        "TWO", "WAY", "WHO", "BOY", "DID", "ITS", "LET", "PUT", "SAY",
        "SHE", "TOO", "USE",
        // Financial terms that look like tickers
        "CEO", "CFO", "COO", "IPO", "ETF", "GDP", "CPI", "FED", "SEC",
        "NYSE", "NASDAQ", "IMF", "ESG", "OTC", "ATH", "ATL", "ROI",
        "EPS", "PE", "DD", "TA", "FA", "PT", "YOY", "QOQ", "MOM",
        "FOMO", "HODL", "YOLO", "BTFD", "ATM", "ITM", "OTM",
        // Country and region codes
        "USA", "USD", "EUR", "GBP", "JPY", "CAD", "AUD", "ZAR",
        "UK", "EU", "US", "SA",
        // Common Reddit/social terms
        "EDIT", "UPDATE", "NEWS", "BREAKING", "WATCH", "SELL", "HOLD",
        "LONG", "SHORT", "CALL", "PUT", "BUY", "MOON", "BEAR", "BULL",
        "GAIN", "LOSS", "YOLO", "TLDR", "IMO", "IIRC", "AFAIK",
        // Government / regulatory
        "FDA", "CDC", "WHO", "FBI", "CIA", "DOJ", "FTC", "IRS",
        "NATO", "OPEC", "FOMC", "FDIC",
    };

    // Known valid tickers — loaded from database at startup
    private static HashSet<string> _validTickers = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime _lastLoaded = DateTime.MinValue;
    private static readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Load valid tickers from the database. Call once at startup.
    /// </summary>
    public static void LoadValidTickers(IEnumerable<string> tickers)
    {
        _validTickers = new HashSet<string>(tickers, StringComparer.OrdinalIgnoreCase);
        _lastLoaded = DateTime.UtcNow;
    }

    /// <summary>
    /// Extract valid ticker symbols from raw text.
    /// Returns distinct list of validated tickers found in the text.
    /// </summary>
    public static List<string> Extract(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<string>();

        var matches = TickerPattern.Matches(text);
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in matches)
        {
            // Group 1 = $TICKER format (higher confidence)
            // Group 2 = plain TICKER format
            var ticker = match.Groups[1].Success
                ? match.Groups[1].Value
                : match.Groups[2].Value;

            ticker = ticker.ToUpperInvariant().Trim();

            // Skip if in blacklist
            if (Blacklist.Contains(ticker)) continue;

            // Skip single characters
            if (ticker.Length < 2) continue;

            // If we have a valid ticker list loaded, validate against it
            // If not loaded yet, allow through (will be validated later)
            if (_validTickers.Count > 0 && !_validTickers.Contains(ticker)) continue;

            found.Add(ticker);
        }

        return found.ToList();
    }

    /// <summary>
    /// Extract tickers with confidence score.
    /// $TICKER format = high confidence (0.9)
    /// Plain TICKER format = normal confidence (0.6)
    /// </summary>
    public static List<(string Ticker, double Confidence)> ExtractWithConfidence(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<(string, double)>();

        var matches = TickerPattern.Matches(text);
        var found = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in matches)
        {
            bool isDollarPrefixed = match.Groups[1].Success;
            var ticker = isDollarPrefixed
                ? match.Groups[1].Value
                : match.Groups[2].Value;

            ticker = ticker.ToUpperInvariant().Trim();

            if (Blacklist.Contains(ticker)) continue;
            if (ticker.Length < 2) continue;
            if (_validTickers.Count > 0 && !_validTickers.Contains(ticker)) continue;

            // Dollar sign prefix = higher confidence
            double confidence = isDollarPrefixed ? 0.9 : 0.6;

            // Keep highest confidence if seen multiple times
            if (!found.ContainsKey(ticker) || found[ticker] < confidence)
                found[ticker] = confidence;
        }

        return found.Select(kv => (kv.Key, kv.Value)).ToList();
    }

    public static bool IsValidTicker(string ticker)
    {
        if (string.IsNullOrWhiteSpace(ticker)) return false;
        if (Blacklist.Contains(ticker)) return false;
        if (ticker.Length < 2 || ticker.Length > 5) return false;
        if (_validTickers.Count > 0) return _validTickers.Contains(ticker);
        return true;
    }
}
