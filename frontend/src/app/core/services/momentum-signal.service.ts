import { Injectable, signal, OnDestroy } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { environment } from '../../../environments/environment';
import { AuthService } from './auth.service';

export interface MomentumUpdate {
  tickerSymbol: string;
  totalScore: number;
  redditScore: number;
  newsScore: number;
  volumeScore: number;
  optionsScore: number;
  sentimentScore: number;
  tradeBias: 'Long' | 'Short' | 'Watch' | 'NoTrade';
  confidence: string;
  signalSummary: string;
  aiAnalysis: string;
  session: string;
  scoredAtSast: string;
}

@Injectable({ providedIn: 'root' })
export class MomentumSignalService implements OnDestroy {
  private hub!: signalR.HubConnection;

  isConnected = signal<boolean>(false);
  lastUpdate = signal<MomentumUpdate | null>(null);
  connectionError = signal<string | null>(null);

  constructor(private authService: AuthService) {}

  connect(): void {
    const token = this.authService.getToken();

    this.hub = new signalR.HubConnectionBuilder()
      .withUrl(environment.hubUrl, {
        accessTokenFactory: () => token ?? ''
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    this.hub.on('MomentumUpdate', (update: MomentumUpdate) => {
      this.lastUpdate.set(update);
    });

    this.hub.onreconnecting(() => {
      this.isConnected.set(false);
    });

    this.hub.onreconnected(() => {
      this.isConnected.set(true);
      this.connectionError.set(null);
    });

    this.hub.onclose((error) => {
      this.isConnected.set(false);
      if (error) this.connectionError.set(error.message);
    });

    this.hub.start()
      .then(() => {
        this.isConnected.set(true);
        this.connectionError.set(null);
      })
      .catch(err => {
        this.connectionError.set(err.message);
      });
  }

  subscribeTicker(ticker: string): void {
    if (this.hub?.state === signalR.HubConnectionState.Connected) {
      this.hub.invoke('SubscribeTicker', ticker);
    }
  }

  disconnect(): void {
    this.hub?.stop();
  }

  ngOnDestroy(): void {
    this.disconnect();
  }
}