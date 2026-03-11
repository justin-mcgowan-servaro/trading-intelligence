using System;
using System.Collections.Generic;
using System.Text;

namespace TradingIntelligence.Core.Enums;

public enum SignalType
{
    RedditMomentum,
    NewsCatalyst,
    VolumeSpike,
    OptionsActivity,
    SocialSentiment
}

public enum TradeBias
{
    Long,
    Short,
    Watch,
    NoTrade
}

public enum MarketSession
{
    PreMarket,
    MarketOpen,
    MidSession,
    MarketClose,
    AfterHours,
    WeekendClosed
}

public enum UserTier
{
    Free,
    Pro,
    Enterprise
}
