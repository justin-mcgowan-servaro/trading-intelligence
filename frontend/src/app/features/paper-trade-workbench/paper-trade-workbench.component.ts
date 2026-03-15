import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { forkJoin } from 'rxjs';
import { RouterLink } from '@angular/router';
import {
  PaperTrade,
  PaperTradeWorkbenchService,
  SignalAccuracy
} from '../../core/services/paper-trade-workbench.service';

type TradeViewFilter = 'all' | 'open' | 'closed';
type DirectionFilter = 'all' | 'long' | 'short';

@Component({
  selector: 'app-paper-trade-workbench',
  imports: [CommonModule, DecimalPipe, RouterLink],
  templateUrl: './paper-trade-workbench.component.html',
  styleUrl: './paper-trade-workbench.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PaperTradeWorkbenchComponent {
  private readonly workbenchService = inject(PaperTradeWorkbenchService);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly openTrades = signal<PaperTrade[]>([]);
  readonly allRecentTrades = signal<PaperTrade[]>([]);
  readonly accuracyByTicker = signal<Map<string, SignalAccuracy>>(new Map());

  readonly tradeViewFilter = signal<TradeViewFilter>('all');
  readonly directionFilter = signal<DirectionFilter>('all');
  readonly tickerSearch = signal('');

  readonly closedTrades = computed(() =>
    this.allRecentTrades()
      .filter((trade) => this.tradeStatus(trade.status) !== 'Open')
      .slice(0, 15)
  );

  readonly wins = computed(() =>
    this.closedTrades().filter((trade) => this.tradeOutcome(trade.outcome) === 'Win').length
  );

  readonly losses = computed(() =>
    this.closedTrades().filter((trade) => this.tradeOutcome(trade.outcome) === 'Loss').length
  );

  readonly avgWinRate = computed(() => {
    const rows = Array.from(this.accuracyByTicker().values());
    if (!rows.length) return null;
    const total = rows.reduce((sum, row) => sum + Number(row.winRate || 0), 0);
    return total / rows.length;
  });

  readonly filteredOpenTrades = computed(() => this.applyFilters(this.openTrades()));
  readonly filteredClosedTrades = computed(() => this.applyFilters(this.closedTrades()));

  constructor() {
    this.loadWorkbench();
  }

  loadWorkbench(): void {
    this.loading.set(true);
    this.error.set(null);

    forkJoin({
      openTrades: this.workbenchService.getOpenPaperTrades(),
      recentTrades: this.workbenchService.getPaperTrades(1, 120),
      accuracy: this.workbenchService.getSignalAccuracy()
    }).subscribe({
      next: ({ openTrades, recentTrades, accuracy }) => {
        this.openTrades.set(openTrades);
        this.allRecentTrades.set(recentTrades);
        this.accuracyByTicker.set(new Map(accuracy.map((row) => [row.tickerSymbol, row])));
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Paper trade workbench is unavailable right now. Please retry shortly.');
        this.loading.set(false);
      }
    });
  }

  setTradeViewFilter(value: string): void {
    if (value === 'open' || value === 'closed' || value === 'all') {
      this.tradeViewFilter.set(value);
    }
  }

  setDirectionFilter(value: string): void {
    if (value === 'long' || value === 'short' || value === 'all') {
      this.directionFilter.set(value);
    }
  }

  setTickerSearch(value: string): void {
    this.tickerSearch.set(value);
  }

  tradeStatus(status: number | string): 'Open' | 'Closed' | 'Expired' | 'Unknown' {
    if (status === 0 || status === 'Open') return 'Open';
    if (status === 1 || status === 'Closed') return 'Closed';
    if (status === 2 || status === 'Expired') return 'Expired';
    return 'Unknown';
  }

  tradeDirection(direction: number | string): 'Long' | 'Short' | 'Unknown' {
    if (direction === 0 || direction === 'Long') return 'Long';
    if (direction === 1 || direction === 'Short') return 'Short';
    return 'Unknown';
  }

  tradeOutcome(outcome: number | string): 'Pending' | 'Win' | 'Loss' | 'Breakeven' | 'Unknown' {
    if (outcome === 0 || outcome === 'Pending') return 'Pending';
    if (outcome === 1 || outcome === 'Win') return 'Win';
    if (outcome === 2 || outcome === 'Loss') return 'Loss';
    if (outcome === 3 || outcome === 'Breakeven') return 'Breakeven';
    return 'Unknown';
  }

  rowClass(trade: PaperTrade): string {
    const directionClass = this.tradeDirection(trade.direction).toLowerCase();
    const statusClass = this.tradeStatus(trade.status).toLowerCase();
    return `${directionClass} ${statusClass}`;
  }

  tickerAccuracy(ticker: string): SignalAccuracy | undefined {
    return this.accuracyByTicker().get(ticker);
  }

  private applyFilters(trades: PaperTrade[]): PaperTrade[] {
    const search = this.tickerSearch().trim().toUpperCase();
    const direction = this.directionFilter();
    const view = this.tradeViewFilter();

    return trades.filter((trade) => {
      const tradeDirection = this.tradeDirection(trade.direction).toLowerCase();
      const tradeStatus = this.tradeStatus(trade.status);

      if (search && !trade.tickerSymbol.toUpperCase().includes(search)) return false;
      if (direction !== 'all' && tradeDirection !== direction) return false;
      if (view === 'open' && tradeStatus !== 'Open') return false;
      if (view === 'closed' && tradeStatus === 'Open') return false;
      return true;
    });
  }
}
