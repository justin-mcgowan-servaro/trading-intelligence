import {
  Component, OnInit, OnDestroy, signal, computed, inject, effect
} from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { MomentumSignalService, MomentumUpdate } from '../../core/services/momentum-signal.service';
import { environment } from '../../../environments/environment';
import { WatchlistService } from '../../core/services/watchlist.service';
import { AuthService } from '../../core/services/auth.service';
import { SparklineComponent } from '../../core/components/sparkline.component';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, SparklineComponent],
  template: `
    <div class="dashboard">

      <!-- Header -->
      <div class="header">
        <div class="header-left">
          <h1>⚡ Servaro Trading Intelligence</h1>
          <span class="session-badge" [class]="sessionClass()">{{ session() }}</span>
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

      @if (loading()) {
        <div class="loading">
          <div class="spinner"></div>
          <p>Loading momentum data...</p>
        </div>
      }

      @if (error()) {
        <div class="error-banner">⚠️ {{ error() }}</div>
      }

      <!-- Scores Table -->
      @if (!loading() && scores().length > 0) {
        <div class="table-container">
          <table class="scores-table">
            <thead>
              <tr>
                <th class="star-col"></th>
                <th>Ticker</th>
                <th>Score</th>
                <th>Trend</th>
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
              @for (score of sortedScores(); track score.tickerSymbol) {
                <tr [class]="rowClass(score)" (click)="selectTicker(score.tickerSymbol)">
                  <td class="star-cell" (click)="$event.stopPropagation()">
                    @if (authService.isAuthenticated()) {
                      <button class="star-btn"
                              [class.starred]="watchlistService.isWatchlisted(score.tickerSymbol)"
                              (click)="watchlistService.toggle(score.tickerSymbol)">
                        {{ watchlistService.isWatchlisted(score.tickerSymbol) ? '★' : '☆' }}
                      </button>
                    }
                  </td>
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
                  <td class="spark-cell">
                    <app-sparkline
                      [data]="getHistory(score.tickerSymbol)"
                      [latestScore]="score.totalScore"
                      [width]="80"
                      [height]="26">
                    </app-sparkline>
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

      @if (!loading() && scores().length === 0) {
        <div class="empty-state">
          <p>No momentum scores yet. Collectors run every 30 minutes.</p>
        </div>
      }

      <!-- ═══════════════════════════════════════════════════════
           AI ANALYSIS MODAL
           ═══════════════════════════════════════════════════════ -->
      @if (selectedTicker()) {
        <div class="modal-overlay" (click)="closeModal()">
          <div class="modal-card" (click)="$event.stopPropagation()">

            <!-- Modal Header -->
            <div class="modal-header">
              <div class="modal-title-group">
                <span class="modal-ticker">{{ selectedTicker() }}</span>
                @if (tickerDetail()) {
                  <span class="modal-score-pill" [class]="scoreBarClass(tickerDetail()!.latest.totalScore)">
                    {{ tickerDetail()!.latest.totalScore | number:'1.0-0' }}/100
                  </span>
                  <span class="bias-badge" [class]="biasBadgeClass(tickerDetail()!.latest.tradeBias)">
                    {{ tickerDetail()!.latest.tradeBias }}
                  </span>
                  @if (authService.isAuthenticated() && selectedTicker()) {
                    <button class="star-btn modal-star"
                            [class.starred]="watchlistService.isWatchlisted(selectedTicker()!)"
                            (click)="watchlistService.toggle(selectedTicker()!)">
                      {{ watchlistService.isWatchlisted(selectedTicker()!) ? '★ Watchlisted' : '☆ Add to Watchlist' }}
                    </button>
                  }
                }
              </div>
              <button class="modal-close" (click)="closeModal()">✕</button>
            </div>

            <!-- Tab Bar -->
            <div class="modal-tabs">
              <button class="tab-btn" [class.active]="activeTab() === 'overview'"
                      (click)="activeTab.set('overview')">Overview</button>
              <button class="tab-btn" [class.active]="activeTab() === 'analysis'"
                      (click)="activeTab.set('analysis')">AI Analysis</button>
              <button class="tab-btn" [class.active]="activeTab() === 'history'"
                      (click)="activeTab.set('history')">History</button>
            </div>

            <!-- Modal Body -->
            <div class="modal-body">

              <!-- Loading skeleton -->
              @if (tickerLoading()) {
                <div class="modal-loading">
                  <div class="spinner"></div>
                  <p>Fetching latest data...</p>
                </div>
              }

              <!-- ── OVERVIEW TAB ── -->
              @if (!tickerLoading() && tickerDetail() && activeTab() === 'overview') {
                <div class="tab-content">

                  @if (getHistory(tickerDetail()!.latest.tickerSymbol).length > 1) {
                    <div class="section-title">Score Trend</div>
                    <div class="modal-spark-wrap">
                      <app-sparkline
                        [data]="getHistory(tickerDetail()!.latest.tickerSymbol)"
                        [latestScore]="tickerDetail()!.latest.totalScore"
                        [width]="780"
                        [height]="60">
                      </app-sparkline>
                      <div class="spark-axis">
                        <span>{{ getHistory(tickerDetail()!.latest.tickerSymbol)[0] | number:'1.0-0' }}</span>
                        <span>{{ getHistory(tickerDetail()!.latest.tickerSymbol)[getHistory(tickerDetail()!.latest.tickerSymbol).length - 1] | number:'1.0-0' }}</span>
                      </div>
                    </div>
                  }  
                  <!-- Signal Breakdown -->
                  <div class="section-title">Signal Breakdown</div>
                  <div class="signal-grid">
                    <div class="signal-row">
                      <span class="signal-label">Reddit Momentum</span>
                      <div class="signal-bar-wrap">
                        <div class="signal-bar" [style.width.%]="(tickerDetail()!.latest.redditScore / 20) * 100"
                             [class]="scoreBarClass(tickerDetail()!.latest.redditScore * 5)"></div>
                      </div>
                      <span class="signal-val">{{ tickerDetail()!.latest.redditScore | number:'1.0-0' }}/20</span>
                    </div>
                    <div class="signal-row">
                      <span class="signal-label">News Catalyst</span>
                      <div class="signal-bar-wrap">
                        <div class="signal-bar" [style.width.%]="(tickerDetail()!.latest.newsScore / 20) * 100"
                             [class]="scoreBarClass(tickerDetail()!.latest.newsScore * 5)"></div>
                      </div>
                      <span class="signal-val">{{ tickerDetail()!.latest.newsScore | number:'1.0-0' }}/20</span>
                    </div>
                    <div class="signal-row">
                      <span class="signal-label">Volume Spike</span>
                      <div class="signal-bar-wrap">
                        <div class="signal-bar" [style.width.%]="(tickerDetail()!.latest.volumeScore / 20) * 100"
                             [class]="scoreBarClass(tickerDetail()!.latest.volumeScore * 5)"></div>
                      </div>
                      <span class="signal-val">{{ tickerDetail()!.latest.volumeScore | number:'1.0-0' }}/20</span>
                    </div>
                    <div class="signal-row">
                      <span class="signal-label">Options Activity</span>
                      <div class="signal-bar-wrap">
                        <div class="signal-bar" [style.width.%]="(tickerDetail()!.latest.optionsScore / 20) * 100"
                             [class]="scoreBarClass(tickerDetail()!.latest.optionsScore * 5)"></div>
                      </div>
                      <span class="signal-val">{{ tickerDetail()!.latest.optionsScore | number:'1.0-0' }}/20</span>
                    </div>
                    <div class="signal-row">
                      <span class="signal-label">Social Sentiment</span>
                      <div class="signal-bar-wrap">
                        <div class="signal-bar" [style.width.%]="(tickerDetail()!.latest.sentimentScore / 20) * 100"
                             [class]="scoreBarClass(tickerDetail()!.latest.sentimentScore * 5)"></div>
                      </div>
                      <span class="signal-val">{{ tickerDetail()!.latest.sentimentScore | number:'1.0-0' }}/20</span>
                    </div>
                  </div>

                  <!-- Signal Summary -->
                  @if (tickerDetail()!.latest.signalSummary) {
                    <div class="section-title" style="margin-top:20px">Signal Summary</div>
                    <div class="signal-summary-text">{{ tickerDetail()!.latest.signalSummary }}</div>
                  }

                  <!-- Buffer Status -->
                  <div class="section-title" style="margin-top:20px">Live Buffer</div>
                  <div class="buffer-info">
                    <span class="buffer-count">{{ tickerDetail()!.currentBuffer.signalCount }} signals buffered</span>
                    <div class="buffer-types">
                      @for (type of tickerDetail()!.currentBuffer.signalTypes; track type) {
                        <span class="buffer-tag">{{ type }}</span>
                      }
                    </div>
                  </div>

                  <!-- Scored At -->
                  <div class="scored-at">
                    Last scored: {{ tickerDetail()!.latest.scoredAtSast }}
                    · Session: {{ tickerDetail()!.latest.session }}
                  </div>
                </div>
              }

              <!-- ── AI ANALYSIS TAB ── -->
              @if (!tickerLoading() && tickerDetail() && activeTab() === 'analysis') {
                <div class="tab-content">
                  @if (tickerDetail()!.latest.aiAnalysis) {
                    <div class="ai-analysis-block">
                      <pre class="ai-pre">{{ tickerDetail()!.latest.aiAnalysis }}</pre>
                    </div>
                  } @else {
                    <div class="no-analysis">
                      <div class="no-analysis-icon">🤖</div>
                      <p>No AI analysis available yet.</p>
                      <p class="no-analysis-sub">
                        AI analysis fires automatically when a ticker scores 60+.
                        Current score: <strong>{{ tickerDetail()!.latest.totalScore | number:'1.0-0' }}/100</strong>
                      </p>
                      @if (tickerDetail()!.latest.totalScore >= 60) {
                        <button class="analyze-btn" [disabled]="analyzing()"
                                (click)="triggerAnalysis()">
                          {{ analyzing() ? 'Triggering...' : '⚡ Trigger Analysis Now' }}
                        </button>
                      }
                    </div>
                  }
                </div>
              }

              <!-- ── HISTORY TAB ── -->
              @if (!tickerLoading() && tickerDetail() && activeTab() === 'history') {
                <div class="tab-content">
                  <table class="history-table">
                    <thead>
                      <tr>
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
                      @for (h of tickerDetail()!.history; track h.id) {
                        <tr>
                          <td><strong>{{ h.totalScore | number:'1.0-0' }}</strong></td>
                          <td><span class="bias-badge" [class]="biasBadgeClass(h.tradeBias)">{{ h.tradeBias }}</span></td>
                          <td class="signal-score">{{ h.redditScore | number:'1.0-0' }}</td>
                          <td class="signal-score">{{ h.newsScore | number:'1.0-0' }}</td>
                          <td class="signal-score">{{ h.volumeScore | number:'1.0-0' }}</td>
                          <td class="signal-score">{{ h.sentimentScore | number:'1.0-0' }}</td>
                          <td class="session-cell">{{ h.session }}</td>
                          <td class="time-cell">{{ h.scoredAtSast }}</td>
                          <td>
                            @if (h.aiAnalysis) {
                              <span class="ai-badge">AI ✓</span>
                            }
                          </td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </div>
              }

            </div>
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
              <div class="alerts-empty">No alerts yet. Alerts fire when a ticker scores 60+.</div>
            }
            @for (alert of alerts(); track alert.tickerSymbol + alert.alertedAt) {
              <div class="alert-item" [class]="'alert-' + alert.tradeBias?.toLowerCase()">
                <div class="alert-top">
                  <span class="alert-ticker">{{ alert.tickerSymbol }}</span>
                  <span class="alert-score">{{ alert.totalScore }}/100</span>
                  <span class="alert-bias" [class]="biasBadgeClass(alert.tradeBias)">{{ alert.tradeBias }}</span>
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

    /* ── Header ── */
    .header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 24px; padding-bottom: 16px; border-bottom: 1px solid #21262d; }
    .header h1 { margin: 0; font-size: 24px; font-weight: 700; color: #00c2ff; }
    .header-right { display: flex; align-items: center; gap: 16px; }
    .connection-status { font-size: 13px; font-weight: 600; color: #6e7681; }
    .connection-status.connected { color: #3fb950; }
    .session-badge { padding: 4px 12px; border-radius: 20px; font-size: 12px; font-weight: 600; background: #21262d; color: #8b949e; margin-left: 12px; }
    .session-badge.open { background: #1a4731; color: #3fb950; }
    .session-badge.pre { background: #1a3a5c; color: #58a6ff; }
    .timestamp { font-size: 13px; color: #8b949e; }

    /* ── Stats ── */
    .stats-bar { display: grid; grid-template-columns: repeat(4, 1fr); gap: 16px; margin-bottom: 24px; }
    .stat { background: #161b22; border: 1px solid #21262d; border-radius: 8px; padding: 16px; text-align: center; }
    .stat-value { display: block; font-size: 28px; font-weight: 700; color: #e6edf3; }
    .stat-label { font-size: 12px; color: #8b949e; }
    .text-green { color: #3fb950 !important; }
    .text-red { color: #f85149 !important; }
    .text-yellow { color: #d29922 !important; }

    /* ── Table ── */
    .table-container { overflow-x: auto; border-radius: 8px; border: 1px solid #21262d; }
    .scores-table { width: 100%; border-collapse: collapse; }
    .scores-table thead tr { background: #161b22; }
    .scores-table th { padding: 12px 16px; text-align: left; font-size: 12px; font-weight: 600; color: #8b949e; text-transform: uppercase; letter-spacing: 0.5px; border-bottom: 1px solid #21262d; }
    .scores-table tbody tr { border-bottom: 1px solid #21262d; cursor: pointer; transition: background 0.15s; }
    .scores-table tbody tr:hover { background: #161b22; }
    .scores-table tbody tr.strong-signal { background: rgba(63, 185, 80, 0.05); }
    .scores-table tbody tr.watch-signal { background: rgba(210, 153, 34, 0.05); }
    .scores-table td { padding: 12px 16px; font-size: 14px; }
    . { font-weight: 700; font-size: 15px; color: #58a6ff; }
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
    .ai-cell { text-align: center; }

    /* ── Loading / Error / Empty ── */
    .loading { display: flex; flex-direction: column; align-items: center; padding: 60px; color: #8b949e; }
    .spinner { width: 40px; height: 40px; border: 3px solid #21262d; border-top-color: #00c2ff; border-radius: 50%; animation: spin 0.8s linear infinite; margin-bottom: 16px; }
    @keyframes spin { to { transform: rotate(360deg); } }
    .error-banner { background: #3d0f0e; border: 1px solid #f85149; border-radius: 8px; padding: 16px; margin-bottom: 16px; color: #f85149; }
    .empty-state { text-align: center; padding: 60px; color: #8b949e; }

    /* ── Modal ── */
    .modal-overlay { position: fixed; inset: 0; background: rgba(0,0,0,0.85); display: flex; align-items: center; justify-content: center; z-index: 100; backdrop-filter: blur(4px); }
    .modal-card { background: #161b22; border: 1px solid #30363d; border-radius: 12px; width: 92%; max-width: 860px; max-height: 85vh; display: flex; flex-direction: column; overflow: hidden; box-shadow: 0 24px 64px rgba(0,0,0,0.6); }

    .modal-header { display: flex; justify-content: space-between; align-items: center; padding: 20px 24px; border-bottom: 1px solid #21262d; flex-shrink: 0; }
    .modal-title-group { display: flex; align-items: center; gap: 12px; }
    .modal-ticker { font-size: 26px; font-weight: 800; color: #58a6ff; letter-spacing: 1px; }
    .modal-score-pill { padding: 4px 14px; border-radius: 20px; font-size: 14px; font-weight: 700; }
    .modal-score-pill.high { background: #1a4731; color: #3fb950; }
    .modal-score-pill.medium { background: #3d2d00; color: #d29922; }
    .modal-score-pill.low { background: #21262d; color: #6e7681; }
    .modal-close { background: none; border: none; color: #6e7681; font-size: 20px; cursor: pointer; padding: 4px 8px; border-radius: 4px; transition: color 0.15s; }
    .modal-close:hover { color: #e6edf3; }

    .modal-tabs { display: flex; gap: 0; border-bottom: 1px solid #21262d; flex-shrink: 0; padding: 0 24px; }
    .tab-btn { background: none; border: none; color: #8b949e; font-size: 14px; font-weight: 500; padding: 12px 16px; cursor: pointer; border-bottom: 2px solid transparent; margin-bottom: -1px; transition: color 0.15s; }
    .tab-btn:hover { color: #e6edf3; }
    .tab-btn.active { color: #58a6ff; border-bottom-color: #58a6ff; font-weight: 600; }

    .modal-body { flex: 1; overflow-y: auto; padding: 24px; }
    .modal-loading { display: flex; flex-direction: column; align-items: center; padding: 40px; color: #8b949e; }

    /* ── Overview Tab ── */
    .section-title { font-size: 11px; font-weight: 700; color: #8b949e; text-transform: uppercase; letter-spacing: 0.8px; margin-bottom: 12px; }
    .signal-grid { display: flex; flex-direction: column; gap: 10px; }
    .signal-row { display: grid; grid-template-columns: 160px 1fr 60px; align-items: center; gap: 12px; }
    .signal-label { font-size: 13px; color: #8b949e; }
    .signal-bar-wrap { background: #21262d; border-radius: 4px; height: 8px; overflow: hidden; }
    .signal-bar { height: 100%; border-radius: 4px; transition: width 0.5s ease; }
    .signal-val { font-size: 13px; font-weight: 600; color: #e6edf3; text-align: right; }
    .signal-summary-text { font-size: 13px; color: #8b949e; background: #0d1117; padding: 12px; border-radius: 6px; border: 1px solid #21262d; }
    .buffer-info { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
    .buffer-count { font-size: 13px; color: #8b949e; }
    .buffer-types { display: flex; gap: 6px; flex-wrap: wrap; }
    .buffer-tag { background: #1a3a5c; color: #58a6ff; padding: 2px 8px; border-radius: 10px; font-size: 11px; font-weight: 600; }
    .scored-at { margin-top: 20px; font-size: 12px; color: #6e7681; }

    /* ── AI Analysis Tab ── */
    .ai-analysis-block { }
    .ai-pre { white-space: pre-wrap; font-family: 'Fira Code', 'Cascadia Code', monospace; font-size: 12.5px; line-height: 1.65; color: #e6edf3; background: #0d1117; padding: 20px; border-radius: 8px; border: 1px solid #21262d; margin: 0; }
    .no-analysis { display: flex; flex-direction: column; align-items: center; padding: 40px 20px; text-align: center; color: #8b949e; }
    .no-analysis-icon { font-size: 48px; margin-bottom: 16px; }
    .no-analysis-sub { font-size: 13px; margin-top: 8px; }
    .analyze-btn { margin-top: 20px; background: #1a3a5c; color: #58a6ff; border: 1px solid #58a6ff; border-radius: 8px; padding: 10px 24px; font-size: 14px; font-weight: 600; cursor: pointer; transition: background 0.15s; }
    .analyze-btn:hover:not(:disabled) { background: #1f4a7a; }
    .analyze-btn:disabled { opacity: 0.5; cursor: not-allowed; }

    /* ── History Tab ── */
    .history-table { width: 100%; border-collapse: collapse; font-size: 13px; }
    .history-table th { padding: 8px 12px; text-align: left; font-size: 11px; font-weight: 600; color: #8b949e; text-transform: uppercase; border-bottom: 1px solid #21262d; }
    .history-table td { padding: 10px 12px; border-bottom: 1px solid #21262d; }
    .history-table tbody tr:hover { background: #1c2128; }

    /* ── Alerts Panel ── */
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

    .star-col { width: 40px; }
.star-cell { padding: 0 8px !important; }
.star-btn {
  background: none;
  border: none;
  font-size: 18px;
  color: #6e7681;
  cursor: pointer;
  padding: 4px;
  line-height: 1;
  transition: color 0.15s, transform 0.1s;
}
.star-btn:hover { color: #d29922; transform: scale(1.2); }
.star-btn.starred { color: #d29922; }

.modal-star {
  font-size: 13px;
  padding: 5px 14px;
  border-radius: 20px;
  border: 1px solid #30363d !important;
  color: #8b949e !important;
  background: #21262d !important;
  font-weight: 600;
}
.modal-star.starred {
  color: #d29922 !important;
  border-color: #d29922 !important;
  background: #3d2d00 !important;
}
.modal-star:hover { opacity: 0.85; transform: none !important; }

/* Pinned row highlight */
.scores-table tbody tr.pinned-row {
  border-left: 2px solid #d29922;
}
.spark-cell { padding: 8px 16px !important; }
.modal-spark-wrap { background: #0d1117; border: 1px solid #21262d; border-radius: 8px; padding: 12px; margin-bottom: 20px; }
.modal-spark-wrap app-sparkline { width: 100%; display: block; }
.spark-axis { display: flex; justify-content: space-between; font-size: 11px; color: #6e7681; margin-top: 4px; padding: 0 3px; }
  `]
})
export class DashboardComponent implements OnInit, OnDestroy {
  private http = inject(HttpClient);
  signalService = inject(MomentumSignalService);
  watchlistService = inject(WatchlistService);
  authService = inject(AuthService);

