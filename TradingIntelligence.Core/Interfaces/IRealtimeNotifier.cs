using System;
using System.Collections.Generic;
using System.Text;
using TradingIntelligence.Core.Models;

namespace TradingIntelligence.Core.Interfaces;

public interface IRealtimeNotifier
{
    Task NotifyMomentumUpdate(MomentumScoreResult result,
        CancellationToken cancellationToken = default);
}