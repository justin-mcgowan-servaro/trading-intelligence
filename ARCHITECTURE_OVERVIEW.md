# Servaro Trading Intelligence — Architecture Overview

## 1) Project Structure (meaningful architecture view)

```text
/trading-intelligence
├─ TradingIntelligence.slnx
├─ TradingIntelligence.Api/                    # ASP.NET Core API host
│  ├─ Program.cs                               # DI, middleware, Quartz, SignalR
│  ├─ Controllers/
│  │  ├─ AuthController.cs                     # OTP auth + JWT
│  │  ├─ MomentumController.cs                 # momentum data + AI trigger/status
│  │  ├─ TradesController.cs                   # paper/broker trades + accuracy
│  │  ├─ WatchlistController.cs                # user watchlist APIs
│  │  └─ DevToolsController.cs                 # dev-only replay/trigger endpoints
│  ├─ Hubs/
│  │  └─ MomentumHub.cs                        # SignalR hub for live updates
│  └─ Services/
│     └─ SignalRNotifier.cs                    # IRealtimeNotifier -> hub broadcast
│
├─ TradingIntelligence.Core/                   # domain contracts + entities
│  ├─ Entities/
│  │  ├─ MomentumScore.cs
│  │  ├─ SignalEvent.cs
│  │  ├─ PaperTrade.cs
│  │  ├─ BrokerTrade.cs
│  │  ├─ SignalAccuracy.cs
│  │  ├─ User.cs
│  │  ├─ Watchlist.cs
│  │  ├─ OtpCode.cs
│  │  └─ Ticker.cs
│  ├─ Enums/                                   # SignalType, TradeBias, statuses, etc.
│  ├─ Interfaces/                              # service contracts
│  ├─ Models/                                  # DTO/result models (RawSignalEvent, etc.)
│  └─ Prompts/
│     └─ SystemPrompts.cs                      # OpenAI system prompt content
│
├─ TradingIntelligence.Infrastructure/         # integrations + background processing
│  ├─ Data/
│  │  └─ AppDbContext.cs                       # EF Core PostgreSQL model + mappings
│  ├─ Collectors/
│  │  ├─ RedditCollector.cs
│  │  ├─ StockTwitsCollector.cs
│  │  ├─ NewsCollector.cs
│  │  ├─ NewsApiCollector.cs
│  │  ├─ VolumeCollector.cs
│  │  ├─ PolygonCollector.cs
│  │  ├─ OptionsCollector.cs
│  │  ├─ FearGreedCollector.cs
│  │  └─ GoogleTrendsCollector.cs
│  ├─ Services/
│  │  ├─ SignalAggregatorService.cs            # raw-signals -> scored-signals
│  │  ├─ MomentumScoringService.cs             # scores + OpenAI + SignalR + Telegram
│  │  ├─ TelegramAlertService.cs               # Telegram alerts + Redis alert cache
│  │  ├─ PaperTradeService.cs                  # auto paper trading + evaluation
│  │  ├─ PolygonPriceService.cs                # market price lookup
│  │  ├─ Mt5BridgeService.cs                   # API client to mt5 bridge microservice
│  │  └─ BrevoEmailService.cs                  # OTP email delivery
│  ├─ Jobs/
│  │  ├─ RedditCollectorJob.cs
│  │  ├─ StockTwitsCollectorJob.cs
│  │  ├─ NewsCollectorJob.cs
│  │  ├─ NewsApiCollectorJob.cs
│  │  ├─ VolumeCollectorJob.cs
│  │  ├─ PolygonCollectorJob.cs
│  │  ├─ OptionsCollectorJob.cs
│  │  ├─ FearGreedCollectorJob.cs
│  │  ├─ GoogleTrendsCollectorJob.cs (registered but currently commented in Program)
│  │  ├─ MorningBriefingJob.cs
│  │  ├─ PaperTradeEvaluatorJob.cs
│  │  ├─ BrokerSyncJob.cs
│  │  └─ OtpCleanupJob.cs
│  └─ Helpers/
│     ├─ MarketSessionHelper.cs
│     ├─ SentimentAnalyser.cs
│     └─ TickerExtractor.cs
│
├─ TradingIntelligence.Tests/                  # xUnit test project (minimal currently)
│
├─ frontend/                                   # Angular frontend app
│  ├─ src/main.ts                              # Angular bootstrap
│  └─ src/app/
│     ├─ app.routes.ts                         # route map + lazy paper-trades route
│     ├─ core/
│     │  ├─ guards/auth.guard.ts
│     │  ├─ interceptors/auth.interceptor.ts
│     │  ├─ services/
│     │  │  ├─ auth.service.ts
│     │  │  ├─ momentum-signal.service.ts      # SignalR client
│     │  │  ├─ watchlist.service.ts
│     │  │  └─ paper-trade-workbench.service.ts
│     │  └─ components/sparkline.component.ts
│     └─ features/
│        ├─ auth/auth.component.ts             # OTP login flow
│        ├─ dashboard/                         # primary workspace
│        │  ├─ dashboard.component.ts/html/scss
│        │  └─ components/
│        │     ├─ dashboard-toolbar/
│        │     ├─ trade-candidates-panel/
│        │     ├─ watchlist-intelligence-panel/
│        │     ├─ opportunity-review-panel/
│        │     ├─ ticker-detail-modal/
│        │     └─ alerts-panel/
│        └─ paper-trade-workbench/
│           └─ paper-trade-workbench.component.ts/html/scss
│
├─ mt5_bridge/
│  ├─ main.py                                  # FastAPI wrapper for MetaTrader5
│  └─ requirements.txt
│
├─ docker-compose.yml                          # postgres + redis + api + frontend + nginx
├─ TradingIntelligence.Api/Dockerfile
├─ frontend/Dockerfile
└─ mt5_bridge/Dockerfile
```

