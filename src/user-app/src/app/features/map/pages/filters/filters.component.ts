import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { AnalyticsService } from '../../../../core/services/analytics.service';
import { MapFilterOption, MapFilterStateService } from '../../services/map-filter-state.service';

interface Category {
  id: string;
  label: string;
  count?: number;
}

@Component({
  selector: 'app-filters',
  imports: [CommonModule],
  templateUrl: './filters.component.html',
  styleUrl: './filters.component.css',
})
export class Filters {
  selectedCategories: string[] = [];
  selectedCategoryLabelFilters: string[] = [];

  constructor(
    private router: Router,
    private analytics: AnalyticsService,
    protected readonly mapFilterState: MapFilterStateService,
  ) {
    const savedFilters = this.mapFilterState.readSelection();
    this.selectedCategories = savedFilters.categories;
    this.selectedCategoryLabelFilters = savedFilters.categoryLabelIds;
  }

  allCategories(): Category[] {
    return [
      { id: 'all', label: 'All' },
      ...this.mapFilterState.availablePlaceTypes().map(option => ({
        id: option.id,
        label: option.label,
        count: option.count,
      })),
    ];
  }

  availableMoreFilters(): MapFilterOption[] {
    return this.mapFilterState.availableMoreFilters();
  }

  toggleCategory(cat: Category): void {
    if (cat.id === 'all' || this.selectedCategories.includes(cat.id)) {
      this.selectedCategories = [];
      return;
    }

    // A place can only have one place type, so primary filters are mutually exclusive.
    this.selectedCategories = [cat.id];
  }

  isSelected(cat: Category): boolean {
    return cat.id === 'all'
      ? this.selectedCategories.length === 0
      : this.selectedCategories.includes(cat.id);
  }

  toggleMoreFilter(filter: MapFilterOption): void {
    this.selectedCategoryLabelFilters = this.mapFilterState.toggleCategoryLabelFilter(
      this.selectedCategoryLabelFilters,
      filter,
    );
  }

  isMoreFilterSelected(filter: MapFilterOption): boolean {
    return this.selectedCategoryLabelFilters.includes(filter.id);
  }

  clearAll(): void {
    const previousFilters = this.mapFilterState.readSelection();
    const hadEffectiveFilters = previousFilters.categories.length > 0 ||
      previousFilters.categoryLabelIds.length > 0 ||
      this.selectedCategories.length > 0 ||
      this.selectedCategoryLabelFilters.length > 0;

    this.selectedCategories = [];
    this.selectedCategoryLabelFilters = [];
    this.mapFilterState.clearSelection();

    if (hadEffectiveFilters) {
      this.analytics.track('filter_applied', {
        metadata: {
          surface: 'map_filters',
          placeTypes: [],
          recommendedTags: [],
          cleared: true,
        },
      });
    }
    this.router.navigate(['/map'], {
      state: { fromFilters: true, cleared: true },
    });
  }

  applyFilters(): void {
    const previousFilters = this.mapFilterState.readSelection();
    const nextCategories = this.mapFilterState.normalizeCategories(this.selectedCategories);
    const nextCategoryLabelIds = this.mapFilterState.normalizeCategoryLabelIds(this.selectedCategoryLabelFilters);
    this.selectedCategories = nextCategories;
    this.selectedCategoryLabelFilters = nextCategoryLabelIds;
    this.mapFilterState.persistSelection(nextCategories, nextCategoryLabelIds);

    if (!this.sameValues(previousFilters.categories, nextCategories) ||
      !this.sameValues(previousFilters.categoryLabelIds, nextCategoryLabelIds)) {
      this.analytics.track('filter_applied', {
        metadata: {
          surface: 'map_filters',
          placeTypes: nextCategories,
          recommendedTags: this.mapFilterState.labelsForCategoryLabelIds(nextCategoryLabelIds),
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

  private sameValues(left: string[], right: string[]): boolean {
    if (left.length !== right.length) return false;
    return left.every((value, index) => value === right[index]);
  }
}
