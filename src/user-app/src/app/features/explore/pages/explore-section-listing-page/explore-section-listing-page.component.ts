import { Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { EXPLORE_IMAGE_FALLBACK } from '../../../../core/constants/explore-media.constants';
import { DestinationService } from '../../../../core/services/destination.service';
import { ExploreSectionListingService } from '../../services/explore-section-listing.service';
import { PageTopNavComponent } from '../../../../shared/components/page-top-nav/page-top-nav.component';
import { EmptyStateComponent } from '../../../../shared/components/empty-state/empty-state.component';
import { FavoriteButtonComponent } from '../../../../shared/components/favorite-button/favorite-button.component';
import type { ExploreSectionItem } from '../../models/explore-extras.model';

@Component({
  selector: 'app-explore-section-listing-page',
  standalone: true,
  imports: [PageTopNavComponent, EmptyStateComponent, FavoriteButtonComponent],
  templateUrl: './explore-section-listing-page.component.html',
  styleUrl: './explore-section-listing-page.component.scss',
})
export class ExploreSectionListingPageComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly destinationSvc = inject(DestinationService);
  private readonly listing = inject(ExploreSectionListingService);

  protected readonly title = this.listing.title;
  protected readonly subtitle = this.listing.subtitle;
  protected readonly totalCount = this.listing.totalCount;
  protected readonly items = this.listing.items;
  protected readonly loading = this.listing.loading;
  protected readonly loadingMore = this.listing.loadingMore;
  protected readonly loadError = this.listing.loadError;
  protected readonly loadMoreError = this.listing.loadMoreError;
  protected readonly hasMore = this.listing.hasMore;
  protected readonly emptyReason = this.listing.emptyReason;
  protected readonly filters = this.listing.filters;
  protected readonly hasActiveFilters = this.listing.hasActiveFilters;
  protected readonly activeFilterCount = computed(() => this.listing.selectedTagIds().length);
  protected readonly filtersExpanded = signal(false);

  protected readonly imgSrcById = signal<Record<string, string>>({});
  private readonly fallbackUsed = new Set<string>();

  constructor() {
    this.route.paramMap
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.loadFromRoute());

    this.route.queryParamMap
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.loadFromRoute());

    effect(() => {
      const next: Record<string, string> = {};
      for (const item of this.items()) {
        next[this.itemKey(item)] = item.image || EXPLORE_IMAGE_FALLBACK;
      }
      this.imgSrcById.set(next);
      this.fallbackUsed.clear();
    });
  }

  protected openItem(item: ExploreSectionItem): void {
    void this.router.navigateByUrl(item.detailRoute || this.defaultDetailRoute(item));
  }

  protected loadMore(): void {
    this.listing.loadMore();
  }

  protected toggleFilters(): void {
    this.filtersExpanded.update(v => !v);
  }

  protected toggleFilter(tagId: number): void {
    this.listing.toggleTagFilter(tagId);
  }

  protected clearFilters(): void {
    this.listing.clearFilters();
  }

  protected isFilterSelected(tagId: number): boolean {
    return this.listing.isTagSelected(tagId);
  }

  protected saveItem(item: ExploreSectionItem): void {
    this.listing.toggleSaved(item);
  }

  protected canSave(item: ExploreSectionItem): boolean {
    return item.entityType === 'place' || item.entityType === 'stay';
  }

  protected itemKey(item: ExploreSectionItem): string {
    return `${item.entityType}:${item.id}`;
  }

  protected onImgError(item: ExploreSectionItem): void {
    const key = this.itemKey(item);
    if (this.fallbackUsed.has(key)) return;

    this.fallbackUsed.add(key);
    this.imgSrcById.update(map => ({
      ...map,
      [key]: EXPLORE_IMAGE_FALLBACK,
    }));
  }

  private loadFromRoute(): void {
    const sectionKey = this.route.snapshot.paramMap.get('sectionKey')?.trim();
    const destinationId = this.resolveDestinationId();

    if (!sectionKey || destinationId == null) {
      return;
    }

    this.listing.load(sectionKey, destinationId);
  }

  private resolveDestinationId(): number | null {
    const fromQuery = Number(this.route.snapshot.queryParamMap.get('destinationId'));
    if (Number.isFinite(fromQuery) && fromQuery > 0) {
      return fromQuery;
    }

    const fromState = this.destinationSvc.id();
    return fromState != null && fromState > 0 ? fromState : null;
  }

  private defaultDetailRoute(item: ExploreSectionItem): string {
    if (item.entityType === 'event') return `/events/${item.id}`;
    if (item.entityType === 'stay') return `/stays/${item.id}`;
    return `/places/${item.id}`;
  }
}