## 2) System Entry Points

### Backend entry points
- `TradingIntelligence.Api/Program.cs` is the API process entry point, wiring EF Core, Redis, JWT auth, CORS, SignalR, Quartz jobs, hosted services, and endpoint mapping.
- Hosted services started by DI:
  - `SignalAggregatorService` (Redis `raw-signals` subscriber)
  - `MomentumScoringService` (Redis `scored-signals` subscriber)
- SignalR hub endpoint:
  - `MomentumHub` mapped at `/hubs/momentum`.
- Quartz jobs are also bootstrapped in `Program.cs` and run collectors, cleanup, trade evaluation/sync, and briefing jobs.

### Frontend entry points
- Angular bootstrap starts at `frontend/src/main.ts` via `bootstrapApplication(App, appConfig)`.
- Router entry map in `frontend/src/app/app.routes.ts`:
  - `/login` -> `AuthComponent`
  - `/` -> `DashboardComponent` (guarded)
  - `/paper-trades` -> lazy-loaded `PaperTradeWorkbenchComponent` (guarded)
- The dashboard (`dashboard.component.ts`) acts as the main live intelligence workspace.

## 3) High-Level System Architecture

### Signal collection -> aggregation -> scoring flow
1. Quartz jobs run data collectors on schedules (Reddit, StockTwits, RSS/NewsAPI, Polygon, Options, etc.).
2. Collectors normalize events into `RawSignalEvent` and publish JSON to Redis channel `raw-signals`.
3. `SignalAggregatorService` consumes `raw-signals`, validates/extracts tickers, buffers 24h signals in memory per ticker, and publishes ticker symbols to `scored-signals` (cooldown-protected).
4. `MomentumScoringService` consumes `scored-signals`, pulls buffered signals, computes component scores and total momentum score, derives trade bias, then persists `MomentumScore` rows into PostgreSQL.

### AI analysis generation
- Automatic AI analysis is attempted in `MomentumScoringService` only when total score meets minimum threshold (`MinScoreForAi = 60`).
- OpenAI calls use `SystemPrompts.TradingIntelligence` plus a generated structured payload.
- Manual AI trigger path exists via `POST /api/momentum/{ticker}/analyze`, which creates a Redis-tracked analysis job and delegates to `GenerateManualAnalysisAsync`.

### Alerts and frontend live updates
- Scoring service sends:
  - SignalR updates (`IRealtimeNotifier` implemented by `SignalRNotifier`) to all clients and ticker groups.
  - Telegram alerts through `TelegramAlertService`.
  - Alert cache snapshots into Redis for `/api/momentum/alerts`.