  scores = signal<any[]>([]);
  loading = signal(true);
  error = signal<string | null>(null);
  selectedTicker = signal<string | null>(null);
  tickerDetail = signal<any | null>(null);
  tickerLoading = signal(false);
  activeTab = signal<'overview' | 'analysis' | 'history'>('overview');
  analyzing = signal(false);
  currentTime = signal(this.getSastTime());
  session = signal('Loading...');
  alerts = signal<any[]>([]);
  showAlertsPanel = signal(false);
  unreadCount = signal(0);
  scoreHistory = signal<Map<string, number[]>>(new Map());
  
  sessionClass = computed(() => {
    const s = this.session();
    if (s.includes('Open')) return 'open';
    if (s.includes('Pre')) return 'pre';
    return '';
  });

  longCount = computed(() => this.scores().filter(s => s.tradeBias === 'Long').length);
  shortCount = computed(() => this.scores().filter(s => s.tradeBias === 'Short').length);
  watchCount = computed(() => this.scores().filter(s => s.tradeBias === 'Watch').length);

  constructor() {
    effect(() => {
      const update = this.signalService.lastUpdate();
      if (!update) return;
    
      // Update scores table
      this.scores.update(current => {
        const idx = current.findIndex(s => s.tickerSymbol === update.tickerSymbol);
        const mapped = this.mapUpdate(update);
        if (idx >= 0) current[idx] = mapped;
        else current.unshift(mapped);
        return [...current].sort((a, b) => b.totalScore - a.totalScore);
      });
    
      // Append to sparkline history (keep last 20 points)
      this.scoreHistory.update(map => {
        const next = new Map(map);
        const existing = next.get(update.tickerSymbol) ?? [];
        next.set(update.tickerSymbol, [...existing, update.totalScore].slice(-20));
        return next;
      });
    });
  }

