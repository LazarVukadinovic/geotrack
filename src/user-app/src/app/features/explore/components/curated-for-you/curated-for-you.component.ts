import { Component, effect, input, output, signal } from '@angular/core';
import { EXPLORE_IMAGE_FALLBACK } from '../../../../core/constants/explore-media.constants';
import { EmptyStateComponent } from '../../../../shared/components/empty-state/empty-state.component';
import type { ExploreCuratedItem } from '../../models/explore-extras.model';
import { SectionHeaderComponent } from '../../../../shared/components/section-header/section-header.component';
import { FavoriteButtonComponent } from '../../../../shared/components/favorite-button/favorite-button.component';

@Component({
  selector: 'app-curated-for-you',
  standalone: true,
  imports: [SectionHeaderComponent, EmptyStateComponent, FavoriteButtonComponent],
  templateUrl: './curated-for-you.component.html',
  styleUrl: './curated-for-you.component.scss',
})
export class CuratedForYouComponent {
  readonly items = input.required<ExploreCuratedItem[]>();
  readonly loading = input(false);

  readonly bookmarkToggled = output<string>();
  readonly cardActivated   = output<string>();
  readonly sectionMore     = output<void>();

  protected readonly imgSrcById = signal<Record<string, string>>({});
  private readonly fallbackUsed = new Set<string>();

  constructor() {
    effect(() => {
      const next: Record<string, string> = {};
      for (const item of this.items()) {
        next[item.id] = item.image;
      }
      this.imgSrcById.set(next);
      this.fallbackUsed.clear();
    });
  }

  protected onCardClick(id: string): void {
    this.cardActivated.emit(id);
  }

  protected onBookmarkClick(id: string): void {
    this.bookmarkToggled.emit(id);
  }

  protected onImgError(id: string): void {
    if (this.fallbackUsed.has(id)) return;
    this.fallbackUsed.add(id);
    this.imgSrcById.update((m) => ({ ...m, [id]: EXPLORE_IMAGE_FALLBACK }));
  }
}
