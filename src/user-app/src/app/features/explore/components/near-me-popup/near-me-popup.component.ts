import { Component, HostBinding, effect, input, output, signal } from '@angular/core';

export interface NearMeSettings {
  radius: number;
  enabled: boolean;
}

@Component({
  selector: 'app-near-me-popup',
  standalone: true,
  templateUrl: './near-me-popup.component.html',
  styleUrl: './near-me-popup.component.scss',
})
export class NearMePopupComponent {
  readonly initialRadius = input<number>(2.5);
  readonly initialEnabled = input<boolean>(true);
  readonly bottom = input<string>('0px');
  readonly left = input<string>('0px');

  readonly closed = output<void>();
  readonly applied = output<NearMeSettings>();

  protected tempRadius = signal(2.5);
  protected tempEnabled = signal(true);

  @HostBinding('style.bottom') get styleBottom() { return this.bottom(); }
  @HostBinding('style.left')   get styleLeft()   { return this.left(); }

  constructor() {
    effect(() => {
      this.tempRadius.set(this.initialRadius());
      this.tempEnabled.set(this.initialEnabled());
    });
  }

  protected get displayValue(): string {
    const r = this.tempRadius();
    if (r < 1) return String(Math.round(r * 1000));
    return r % 1 === 0 ? r.toFixed(0) : r.toFixed(1);
  }

  protected get displayUnit(): string {
    return this.tempRadius() < 1 ? 'm' : 'km';
  }

  protected get sliderPercent(): number {
    const min = 0.5, max = 25;
    return ((this.tempRadius() - min) / (max - min)) * 100;
  }

  protected onSliderInput(event: Event): void {
    this.tempRadius.set(parseFloat((event.target as HTMLInputElement).value));
  }

  protected onToggleChange(event: Event): void {
    this.tempEnabled.set((event.target as HTMLInputElement).checked);
  }

  protected close(): void {
    this.closed.emit();
  }

  protected reset(): void {
    this.tempRadius.set(2.5);
    this.tempEnabled.set(true);
  }

  protected apply(): void {
    this.applied.emit({ radius: this.tempRadius(), enabled: this.tempEnabled() });
  }
}
