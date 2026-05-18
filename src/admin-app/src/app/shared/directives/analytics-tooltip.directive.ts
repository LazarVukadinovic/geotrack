import { DOCUMENT } from '@angular/common';
import {
  Directive,
  ElementRef,
  HostListener,
  Input,
  OnDestroy,
  Renderer2,
  inject,
} from '@angular/core';

export interface AnalyticsTooltipRow {
  label: string;
  value: string | number;
}

export interface AnalyticsTooltipContent {
  title?: string;
  rows?: AnalyticsTooltipRow[];
  footer?: string;
}

@Directive({
  selector: '[appAnalyticsTooltip]',
  standalone: true,
})
export class AnalyticsTooltipDirective implements OnDestroy {
  private static nextId = 0;

  private readonly elementRef = inject<ElementRef<HTMLElement | SVGElement>>(ElementRef);
  private readonly renderer = inject(Renderer2);
  private readonly document = inject(DOCUMENT);
  private readonly tooltipId = `analytics-chart-tooltip-${++AnalyticsTooltipDirective.nextId}`;

  private tooltipEl: HTMLElement | null = null;
  private hideTimer: ReturnType<typeof setTimeout> | null = null;

  @Input('appAnalyticsTooltip') tooltip: AnalyticsTooltipContent | string | null | undefined;
  @Input() analyticsTooltipDisabled = false;

  ngOnDestroy(): void {
    this.clearHideTimer();
    this.hide();
  }

  @HostListener('mouseenter', ['$event'])
  onMouseEnter(event: MouseEvent): void {
    this.showAt(event.clientX, event.clientY);
  }

  @HostListener('mousemove', ['$event'])
  onMouseMove(event: MouseEvent): void {
    if (this.tooltipEl) {
      this.positionTooltip(event.clientX, event.clientY);
    }
  }

  @HostListener('mouseleave')
  onMouseLeave(): void {
    this.hide();
  }

  @HostListener('focus')
  onFocus(): void {
    const rect = this.elementRef.nativeElement.getBoundingClientRect();
    this.showAt(rect.left + rect.width / 2, rect.top + rect.height / 2);
  }

  @HostListener('blur')
  onBlur(): void {
    this.hide();
  }

  @HostListener('click', ['$event'])
  onClick(event: MouseEvent): void {
    this.showAt(event.clientX, event.clientY);
    this.scheduleTouchHide();
  }

  @HostListener('touchstart', ['$event'])
  onTouchStart(event: TouchEvent): void {
    const touch = event.touches.item(0);
    if (!touch) {
      return;
    }

    this.showAt(touch.clientX, touch.clientY);
    this.scheduleTouchHide();
  }

  @HostListener('keydown.escape', ['$event'])
  onEscape(event: Event): void {
    if (!this.tooltipEl) {
      return;
    }

    event.preventDefault();
    this.hide();
  }

  private showAt(clientX: number, clientY: number): void {
    if (this.analyticsTooltipDisabled) {
      this.hide();
      return;
    }

    const content = this.normalizeContent();
    if (!content) {
      this.hide();
      return;
    }

    this.clearHideTimer();
    this.renderTooltip(content);
    this.renderer.setAttribute(this.elementRef.nativeElement, 'aria-describedby', this.tooltipId);
    this.positionTooltip(clientX, clientY);
  }

