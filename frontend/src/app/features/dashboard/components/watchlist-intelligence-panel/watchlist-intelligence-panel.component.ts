import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ScoreRow } from '../../dashboard.models';

@Component({
  selector: 'app-watchlist-intelligence-panel',
  imports: [CommonModule],
  templateUrl: './watchlist-intelligence-panel.component.html',
  styleUrl: './watchlist-intelligence-panel.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class WatchlistIntelligencePanelComponent {
  readonly pinnedTickers = input.required<string[]>();
  readonly watchlistIntelligence = input.required<ScoreRow[]>();

  readonly selectTicker = output<string>();
  readonly togglePin = output<string>();
}
