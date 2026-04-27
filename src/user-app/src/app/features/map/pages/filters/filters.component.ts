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
  selectedDistance = 'Any';

  allCategories: Category[] = [
    { id: 'places', label: 'Places' },
    { id: 'stays', label: 'Stays' },
    { id: 'events', label: 'Events' },
  ];

  distances = ['Any', '0.5km', '1km', '2km', '5km'];

  constructor(
    private router: Router,
    private analytics: AnalyticsService,
  ) {
    const saved = sessionStorage.getItem('mapFilters');
    if (saved) {
      try {
        const f = JSON.parse(saved);
        this.selectedCategories = f.categories ?? [];
        this.selectedDistance = f.distance ?? 'Any';
      } catch {
        sessionStorage.removeItem('mapFilters');
      }
    }
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
    this.selectedCategories = [];
    this.selectedDistance = 'Any';
    sessionStorage.removeItem('mapFilters');
    this.analytics.track('filter_applied', {
      metadata: {
        surface: 'map_filters',
        categories: [],
        distance: 'Any',
        cleared: true,
      },
    });
    this.router.navigate(['/map'], {
      state: { fromFilters: true, cleared: true },
    });
  }

  applyFilters(): void {
    sessionStorage.setItem('mapFilters', JSON.stringify({
      categories: this.selectedCategories,
      distance: this.selectedDistance,
    }));

    this.analytics.track('filter_applied', {
      metadata: {
        surface: 'map_filters',
        categories: this.selectedCategories,
        distance: this.selectedDistance,
      },
    });

    this.router.navigate(['/map'], {
      state: { applyFilters: true, fromFilters: true },
    });
  }

  goBack(): void {
    this.router.navigate(['/map']);
  }
}
