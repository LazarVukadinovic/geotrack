import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from './auth.service';

export type SaveCollectionEntityType = 'place' | 'event';

export type SaveAnalyticsEntityType = 'place' | 'hotel' | 'event' | 'destination';

export interface SaveToCollectionTarget {
  entityType: SaveCollectionEntityType;
  entityId: number;
  title: string;
  imageUrl?: string;
  surface?: string;
  analyticsEntityType?: SaveAnalyticsEntityType;
}

@Injectable({ providedIn: 'root' })
export class SaveToCollectionService {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly _target = signal<SaveToCollectionTarget | null>(null);

  readonly target = computed(() => this._target());

  open(target: SaveToCollectionTarget): void {
    if (!this.auth.isLoggedIn()) {
      void this.router.navigate(['/profile']);
      return;
    }

    if (!Number.isFinite(target.entityId) || target.entityId <= 0) {
      return;
    }

    this._target.set({
      ...target,
      title: target.title.trim() || 'This item',
      imageUrl: target.imageUrl?.trim() || undefined,
      surface: target.surface?.trim() || undefined,
      analyticsEntityType: target.analyticsEntityType ?? target.entityType,
    });
  }

  close(): void {
    this._target.set(null);
  }
}
