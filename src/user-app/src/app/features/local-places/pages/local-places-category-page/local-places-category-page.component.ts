import {
  AfterViewInit,
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  signal,
} from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs/operators';
import { LocalPlacesService } from '../../services/local-places.service';
import { LocalPlacesCategoryService } from '../../services/local-places-category.service';
import { LocalPlacesCategoryScrollStateService } from '../../services/local-places-category-scroll-state.service';
import { EXPLORE_IMAGE_FALLBACK } from '../../../../core/constants/explore-media.constants';
import { EmptyStateComponent } from '../../../../shared/components/empty-state/empty-state.component';
import { PageTopNavComponent } from '../../../../shared/components/page-top-nav/page-top-nav.component';

@Component({
  selector: 'app-local-places-category-page',
  standalone: true,
  imports: [EmptyStateComponent, PageTopNavComponent],
  templateUrl: './local-places-category-page.component.html',
  styleUrl: './local-places-category-page.component.scss',
})
export class LocalPlacesCategoryPageComponent implements AfterViewInit {
  private readonly route       = inject(ActivatedRoute);
  private readonly router      = inject(Router);
  private readonly metaService = inject(LocalPlacesService);
  private readonly catService  = inject(LocalPlacesCategoryService);
  private readonly scrollState = inject(LocalPlacesCategoryScrollStateService);
  private readonly elRef       = inject(ElementRef<HTMLElement>);

  protected readonly categoryKey = toSignal(
    this.route.paramMap.pipe(map((p) => p.get('category') ?? '')),
    { initialValue: '' },
  );

  protected readonly meta    = computed(() => this.metaService.getCategoryMeta(this.categoryKey()));
  protected readonly items   = computed(() => this.catService.itemsFor(this.categoryKey())());
  protected readonly loadError = computed(() => this.catService.loadErrorFor(this.categoryKey())());
  protected readonly loadMoreError = computed(() => this.catService.loadMoreErrorFor(this.categoryKey())());
  protected readonly loading = computed(() => this.catService.loadingFor(this.categoryKey())());
  protected readonly loadingMore = computed(() => this.catService.loadingMoreFor(this.categoryKey())());
  protected readonly hasMore = computed(() => this.catService.hasMoreFor(this.categoryKey())());

  protected readonly imgSrcById = signal<Record<string, string>>({});
  private readonly fallbackUsed = new Set<string>();

  constructor() {
    effect(() => {
      const key = this.categoryKey();
      if (key) this.catService.loadCategory(key);
      const next: Record<string, string> = {};
      for (const item of this.catService.itemsFor(key)()) {
        next[item.id] = item.image;
      }
      this.imgSrcById.set(next);
      this.fallbackUsed.clear();
    });
  }

  // ── Scroll restoration ──────────────────────────────────────────────────────

  ngAfterViewInit(): void {
    const state = this.scrollState.pop();
    if (!state) return;

    const host = this.elRef.nativeElement as HTMLElement;

    requestAnimationFrame(() => {
      requestAnimationFrame(() => {
        const scrollEl = host.querySelector<HTMLElement>('.lpc-page__scroll');
        if (!scrollEl) return;

        if (state.clickedItemId) {
          const card = scrollEl.querySelector<HTMLElement>(`[data-lpc-id="${state.clickedItemId}"]`);
          if (card) {
            card.scrollIntoView({ block: 'nearest', behavior: 'instant' });
          } else if (state.scrollTop > 0) {
            scrollEl.scrollTop = state.scrollTop;
          }
        } else if (state.scrollTop > 0) {
          scrollEl.scrollTop = state.scrollTop;
        }
      });
    });
  }

  // ── Interactions ────────────────────────────────────────────────────────────

  protected onImgError(id: string): void {
    if (this.fallbackUsed.has(id)) return;
    this.fallbackUsed.add(id);
    this.imgSrcById.update((m) => ({ ...m, [id]: EXPLORE_IMAGE_FALLBACK }));
  }

  protected toggleSaved(event: Event, id: string): void {
    event.stopPropagation();
    this.catService.toggleSaved(id, this.categoryKey());
  }

  protected onItemCard(id: string): void {
    const host     = this.elRef.nativeElement as HTMLElement;
    const scrollEl = host.querySelector<HTMLElement>('.lpc-page__scroll');

    this.scrollState.save(scrollEl?.scrollTop ?? 0, id);
    this.router.navigate(['/places', id]);
  }

  protected loadMore(): void {
    this.catService.loadMore(this.categoryKey());
  }
}
