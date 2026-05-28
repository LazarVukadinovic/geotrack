import { Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { ActivatedRoute, ParamMap, Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { EXPLORE_IMAGE_FALLBACK } from '../../../../core/constants/explore-media.constants';
import { DestinationService } from '../../../../core/services/destination.service';
import { LocationService } from '../../../../core/services/location.service';
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
  private readonly locationSvc = inject(LocationService);
  private readonly listing = inject(ExploreSectionListingService);

  private readonly locationRequestKey = computed(() => {
    const mode = this.destinationSvc.mode();
    const destId = this.destinationSvc.id();
    const loc = this.locationSvc.location();
    if (!loc) return `${mode}:${destId ?? 'none'}:none`;
    return `${mode}:${destId ?? 'none'}:${loc.latitude.toFixed(4)}:${loc.longitude.toFixed(4)}:${loc.source}`;
  });

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
  protected readonly targetCollectionId = signal<number | null>(null);

  protected readonly imgSrcById = signal<Record<string, string>>({});
  private readonly fallbackUsed = new Set<string>();

  constructor() {
    this.route.paramMap
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.loadFromRoute());

    this.route.queryParamMap
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((params) => {
        this.targetCollectionId.set(this.readTargetCollectionId(params));
        this.loadFromRoute();
      });

    effect(() => {
      this.locationRequestKey();
      untracked(() => this.loadFromRoute());
    });

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
    void this.router.navigateByUrl(this.withCollectionContext(item.detailRoute || this.defaultDetailRoute(item)));
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
    this.listing.toggleSaved(item, this.targetCollectionId());
  }

  protected canSave(item: ExploreSectionItem): boolean {
    return item.entityType === 'place' || item.entityType === 'stay' || item.entityType === 'event';
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

  private readTargetCollectionId(params: ParamMap = this.route.snapshot.queryParamMap): number | null {
    const collectionId = Number(params.get('collectionId'));
    return Number.isFinite(collectionId) && collectionId > 0 ? collectionId : null;
  }

  private defaultDetailRoute(item: ExploreSectionItem): string {
    if (item.entityType === 'event') return `/events/${item.id}`;
    if (item.entityType === 'stay') return `/stays/${item.id}`;
    return `/places/${item.id}`;
  }

  private withCollectionContext(route: string): string {
    const collectionId = this.targetCollectionId();
    if (collectionId == null || collectionId <= 0) return route;

    const [path, query = ''] = route.split('?');
    const params = new URLSearchParams(query);
    params.set('collectionId', String(collectionId));
    return `${path}?${params.toString()}`;
  }
}