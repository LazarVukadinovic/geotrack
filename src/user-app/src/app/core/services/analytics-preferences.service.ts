import { Injectable, computed, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class AnalyticsPreferencesService {
  private readonly _enabled = signal(true);

  readonly enabled = computed(() => this._enabled());

  setEnabled(enabled: boolean): void {
    this._enabled.set(enabled);
  }

  resetForGuest(): void {
    this._enabled.set(true);
  }
}