- Angular dashboard combines REST polling with SignalR live updates (`MomentumSignalService`) and user state (watchlist, review status, pins).

### Technology interaction map (observed from code)
- **ASP.NET API**: orchestration host for controllers, auth, background services, SignalR, Quartz.
- **PostgreSQL**: primary persistent store via EF Core (`AppDbContext`) for users, watchlists, scores, trades, OTP codes, etc.
- **Redis**: pub/sub bus (`raw-signals`, `scored-signals`, `scored-results`) + cache (alerts, fear/greed, analysis jobs, dedup keys).
- **SignalR**: server push channel for momentum updates to Angular.
- **Angular**: authenticated UI for dashboard, watchlist, analysis triggering, paper-trade views.
- **OpenAI**: analysis and morning briefing generation where configured.
- **Polygon**: collector data and price lookups used in scoring/trade evaluation.
- **Telegram**: outbound score alerts + morning briefing delivery.
- **MT5 bridge (FastAPI)**: separate Python microservice used by .NET `Mt5BridgeService` for live broker order/position operations.

## 4) Backend Endpoint -> Feature Mapping

| Endpoint | Purpose | Used by / likely consumer |
|---|---|---|
| `GET /api/health` | Service health + market session + aggregator buffer summary | Ops checks / diagnostics |
| `POST /api/auth/request-otp` | Generate + email OTP (rate-limited) | Angular login screen |
| `POST /api/auth/verify-otp` | Verify OTP, upsert user, issue JWT | Angular login screen |
| `DELETE /api/auth/cleanup-otp` | Remove expired/used OTP records | Internal cleanup (also mirrored by Quartz job) |
| `GET /api/momentum/top` | Latest top-scoring tickers | Dashboard main table/panels |
| `GET /api/momentum/{ticker}` | Latest + history + live buffer summary per ticker | Dashboard ticker detail modal |
| `GET /api/momentum/history` | score-history series per ticker | Dashboard sparklines/charts |
| `GET /api/momentum/buffer` | Current in-memory raw buffer summary | Diagnostics / likely dashboard tooling |
| `GET /api/momentum/alerts` | Recent cached Telegram alerts | Dashboard notifications panel |
| `POST /api/momentum/{ticker}/analyze` | Trigger manual AI analysis job | Dashboard “Analyze” action |
| `GET /api/momentum/analysis-jobs/{jobId}` | Poll manual analysis job state | Dashboard analysis polling |
| `GET /api/watchlist` | Fetch current user watchlist (+ latest score enrichment) | Dashboard watchlist UX |
| `POST /api/watchlist` | Add ticker to watchlist | Dashboard watchlist UX |
| `DELETE /api/watchlist/{ticker}` | Remove ticker from watchlist | Dashboard watchlist UX |
| `GET /api/trades/paper` | Paged paper-trade list | Paper Trade Workbench |
| `GET /api/trades/paper/open` | Open paper trades | Paper Trade Workbench |
| `GET /api/trades/paper/{id}` | Single paper trade details | Paper Trade Workbench |
| `POST /api/trades/paper/{id}/close` | Manual close of an open paper trade | Paper Trade Workbench |
| `GET /api/trades/accuracy` | Signal accuracy leaderboard | Paper Trade Workbench |
| `GET /api/trades/accuracy/{ticker}` | Ticker-specific accuracy | Paper Trade Workbench / detail |
| `GET /api/trades/broker` | Paged linked broker trades | Paper Trade Workbench |
| `GET /api/trades/broker/open` | Open broker-linked trades | Paper Trade Workbench |
| `POST /api/trades/broker/{id}/close` | Manually close broker trade via MT5 bridge | Paper Trade Workbench / admin ops |
| `POST /api/dev/trigger-score/{ticker}` | Dev-only publish to `scored-signals` | Local dev testing |
| `POST /api/dev/replay-latest-alert/{ticker}` | Dev-only resend latest score alert | Local dev testing |
| `POST /api/dev/replay-latest-papertrade/{ticker}` | Dev-only retry auto paper-trade path | Local dev testing |
| `GET /api/dev/latest-score/{ticker}` | Dev-only latest score snapshot | Local dev testing |

## 5) Frontend Feature Map

### Main pages and responsibilities
1. **Auth page (`/login`)**
   - Handles OTP request + OTP verify.
   - Stores JWT and user info through `AuthService`.

