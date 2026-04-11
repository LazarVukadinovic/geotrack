import { Injectable, signal } from '@angular/core';
import { delay, Observable, of } from 'rxjs';
import type { LocalPlacesCategory, CategoryMeta } from '../models/local-places.model';
import { MOCK_LOCAL_PLACES_CATEGORIES } from '../data/mock-local-places.data';
import { CATEGORY_META } from '../data/mock-category-items.data';

/**
 * Local Places & Landmarks category data access.
 * Replace mock observable with HttpClient call when the API is ready.
 */
@Injectable({ providedIn: 'root' })
export class LocalPlacesService {
  private readonly _categories = signal<LocalPlacesCategory[]>([...MOCK_LOCAL_PLACES_CATEGORIES]);

  readonly categories = this._categories.asReadonly();

  /** API-shaped stream — swap for HttpClient.get<LocalPlacesCategory[]>('/api/categories') when ready. */
  getCategories$(): Observable<LocalPlacesCategory[]> {
    return of(this._categories()).pipe(delay(0));
  }

  getCategoryMeta(key: string): CategoryMeta {
    return CATEGORY_META[key] ?? { title: key, subtitle: '' };
  }
}
