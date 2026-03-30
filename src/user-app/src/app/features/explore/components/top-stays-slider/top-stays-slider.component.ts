import { Component, effect, input, output, signal } from '@angular/core';
import { EXPLORE_IMAGE_FALLBACK } from '../../../../core/constants/explore-media.constants';
import type { ExploreStay } from '../../models/explore-extras.model';
import { SectionHeaderComponent } from '../../../../shared/components/section-header/section-header.component';

@Component({
  selector: 'app-top-stays-slider',
  standalone: true,
  imports: [SectionHeaderComponent],
  templateUrl: './top-stays-slider.component.html',
  styleUrl: './top-stays-slider.component.scss',
})
export class TopStaysSliderComponent {
  readonly items = input.required<ExploreStay[]>();

  readonly bookmarkToggled = output<string>();
  readonly sectionMore = output<void>();

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

  protected onImgError(id: string): void {
    if (this.fallbackUsed.has(id)) return;
    this.fallbackUsed.add(id);
    this.imgSrcById.update((m) => ({ ...m, [id]: EXPLORE_IMAGE_FALLBACK }));
  }

  protected toggleBookmark(event: Event, id: string): void {
    event.stopPropagation();
    this.bookmarkToggled.emit(id);
  }
}
