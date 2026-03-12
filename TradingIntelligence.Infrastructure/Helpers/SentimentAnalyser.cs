using System;
using System.Collections.Generic;
using System.Text;
namespace TradingIntelligence.Infrastructure.Helpers;

public static class SentimentAnalyser
{
    // Weighted financial sentiment lexicon
    // Positive words → positive score, Negative words → negative score
    private static readonly Dictionary<string, double> Lexicon = new(StringComparer.OrdinalIgnoreCase)
    {
        // Strong bullish signals
        { "moon", 0.9 }, { "mooning", 0.9 }, { "rocket", 0.8 }, { "breakout", 0.8 },
        { "surge", 0.8 }, { "surging", 0.8 }, { "soar", 0.8 }, { "soaring", 0.8 },
        { "skyrocket", 0.9 }, { "explode", 0.8 }, { "exploding", 0.8 },
        { "bullish", 0.8 }, { "bull", 0.6 }, { "uptrend", 0.7 }, { "breakthrough", 0.7 },
        { "upgrade", 0.7 }, { "upgraded", 0.7 }, { "outperform", 0.7 },
        { "beat", 0.6 }, { "beats", 0.6 }, { "record", 0.6 },
        { "partnership", 0.7 }, { "acquisition", 0.6 }, { "merger", 0.5 },
        { "revenue", 0.3 }, { "growth", 0.6 }, { "profit", 0.6 }, { "earnings", 0.3 },
        { "dividend", 0.5 }, { "buyback", 0.6 }, { "buy", 0.5 }, { "long", 0.5 },
        { "calls", 0.5 }, { "call", 0.4 }, { "rally", 0.7 }, { "rallying", 0.7 },
        { "opportunity", 0.5 }, { "undervalued", 0.7 }, { "cheap", 0.4 },
        { "strong", 0.5 }, { "strength", 0.5 }, { "positive", 0.5 },
        { "green", 0.4 }, { "gains", 0.6 }, { "gain", 0.5 }, { "winner", 0.6 },
        { "winning", 0.6 }, { "amazing", 0.6 }, { "incredible", 0.7 },
        { "massive", 0.5 }, { "huge", 0.5 }, { "pumping", 0.6 },

        // Strong bearish signals
        { "crash", -0.9 }, { "crashing", -0.9 }, { "collapse", -0.9 },
        { "collapsing", -0.9 }, { "dump", -0.8 }, { "dumping", -0.8 },
        { "bearish", -0.8 }, { "bear", -0.6 }, { "downtrend", -0.7 },
        { "downgrade", -0.7 }, { "downgraded", -0.7 }, { "underperform", -0.7 },
        { "miss", -0.6 }, { "missed", -0.6 }, { "disappoint", -0.7 },
        { "disappointing", -0.7 }, { "disappoints", -0.7 },
        { "loss", -0.6 }, { "losses", -0.6 }, { "bankrupt", -0.9 },
        { "bankruptcy", -0.9 }, { "fraud", -0.9 }, { "scandal", -0.8 },
        { "investigation", -0.6 }, { "lawsuit", -0.6 }, { "recall", -0.7 },
        { "sell", -0.5 }, { "selling", -0.5 }, { "short", -0.5 },
        { "puts", -0.5 }, { "put", -0.4 }, { "overvalued", -0.7 },
        { "expensive", -0.4 }, { "weak", -0.5 }, { "weakness", -0.5 },
        { "negative", -0.5 }, { "red", -0.4 }, { "tank", -0.8 }, { "tanking", -0.8 },
        { "falling", -0.6 }, { "fall", -0.5 }, { "drop", -0.6 }, { "dropping", -0.6 },
        { "plunge", -0.8 }, { "plunging", -0.8 }, { "brutal", -0.7 },
        { "terrible", -0.7 }, { "horrible", -0.8 }, { "worst", -0.8 },
        { "danger", -0.7 }, { "dangerous", -0.7 }, { "risky", -0.5 },
        { "warning", -0.6 }, { "avoid", -0.6 }, { "stay away", -0.7 },
    };

    /// <summary>
    /// Returns a sentiment score between -1.0 (very bearish) and +1.0 (very bullish)
    /// </summary>
    public static decimal Score(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;

        var words = text.ToLowerInvariant()
            .Split(new[] { ' ', '\n', '\r', '\t', '.', ',', '!', '?', ':', ';', '(', ')' },
                StringSplitOptions.RemoveEmptyEntries);

        double totalScore = 0;
        int matchCount = 0;

        for (int i = 0; i < words.Length; i++)
        {
            var word = words[i];

            // Check for negation — "not bullish", "never going up"
            bool negated = i > 0 &&
                (words[i - 1] == "not" || words[i - 1] == "never" ||
                 words[i - 1] == "no" || words[i - 1] == "dont" ||
                 words[i - 1] == "don't" || words[i - 1] == "cant" ||
                 words[i - 1] == "can't" || words[i - 1] == "won't");

            if (Lexicon.TryGetValue(word, out double score))
            {
                totalScore += negated ? -score : score;
                matchCount++;
            }
        }

        if (matchCount == 0) return 0;

        // Normalise to -1.0 to +1.0
        double averaged = totalScore / matchCount;
        double clamped = Math.Max(-1.0, Math.Min(1.0, averaged));
        return (decimal)Math.Round(clamped, 4);
    }

    /// <summary>
    /// Returns a human-readable sentiment label
    /// </summary>
    public static string Label(decimal score) => score switch
    {
        > 0.6m => "Very Bullish",
        > 0.3m => "Bullish",
        > 0.1m => "Slightly Bullish",
        > -0.1m => "Neutral",
        > -0.3m => "Slightly Bearish",
        > -0.6m => "Bearish",
        _ => "Very Bearish"
    };
}
