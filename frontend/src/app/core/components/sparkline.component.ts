import { Component, input, computed } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-sparkline',
  standalone: true,
  imports: [CommonModule],
  template: `
    @if (points().length > 1) {
      <svg [attr.width]="width()" [attr.height]="height()" [attr.viewBox]="'0 0 ' + width() + ' ' + height()" preserveAspectRatio="none">
        <defs>
          <linearGradient [id]="gradId()" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" [attr.stop-color]="lineColor()" stop-opacity="0.3"/>
            <stop offset="100%" [attr.stop-color]="lineColor()" stop-opacity="0"/>
          </linearGradient>
        </defs>
        <path [attr.d]="areaPath()" [attr.fill]="'url(#' + gradId() + ')'" />
        <path [attr.d]="linePath()" [attr.stroke]="lineColor()" stroke-width="1.5" fill="none" stroke-linecap="round" stroke-linejoin="round"/>
        <circle [attr.cx]="lastPoint().x" [attr.cy]="lastPoint().y" r="2.5" [attr.fill]="lineColor()"/>
      </svg>
    } @else {
      <span class="spark-empty">—</span>
    }
  `,
  styles: [`:host { display: inline-block; } .spark-empty { color: #6e7681; font-size: 12px; }`]
})
export class SparklineComponent {
  data = input<number[]>([]);
  width = input<number>(80);
  height = input<number>(28);
  latestScore = input<number>(0);

  // Unique gradient ID per instance to avoid SVG conflicts
  gradId = computed(() => `spark-grad-${Math.abs(this.data().join('').hashCode ? 0 : this.data().reduce((a, b) => a + b, 0))}-${this.width()}`);

  lineColor = computed(() => {
    const s = this.latestScore();
    if (s >= 60) return '#3fb950';
    if (s >= 40) return '#d29922';
    return '#6e7681';
  });

  points = computed(() => {
    const d = this.data();
    if (d.length < 2) return [];
    const w = this.width();
    const h = this.height();
    const pad = 3;
    const min = Math.min(...d);
    const max = Math.max(...d);
    const range = max - min || 1;
    return d.map((v, i) => ({
      x: pad + (i / (d.length - 1)) * (w - pad * 2),
      y: pad + (1 - (v - min) / range) * (h - pad * 2)
    }));
  });

  linePath = computed(() => {
    const pts = this.points();
    if (!pts.length) return '';
    return pts.map((p, i) => `${i === 0 ? 'M' : 'L'}${p.x.toFixed(1)},${p.y.toFixed(1)}`).join(' ');
  });

  areaPath = computed(() => {
    const pts = this.points();
    if (!pts.length) return '';
    const h = this.height();
    const line = pts.map((p, i) => `${i === 0 ? 'M' : 'L'}${p.x.toFixed(1)},${p.y.toFixed(1)}`).join(' ');
    return `${line} L${pts[pts.length - 1].x.toFixed(1)},${h} L${pts[0].x.toFixed(1)},${h} Z`;
  });

  lastPoint = computed(() => {
    const pts = this.points();
    return pts[pts.length - 1] ?? { x: 0, y: 0 };
  });
}
