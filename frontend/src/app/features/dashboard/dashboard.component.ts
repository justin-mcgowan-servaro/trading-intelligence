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
import { HttpClient } from '@angular/common/http';
import { MomentumSignalService, MomentumUpdate } from '../../core/services/momentum-signal.service';
import { environment } from '../../../environments/environment';
import { WatchlistService } from '../../core/services/watchlist.service';
import { AuthService } from '../../core/services/auth.service';
import { SparklineComponent } from '../../core/components/sparkline.component';

type BiasFilter = 'All' | 'Long' | 'Short' | 'Watch' | 'NoTrade';
type SortBy = 'scoreDesc' | 'scoreAsc' | 'updatedDesc' | 'ticker';

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

@Component({
  selector: 'app-dashboard',
  imports: [CommonModule, SparklineComponent],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class DashboardComponent implements OnInit, OnDestroy {
  private http = inject(HttpClient);
  signalService = inject(MomentumSignalService);
  watchlistService = inject(WatchlistService);
  authService = inject(AuthService);

  private readonly pinnedStorageKey = 'serv_dashboard_pinned_tickers';
  private refreshIntervals: ReturnType<typeof setInterval>[] = [];

  scores = signal<ScoreRow[]>([]);
  loading = signal(true);
  error = signal<string | null>(null);

  selectedTicker = signal<string | null>(null);
  tickerDetail = signal<TickerDetail | null>(null);
  tickerLoading = signal(false);
  activeTab = signal<'overview' | 'analysis' | 'history'>('overview');
  analyzing = signal(false);

  currentTime = signal(this.getSastTime());
  session = signal('Loading...');
  lastFeedUpdateTime = signal<string>('—');

  alerts = signal<MomentumAlert[]>([]);
  showAlertsPanel = signal(false);
  unreadCount = signal(0);

  scoreHistory = signal<Map<string, number[]>>(new Map());
  updatedAtEpochByTicker = signal<Map<string, number>>(new Map());

  pinnedTickers = signal<string[]>(this.readPinnedTickers());

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
  }

  ngOnInit(): void {
    this.loadScores();
    this.loadHistory();
    this.loadAlerts();
    this.signalService.connect();
    this.watchlistService.load();

    this.refreshIntervals.push(setInterval(() => this.currentTime.set(this.getSastTime()), 1000));
    this.refreshIntervals.push(setInterval(() => this.loadScores(), 5 * 60 * 1000));
    this.refreshIntervals.push(setInterval(() => this.loadAlerts(), 2 * 60 * 1000));
  }

  ngOnDestroy(): void {
    this.signalService.disconnect();
    this.refreshIntervals.forEach((intervalId) => clearInterval(intervalId));
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
            this.unreadCount.set(parsed.length);
          }
        }
      });
  }

  getHistory(ticker: string): number[] {
    return this.scoreHistory().get(ticker) ?? [];
  }

  selectTicker(ticker: string): void {
    this.selectedTicker.set(ticker);
    this.tickerDetail.set(null);
    this.tickerLoading.set(true);
    this.activeTab.set('overview');

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
          if (detail?.latest?.aiAnalysis) {
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
    this.analyzing.set(true);
    this.http.post<{ cached?: boolean; aiAnalysis?: string }>(`${environment.apiUrl}/api/momentum/${ticker}/analyze`, {})
      .subscribe({
        next: (result) => {
          if (refreshSelected && this.tickerDetail() && result.cached && result.aiAnalysis) {
            this.tickerDetail.update((detail) => {
              if (!detail) return detail;
              return { ...detail, latest: { ...detail.latest, aiAnalysis: result.aiAnalysis, hasAiAnalysis: true } };
            });
          } else if (refreshSelected) {
            setTimeout(() => {
              this.http.get<TickerDetail>(`${environment.apiUrl}/api/momentum/${ticker}`)
                .subscribe({
                  next: (detail) => this.tickerDetail.set(detail)
                });
            }, 5000);
          }
          this.analyzing.set(false);
        },
        error: () => {
          this.analyzing.set(false);
        }
      });
  }

  openAlert(alert: MomentumAlert): void {
    this.showAlertsPanel.set(false);
    this.selectTicker(alert.tickerSymbol);
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
}
