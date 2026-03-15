import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

export interface PaperTrade {
  id: number;
  userId?: number | null;
  tickerSymbol: string;
  momentumScoreId: number;
  entryPrice: number;
  direction: number | string;
  tradeBias: number | string;
  totalScoreAtEntry: number;
  status: number | string;
  openedAt: string;
  closedAt?: string | null;
  exitPrice?: number | null;
  pnlPoints?: number | null;
  pnlPercent?: number | null;
  outcome: number | string;
  notes?: string | null;
}

export interface SignalAccuracy {
  id: number;
  tickerSymbol: string;
  totalTrades: number;
  wins: number;
  losses: number;
  breakevens: number;
  winRate: number;
  avgPnlPercent: number;
  avgScoreAtEntry: number;
  lastUpdatedAt: string;
}

@Injectable({ providedIn: 'root' })
export class PaperTradeWorkbenchService {
  private readonly http = inject(HttpClient);

  getOpenPaperTrades() {
    return this.http.get<PaperTrade[]>(`${environment.apiUrl}/api/trades/paper/open`);
  }

  getPaperTrades(page = 1, size = 100) {
    return this.http.get<PaperTrade[]>(`${environment.apiUrl}/api/trades/paper`, {
      params: {
        page,
        size
      }
    });
  }

  getSignalAccuracy() {
    return this.http.get<SignalAccuracy[]>(`${environment.apiUrl}/api/trades/accuracy`);
  }
}
