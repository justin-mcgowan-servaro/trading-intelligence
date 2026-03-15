import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { SparklineComponent } from '../../../../core/components/sparkline.component';
import { ReviewFilter, ReviewStatus, ReviewedTickerView } from '../../dashboard.models';

@Component({
  selector: 'app-opportunity-review-panel',
  imports: [CommonModule, SparklineComponent],
  templateUrl: './opportunity-review-panel.component.html',
  styleUrl: './opportunity-review-panel.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class OpportunityReviewPanelComponent {
  readonly reviewFilter = input.required<ReviewFilter>();
  readonly reviewStatuses = input.required<ReviewStatus[]>();
  readonly reviewCounts = input.required<Record<ReviewStatus, number>>();
  readonly reviewedByStatus = input.required<Map<ReviewStatus, ReviewedTickerView[]>>();
  readonly scoreHistory = input.required<Map<string, number[]>>();
  readonly watchlistedTickers = input.required<Set<string>>();
  readonly pinnedTickers = input.required<Set<string>>();

  readonly setReviewFilter = output<string>();
  readonly selectTicker = output<string>();
  readonly setReviewStatus = output<{ ticker: string; status: string }>();
  readonly removeFromReview = output<string>();
  readonly updateReviewNote = output<{ ticker: string; note: string }>();

  biasBadgeClass(bias: string): string {
    return bias?.toLowerCase() ?? 'notrade';
  }

  getHistory(ticker: string): number[] {
    return this.scoreHistory().get(ticker) ?? [];
  }
}
