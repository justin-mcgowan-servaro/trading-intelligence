using System;
using System.Collections.Generic;
using System.Text;

namespace TradingIntelligence.Core.Entities;

public class Ticker
{
    public int Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public string? Exchange { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<SignalEvent> SignalEvents { get; set; } = new List<SignalEvent>();
    public ICollection<MomentumScore> MomentumScores { get; set; } = new List<MomentumScore>();
}
