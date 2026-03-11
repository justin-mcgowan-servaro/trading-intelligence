using System;
using System.Collections.Generic;
using System.Text;
using TradingIntelligence.Core.Models;

namespace TradingIntelligence.Core.Interfaces;

public interface IAlertService
{
    Task SendMomentumAlertAsync(MomentumScoreResult score, CancellationToken cancellationToken = default);
    Task SendDailyReportAsync(IEnumerable<MomentumScoreResult> topScores, CancellationToken cancellationToken = default);
}
