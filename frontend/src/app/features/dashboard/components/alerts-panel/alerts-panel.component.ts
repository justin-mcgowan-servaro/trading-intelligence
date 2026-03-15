import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DashboardNotification } from '../../dashboard.models';

@Component({
  selector: 'app-alerts-panel',
  imports: [CommonModule],
  templateUrl: './alerts-panel.component.html',
  styleUrl: './alerts-panel.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AlertsPanelComponent {
  readonly notifications = input.required<DashboardNotification[]>();
  readonly reviewedTickerSymbols = input.required<Set<string>>();

  readonly close = output<void>();
  readonly openNotification = output<DashboardNotification>();
  readonly addToReview = output<string>();

  biasBadgeClass(bias: string): string {
    return bias?.toLowerCase() ?? 'notrade';
  }

  isReviewed(ticker: string): boolean {
    return this.reviewedTickerSymbols().has(ticker);
  }
}
