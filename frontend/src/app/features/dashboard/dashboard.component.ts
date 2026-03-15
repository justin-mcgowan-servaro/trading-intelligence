import {
  ChangeDetectionStrategy,
  Component,
  OnDestroy,
  OnInit,
  computed,
  effect,
  inject,
  signal
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { MomentumSignalService, MomentumUpdate } from '../../core/services/momentum-signal.service';
import { environment } from '../../../environments/environment';
import { WatchlistService } from '../../core/services/watchlist.service';
import { AuthService } from '../../core/services/auth.service';
import { SparklineComponent } from '../../core/components/sparkline.component';

type BiasFilter = 'All' | 'Long' | 'Short' | 'Watch' | 'NoTrade';
type SortBy = 'scoreDesc' | 'scoreAsc' | 'updatedDesc' | 'ticker';
type ReviewStatus = 'New' | 'Reviewing' | 'Watching' | 'Ready' | 'Archived';
type ReviewFilter = 'All' | 'Reviewing' | 'Ready';

interface ScoreRow {
  tickerSymbol: string;
  totalScore: number;
  redditScore: number;
  newsScore: number;
  volumeScore: number;
  optionsScore: number;
  sentimentScore: number;
  tradeBias: 'Long' | 'Short' | 'Watch' | 'NoTrade';
  confidence?: string;
  signalSummary?: string;
  aiAnalysis?: string;
  hasAiAnalysis?: boolean;
  session: string;
  scoredAtSast: string;
}

interface TickerDetail {
  latest: ScoreRow;
  history: Array<ScoreRow & { id: number; aiAnalysis?: string }>;
  currentBuffer: {
    signalCount: number;
    signalTypes: string[];
  };
}

interface MomentumAlert {
  tickerSymbol: string;
  totalScore: number;
  tradeBias: ScoreRow['tradeBias'];
  signalSummary?: string;
  alertedAt?: string;
}

type AnalysisStatus = 'idle' | 'triggering' | 'processing' | 'completed' | 'timeout' | 'error';

interface AnalysisJobStatusResponse {
  jobId: string;
  ticker: string;
  status: 'processing' | 'completed';
  hasAnalysis?: boolean;
}

interface DashboardNotification {
  id: string;
  type: 'alert' | 'ai-ready';
  tickerSymbol: string;
  title: string;
  message: string;
  time: string;
  createdAtEpoch: number;
  tradeBias?: ScoreRow['tradeBias'];
  totalScore?: number;
}

interface ReviewedTickerRecord {
  tickerSymbol: string;
  status: ReviewStatus;
  note: string;
  addedAt: string;
  snapshot: ScoreRow | null;
}

interface ReviewedTickerView {
  tickerSymbol: string;
  status: ReviewStatus;
  note: string;
  addedAt: string;
  score: ScoreRow | null;
}

@Component({
  selector: 'app-dashboard',
  imports: [CommonModule, SparklineComponent, RouterLink],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class DashboardComponent implements OnInit, OnDestroy {
  private http = inject(HttpClient);
  private route = inject(ActivatedRoute);
  signalService = inject(MomentumSignalService);
  watchlistService = inject(WatchlistService);
  authService = inject(AuthService);

  private readonly pinnedStorageKey = 'serv_dashboard_pinned_tickers';
  private readonly reviewStorageKey = 'serv_dashboard_review_records';
  readonly reviewStatuses: ReviewStatus[] = ['New', 'Reviewing', 'Watching', 'Ready', 'Archived'];
  private refreshIntervals: ReturnType<typeof setInterval>[] = [];

  scores = signal<ScoreRow[]>([]);
  loading = signal(true);
  error = signal<string | null>(null);

  selectedTicker = signal<string | null>(null);
  tickerDetail = signal<TickerDetail | null>(null);
  tickerLoading = signal(false);
  activeTab = signal<'overview' | 'analysis' | 'history'>('overview');
  analyzing = signal(false);
  analysisStatusByTicker = signal<Record<string, AnalysisStatus>>({});
  analysisErrorByTicker = signal<Record<string, string>>({});
  private analysisPollingByTicker = new Map<string, ReturnType<typeof setInterval>>();
  private analysisTimeoutByTicker = new Map<string, ReturnType<typeof setTimeout>>();
  private analysisJobIdByTicker = signal<Record<string, string>>({});
  private aiNotificationKeys = signal<Set<string>>(new Set());
  aiNotifications = signal<DashboardNotification[]>([]);

  currentTime = signal(this.getSastTime());
  session = signal('Loading...');
  lastFeedUpdateTime = signal<string>('—');

  alerts = signal<MomentumAlert[]>([]);
  showAlertsPanel = signal(false);
  unreadCount = signal(0);

  notifications = computed(() => {
    const scoreAlerts = this.alerts().map((alert, index) => this.toAlertNotification(alert, index));
    return [...this.aiNotifications(), ...scoreAlerts]
      .sort((a, b) => b.createdAtEpoch - a.createdAtEpoch)
      .slice(0, 50);
  });

  scoreHistory = signal<Map<string, number[]>>(new Map());
  updatedAtEpochByTicker = signal<Map<string, number>>(new Map());

  pinnedTickers = signal<string[]>(this.readPinnedTickers());
  reviewRecords = signal<Record<string, ReviewedTickerRecord>>(this.readReviewRecords());
  reviewFilter = signal<ReviewFilter>('All');

  searchTerm = signal('');
  biasFilter = signal<BiasFilter>('All');
  minScoreFilter = signal(0);
  watchlistOnly = signal(false);
  sortBy = signal<SortBy>('scoreDesc');

  sessionClass = computed(() => {
    const currentSession = this.session();
    if (currentSession.includes('Open')) return 'open';
    if (currentSession.includes('Pre')) return 'pre';
    return '';
  });

  filteredScores = computed(() => {
    const query = this.searchTerm().trim().toUpperCase();
    const bias = this.biasFilter();
    const minScore = this.minScoreFilter();
    const watchlistOnly = this.watchlistOnly();
    const watchlisted = this.watchlistService.watchlisted();
    const sort = this.sortBy();

    const filtered = this.scores().filter((score) => {
      if (query && !score.tickerSymbol.includes(query)) return false;
      if (bias !== 'All' && score.tradeBias !== bias) return false;
      if (score.totalScore < minScore) return false;
      if (watchlistOnly && !watchlisted.has(score.tickerSymbol)) return false;
      return true;
    });

    return [...filtered].sort((a, b) => {
      if (sort === 'ticker') return a.tickerSymbol.localeCompare(b.tickerSymbol);
      if (sort === 'scoreAsc') return a.totalScore - b.totalScore;
      if (sort === 'updatedDesc') return this.getTickerUpdatedEpoch(b.tickerSymbol) - this.getTickerUpdatedEpoch(a.tickerSymbol);

      const aWatched = watchlisted.has(a.tickerSymbol) ? 1 : 0;
      const bWatched = watchlisted.has(b.tickerSymbol) ? 1 : 0;
      if (aWatched !== bWatched) return bWatched - aWatched;
      return b.totalScore - a.totalScore;
    });
  });

  triageSummary = computed(() => {
    const visible = this.filteredScores();
    const watchlisted = this.watchlistService.watchlisted();

    return {
      visible: visible.length,
      longs: visible.filter((score) => score.tradeBias === 'Long').length,
      shorts: visible.filter((score) => score.tradeBias === 'Short').length,
      watch: visible.filter((score) => score.tradeBias === 'Watch').length,
      watchlistHits: visible.filter((score) => watchlisted.has(score.tickerSymbol)).length
    };
  });

  strongLongs = computed(() => this.scores().filter((score) => score.totalScore >= 60 && score.tradeBias === 'Long').slice(0, 6));
  strongShorts = computed(() => this.scores().filter((score) => score.totalScore >= 60 && score.tradeBias === 'Short').slice(0, 6));
  onWatch = computed(() =>
    this.scores()
      .filter((score) => score.tradeBias === 'Watch' || (score.totalScore >= 45 && score.totalScore < 60))
      .slice(0, 6)
  );

  watchlistCandidates = computed(() => {
    const watchlisted = this.watchlistService.watchlisted();
    return this.scores()
      .filter((score) => watchlisted.has(score.tickerSymbol))
      .sort((a, b) => b.totalScore - a.totalScore)
      .slice(0, 8);
  });

  recentMovers = computed(() => {
    const now = Date.now();
    return this.scores()
      .filter((score) => now - this.getTickerUpdatedEpoch(score.tickerSymbol) < 15 * 60 * 1000)
      .sort((a, b) => this.getTickerUpdatedEpoch(b.tickerSymbol) - this.getTickerUpdatedEpoch(a.tickerSymbol))
      .slice(0, 8);
  });

  needsReview = computed(() => {
    const now = Date.now();
    return this.scores()
      .filter((score) => (score.totalScore >= 60 && !score.hasAiAnalysis) || now - this.getTickerUpdatedEpoch(score.tickerSymbol) < 5 * 60 * 1000)
      .slice(0, 8);
  });

  watchlistIntelligence = computed(() => {
    const watchlisted = this.watchlistService.watchlisted();
    return this.scores()
      .filter((score) => watchlisted.has(score.tickerSymbol))
      .sort((a, b) => {
        const scoreDelta = b.totalScore - a.totalScore;
        if (scoreDelta !== 0) return scoreDelta;
        return this.getTickerUpdatedEpoch(b.tickerSymbol) - this.getTickerUpdatedEpoch(a.tickerSymbol);
      });
  });

  systemStats = computed(() => ({
    visibleTickers: this.filteredScores().length,
    pinnedCount: this.pinnedTickers().length,
    watchlistCount: this.watchlistService.watchlisted().size,
    highConviction: this.scores().filter((score) => score.totalScore >= 60).length
  }));

  reviewedTickerViews = computed(() => {
    const records = this.reviewRecords();
    const liveScores = new Map(this.scores().map((score) => [score.tickerSymbol, score]));

    return Object.values(records)
      .map((record): ReviewedTickerView => ({
        tickerSymbol: record.tickerSymbol,
        status: record.status,
        note: record.note,
        addedAt: record.addedAt,
        score: liveScores.get(record.tickerSymbol) ?? record.snapshot
      }))
      .sort((a, b) => {
        const scoreDelta = (b.score?.totalScore ?? -1) - (a.score?.totalScore ?? -1);
        if (scoreDelta !== 0) return scoreDelta;
        return a.tickerSymbol.localeCompare(b.tickerSymbol);
      });
  });

  visibleReviewedTickers = computed(() => {
    const filter = this.reviewFilter();
    const views = this.reviewedTickerViews();
    if (filter === 'All') return views;
    return views.filter((item) => item.status === filter);
  });

  reviewCounts = computed(() => {
    const counts: Record<ReviewStatus, number> = {
      New: 0,
      Reviewing: 0,
      Watching: 0,
      Ready: 0,
      Archived: 0
    };

    for (const item of this.reviewedTickerViews()) {
      counts[item.status] += 1;
    }

    return counts;
  });

  reviewedByStatus = computed(() => {
    const grouped = new Map<ReviewStatus, ReviewedTickerView[]>();
    for (const status of this.reviewStatuses) {
      grouped.set(status, []);
    }

    for (const item of this.visibleReviewedTickers()) {
      grouped.get(item.status)?.push(item);
    }

    return grouped;
  });

  constructor() {
    effect(() => {
      const update = this.signalService.lastUpdate();
      if (!update) return;

      const mapped = this.mapUpdate(update);
      this.applyIncomingScore(mapped);
      this.bumpTickerUpdateTime(mapped.tickerSymbol);
      this.lastFeedUpdateTime.set(this.getSastTime());
    });

    effect(() => {
      if (this.showAlertsPanel()) {
        this.unreadCount.set(0);
      }
    });

    effect(() => {
      localStorage.setItem(this.pinnedStorageKey, JSON.stringify(this.pinnedTickers()));
    });

    effect(() => {
      localStorage.setItem(this.reviewStorageKey, JSON.stringify(this.reviewRecords()));
    });
  }

  ngOnInit(): void {
    this.loadScores();
    this.loadHistory();
    this.loadAlerts();
    this.signalService.connect();
    this.watchlistService.load();

    const tickerFromQuery = this.route.snapshot.queryParamMap.get('ticker');
    if (tickerFromQuery) {
      this.selectTicker(tickerFromQuery.toUpperCase());
    }

    this.refreshIntervals.push(setInterval(() => this.currentTime.set(this.getSastTime()), 1000));
    this.refreshIntervals.push(setInterval(() => this.loadScores(), 5 * 60 * 1000));
    this.refreshIntervals.push(setInterval(() => this.loadAlerts(), 2 * 60 * 1000));
  }

  ngOnDestroy(): void {
    this.signalService.disconnect();
    this.refreshIntervals.forEach((intervalId) => clearInterval(intervalId));
    this.stopAllAnalysisMonitoring();
  }

  loadHistory(): void {
    this.http.get<Record<string, number[]>>(`${environment.apiUrl}/api/momentum/history`)
      .subscribe({
        next: (data) => {
          this.scoreHistory.update((map) => {
            const next = new Map(map);
            for (const [ticker, history] of Object.entries(data)) {
              next.set(ticker, [...history, ...(next.get(ticker) ?? [])].slice(-20));
            }
            return next;
          });
        }
      });
  }

  loadScores(): void {
    this.http.get<{ data: ScoreRow[]; session: string }>(`${environment.apiUrl}/api/momentum/top?limit=50`)
      .subscribe({
        next: (response) => {
          this.scores.set(response.data);
          this.session.set(response.session);
          this.loading.set(false);
          this.lastFeedUpdateTime.set(this.getSastTime());

          this.scoreHistory.update((map) => {
            const next = new Map(map);
            for (const score of response.data) {
              if (!next.has(score.tickerSymbol)) {
                next.set(score.tickerSymbol, [score.totalScore]);
              }
            }
            return next;
          });

          this.updatedAtEpochByTicker.update((map) => {
            const next = new Map(map);
            const now = Date.now();
            response.data.forEach((score) => next.set(score.tickerSymbol, now));
            return next;
          });
        },
        error: () => {
          this.error.set('Failed to load momentum data. Is the API running?');
          this.loading.set(false);
        }
      });
  }

  loadAlerts(): void {
    this.http.get<string[]>(`${environment.apiUrl}/api/momentum/alerts`)
      .subscribe({
        next: (data) => {
          const parsed = data
            .map((alertJson) => {
              try {
                return JSON.parse(alertJson) as MomentumAlert;
              } catch {
                return null;
              }
            })
            .filter((alert): alert is MomentumAlert => !!alert);

          this.alerts.set(parsed);
          if (!this.showAlertsPanel()) {
            this.unreadCount.set(parsed.length + this.aiNotifications().length);
          }
        }
      });
  }

  getHistory(ticker: string): number[] {
    return this.scoreHistory().get(ticker) ?? [];
  }

  selectTicker(ticker: string, preferredTab: 'overview' | 'analysis' | 'history' = 'overview'): void {
    this.selectedTicker.set(ticker);
    this.tickerDetail.set(null);
    this.tickerLoading.set(true);
    this.activeTab.set(preferredTab);

    this.http.get<TickerDetail>(`${environment.apiUrl}/api/momentum/${ticker}`)
      .subscribe({
        next: (detail) => {
          this.tickerDetail.set(detail);
          if (detail?.history?.length > 1) {
            const dbPoints = [...detail.history].reverse().map((h) => Number(h.totalScore));
            this.scoreHistory.update((map) => {
              const next = new Map(map);
              next.set(ticker, [...dbPoints, ...(next.get(ticker) ?? [])].slice(-20));
              return next;
            });
          }
          this.tickerLoading.set(false);
          if (detail?.latest?.aiAnalysis && preferredTab === 'overview') {
            this.activeTab.set('analysis');
          }
        },
        error: () => {
          this.tickerLoading.set(false);
        }
      });
  }

  onRowKeydown(event: KeyboardEvent, ticker: string): void {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      this.selectTicker(ticker);
    }
  }

  closeModal(): void {
    this.selectedTicker.set(null);
    this.tickerDetail.set(null);
    this.analyzing.set(false);
  }

  triggerAnalysis(): void {
    const ticker = this.selectedTicker();
    if (!ticker) return;
    this.triggerAnalysisForTicker(ticker, true);
  }

  triggerAnalysisForTicker(ticker: string, refreshSelected = false): void {
    if (this.isAnalysisBusy(ticker)) return;

    this.setAnalysisState(ticker, 'triggering');
    this.analysisErrorByTicker.update((errors) => ({
      ...errors,
      [ticker]: ''
    }));
    this.analyzing.set(true);
    this.http.post<{ cached?: boolean; aiAnalysis?: string; analysisJobId?: string }>(`${environment.apiUrl}/api/momentum/${ticker}/analyze`, {})
      .subscribe({
        next: (result) => {
          if (result.cached && result.aiAnalysis) {
            this.markAnalysisCompleted(ticker, result.aiAnalysis, refreshSelected);
          } else {
            this.setAnalysisState(ticker, 'processing');
            if (result.analysisJobId) {
              this.analysisJobIdByTicker.update((jobs) => ({ ...jobs, [ticker]: result.analysisJobId! }));
            }
            this.startAnalysisMonitoring(ticker, refreshSelected, result.analysisJobId);
          }
          this.analyzing.set(false);
        },
        error: () => {
          this.setAnalysisState(ticker, 'error', 'Analysis could not be started. Please try again shortly.');
          this.analyzing.set(false);
        }
      });
  }

  openNotification(notification: DashboardNotification): void {
    this.showAlertsPanel.set(false);
    this.selectTicker(notification.tickerSymbol, notification.type === 'ai-ready' ? 'analysis' : 'overview');
  }

  resetFilters(): void {
    this.searchTerm.set('');
    this.biasFilter.set('All');
    this.minScoreFilter.set(0);
    this.watchlistOnly.set(false);
    this.sortBy.set('scoreDesc');
  }

  setSearchTerm(value: string): void {
    this.searchTerm.set(value);
  }

  setMinScore(value: string): void {
    this.minScoreFilter.set(Number(value) || 0);
  }

  setSortBy(value: string): void {
    this.sortBy.set(value as SortBy);
  }

  setBias(value: string): void {
    this.biasFilter.set(value as BiasFilter);
  }

  togglePin(ticker: string): void {
    this.pinnedTickers.update((current) => {
      if (current.includes(ticker)) {
        return current.filter((item) => item !== ticker);
      }
      return [ticker, ...current].slice(0, 25);
    });
  }

  addTickerToReview(ticker: string, status: ReviewStatus = 'New'): void {
    const liveScore = this.scores().find((score) => score.tickerSymbol === ticker) ?? null;
    this.reviewRecords.update((records) => {
      const existing = records[ticker];
      return {
        ...records,
        [ticker]: {
          tickerSymbol: ticker,
          status,
          note: existing?.note ?? '',
          addedAt: existing?.addedAt ?? new Date().toISOString(),
          snapshot: liveScore ?? existing?.snapshot ?? null
        }
      };
    });
  }

  setReviewStatus(ticker: string, status: string): void {
    const nextStatus = this.toReviewStatus(status);
    if (!nextStatus) return;
    this.reviewRecords.update((records) => {
      const current = records[ticker];
      if (!current) return records;
      return {
        ...records,
        [ticker]: {
          ...current,
          status: nextStatus
        }
      };
    });
  }

  updateReviewNote(ticker: string, note: string): void {
    this.reviewRecords.update((records) => {
      const current = records[ticker];
      if (!current) return records;
      return {
        ...records,
        [ticker]: {
          ...current,
          note
        }
      };
    });
  }

  removeFromReview(ticker: string): void {
    this.reviewRecords.update((records) => {
      if (!records[ticker]) return records;
      const next = { ...records };
      delete next[ticker];
      return next;
    });
  }

  isReviewed(ticker: string): boolean {
    return !!this.reviewRecords()[ticker];
  }
  setReviewFilter(value: string): void {
    if (value === 'All' || value === 'Reviewing' || value === 'Ready') {
      this.reviewFilter.set(value);
    }
  }

  isPinned(ticker: string): boolean {
    return this.pinnedTickers().includes(ticker);
  }

  rowClass(score: ScoreRow): string {
    const classes = [
      score.totalScore >= 60 ? 'strong-signal' : '',
      score.totalScore >= 40 && score.totalScore < 60 ? 'watch-signal' : '',
      this.watchlistService.isWatchlisted(score.tickerSymbol) ? 'watchlisted-row' : '',
      this.selectedTicker() === score.tickerSymbol ? 'selected-row' : '',
      this.isPinned(score.tickerSymbol) ? 'pinned-row' : ''
    ].filter(Boolean);

    return classes.join(' ');
  }

  scoreBarClass(score: number): string {
    if (score >= 60) return 'high';
    if (score >= 40) return 'medium';
    return 'low';
  }

  biasBadgeClass(bias: string): string {
    return bias?.toLowerCase() ?? 'notrade';
  }

  scoreBand(score: number): string {
    if (score >= 75) return 'A+ conviction';
    if (score >= 60) return 'A conviction';
    if (score >= 45) return 'B setup';
    return 'Needs validation';
  }

  isDevelopmentMode(): boolean {
    return !environment.production;
  }

  getAnalysisStatus(ticker: string | null): AnalysisStatus {
    if (!ticker) return 'idle';
    return this.analysisStatusByTicker()[ticker] ?? 'idle';
  }

  isAnalysisBusy(ticker: string): boolean {
    const status = this.getAnalysisStatus(ticker);
    return status === 'triggering' || status === 'processing';
  }

  analysisStatusMessage(ticker: string | null): string {
    const status = this.getAnalysisStatus(ticker);
    if (status === 'triggering') return 'Triggering analysis...';
    if (status === 'processing') return 'Analysis in progress. Please wait...';
    if (status === 'timeout') return 'Analysis did not complete yet. Please try again shortly.';
    if (status === 'error') return this.analysisErrorByTicker()[ticker ?? ''] || 'Analysis could not be started. Please try again shortly.';
    return '';
  }

  analyzeButtonLabel(ticker: string | null): string {
    const status = this.getAnalysisStatus(ticker);
    if (status === 'triggering') return 'Triggering...';
    if (status === 'processing') return 'Analyzing...';
    return '⚡ Trigger Analysis Now';
  }

  private applyIncomingScore(score: ScoreRow): void {
    this.scores.update((current) => {
      const index = current.findIndex((item) => item.tickerSymbol === score.tickerSymbol);
      if (index >= 0) {
        const next = [...current];
        next[index] = { ...next[index], ...score };
        return next;
      }
      return [score, ...current];
    });

    this.scoreHistory.update((map) => {
      const next = new Map(map);
      const existing = next.get(score.tickerSymbol) ?? [];
      next.set(score.tickerSymbol, [...existing, score.totalScore].slice(-20));
      return next;
    });

    if (score.aiAnalysis && this.isAnalysisBusy(score.tickerSymbol)) {
      this.markAnalysisCompleted(score.tickerSymbol, score.aiAnalysis, this.selectedTicker() === score.tickerSymbol);
    }
  }

  private mapUpdate(update: MomentumUpdate): ScoreRow {
    return {
      tickerSymbol: update.tickerSymbol,
      totalScore: update.totalScore,
      redditScore: update.redditScore,
      newsScore: update.newsScore,
      volumeScore: update.volumeScore,
      optionsScore: update.optionsScore,
      sentimentScore: update.sentimentScore,
      tradeBias: update.tradeBias,
      confidence: update.confidence,
      signalSummary: update.signalSummary,
      aiAnalysis: update.aiAnalysis,
      hasAiAnalysis: !!update.aiAnalysis,
      session: update.session,
      scoredAtSast: update.scoredAtSast
    };
  }

  private getSastTime(): string {
    return `${new Date().toLocaleTimeString('en-ZA', {
      timeZone: 'Africa/Johannesburg',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit'
    })} SAST`;
  }


  private toReviewStatus(value: string): ReviewStatus | null {
    const match = this.reviewStatuses.find((status) => status === value);
    return match ?? null;
  }

  private readPinnedTickers(): string[] {
    try {
      const raw = localStorage.getItem(this.pinnedStorageKey);
      if (!raw) return [];
      const parsed = JSON.parse(raw) as unknown;
      if (!Array.isArray(parsed)) return [];
      return parsed.filter((item): item is string => typeof item === 'string');
    } catch {
      return [];
    }
  }

  private readReviewRecords(): Record<string, ReviewedTickerRecord> {
    try {
      const raw = localStorage.getItem(this.reviewStorageKey);
      if (!raw) return {};
      const parsed = JSON.parse(raw) as unknown;
      if (!parsed || typeof parsed !== 'object') return {};

      const validEntries = Object.entries(parsed as Record<string, unknown>)
        .filter(([, value]) => this.isValidReviewRecord(value))
        .map(([ticker, value]) => [ticker, value as ReviewedTickerRecord]);

      return Object.fromEntries(validEntries);
    } catch {
      return {};
    }
  }

  private isValidReviewRecord(value: unknown): value is ReviewedTickerRecord {
    if (!value || typeof value !== 'object') return false;
    const candidate = value as Partial<ReviewedTickerRecord>;
    return typeof candidate.tickerSymbol === 'string'
      && typeof candidate.note === 'string'
      && typeof candidate.addedAt === 'string'
      && typeof candidate.status === 'string'
      && this.reviewStatuses.includes(candidate.status as ReviewStatus);
  }

  private getTickerUpdatedEpoch(ticker: string): number {
    return this.updatedAtEpochByTicker().get(ticker) ?? 0;
  }

  private bumpTickerUpdateTime(ticker: string): void {
    this.updatedAtEpochByTicker.update((map) => {
      const next = new Map(map);
      next.set(ticker, Date.now());
      return next;
    });
  }

  private setAnalysisState(ticker: string, status: AnalysisStatus, errorMessage = ''): void {
    this.analysisStatusByTicker.update((current) => ({
      ...current,
      [ticker]: status
    }));

    if (status !== 'error' && status !== 'timeout') {
      this.analysisErrorByTicker.update((errors) => ({ ...errors, [ticker]: '' }));
    } else {
      this.analysisErrorByTicker.update((errors) => ({ ...errors, [ticker]: errorMessage }));
    }
  }

  private startAnalysisMonitoring(ticker: string, refreshSelected: boolean, analysisJobId?: string): void {
    this.stopAnalysisMonitoring(ticker);
    const poll = () => this.pollAnalysisStatus(ticker, refreshSelected, analysisJobId);
    poll();

    const pollId = setInterval(poll, 4000);
    this.analysisPollingByTicker.set(ticker, pollId);

    const timeoutId = setTimeout(() => {
      this.stopAnalysisMonitoring(ticker);
      if (this.isAnalysisBusy(ticker)) {
        this.setAnalysisState(ticker, 'timeout', 'Analysis did not complete yet. Please try again shortly.');
      }
    }, 90000);

    this.analysisTimeoutByTicker.set(ticker, timeoutId);
  }

  private pollAnalysisStatus(ticker: string, refreshSelected: boolean, analysisJobId?: string): void {
    if (analysisJobId) {
      this.http.get<AnalysisJobStatusResponse>(`${environment.apiUrl}/api/momentum/analysis-jobs/${analysisJobId}`)
        .subscribe({
          next: (status) => {
            if (status.status === 'completed') {
              this.fetchTickerForAnalysisCompletion(ticker, refreshSelected);
              return;
            }

            this.fetchTickerForAnalysisCompletion(ticker, refreshSelected);
          },
          error: () => {
            this.fetchTickerForAnalysisCompletion(ticker, refreshSelected);
          }
        });
      return;
    }

    this.fetchTickerForAnalysisCompletion(ticker, refreshSelected);
  }

  private fetchTickerForAnalysisCompletion(ticker: string, refreshSelected: boolean): void {
    this.http.get<TickerDetail>(`${environment.apiUrl}/api/momentum/${ticker}`)
      .subscribe({
        next: (detail) => {
          if (refreshSelected && this.selectedTicker() === ticker) {
            this.tickerDetail.set(detail);
          }

          if (detail.latest.aiAnalysis) {
            this.markAnalysisCompleted(ticker, detail.latest.aiAnalysis, refreshSelected, detail);
          }
        }
      });
  }

  private markAnalysisCompleted(ticker: string, aiAnalysis: string, refreshSelected: boolean, detail?: TickerDetail): void {
    this.stopAnalysisMonitoring(ticker);
    this.setAnalysisState(ticker, 'completed');

    if (detail) {
      if (refreshSelected && this.selectedTicker() === ticker) {
        this.tickerDetail.set(detail);
      }
    } else if (refreshSelected && this.selectedTicker() === ticker) {
      this.tickerDetail.update((current) => {
        if (!current) return current;
        return {
          ...current,
          latest: { ...current.latest, aiAnalysis, hasAiAnalysis: true }
        };
      });
    }

    this.scores.update((scores) => scores.map((score) => (
      score.tickerSymbol === ticker
        ? { ...score, aiAnalysis, hasAiAnalysis: true }
        : score
    )));

    this.addAiReadyNotification(ticker);
  }

  private addAiReadyNotification(ticker: string): void {
    const selected = this.scores().find((score) => score.tickerSymbol === ticker);
    const time = this.getSastTime();
    const key = `${ticker}:${selected?.scoredAtSast ?? time}`;
    if (this.aiNotificationKeys().has(key)) return;

    this.aiNotificationKeys.update((current) => {
      const next = new Set(current);
      next.add(key);
      return next;
    });

    const notification: DashboardNotification = {
      id: `ai-ready:${key}`,
      type: 'ai-ready',
      tickerSymbol: ticker,
      title: `AI analysis ready for ${ticker}`,
      message: 'Tap to open AI Analysis tab.',
      time,
      createdAtEpoch: Date.now(),
      tradeBias: selected?.tradeBias,
      totalScore: selected?.totalScore
    };

    this.aiNotifications.update((current) => [notification, ...current].slice(0, 30));
    if (!this.showAlertsPanel()) {
      this.unreadCount.update((count) => count + 1);
    }
  }

  private toAlertNotification(alert: MomentumAlert, index: number): DashboardNotification {
    return {
      id: `alert:${alert.tickerSymbol}:${alert.alertedAt ?? index}`,
      type: 'alert',
      tickerSymbol: alert.tickerSymbol,
      title: `${alert.tickerSymbol} scored ${alert.totalScore}/100`,
      message: alert.signalSummary || 'No summary provided',
      time: alert.alertedAt || '',
      createdAtEpoch: Date.parse(alert.alertedAt || '') || 0,
      tradeBias: alert.tradeBias,
      totalScore: alert.totalScore
    };
  }

  private stopAnalysisMonitoring(ticker: string): void {
    this.analysisJobIdByTicker.update((jobs) => {
      const next = { ...jobs };
      delete next[ticker];
      return next;
    });

    const pollId = this.analysisPollingByTicker.get(ticker);
    if (pollId) {
      clearInterval(pollId);
      this.analysisPollingByTicker.delete(ticker);
    }

    const timeoutId = this.analysisTimeoutByTicker.get(ticker);
    if (timeoutId) {
      clearTimeout(timeoutId);
      this.analysisTimeoutByTicker.delete(ticker);
    }
  }

  private stopAllAnalysisMonitoring(): void {
    for (const ticker of this.analysisPollingByTicker.keys()) {
      this.stopAnalysisMonitoring(ticker);
    }
  }
}
