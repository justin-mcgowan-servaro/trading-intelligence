using System;
using System.Collections.Generic;
using System.Text;
namespace TradingIntelligence.Core.Entities;

public class Watchlist
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string TickerSymbol { get; set; } = string.Empty;
    public decimal? AlertThreshold { get; set; }  // Score threshold to alert user
    public bool AlertEnabled { get; set; } = true;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User? User { get; set; }
}
