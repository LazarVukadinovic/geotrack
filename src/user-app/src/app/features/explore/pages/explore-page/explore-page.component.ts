import { Component, computed, inject, signal, viewChild } from '@angular/core';
import type { ExploreCategoryFilter } from '../../../../core/models/explore-item.model';
import { EXPLORE_CATEGORY_FILTERS } from '../../../../core/constants/explore-ui.constants';
import { AppHeaderComponent } from '../../../../shared/components/app-header/app-header.component';
import { SearchBarComponent } from '../../../../shared/components/search-bar/search-bar.component';
import { FilterPillsComponent } from '../../../../shared/components/filter-pills/filter-pills.component';
import { WelcomeHeroComponent } from '../../components/welcome-hero/welcome-hero.component';
import { NearbyStatusRowComponent } from '../../components/nearby-status-row/nearby-status-row.component';
import { PromoCardComponent } from '../../components/promo-card/promo-card.component';
import { EventsSliderComponent } from '../../components/events-slider/events-slider.component';
import { RecommendedSliderComponent } from '../../components/recommended-slider/recommended-slider.component';
import { LocalFavoritesListComponent } from '../../components/local-favorites-list/local-favorites-list.component';
import { ExploreService } from '../../services/explore.service';
import { itemMatchesExploreFilters } from '../../utils/explore-filter';

@Component({
  selector: 'app-explore-page',
  standalone: true,
  imports: [
    AppHeaderComponent,
    SearchBarComponent,
    FilterPillsComponent,
    WelcomeHeroComponent,
    NearbyStatusRowComponent,
    PromoCardComponent,
    EventsSliderComponent,
    RecommendedSliderComponent,
    LocalFavoritesListComponent,
  ],
  templateUrl: './explore-page.component.html',
  styleUrl: './explore-page.component.scss',
})
export class ExplorePageComponent {
  private readonly explore = inject(ExploreService);
  private readonly searchBar = viewChild(SearchBarComponent);

  /** Mock hero location — later replace with user / geo service. */
  protected readonly heroCity = signal('Dubrovnik');
  protected readonly heroCountry = signal('Croatia');

  protected readonly categoryOptions = EXPLORE_CATEGORY_FILTERS;
  protected readonly searchQuery = signal('');
  protected readonly activeCategory = signal<ExploreCategoryFilter>('all');
  protected readonly nearbyStatus = computed(() => this.explore.nearbyStatus());
  protected readonly promo = computed(() => this.explore.promo());

  protected readonly filteredRecommended = computed(() => {
    const q = this.searchQuery();
    const cat = this.activeCategory();
    return this.explore.recommended().filter((item) => itemMatchesExploreFilters(item, q, cat));
  });

  protected readonly filteredLocalFavorites = computed(() => {
    const q = this.searchQuery();
    const cat = this.activeCategory();
    return this.explore.localFavorites().filter((item) => itemMatchesExploreFilters(item, q, cat));
  });

  protected readonly filteredEventsThisWeek = computed(() => {
    const q = this.searchQuery().trim().toLowerCase();
    if (!q) {
      return this.explore.eventsThisWeek();
    }
    return this.explore.eventsThisWeek().filter((ev) =>
      [ev.title, ev.subtitle, ev.dateLabel].join(' ').toLowerCase().includes(q),
    );
  });

  protected onSearchChange(value: string): void {
    this.searchQuery.set(value);
  }

  protected onCategoryChange(id: ExploreCategoryFilter): void {
    this.activeCategory.set(id);
  }

  protected onHeaderSearch(): void {
    this.searchBar()?.focusInput();
  }

  protected toggleRecommendedSaved(id: string): void {
    this.explore.toggleSaved(id, 'recommended');
  }

  protected toggleLocalSaved(id: string): void {
    this.explore.toggleSaved(id, 'localFavorites');
  }

  /** Future: router.navigate(['/places', id]) */
  protected onFeaturedMore(_id: string): void {
    /* intentionally empty */
  }

  protected onLocalCard(_id: string): void {
    /* intentionally empty */
  }

  protected onPromoCta(): void {
    /* future: router.navigate(['/auth/register']) */
  }

  protected onEvent(_id: string): void {
    /* future: open event detail */
  }
}