  ngOnInit(): void {
    this.loadScores();
    this.loadHistory();
    this.loadAlerts();
    this.signalService.connect();
    this.watchlistService.load();
    setInterval(() => this.currentTime.set(this.getSastTime()), 1000);
    setInterval(() => this.loadScores(), 5 * 60 * 1000);
    setInterval(() => this.loadAlerts(), 2 * 60 * 1000);
  }

  ngOnDestroy(): void {
    this.signalService.disconnect();
  }

  loadHistory(): void {
    this.http.get<Record<string, number[]>>(`${environment.apiUrl}/api/momentum/history`)
      .subscribe({
        next: (data) => {
          this.scoreHistory.update(map => {
            const next = new Map(map);
            for (const [ticker, scores] of Object.entries(data)) {
              // Merge with any live points already accumulated
              const existing = next.get(ticker) ?? [];
              const merged = [...scores, ...existing]
                .slice(-20);
              next.set(ticker, merged);
            }
            return next;
          });
        },
        error: () => {}
      });
  }
  
  loadScores(): void {
    this.http.get<any>(`${environment.apiUrl}/api/momentum/top?limit=20`)
      .subscribe({
        next: (response) => {
          this.scores.set(response.data);
          this.session.set(response.session);
          this.loading.set(false);
          // Seed history — initial load gives us the latest score per ticker
          // History will grow as SignalR updates arrive
          this.scoreHistory.update(map => {
            const next = new Map(map);
            for (const s of response.data) {
              if (!next.has(s.tickerSymbol)) {
                next.set(s.tickerSymbol, [s.totalScore]);
              }
            }
            return next;
          });
        },
        error: () => {
          this.error.set('Failed to load momentum data. Is the API running?');
          this.loading.set(false);
        }
      });
  }

