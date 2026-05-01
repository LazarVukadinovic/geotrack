import { Directive, ElementRef, HostListener, Input, inject } from '@angular/core';
import { EXPLORE_IMAGE_FALLBACK } from '../../core/constants/explore-media.constants';

export const EMBEDDED_IMAGE_FALLBACK =
  'data:image/svg+xml,%3Csvg xmlns=%22http://www.w3.org/2000/svg%22 width=%22800%22 height=%22500%22 viewBox=%220 0 800 500%22%3E%3Crect width=%22800%22 height=%22500%22 fill=%22%231a2b4c%22/%3E%3Cpath d=%22M0 320 180 205l145 70 200-130 275 155v200H0z%22 fill=%22%23f8f9fa%22 fill-opacity=%22.14%22/%3E%3Ccircle cx=%22625%22 cy=%22115%22 r=%2242%22 fill=%22%23d97d54%22 fill-opacity=%22.48%22/%3E%3C/svg%3E';

@Directive({
  selector: 'img[appImageFallback]',
  standalone: true,
})
export class ImageFallbackDirective {
  @Input() appImageFallback = EXPLORE_IMAGE_FALLBACK;

  private readonly elementRef = inject(ElementRef<HTMLImageElement>);

  @HostListener('error')
  protected onError(): void {
    const image = this.elementRef.nativeElement;
    const fallback = this.appImageFallback || EXPLORE_IMAGE_FALLBACK;

    if (!this.matchesCurrentSource(fallback)) {
      image.classList.add('image-fallback-active');
      image.src = fallback;
      return;
    }

    if (!this.matchesCurrentSource(EMBEDDED_IMAGE_FALLBACK)) {
      image.classList.add('image-fallback-active');
      image.src = EMBEDDED_IMAGE_FALLBACK;
      return;
    }

    image.removeAttribute('src');
  }

  private matchesCurrentSource(source: string): boolean {
    const image = this.elementRef.nativeElement;
    if (image.getAttribute('src') === source || image.src === source) {
      return true;
    }

    try {
      return image.src === new URL(source, image.ownerDocument.baseURI).href;
    } catch {
      return false;
    }
  }
}
