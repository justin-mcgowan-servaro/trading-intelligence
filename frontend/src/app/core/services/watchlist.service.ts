import { Injectable, signal, inject, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { AuthService } from './auth.service';

@Injectable({ providedIn: 'root' })
export class WatchlistService {
  private http = inject(HttpClient);
  private auth = inject(AuthService);

  // Set of ticker symbols currently watchlisted
  private _watchlisted = signal<Set<string>>(new Set());

  // Public read-only view
  watchlisted = computed(() => this._watchlisted());

  isWatchlisted(ticker: string): boolean {
    return this._watchlisted().has(ticker.toUpperCase());
  }

  load(): void {
    if (!this.auth.isAuthenticated()) return;

    this.http.get<any[]>(`${environment.apiUrl}/api/watchlist`)
      .subscribe({
        next: (items) => {
          const symbols = new Set<string>(items.map(i => i.tickerSymbol));
          this._watchlisted.set(symbols);
        },
        error: () => {} // Silently fail — not critical
      });
  }

  toggle(ticker: string): void {
    if (!this.auth.isAuthenticated()) return;

    ticker = ticker.toUpperCase();
    const current = new Set(this._watchlisted());

    if (current.has(ticker)) {
      // Optimistic remove
      current.delete(ticker);
      this._watchlisted.set(current);

      this.http.delete(`${environment.apiUrl}/api/watchlist/${ticker}`)
        .subscribe({
          error: () => {
            // Rollback on failure
            const rollback = new Set(this._watchlisted());
            rollback.add(ticker);
            this._watchlisted.set(rollback);
          }
        });
    } else {
      // Optimistic add
      current.add(ticker);
      this._watchlisted.set(current);

      this.http.post(`${environment.apiUrl}/api/watchlist`,
        { tickerSymbol: ticker, alertEnabled: true })
        .subscribe({
          error: () => {
            // Rollback on failure
            const rollback = new Set(this._watchlisted());
            rollback.delete(ticker);
            this._watchlisted.set(rollback);
          }
        });
    }
  }
}