  private renderTooltip(content: AnalyticsTooltipContent): void {
    if (!this.tooltipEl) {
      this.tooltipEl = this.renderer.createElement('div') as HTMLElement;
      this.renderer.setAttribute(this.tooltipEl, 'id', this.tooltipId);
      this.renderer.setAttribute(this.tooltipEl, 'role', 'tooltip');
      this.renderer.setAttribute(this.tooltipEl, 'data-testid', 'analytics-chart-tooltip');
      this.renderer.setStyle(this.tooltipEl, 'position', 'fixed');
      this.renderer.setStyle(this.tooltipEl, 'z-index', '1000');
      this.renderer.setStyle(this.tooltipEl, 'max-width', '260px');
      this.renderer.setStyle(this.tooltipEl, 'padding', '8px 10px');
      this.renderer.setStyle(this.tooltipEl, 'border-radius', '8px');
      this.renderer.setStyle(this.tooltipEl, 'background', '#111827');
      this.renderer.setStyle(this.tooltipEl, 'color', '#ffffff');
      this.renderer.setStyle(this.tooltipEl, 'box-shadow', '0 10px 28px rgba(15, 23, 42, 0.22)');
      this.renderer.setStyle(this.tooltipEl, 'font-size', '12px');
      this.renderer.setStyle(this.tooltipEl, 'line-height', '1.35');
      this.renderer.setStyle(this.tooltipEl, 'pointer-events', 'none');
      this.renderer.setStyle(this.tooltipEl, 'white-space', 'normal');
      this.renderer.setStyle(this.tooltipEl, 'opacity', '0');
      this.renderer.setStyle(this.tooltipEl, 'transition', 'opacity 120ms ease');
      this.renderer.appendChild(this.document.body, this.tooltipEl);
    }

    while (this.tooltipEl.firstChild) {
      this.renderer.removeChild(this.tooltipEl, this.tooltipEl.firstChild);
    }

    if (content.title) {
      const title = this.renderer.createElement('div') as HTMLElement;
      this.renderer.setStyle(title, 'font-weight', '700');
      this.renderer.setStyle(title, 'margin-bottom', content.rows?.length || content.footer ? '6px' : '0');
      this.renderer.setProperty(title, 'textContent', content.title);
      this.renderer.appendChild(this.tooltipEl, title);
    }

    for (const row of content.rows ?? []) {
      const line = this.renderer.createElement('div') as HTMLElement;
      this.renderer.setStyle(line, 'display', 'flex');
      this.renderer.setStyle(line, 'justify-content', 'space-between');
      this.renderer.setStyle(line, 'gap', '14px');

      const label = this.renderer.createElement('span') as HTMLElement;
      this.renderer.setStyle(label, 'color', '#cbd5e1');
      this.renderer.setProperty(label, 'textContent', row.label);

      const value = this.renderer.createElement('span') as HTMLElement;
      this.renderer.setStyle(value, 'font-weight', '700');
      this.renderer.setStyle(value, 'text-align', 'right');
      this.renderer.setProperty(value, 'textContent', String(row.value));

      this.renderer.appendChild(line, label);
      this.renderer.appendChild(line, value);
      this.renderer.appendChild(this.tooltipEl, line);
    }

    if (content.footer) {
      const footer = this.renderer.createElement('div') as HTMLElement;
      this.renderer.setStyle(footer, 'margin-top', content.rows?.length ? '6px' : '0');
      this.renderer.setStyle(footer, 'color', '#cbd5e1');
      this.renderer.setProperty(footer, 'textContent', content.footer);
      this.renderer.appendChild(this.tooltipEl, footer);
    }

    this.renderer.setStyle(this.tooltipEl, 'opacity', '1');
  }

  private positionTooltip(clientX: number, clientY: number): void {
    if (!this.tooltipEl) {
      return;
    }

    const view = this.document.defaultView;
    const viewportWidth = view?.innerWidth ?? this.document.documentElement.clientWidth;
    const viewportHeight = view?.innerHeight ?? this.document.documentElement.clientHeight;
    const padding = 8;
    const offset = 12;
    const rect = this.tooltipEl.getBoundingClientRect();
    const tooltipWidth = rect.width || 220;
    const tooltipHeight = rect.height || 80;

    let left = clientX + offset;
    let top = clientY + offset;

    if (left + tooltipWidth > viewportWidth - padding) {
      left = clientX - tooltipWidth - offset;
    }

    if (top + tooltipHeight > viewportHeight - padding) {
      top = clientY - tooltipHeight - offset;
    }

    left = Math.max(padding, Math.min(left, viewportWidth - tooltipWidth - padding));
    top = Math.max(padding, Math.min(top, viewportHeight - tooltipHeight - padding));

    this.renderer.setStyle(this.tooltipEl, 'left', `${left}px`);
    this.renderer.setStyle(this.tooltipEl, 'top', `${top}px`);
  }

  private hide(): void {
    this.renderer.removeAttribute(this.elementRef.nativeElement, 'aria-describedby');

    if (!this.tooltipEl) {
      return;
    }

    this.renderer.removeChild(this.document.body, this.tooltipEl);
    this.tooltipEl = null;
  }

  private normalizeContent(): AnalyticsTooltipContent | null {
    if (typeof this.tooltip === 'string') {
      const title = this.tooltip.trim();
      return title ? { title } : null;
    }

    if (!this.tooltip) {
      return null;
    }

    const title = this.tooltip.title?.trim();
    const rows = (this.tooltip.rows ?? []).filter(row =>
      row.label.trim().length > 0 &&
      row.value !== null &&
      row.value !== undefined &&
      String(row.value).trim().length > 0,
    );
    const footer = this.tooltip.footer?.trim();

    if (!title && rows.length === 0 && !footer) {
      return null;
    }

    return {
      title,
      rows,
      footer,
    };
  }

  private scheduleTouchHide(): void {
    this.clearHideTimer();
    this.hideTimer = setTimeout(() => this.hide(), 3500);
  }

  private clearHideTimer(): void {
    if (!this.hideTimer) {
      return;
    }

    clearTimeout(this.hideTimer);
    this.hideTimer = null;
  }
}
