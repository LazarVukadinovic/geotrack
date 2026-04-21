import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { delay, Observable, of } from 'rxjs';
import { AuthService } from '../../../core/services/auth.service';
import { DestinationService } from '../../../core/services/destination.service';
import { FavoriteStateService } from '../../../core/services/favorite-state.service';
import { LocationService } from '../../../core/services/location.service';
import { ExploreService } from '../../explore/services/explore.service';
import type { CultureHistoryItem } from '../models/culture-history.model';

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
export class CultureHistoryService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly destinationSvc = inject(DestinationService);
  private readonly favoriteState = inject(FavoriteStateService);
  private readonly locationSvc = inject(LocationService);
  private readonly exploreSvc = inject(ExploreService);
  private readonly _items = signal<CultureHistoryItem[]>([]);
  private readonly _loadError = signal<string | null>(null);
  private loadedDestinationId: number | null = null;

  readonly items = computed(() => {
    return this._items().map((item) => ({
      ...item,
      isSaved: this.favoriteState.resolve('place', item.id, item.isSaved),
    }));
  });

  readonly loadError = computed(() => this._loadError());

  constructor() {
    void this.exploreSvc;
    effect(() => {
      const destId = this.destinationSvc.id();
      if (destId == null || destId === this.loadedDestinationId) return;
      this.loadFromApi(destId);
    });
  }

  private loadFromApi(destinationId: number): void {
    this.http.get<ApiPlaceListItem[]>(
      `/api/places?tags=culture,history,historic-sites,museums,galleries,exhibitions,landmarks&destinationId=${destinationId}${this.buildLocationQuery()}`
    ).subscribe({
      next: (items) => {
        this._items.set(items.map((dto, i) => ({
          id: String(dto.id),
          title: dto.title,
          subtitle: dto.subtitle ?? '',
          description: dto.description ?? '',
          location: dto.location ?? '',
          image: dto.image ?? '',
          rating: dto.rating,
          reviewCount: dto.reviewCount,
          openingHours: dto.openingHours,
          tags: dto.tags,
          variant: (i === 0 ? 'large' : 'compact') as CultureHistoryItem['variant'],
          isSaved: dto.isSaved,
        })));
        this.loadedDestinationId = destinationId;
        this._loadError.set(null);
      },
      error: () => {
        this._items.set([]);
        this.loadedDestinationId = destinationId;
        this._loadError.set("We couldn't load culture & history places. Check your connection and try again.");
      },
    });
  }

  private buildLocationQuery(): string {
    const loc = this.locationSvc.location();
    if (!loc) return '';
    return `&latitude=${loc.latitude}&longitude=${loc.longitude}`;
  }

  getItems$(): Observable<CultureHistoryItem[]> {
    return of(this.items()).pipe(delay(0));
  }

  toggleSaved(id: string): void {
    if (!this.auth.isLoggedIn()) return;
    const numId = parseInt(id, 10);
    if (isNaN(numId)) return;
    const current = this.items().find((item) => item.id === id)?.isSaved ?? false;
    this.favoriteState.set('place', id, !current);
    this.http.post('/api/favorites/toggle', { entityType: 'place', entityId: numId }).subscribe({
      error: () => this.favoriteState.set('place', id, current),
    });
  }
}
