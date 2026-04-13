import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { delay, Observable, of } from 'rxjs';
import type { LocalPlacesCategory, CategoryMeta } from '../models/local-places.model';
import { MOCK_LOCAL_PLACES_CATEGORIES } from '../data/mock-local-places.data';
import { CATEGORY_META } from '../data/mock-category-items.data';

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
  private readonly _categories = signal<LocalPlacesCategory[]>([]);

  readonly categories = this._categories.asReadonly();

  constructor() {
    this.http.get<ApiCategory[]>('/api/explore/categories?destinationId=1').subscribe({
      next: (cats) => {
        this._categories.set(cats.map(c => ({
          id: String(c.id),
          title: c.title,
          image: c.image ?? '',
          categoryKey: c.categoryKey,
          subtitle: c.subtitle ?? '',
        })));
      },
      error: () => {
        this._categories.set([...MOCK_LOCAL_PLACES_CATEGORIES]);
      },
    });
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
