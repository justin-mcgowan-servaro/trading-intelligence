import {
  Component, OnInit, OnDestroy, signal, computed, inject, effect,
  Injectable
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { MomentumSignalService, MomentumUpdate } from '../../core/services/momentum-signal.service';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="dashboard">

      <!-- Header -->
      <div class="header">
        <div class="header-left">
          <h1>⚡ Servaro Trading Intelligence</h1>
          <span class="session-badge" [class]="sessionClass()">
            {{ session() }}
          </span>
        </div>
        <div class="header-right">
            <span class="connection-status" [class.connected]="signalService.isConnected()">
                {{ signalService.isConnected() ? '● LIVE' : '○ CONNECTING...' }}
            </span>
            <button class="alert-bell" (click)="showAlertsPanel.set(!showAlertsPanel())">
                🔔
                @if (unreadCount() > 0) {
                <span class="alert-count">{{ unreadCount() }}</span>
                }
            </button>
            <span class="timestamp">{{ currentTime() }}</span>
        </div>
      </div>

      <!-- Stats Bar -->
      <div class="stats-bar">
        <div class="stat">
            <span class="stat-value">{{ scores().length }}</span>
            <span class="stat-label">Tickers Tracked</span>
        </div>
        <div class="stat">
            <span class="stat-value text-green">{{ longCount() }}</span>
            <span class="stat-label">LONG Signals</span>
        </div>
        <div class="stat">
            <span class="stat-value text-red">{{ shortCount() }}</span>
            <span class="stat-label">SHORT Signals</span>
        </div>
        <div class="stat">
            <span class="stat-value text-yellow">{{ watchCount() }}</span>
            <span class="stat-label">WATCH</span>
        </div>
        </div>

      <!-- Loading state -->
      @if (loading()) {
        <div class="loading">
          <div class="spinner"></div>
          <p>Loading momentum data...</p>
        </div>
      }

      <!-- Error state -->
      @if (error()) {
        <div class="error-banner">
          ⚠️ {{ error() }}
        </div>
      }

      <!-- Scores Table -->
      @if (!loading() && scores().length > 0) {
        <div class="table-container">
          <table class="scores-table">
            <thead>
              <tr>
                <th>Ticker</th>
                <th>Score</th>
                <th>Bias</th>
                <th>Reddit</th>
                <th>News</th>
                <th>Volume</th>
                <th>Sentiment</th>
                <th>Session</th>
                <th>Time (SAST)</th>
                <th>AI</th>
              </tr>
            </thead>
            <tbody>
              @for (score of scores(); track score.tickerSymbol) {
                <tr [class]="rowClass(score)" (click)="selectTicker(score.tickerSymbol)">
                  <td class="ticker-symbol">{{ score.tickerSymbol }}</td>
                  <td class="score-cell">
                    <div class="score-bar-container">
                      <div class="score-bar"
                           [style.width.%]="score.totalScore"
                           [class]="scoreBarClass(score.totalScore)">
                      </div>
                      <span class="score-value">{{ score.totalScore | number:'1.0-0' }}</span>
                    </div>
                  </td>
                  <td>
                    <span class="bias-badge" [class]="biasBadgeClass(score.tradeBias)">
                      {{ score.tradeBias }}
                    </span>
                  </td>
                  <td class="signal-score">{{ score.redditScore | number:'1.0-0' }}</td>
                  <td class="signal-score">{{ score.newsScore | number:'1.0-0' }}</td>
                  <td class="signal-score">{{ score.volumeScore | number:'1.0-0' }}</td>
                  <td class="signal-score">{{ score.sentimentScore | number:'1.0-0' }}</td>
                  <td class="session-cell">{{ score.session }}</td>
                  <td class="time-cell">{{ score.scoredAtSast }}</td>
                  <td class="ai-cell">
                    @if (score.hasAiAnalysis) {
                      <span class="ai-badge">AI ✓</span>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }

      <!-- Empty state -->
      @if (!loading() && scores().length === 0) {
        <div class="empty-state">
          <p>No momentum scores yet. Collectors run every 30 minutes.</p>
          <p>Next collection at the top of the hour.</p>
        </div>
      }

      <!-- Selected Ticker Detail -->
      @if (selectedTicker()) {
        <div class="ticker-detail" (click)="selectedTicker.set(null)">
          <div class="detail-card" (click)="$event.stopPropagation()">
            <div class="detail-header">
              <h2>{{ selectedTicker() }}</h2>
              <button (click)="selectedTicker.set(null)">✕</button>
            </div>
            @if (tickerDetail()) {
              <div class="detail-content">
                <div class="detail-score">
                  <span class="big-score">{{ tickerDetail()!.latest.totalScore | number:'1.0-0' }}</span>
                  <span class="detail-bias" [class]="biasBadgeClass(tickerDetail()!.latest.tradeBias)">
                    {{ tickerDetail()!.latest.tradeBias }}
                  </span>
                </div>
                @if (tickerDetail()!.latest.aiAnalysis) {
                  <div class="ai-analysis">
                    <h3>AI Analysis</h3>
                    <pre>{{ tickerDetail()!.latest.aiAnalysis }}</pre>
                  </div>
                }
              </div>
            }
          </div>
        </div>
      }

      <!-- Alerts Panel -->
@if (showAlertsPanel()) {
  <div class="alerts-overlay" (click)="showAlertsPanel.set(false)">
    <div class="alerts-panel" (click)="$event.stopPropagation()">
      <div class="alerts-header">
        <h3>🔔 Recent Alerts</h3>
        <button (click)="showAlertsPanel.set(false)">✕</button>
      </div>
      @if (alerts().length === 0) {
        <div class="alerts-empty">
          No alerts yet. Alerts fire when a ticker scores 60+.
        </div>
      }
      @for (alert of alerts(); track alert.tickerSymbol + alert.alertedAt) {
        <div class="alert-item" [class]="'alert-' + alert.tradeBias?.toLowerCase()">
          <div class="alert-top">
            <span class="alert-ticker">{{ alert.tickerSymbol }}</span>
            <span class="alert-score">{{ alert.totalScore }}/100</span>
            <span class="alert-bias" [class]="biasBadgeClass(alert.tradeBias)">
              {{ alert.tradeBias }}
            </span>
          </div>
          <div class="alert-bottom">
            <span class="alert-signals">{{ alert.signalSummary }}</span>
            <span class="alert-time">{{ alert.alertedAt }}</span>
          </div>
        </div>
      }
    </div>
  </div>
}
    </div>
  `,
  styles: [`
    :host { display: block; min-height: 100vh; background: #0d1117; color: #e6edf3; font-family: 'Inter', -apple-system, sans-serif; }

    .dashboard { max-width: 1400px; margin: 0 auto; padding: 24px; }

    .header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 24px; padding-bottom: 16px; border-bottom: 1px solid #21262d; }
    .header h1 { margin: 0; font-size: 24px; font-weight: 700; color: #00c2ff; }
    .header-right { display: flex; align-items: center; gap: 16px; }

    .connection-status { font-size: 13px; font-weight: 600; color: #6e7681; }
    .connection-status.connected { color: #3fb950; }

    .session-badge { padding: 4px 12px; border-radius: 20px; font-size: 12px; font-weight: 600; background: #21262d; color: #8b949e; margin-left: 12px; }
    .session-badge.open { background: #1a4731; color: #3fb950; }
    .session-badge.pre { background: #1a3a5c; color: #58a6ff; }

    .timestamp { font-size: 13px; color: #8b949e; }

    .stats-bar { display: grid; grid-template-columns: repeat(4, 1fr); gap: 16px; margin-bottom: 24px; }
    .stat { background: #161b22; border: 1px solid #21262d; border-radius: 8px; padding: 16px; text-align: center; }
    .stat-value { display: block; font-size: 28px; font-weight: 700; color: #e6edf3; }
    .stat-label { font-size: 12px; color: #8b949e; }
    .text-green { color: #3fb950 !important; }
    .text-red { color: #f85149 !important; }
    .text-yellow { color: #d29922 !important; }

    .table-container { overflow-x: auto; border-radius: 8px; border: 1px solid #21262d; }
    .scores-table { width: 100%; border-collapse: collapse; }
    .scores-table thead tr { background: #161b22; }
    .scores-table th { padding: 12px 16px; text-align: left; font-size: 12px; font-weight: 600; color: #8b949e; text-transform: uppercase; letter-spacing: 0.5px; border-bottom: 1px solid #21262d; }
    .scores-table tbody tr { border-bottom: 1px solid #21262d; cursor: pointer; transition: background 0.15s; }
    .scores-table tbody tr:hover { background: #161b22; }
    .scores-table tbody tr.strong-signal { background: rgba(63, 185, 80, 0.05); }
    .scores-table tbody tr.watch-signal { background: rgba(210, 153, 34, 0.05); }
    .scores-table td { padding: 12px 16px; font-size: 14px; }

    .ticker-symbol { font-weight: 700; font-size: 15px; color: #58a6ff; }

    .score-bar-container { position: relative; background: #21262d; border-radius: 4px; height: 24px; width: 120px; overflow: hidden; }
    .score-bar { position: absolute; left: 0; top: 0; bottom: 0; border-radius: 4px; transition: width 0.5s ease; }
    .score-bar.high { background: linear-gradient(90deg, #1a4731, #3fb950); }
    .score-bar.medium { background: linear-gradient(90deg, #3d2d00, #d29922); }
    .score-bar.low { background: linear-gradient(90deg, #1c1c1c, #6e7681); }
    .score-value { position: absolute; right: 6px; top: 50%; transform: translateY(-50%); font-size: 12px; font-weight: 700; color: #e6edf3; }

    .bias-badge { padding: 3px 10px; border-radius: 12px; font-size: 11px; font-weight: 700; text-transform: uppercase; }
    .bias-badge.long { background: #1a4731; color: #3fb950; }
    .bias-badge.short { background: #3d0f0e; color: #f85149; }
    .bias-badge.watch { background: #3d2d00; color: #d29922; }
    .bias-badge.notrade { background: #21262d; color: #6e7681; }

    .signal-score { color: #8b949e; font-size: 13px; }
    .session-cell { font-size: 12px; color: #8b949e; }
    .time-cell { font-size: 12px; color: #8b949e; white-space: nowrap; }
    .ai-badge { background: #1a3a5c; color: #58a6ff; padding: 2px 8px; border-radius: 10px; font-size: 11px; font-weight: 600; }

    .loading { display: flex; flex-direction: column; align-items: center; padding: 60px; color: #8b949e; }
    .spinner { width: 40px; height: 40px; border: 3px solid #21262d; border-top-color: #00c2ff; border-radius: 50%; animation: spin 0.8s linear infinite; margin-bottom: 16px; }
    @keyframes spin { to { transform: rotate(360deg); } }

    .error-banner { background: #3d0f0e; border: 1px solid #f85149; border-radius: 8px; padding: 16px; margin-bottom: 16px; color: #f85149; }
    .empty-state { text-align: center; padding: 60px; color: #8b949e; }

    .ticker-detail { position: fixed; inset: 0; background: rgba(0,0,0,0.8); display: flex; align-items: center; justify-content: center; z-index: 100; }
    .detail-card { background: #161b22; border: 1px solid #21262d; border-radius: 12px; width: 90%; max-width: 800px; max-height: 80vh; overflow-y: auto; }
    .detail-header { display: flex; justify-content: space-between; align-items: center; padding: 20px 24px; border-bottom: 1px solid #21262d; }
    .detail-header h2 { margin: 0; color: #58a6ff; font-size: 24px; }
    .detail-header button { background: none; border: none; color: #8b949e; font-size: 20px; cursor: pointer; }
    .detail-content { padding: 24px; }
    .detail-score { display: flex; align-items: center; gap: 16px; margin-bottom: 24px; }
    .big-score { font-size: 48px; font-weight: 700; color: #e6edf3; }
    .ai-analysis h3 { color: #8b949e; font-size: 13px; text-transform: uppercase; margin-bottom: 12px; }
    .ai-analysis pre { white-space: pre-wrap; font-family: 'Fira Code', monospace; font-size: 13px; line-height: 1.6; color: #e6edf3; background: #0d1117; padding: 16px; border-radius: 8px; border: 1px solid #21262d; }
  
    .alert-bell { position: relative; background: none; border: none; font-size: 20px; cursor: pointer; padding: 4px 8px; }
.alert-count { position: absolute; top: -4px; right: -4px; background: #f85149; color: white; border-radius: 10px; font-size: 10px; font-weight: 700; padding: 1px 5px; min-width: 16px; text-align: center; animation: pulse 1s infinite; }
@keyframes pulse { 0%, 100% { opacity: 1; } 50% { opacity: 0.6; } }

.alerts-overlay { position: fixed; inset: 0; z-index: 200; }
.alerts-panel { position: fixed; top: 0; right: 0; bottom: 0; width: 380px; background: #161b22; border-left: 1px solid #21262d; overflow-y: auto; z-index: 201; box-shadow: -4px 0 20px rgba(0,0,0,0.5); }
.alerts-header { display: flex; justify-content: space-between; align-items: center; padding: 20px; border-bottom: 1px solid #21262d; position: sticky; top: 0; background: #161b22; }
.alerts-header h3 { margin: 0; color: #e6edf3; }
.alerts-header button { background: none; border: none; color: #8b949e; font-size: 18px; cursor: pointer; }
.alerts-empty { padding: 40px 20px; text-align: center; color: #8b949e; }

.alert-item { padding: 16px 20px; border-bottom: 1px solid #21262d; transition: background 0.15s; }
.alert-item:hover { background: #1c2128; }
.alert-top { display: flex; align-items: center; gap: 10px; margin-bottom: 6px; }
.alert-ticker { font-weight: 700; font-size: 16px; color: #58a6ff; }
.alert-score { font-weight: 700; color: #e6edf3; }
.alert-bottom { display: flex; justify-content: space-between; }
.alert-signals { font-size: 12px; color: #8b949e; }
.alert-time { font-size: 11px; color: #6e7681; white-space: nowrap; }
  `]
})
export class DashboardComponent implements OnInit, OnDestroy {
  private http = inject(HttpClient);
  signalService = inject(MomentumSignalService);

  scores = signal<any[]>([]);
  loading = signal(true);
  error = signal<string | null>(null);
  selectedTicker = signal<string | null>(null);
  tickerDetail = signal<any | null>(null);
  currentTime = signal(this.getSastTime());
  session = signal('Loading...');

  sessionClass = computed(() => {
    const s = this.session();
    if (s.includes('Open')) return 'open';
    if (s.includes('Pre')) return 'pre';
    return '';
  });

  constructor() {
    // React to live SignalR updates
    effect(() => {
      const update = this.signalService.lastUpdate();
      if (!update) return;

      this.scores.update(current => {
        const idx = current.findIndex(
          s => s.tickerSymbol === update.tickerSymbol);
        const mapped = this.mapUpdate(update);
        if (idx >= 0) current[idx] = mapped;
        else current.unshift(mapped);
        return [...current].sort((a, b) => b.totalScore - a.totalScore);
      });
    });
  }

  ngOnInit(): void {
  this.loadScores();
  this.loadAlerts();
  this.signalService.connect();
  setInterval(() => this.currentTime.set(this.getSastTime()), 1000);
  // Refresh scores every 5 minutes
  setInterval(() => this.loadScores(), 5 * 60 * 1000);
  // Refresh alerts every 2 minutes
  setInterval(() => this.loadAlerts(), 2 * 60 * 1000);
}

  alerts = signal<any[]>([]);
showAlertsPanel = signal(false);
unreadCount = signal(0);
loadAlerts(): void {
  this.http.get<any[]>(`${environment.apiUrl}/api/momentum/alerts`)
    .subscribe({
      next: (data) => {
        const parsed = data.map(a => {
          try { return JSON.parse(a); } catch { return null; }
        }).filter(Boolean);
        this.alerts.set(parsed);
        this.unreadCount.set(parsed.length);
      },
      error: () => {}
    });
}
  ngOnDestroy(): void {
    this.signalService.disconnect();
  }
    longCount = computed(() =>
    this.scores().filter(s => s.tradeBias === 'Long').length);

    shortCount = computed(() =>
    this.scores().filter(s => s.tradeBias === 'Short').length);

    watchCount = computed(() =>
    this.scores().filter(s => s.tradeBias === 'Watch').length);
  loadScores(): void {
    this.http.get<any>(`${environment.apiUrl}/api/momentum/top?limit=20`)
      .subscribe({
        next: (response) => {
          this.scores.set(response.data);
          this.session.set(response.session);
          this.loading.set(false);
        },
        error: (err) => {
          this.error.set('Failed to load momentum data. Is the API running?');
          this.loading.set(false);
        }
      });
  }

  selectTicker(ticker: string): void {
    this.selectedTicker.set(ticker);
    this.http.get<any>(`${environment.apiUrl}/api/momentum/${ticker}`)
      .subscribe({
        next: (detail) => this.tickerDetail.set(detail),
        error: () => this.tickerDetail.set(null)
      });
  }

  rowClass(score: any): string {
    if (score.totalScore >= 60) return 'strong-signal';
    if (score.totalScore >= 40) return 'watch-signal';
    return '';
  }

  scoreBarClass(score: number): string {
    if (score >= 60) return 'high';
    if (score >= 40) return 'medium';
    return 'low';
  }

  biasBadgeClass(bias: string): string {
    return bias?.toLowerCase() ?? 'notrade';
  }

  private mapUpdate(update: MomentumUpdate): any {
    return {
      tickerSymbol: update.tickerSymbol,
      totalScore: update.totalScore,
      redditScore: update.redditScore,
      newsScore: update.newsScore,
      volumeScore: update.volumeScore,
      optionsScore: update.optionsScore,
      sentimentScore: update.sentimentScore,
      tradeBias: update.tradeBias,
      session: update.session,
      scoredAtSast: update.scoredAtSast,
      hasAiAnalysis: !!update.aiAnalysis
    };
  }

  private getSastTime(): string {
    return new Date().toLocaleTimeString('en-ZA', {
      timeZone: 'Africa/Johannesburg',
      hour: '2-digit', minute: '2-digit', second: '2-digit'
    }) + ' SAST';
  }
}