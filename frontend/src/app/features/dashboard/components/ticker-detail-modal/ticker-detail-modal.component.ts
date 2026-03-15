import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { SparklineComponent } from '../../../../core/components/sparkline.component';
import { AnalysisStatus, TickerDetail } from '../../dashboard.models';

@Component({
  selector: 'app-ticker-detail-modal',
  imports: [CommonModule, SparklineComponent],
  templateUrl: './ticker-detail-modal.component.html',
  styleUrl: './ticker-detail-modal.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TickerDetailModalComponent {
  readonly selectedTicker = input.required<string>();
  readonly tickerDetail = input<TickerDetail | null>(null);
  readonly tickerLoading = input.required<boolean>();
  readonly activeTab = input.required<'overview' | 'analysis' | 'history'>();
  readonly isPinned = input.required<boolean>();
  readonly isReviewed = input.required<boolean>();
  readonly isWatchlisted = input.required<boolean>();
  readonly canToggleWatchlist = input.required<boolean>();
  readonly analysisStatus = input.required<AnalysisStatus>();
  readonly analysisStatusMessage = input.required<string>();
  readonly analyzeButtonLabel = input.required<string>();
  readonly isAnalysisBusy = input.required<boolean>();
  readonly scoreHistory = input.required<number[]>();

  readonly close = output<void>();
  readonly activeTabChange = output<'overview' | 'analysis' | 'history'>();
  readonly togglePin = output<string>();
  readonly addToReview = output<string>();
  readonly toggleWatchlist = output<string>();
  readonly triggerAnalysis = output<void>();

  scoreBarClass(score: number): string {
    if (score >= 60) return 'high';
    if (score >= 40) return 'medium';
    return 'low';
  }

  biasBadgeClass(bias: string): string {
    return bias?.toLowerCase() ?? 'notrade';
  }
}
