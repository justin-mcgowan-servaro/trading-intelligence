export type BiasFilter = 'All' | 'Long' | 'Short' | 'Watch' | 'NoTrade';
export type SortBy = 'scoreDesc' | 'scoreAsc' | 'updatedDesc' | 'ticker';
export type ReviewStatus = 'New' | 'Reviewing' | 'Watching' | 'Ready' | 'Archived';
export type ReviewFilter = 'All' | 'Reviewing' | 'Ready';

export interface ScoreRow {
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

export interface TickerDetail {
  latest: ScoreRow;
  history: Array<ScoreRow & { id: number; aiAnalysis?: string }>;
  currentBuffer: {
    signalCount: number;
    signalTypes: string[];
  };
}

export interface MomentumAlert {
  tickerSymbol: string;
  totalScore: number;
  tradeBias: ScoreRow['tradeBias'];
  signalSummary?: string;
  alertedAt?: string;
}

export type AnalysisStatus = 'idle' | 'triggering' | 'processing' | 'completed' | 'timeout' | 'error';

export interface AnalysisJobStatusResponse {
  jobId: string;
  ticker: string;
  status: 'processing' | 'completed';
  hasAnalysis?: boolean;
}

export interface DashboardNotification {
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

export interface ReviewedTickerRecord {
  tickerSymbol: string;
  status: ReviewStatus;
  note: string;
  addedAt: string;
  snapshot: ScoreRow | null;
}

export interface ReviewedTickerView {
  tickerSymbol: string;
  status: ReviewStatus;
  note: string;
  addedAt: string;
  score: ScoreRow | null;
}