2. **Dashboard (`/`)**
   - Primary intelligence workspace:
     - live score table,
     - watchlist intelligence,
     - alerts panel,
     - opportunity review workflow,
     - ticker detail modal,
     - AI analysis trigger/polling.
   - Composes dedicated UI panels:
     - `dashboard-toolbar`
     - `trade-candidates-panel`
     - `watchlist-intelligence-panel`
     - `opportunity-review-panel`
     - `ticker-detail-modal`
     - `alerts-panel`

3. **Paper Trade Workbench (`/paper-trades`)**
   - Displays open/all paper trades and accuracy metrics.
   - Supports paper trade operational review (likely purpose based on code structure).

### Frontend service usage
- `AuthService`: OTP auth and token lifecycle.
- `MomentumSignalService`: SignalR connection + live updates from `/hubs/momentum`.
- `WatchlistService`: load/toggle watchlist via `/api/watchlist` endpoints.
- `PaperTradeWorkbenchService`: fetch paper trades + accuracy data from `/api/trades/*`.
- `authGuard` and `authInterceptor`: route protection and Bearer token injection.

## 6) Important Background Processes

### Always-on hosted services
- **SignalAggregatorService**
  - Trigger: application startup.
  - Reads Redis `raw-signals`, maintains 24h in-memory ticker buffers, emits `scored-signals` when ticker is eligible.
- **MomentumScoringService**
  - Trigger: application startup.
  - Reads Redis `scored-signals`, calculates momentum scores, persists DB records, optionally calls OpenAI, pushes SignalR updates, sends Telegram alerts.

### Quartz scheduled jobs (registered in Program.cs)
- Collector jobs: StockTwits, NewsAPI, Fear&Greed, Polygon, Reddit, RSS News, Volume, Options (GoogleTrends currently commented out).
- `MorningBriefingJob`: daily 04:00 UTC; builds OpenAI-generated daily briefing and sends to Telegram.
- `OtpCleanupJob`: hourly OTP cleanup.
- `PaperTradeEvaluatorJob`: startup + hourly evaluation of open paper trades against thresholds.
- `BrokerSyncJob`: startup + hourly sync of broker trades and linked paper-trade outcomes via MT5 bridge.

## 7) Core Data Entities

- **MomentumScore**: computed per-ticker score snapshot with component scores, bias, AI analysis text, session, timestamp.
- **SignalEvent**: normalized persisted signal record (source, type, sentiment, raw payload).
- **Ticker**: allowed/known symbols and metadata; also used to validate extracted ticker mentions.
- **User**: authenticated platform user (email, tier, active/confirmed flags).
- **Watchlist**: user-to-ticker watch mapping with alert preferences.
- **OtpCode**: OTP verification records with expiry/attempt tracking.
- **PaperTrade**: simulated trade generated from momentum signals, with lifecycle + PnL + outcome.
- **BrokerTrade**: optional real broker execution linkage for a paper trade via MT5 ticket/status.
- **SignalAccuracy**: aggregate performance metrics per ticker from closed/expired paper trades.

## 8) Final System Overview (new developer quick-start)

Servaro Trading Intelligence is a multi-project system that ingests market/social/news signals, scores ticker momentum, optionally enriches high-confidence setups with AI analysis, and distributes those results to both a real-time dashboard and Telegram alerts.

The core flow is:
1. Scheduled collectors fetch external data and publish normalized raw signal events to Redis.
2. The aggregator service buffers these events per ticker and signals when a ticker should be scored.
3. The scoring service computes weighted momentum components, writes `MomentumScore` to PostgreSQL, optionally generates OpenAI analysis, and broadcasts updates through SignalR.
4. Trade automation logic can create and evaluate paper trades, and optionally synchronize linked broker trades through the MT5 bridge service.
5. The Angular frontend authenticates via OTP/JWT, consumes REST + SignalR data, and provides operational workspaces (dashboard, watchlist intelligence, review workflows, and paper-trade workbench).

Redis is used as both an event bus and short-term cache/state layer; PostgreSQL is the system of record. SignalR carries low-latency push updates to the UI, while REST endpoints support querying, actions (analysis/watchlist/trade operations), and auth flows.
