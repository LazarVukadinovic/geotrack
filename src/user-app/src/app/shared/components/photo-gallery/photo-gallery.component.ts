import { Component, ElementRef, HostListener, OnInit, computed, effect, input, output, signal, viewChild } from '@angular/core';
import type { EntityImage } from '../../../core/models/entity-image.model';
import { ImageFallbackDirective } from '../../directives/image-fallback.directive';

@Component({
  selector: 'app-photo-gallery',
  standalone: true,
  imports: [ImageFallbackDirective],
  templateUrl: './photo-gallery.component.html',
  styleUrl: './photo-gallery.component.scss',
})
export class PhotoGalleryComponent implements OnInit {
  readonly images = input.required<EntityImage[]>();
  readonly title = input<string>('');
  readonly startIndex = input<number>(0);
  readonly manageable = input(false);
  readonly closed = output<void>();
  readonly removed = output<number>();
  readonly changed = output<{ index: number; file: File }>();

  // Logical index (0..n-1) — drives dots, counter, caption
  protected readonly currentIndex = signal(0);

  // Track position in the wrapped array: 1..n are real images, 0 = clone of last, n+1 = clone of first
  protected readonly trackPos = signal(1);

  protected readonly isDragging = signal(false);
  protected readonly dragOffset = signal(0);
  protected readonly animationEnabled = signal(true);

  // [cloneLast, img0, img1, ..., imgN-1, cloneFirst]
  protected readonly wrappedImages = computed(() => {
    const imgs = this.images();
    if (imgs.length <= 1) return imgs;
    return [imgs[imgs.length - 1], ...imgs, imgs[0]];
  });

  protected readonly isAnimated = computed(() => !this.isDragging() && this.animationEnabled());

  protected readonly trackTransform = computed(() => {
    const base = -this.trackPos() * 100;
    const offset = this.dragOffset();
    return offset !== 0
      ? `translateX(calc(${base}% + ${offset}px))`
      : `translateX(${base}%)`;
  });

  private readonly root = viewChild<ElementRef<HTMLElement>>('root');

  private dragStartX = 0;
  private dragStartY = 0;
  private isHorizontalDrag: boolean | undefined = undefined;

  constructor() {
    effect(() => {
      const n = this.images().length;
      if (n === 0) return;

      const idx = Math.max(0, Math.min(this.currentIndex(), n - 1));
      if (idx !== this.currentIndex()) {
        this.currentIndex.set(idx);
      }

      const pos = n <= 1 ? 0 : idx + 1;
      if (pos !== this.trackPos()) {
        this.animationEnabled.set(false);
        this.trackPos.set(pos);
        requestAnimationFrame(() => this.animationEnabled.set(true));
      }
    });
  }

  ngOnInit(): void {
    const n = this.images().length;
    const idx = n > 0 ? Math.max(0, Math.min(this.startIndex(), n - 1)) : 0;
    this.currentIndex.set(idx);
    this.trackPos.set(n <= 1 ? 0 : idx + 1);
    setTimeout(() => this.root()?.nativeElement.focus(), 0);
  }

  protected prevImage(): void {
    const n = this.images().length;
    if (n < 2) return;
    // trackPos 0 is the clone-of-last; going below 0 would be out of bounds
    if (this.trackPos() <= 0) return;
    this.animationEnabled.set(true);
    this.trackPos.update(p => p - 1);
    this.currentIndex.update(i => (i - 1 + n) % n);
  }

  protected nextImage(): void {
    const n = this.images().length;
    if (n < 2) return;
    // trackPos n+1 is the clone-of-first; going above n+1 would be out of bounds
    if (this.trackPos() >= n + 1) return;
    this.animationEnabled.set(true);
    this.trackPos.update(p => p + 1);
    this.currentIndex.update(i => (i + 1) % n);
  }

  protected goToIndex(i: number): void {
    this.animationEnabled.set(true);
    this.trackPos.set(i + 1);
    this.currentIndex.set(i);
  }

  // Called when the CSS transition on the track finishes.
  // If we landed on a clone, silently jump to the real counterpart.
  protected onTrackTransitionEnd(): void {
    const n = this.images().length;
    if (n <= 1) return;
    const pos = this.trackPos();
    if (pos === n + 1) {
      this.animationEnabled.set(false);
      this.trackPos.set(1);
      requestAnimationFrame(() => this.animationEnabled.set(true));
    } else if (pos === 0) {
      this.animationEnabled.set(false);
      this.trackPos.set(n);
      requestAnimationFrame(() => this.animationEnabled.set(true));
    }
  }

  protected close(): void {
    this.closed.emit();
  }

  protected removeCurrentImage(): void {
    this.removed.emit(this.currentIndex());
  }

  protected changeCurrentImage(e: Event): void {
    const input = e.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    this.changed.emit({ index: this.currentIndex(), file });
  }

  @HostListener('keydown', ['$event'])
  protected onKey(e: KeyboardEvent): void {
    if (e.key === 'Escape') { e.stopPropagation(); this.close(); }
    else if (e.key === 'ArrowLeft') this.prevImage();
    else if (e.key === 'ArrowRight') this.nextImage();
  }

  protected onPointerDown(e: PointerEvent): void {
    if (e.pointerType === 'mouse' && e.button !== 0) return;
    this.dragStartX = e.clientX;
    this.dragStartY = e.clientY;
    this.isHorizontalDrag = undefined;
    this.isDragging.set(true);
    this.dragOffset.set(0);
    (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
  }

  protected onPointerMove(e: PointerEvent): void {
    if (!this.isDragging()) return;
    const dx = e.clientX - this.dragStartX;
    const dy = e.clientY - this.dragStartY;

    if (this.isHorizontalDrag === undefined && (Math.abs(dx) > 8 || Math.abs(dy) > 8)) {
      this.isHorizontalDrag = Math.abs(dx) > Math.abs(dy);
    }

    if (this.isHorizontalDrag) {
      e.preventDefault();
      this.dragOffset.set(dx);
    }
  }

  protected onPointerUp(): void {
    if (!this.isDragging()) return;
    this.isDragging.set(false);
    const dx = this.dragOffset();
    this.dragOffset.set(0);
    if (Math.abs(dx) > 55) {
      if (dx < 0) this.nextImage();
      else this.prevImage();
    }
  }

  protected onPointerCancel(): void {
    this.isDragging.set(false);
    this.dragOffset.set(0);
  }
}
