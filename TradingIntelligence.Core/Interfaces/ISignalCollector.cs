using System;
using System.Collections.Generic;
using System.Text;
using TradingIntelligence.Core.Models;

namespace TradingIntelligence.Core.Interfaces;

public interface ISignalCollector
{
    Task<IEnumerable<RawSignalEvent>> CollectAsync(CancellationToken cancellationToken = default);
}