import { Component, effect, input, output, signal } from '@angular/core';
import { EXPLORE_IMAGE_FALLBACK } from '../../../core/constants/explore-media.constants';
import type { ExploreItem } from '../../../core/models/explore-item.model';
import { IconButtonComponent } from '../icon-button/icon-button.component';

@Component({
  selector: 'app-destination-card',
  standalone: true,
  imports: [IconButtonComponent],
  templateUrl: './destination-card.component.html',
  styleUrl: './destination-card.component.scss',
})
export class DestinationCardComponent {
  readonly item = input.required<ExploreItem>();

  readonly bookmarkToggled = output<string>();
  readonly cardActivated = output<string>();

  protected readonly imgSrc = signal<string>('');
  private imgFallbackUsed = false;

  constructor() {
    effect(() => {
      const url = this.item().image;
      this.imgSrc.set(url);
      this.imgFallbackUsed = false;
    });
  }

  protected onImageError(): void {
    if (this.imgFallbackUsed) {
      return;
    }
    this.imgFallbackUsed = true;
    this.imgSrc.set(EXPLORE_IMAGE_FALLBACK);
  }

  protected toggleBookmark(): void {
    this.bookmarkToggled.emit(this.item().id);
  }

  protected open(): void {
    this.cardActivated.emit(this.item().id);
  }
}
