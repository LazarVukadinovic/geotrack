import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { AnalyticsService } from '../../../../core/services/analytics.service';

interface Category {
  id: string;
  label: string;
}

@Component({
  selector: 'app-filters',
  imports: [CommonModule],
  templateUrl: './filters.component.html',
  styleUrl: './filters.component.css',
})
export class Filters {
  selectedCategories: string[] = [];

  allCategories: Category[] = [
    { id: 'places', label: 'Places' },
    { id: 'stays', label: 'Stays' },
    { id: 'events', label: 'Events' },
  ];

  constructor(
    private router: Router,
    private analytics: AnalyticsService,
  ) {
    this.selectedCategories = this.readSavedCategories();
  }

  toggleCategory(cat: Category): void {
    if (this.isSelected(cat)) {
      this.selectedCategories = this.selectedCategories.filter((id) => id !== cat.id);
    } else {
      this.selectedCategories = [...this.selectedCategories, cat.id];
    }
  }

  isSelected(cat: Category): boolean {
    return this.selectedCategories.includes(cat.id);
  }

  clearAll(): void {
    const previousCategories = this.readSavedCategories();
    const hadEffectiveFilters = previousCategories.length > 0 || this.selectedCategories.length > 0;
    this.selectedCategories = [];
    sessionStorage.removeItem('mapFilters');
    if (hadEffectiveFilters) {
      this.analytics.track('filter_applied', {
        metadata: {
          surface: 'map_filters',
          categories: [],
          cleared: true,
        },
      });
    }
    this.router.navigate(['/map'], {
      state: { fromFilters: true, cleared: true },
    });
  }

  applyFilters(): void {
    const previousCategories = this.readSavedCategories();
    const nextCategories = this.normalizeCategories(this.selectedCategories);
    sessionStorage.setItem('mapFilters', JSON.stringify({
      categories: nextCategories,
    }));

    if (!this.sameCategories(previousCategories, nextCategories)) {
      this.analytics.track('filter_applied', {
        metadata: {
          surface: 'map_filters',
          categories: nextCategories,
        },
      });
    }

    this.router.navigate(['/map'], {
      state: { applyFilters: true, fromFilters: true },
    });
  }

  goBack(): void {
    this.router.navigate(['/map']);
  }

  private readSavedCategories(): string[] {
    const saved = sessionStorage.getItem('mapFilters');
    if (!saved) return [];

    try {
      const parsed = JSON.parse(saved) as { categories?: unknown };
      return Array.isArray(parsed.categories)
        ? this.normalizeCategories(parsed.categories.filter((value): value is string => typeof value === 'string'))
        : [];
    } catch {
      sessionStorage.removeItem('mapFilters');
      return [];
    }
  }

  private normalizeCategories(categories: string[]): string[] {
    const allowed = new Set(this.allCategories.map((category) => category.id));
    return [...new Set(categories.filter((category) => allowed.has(category)))].sort();
  }

  private sameCategories(left: string[], right: string[]): boolean {
    if (left.length !== right.length) return false;
    return left.every((value, index) => value === right[index]);
  }
}
