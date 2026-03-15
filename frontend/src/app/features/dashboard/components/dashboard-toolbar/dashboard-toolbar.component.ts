import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { BiasFilter, SortBy } from '../../dashboard.models';

@Component({
  selector: 'app-dashboard-toolbar',
  imports: [CommonModule],
  templateUrl: './dashboard-toolbar.component.html',
  styleUrl: './dashboard-toolbar.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class DashboardToolbarComponent {
  readonly searchTerm = input.required<string>();
  readonly biasFilter = input.required<BiasFilter>();
  readonly minScoreFilter = input.required<number>();
  readonly watchlistOnly = input.required<boolean>();
  readonly sortBy = input.required<SortBy>();

  readonly searchTermChange = output<string>();
  readonly biasFilterChange = output<string>();
  readonly minScoreFilterChange = output<string>();
  readonly watchlistOnlyChange = output<boolean>();
  readonly sortByChange = output<string>();
  readonly reset = output<void>();
}