  getHistory(ticker: string): number[] {
    return this.scoreHistory().get(ticker) ?? [];
  }
  
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

  selectTicker(ticker: string): void {
    this.selectedTicker.set(ticker);
    this.tickerDetail.set(null);
    this.tickerLoading.set(true);
    this.activeTab.set('overview');

    this.http.get<any>(`${environment.apiUrl}/api/momentum/${ticker}`)
      .subscribe({
        next: (detail) => {
          this.tickerDetail.set(detail);
          // Seed sparkline from DB history if we have richer data
          if (detail?.history?.length > 1) {
            this.scoreHistory.update(map => {
              const next = new Map(map);
              const dbPoints = [...detail.history]
                .reverse() // history comes back newest-first
                .map((h: any) => Number(h.totalScore));
              const existing = next.get(ticker) ?? [];
              // DB points as base, live SignalR points on top
              const merged = [...dbPoints, ...existing].slice(-20);
              next.set(ticker, merged);
              return next;
            });
          }
          this.tickerLoading.set(false);
          // Auto-switch to analysis tab if AI analysis exists
          if (detail?.latest?.aiAnalysis) {
            this.activeTab.set('analysis');
          }
        },
        error: () => {
          this.tickerLoading.set(false);
        }
      });
  }

  closeModal(): void {
    this.selectedTicker.set(null);
    this.tickerDetail.set(null);
    this.analyzing.set(false);
  }

  triggerAnalysis(): void {
    const ticker = this.selectedTicker();
    if (!ticker) return;

    this.analyzing.set(true);
    this.http.post<any>(`${environment.apiUrl}/api/momentum/${ticker}/analyze`, {})
      .subscribe({
        next: (result) => {
          if (result.cached && result.aiAnalysis) {
            // Got cached analysis back — update the detail directly
            this.tickerDetail.update(d => ({
              ...d,
              latest: { ...d.latest, aiAnalysis: result.aiAnalysis }
            }));
          } else {
            // Analysis triggered async — poll once after 5s
            setTimeout(() => {
              this.http.get<any>(`${environment.apiUrl}/api/momentum/${ticker}`)
                .subscribe({
                  next: (detail) => this.tickerDetail.set(detail),
                  error: () => {}
                });
            }, 5000);
          }
          this.analyzing.set(false);
        },
        error: () => this.analyzing.set(false)
      });
  }

  rowClass(score: any): string {
    const pinned = this.watchlistService.isWatchlisted(score.tickerSymbol)
      ? 'pinned-row ' : '';
    if (score.totalScore >= 60) return pinned + 'strong-signal';
    if (score.totalScore >= 40) return pinned + 'watch-signal';
    return pinned;
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
