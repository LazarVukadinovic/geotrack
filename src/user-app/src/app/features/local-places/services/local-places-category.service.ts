import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { delay, Observable, of } from 'rxjs';
import { AuthService } from '../../../core/services/auth.service';
import { DestinationService } from '../../../core/services/destination.service';
import { FavoriteStateService } from '../../../core/services/favorite-state.service';
import { LocationService } from '../../../core/services/location.service';
import { ExploreService } from '../../explore/services/explore.service';
import type { PlaceCategoryItem } from '../models/local-places.model';

interface ApiPlaceListItem {
  id: number;
  title: string;
  subtitle?: string;
  description?: string;
  location?: string;
  image?: string;
  rating?: number;
  reviewCount?: string;
  openingHours?: string;
  distance?: string;
  tags: string[];
  variant: string;
  isSaved: boolean;
}

@Injectable({ providedIn: 'root' })
export class LocalPlacesCategoryService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly destinationSvc = inject(DestinationService);
  private readonly favoriteState = inject(FavoriteStateService);
  private readonly locationSvc = inject(LocationService);
  private readonly exploreSvc = inject(ExploreService);
  private readonly _apiItems = signal<Record<string, PlaceCategoryItem[]>>({});
  private readonly _loadErrorByKey = signal<Record<string, string>>({});
  private readonly _loadedKeys = new Set<string>();
  private readonly _pendingKeys = signal<string[]>([]);

  constructor() {
    void this.exploreSvc;
    effect(() => {
      const pending = this._pendingKeys();
      const destId = this.destinationSvc.id();
      if (pending.length === 0 || destId == null) return;
      for (const key of pending) {
        if (this._loadedKeys.has(key)) continue;
        this._loadedKeys.add(key);
        this.fetchCategory(key, destId);
      }
      this._pendingKeys.set([]);
    });
  }

  loadCategory(categoryKey: string): void {
    if (this._loadedKeys.has(categoryKey)) return;
    const queued = this._pendingKeys();
    if (queued.includes(categoryKey)) return;
    this._pendingKeys.set([...queued, categoryKey]);
  }

  private fetchCategory(categoryKey: string, destinationId: number): void {
    this.http.get<ApiPlaceListItem[]>(
      `/api/places?category=${categoryKey}&destinationId=${destinationId}${this.buildLocationQuery()}`
    ).subscribe({
      next: (items) => {
        const mapped = items.map((dto, i) => ({
          id: String(dto.id),
          title: dto.title,
          subtitle: dto.subtitle ?? '',
          description: dto.description ?? '',
          location: dto.location ?? '',
          image: dto.image ?? '',
          rating: dto.rating,
          reviewCount: dto.reviewCount,
          openingHours: dto.openingHours,
          variant: (i === 0 ? 'large' : 'compact') as PlaceCategoryItem['variant'],
          isSaved: dto.isSaved,
        }));
        this._apiItems.update(m => ({ ...m, [categoryKey]: mapped }));
        this._loadErrorByKey.update(m => {
          const copy = { ...m };
          delete copy[categoryKey];
          return copy;
        });
      },
      error: () => {
        this._apiItems.update(m => ({ ...m, [categoryKey]: [] }));
        this._loadErrorByKey.update(m => ({
          ...m,
          [categoryKey]: "We couldn't load these places. Check your connection and try again.",
        }));
      },
    });
  }

  private buildLocationQuery(): string {
    const loc = this.locationSvc.location();
    if (!loc) return '';
    return `&latitude=${loc.latitude}&longitude=${loc.longitude}`;
  }

  getItems(categoryKey: string): PlaceCategoryItem[] {
    const source = this._apiItems()[categoryKey] ?? [];
    return source.map((item) => ({
      ...item,
      isSaved: this.favoriteState.resolve('place', item.id, item.isSaved),
    }));
  }

  itemsFor(categoryKey: string) {
    return computed(() => {
      const source = this._apiItems()[categoryKey] ?? [];
      return source.map((item) => ({
        ...item,
        isSaved: this.favoriteState.resolve('place', item.id, item.isSaved),
      }));
    });
  }

  loadErrorFor(categoryKey: string) {
    return computed(() => this._loadErrorByKey()[categoryKey] ?? null);
  }

  getItems$(categoryKey: string): Observable<PlaceCategoryItem[]> {
    return of(this.getItems(categoryKey)).pipe(delay(0));
  }

  toggleSaved(id: string, categoryKey: string): void {
    if (!this.auth.isLoggedIn()) return;
    const numId = parseInt(id, 10);
    if (isNaN(numId)) return;
    const current = this.getItems(categoryKey).find((item) => item.id === id)?.isSaved ?? false;
    this.favoriteState.set('place', id, !current);
    this.http.post('/api/favorites/toggle', { entityType: 'place', entityId: numId }).subscribe({
      error: () => this.favoriteState.set('place', id, current),
    });
  }
}
