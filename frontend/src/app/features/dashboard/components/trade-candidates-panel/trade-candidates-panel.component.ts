import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { SparklineComponent } from '../../../../core/components/sparkline.component';
import { ScoreRow } from '../../dashboard.models';

@Component({
  selector: 'app-trade-candidates-panel',
  imports: [CommonModule, SparklineComponent],
  templateUrl: './trade-candidates-panel.component.html',
  styleUrl: './trade-candidates-panel.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TradeCandidatesPanelComponent {
  readonly strongLongs = input.required<ScoreRow[]>();
  readonly strongShorts = input.required<ScoreRow[]>();
  readonly onWatch = input.required<ScoreRow[]>();
  readonly watchlistCandidates = input.required<ScoreRow[]>();
  readonly recentMovers = input.required<ScoreRow[]>();
  readonly needsReview = input.required<ScoreRow[]>();
  readonly scoreHistory = input.required<Map<string, number[]>>();
  readonly reviewedTickerSymbols = input.required<Set<string>>();

  readonly selectTicker = output<string>();
  readonly addTickerToReview = output<string>();

  scoreBand(score: number): string {
    if (score >= 75) return 'A+ conviction';
    if (score >= 60) return 'A conviction';
    if (score >= 45) return 'B setup';
    return 'Needs validation';
  }

  biasBadgeClass(bias: string): string {
    return bias?.toLowerCase() ?? 'notrade';
  }

  isReviewed(ticker: string): boolean {
    return this.reviewedTickerSymbols().has(ticker);
  }

  getHistory(ticker: string): number[] {
    return this.scoreHistory().get(ticker) ?? [];
  }
}
