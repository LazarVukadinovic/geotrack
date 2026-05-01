import {
  AfterViewInit,
  Component,
  ElementRef,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { Router } from '@angular/router';
import { EXPLORE_IMAGE_FALLBACK } from '../../../../core/constants/explore-media.constants';
import { AnalyticsService } from '../../../../core/services/analytics.service';
import { LocationService } from '../../../../core/services/location.service';
import { EmptyStateComponent } from '../../../../shared/components/empty-state/empty-state.component';
import { PageTopNavComponent } from '../../../../shared/components/page-top-nav/page-top-nav.component';
import { RecommendationsScrollStateService } from '../../services/recommendations-scroll-state.service';
import { RecommendationsService } from '../../services/recommendations.service';

@Component({
  selector: 'app-recommendations-page',
  standalone: true,
  imports: [EmptyStateComponent, PageTopNavComponent],
  templateUrl: './recommendations-page.component.html',
  styleUrl: './recommendations-page.component.scss',
})
export class RecommendationsPageComponent implements AfterViewInit {
  private readonly recommendations = inject(RecommendationsService);
  private readonly scrollState = inject(RecommendationsScrollStateService);
  private readonly locationSvc = inject(LocationService);
  private readonly router = inject(Router);
  private readonly elRef = inject(ElementRef<HTMLElement>);
  private readonly analytics = inject(AnalyticsService);

  protected readonly items = this.recommendations.items;
  protected readonly filters = this.recommendations.filters;
  protected readonly activeTagId = this.recommendations.activeTagId;
  protected readonly loading = this.recommendations.loading;
  protected readonly loadingMore = this.recommendations.loadingMore;
  protected readonly loadError = this.recommendations.loadError;
  protected readonly loadMoreError = this.recommendations.loadMoreError;
  protected readonly hasSignals = this.recommendations.hasSignals;
  protected readonly emptyReason = this.recommendations.emptyReason;
  protected readonly hasMore = this.recommendations.hasMore;

  protected readonly destinationLabel = computed(() => {
    const destination = this.recommendations.destination();
    if (destination?.name) return destination.name;
    return this.locationSvc.displayCity();
  });
  protected readonly isInitialLoading = computed(() => this.loading() && this.items().length === 0);
  protected readonly showFilters = computed(() => this.filters().length > 0 || this.isInitialLoading());
  protected readonly imgSrcById = signal<Record<string, string>>({});
  private readonly fallbackUsed = new Set<string>();

  constructor() {
    this.locationSvc.tryBackgroundGpsRefresh();

    effect(() => {
      const loc = this.locationSvc.location();
      if (!loc) return;
      untracked(() => this.recommendations.loadInitial());
    });

    this.recommendations.loadInitial();

    effect(() => {
      const next: Record<string, string> = {};
      for (const item of this.items()) {
        next[item.id] = item.image || EXPLORE_IMAGE_FALLBACK;
      }
      this.imgSrcById.set(next);
      this.fallbackUsed.clear();
    });
  }

  ngAfterViewInit(): void {
    const state = this.scrollState.pop();
    if (!state) return;

    const host = this.elRef.nativeElement as HTMLElement;

    requestAnimationFrame(() => {
      requestAnimationFrame(() => {
        const scrollEl = host.querySelector<HTMLElement>('.rec-page__scroll');
        const filtersEl = host.querySelector<HTMLElement>('.rec-page__filters');

        if (scrollEl) {
          if (state.clickedItemId) {
            const card = scrollEl.querySelector<HTMLElement>(
              `[data-rec-id="${state.clickedItemId}"]`,
            );
            if (card) {
              card.scrollIntoView({ block: 'nearest', behavior: 'instant' });
            } else {
              scrollEl.scrollTop = state.scrollTop;
            }
          } else {
            scrollEl.scrollTop = state.scrollTop;
          }
        }

        if (filtersEl) {
          filtersEl.scrollLeft = state.filtersLeft;
        }
      });
    });
  }

  protected setFilter(tagId: number | null): void {
    this.recommendations.setFilter(tagId);
    this.scrollListToTop();
  }

  protected loadMore(): void {
    this.recommendations.loadMore();
  }

  protected retry(): void {
    this.recommendations.refresh();
  }

  protected toggleSaved(event: Event, id: string): void {
    event.stopPropagation();
    this.recommendations.toggleSaved(id);
  }

  protected onImgError(id: string): void {
    if (this.fallbackUsed.has(id)) return;
    this.fallbackUsed.add(id);
    this.imgSrcById.update((m) => ({ ...m, [id]: EXPLORE_IMAGE_FALLBACK }));
  }

  protected onItemCard(id: string): void {
    const host = this.elRef.nativeElement as HTMLElement;
    const scrollEl = host.querySelector<HTMLElement>('.rec-page__scroll');
    const filtersEl = host.querySelector<HTMLElement>('.rec-page__filters');

    this.scrollState.save(
      scrollEl?.scrollTop ?? 0,
      filtersEl?.scrollLeft ?? 0,
      id,
    );

    const entityId = parseInt(id, 10);
    this.analytics.track('detail_view', {
      entityType: 'place',
      entityId: isNaN(entityId) ? undefined : entityId,
      metadata: { surface: 'recommendations' },
    });

    this.router.navigate(['/places', id]);
  }

  private scrollListToTop(): void {
    const host = this.elRef.nativeElement as HTMLElement;
    const scrollEl = host.querySelector<HTMLElement>('.rec-page__scroll');
    if (!scrollEl) return;
    scrollEl.scrollTo({ top: 0, behavior: 'smooth' });
  }
}
