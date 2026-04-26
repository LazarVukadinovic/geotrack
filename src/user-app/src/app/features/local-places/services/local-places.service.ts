import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { delay, Observable, of } from 'rxjs';
import { DestinationService } from '../../../core/services/destination.service';
import { LocationService } from '../../../core/services/location.service';
import { ExploreService } from '../../explore/services/explore.service';
import type { LocalPlacesCategory, CategoryMeta } from '../models/local-places.model';

const CATEGORY_META: Record<string, CategoryMeta> = {
  beaches: { title: 'Beaches', subtitle: 'Explore beach spots and coastal places' },
  museums: { title: 'Museums', subtitle: 'Discover museums and exhibitions' },
  parks: { title: 'Parks', subtitle: 'Browse parks and green spaces' },
  markets: { title: 'Markets', subtitle: 'Local markets and fresh finds' },
  restaurants: { title: 'Restaurants', subtitle: 'Places to eat nearby' },
  cafes: { title: 'Cafes', subtitle: 'Coffee and casual stops nearby' },
  'historic-sites': { title: 'Historic Sites', subtitle: 'History-backed places in this destination' },
  galleries: { title: 'Galleries', subtitle: 'Art and exhibition spaces' },
  'nature-reserves': { title: 'Nature Reserves', subtitle: 'Protected nature and outdoor places' },
  viewpoints: { title: 'Viewpoints', subtitle: 'Scenic places from the catalog' },
};

interface ApiCategory {
  id: number;
  title: string;
  image?: string;
  categoryKey: string;
  subtitle?: string;
}

@Injectable({ providedIn: 'root' })
export class LocalPlacesService {
  private readonly http = inject(HttpClient);
  private readonly destinationSvc = inject(DestinationService);
  private readonly locationSvc = inject(LocationService);
  private readonly exploreSvc = inject(ExploreService);
  private readonly _categories = signal<LocalPlacesCategory[]>([]);
  private readonly _loadError = signal<string | null>(null);
  private loadedDestinationId: number | null = null;

  readonly categories = this._categories.asReadonly();
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
    this.http.get<ApiCategory[]>(`/api/explore/categories?destinationId=${destinationId}${this.buildLocationQuery()}`).subscribe({
      next: (cats) => {
        this._categories.set(cats.map(c => ({
          id: String(c.id),
          title: c.title,
          image: c.image ?? '',
          categoryKey: c.categoryKey,
          subtitle: c.subtitle ?? '',
        })));
        this.loadedDestinationId = destinationId;
        this._loadError.set(null);
      },
      error: () => {
        this._categories.set([]);
        this.loadedDestinationId = destinationId;
        this._loadError.set("We couldn't load local places. Check your connection and try again.");
      },
    });
  }

  private buildLocationQuery(): string {
    const loc = this.locationSvc.location();
    if (!loc) return '';
    return `&latitude=${loc.latitude}&longitude=${loc.longitude}`;
  }

  getCategories$(): Observable<LocalPlacesCategory[]> {
    return of(this._categories()).pipe(delay(0));
  }

  getCategoryMeta(key: string): CategoryMeta {
    const found = this._categories().find(c => c.categoryKey === key);
    if (found) return { title: found.title, subtitle: found.subtitle };
    return CATEGORY_META[key] ?? { title: key, subtitle: '' };
  }
}
