import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';

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
    { id: 'restaurants', label: 'Restaurants'},
    { id: 'hotels',      label: 'Hotels'},
    { id: 'attractions', label: 'Attractions'},
    { id: 'events',      label: 'Events'},
    { id: 'shopping',    label: 'Shopping'},
    { id: 'cafes',       label: 'Cafés'},
    { id: 'bars',        label: 'Bars'},
    { id: 'parks',       label: 'Parks'},
    { id: 'museums',     label: 'Museums'},
    { id: 'transport',   label: 'Transport'},
    { id: 'health',      label: 'Health'},
    { id: 'sports',      label: 'Sports'},
  ];

  distances = ['Any', '0.5km', '1km', '2km', '5km'];

  constructor(private router: Router) {
    const saved = sessionStorage.getItem('mapFilters');
    if (saved) {
      const f = JSON.parse(saved);
      this.selectedCategories = f.categories ?? [];
      this.selectedDistance   = f.distance   ?? 'Any';
    }
  }

  toggleCategory(cat: Category): void {
    if (this.isSelected(cat)) {
      this.selectedCategories = this.selectedCategories.filter(id => id !== cat.id);
    } else {
      this.selectedCategories = [...this.selectedCategories, cat.id];
    }
  }

  isSelected(cat: Category): boolean {
    return this.selectedCategories.includes(cat.id);
  }

  clearAll(): void {
    this.selectedCategories = [];
    this.selectedDistance   = 'Any';
    sessionStorage.removeItem('mapFilters');
    this.router.navigate(['/map'], {
      state: { fromFilters: true, cleared: true }
    });
  }

  applyFilters(): void {
    sessionStorage.setItem('mapFilters', JSON.stringify({
      categories: this.selectedCategories,
      distance:   this.selectedDistance,
    }));

    this.router.navigate(['/map'], {
      state: { applyFilters: true, fromFilters: true }
    });
  }

  goBack(): void {
    this.router.navigate(['/map']);
  }
}
