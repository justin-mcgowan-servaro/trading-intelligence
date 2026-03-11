using System;
using System.Collections.Generic;
using System.Text;
using TradingIntelligence.Core.Models;

namespace TradingIntelligence.Core.Interfaces;

public interface IMomentumScorer
{
    Task<MomentumScoreResult> ScoreAsync(
        string ticker,
        List<RawSignalEvent> signals,
        CancellationToken cancellationToken = default);
}
