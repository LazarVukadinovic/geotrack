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
import { PopularNearbySliderComponent } from '../../components/popular-nearby-slider/popular-nearby-slider.component';
import { TopStaysSliderComponent } from '../../components/top-stays-slider/top-stays-slider.component';
import { MustSeeSliderComponent } from '../../components/must-see-slider/must-see-slider.component';
import { LandmarksSliderComponent } from '../../components/landmarks-slider/landmarks-slider.component';
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
    PopularNearbySliderComponent,
    TopStaysSliderComponent,
    MustSeeSliderComponent,
    LandmarksSliderComponent,
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

  protected readonly eventsThisWeek = computed(() => {
    const q = this.searchQuery().trim().toLowerCase();
    if (!q) return this.explore.eventsThisWeek();
    return this.explore.eventsThisWeek().filter((ev) =>
      [ev.title, ev.subtitle, ev.dateLabel].join(' ').toLowerCase().includes(q),
    );
  });

  protected readonly popularNearby = computed(() => this.explore.popularNearby());
  protected readonly topStays = computed(() => this.explore.topStays());
  protected readonly mustSeeAttractions = computed(() => this.explore.mustSeeAttractions());
  protected readonly landmarkCategories = computed(() => this.explore.landmarkCategories());

  protected readonly filteredLocalFavorites = computed(() => {
    const q = this.searchQuery();
    const cat = this.activeCategory();
    return this.explore.localFavorites().filter((item) => itemMatchesExploreFilters(item, q, cat));
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

  protected toggleNearbySaved(id: string): void {
    this.explore.toggleSaved(id, 'popularNearby');
  }

  protected toggleStaySaved(id: string): void {
    this.explore.toggleSaved(id, 'topStays');
  }

  protected toggleLocalSaved(id: string): void {
    this.explore.toggleSaved(id, 'localFavorites');
  }

  protected onLocalCard(_id: string): void {
    /* future: router.navigate(['/places', id]) */
  }

  protected onPromoCta(): void {
    /* future: router.navigate(['/auth/register']) */
  }

  protected onEvent(_id: string): void {
    /* future: open event detail */
  }

  protected onLandmarkTile(_id: string): void {
    /* future: router.navigate(['/places/category', id]) */
  }

  /** Placeholder — replace with real bottom sheet / modal when the popup design is ready. */
  protected openNearMePopup(): void {
    /* future: open Near Me proximity settings popup */
  }
}
