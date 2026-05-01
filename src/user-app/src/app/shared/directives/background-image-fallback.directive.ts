import { Directive, ElementRef, Input, OnChanges, SimpleChanges, inject } from '@angular/core';
import { EXPLORE_IMAGE_FALLBACK } from '../../core/constants/explore-media.constants';
import { EMBEDDED_IMAGE_FALLBACK } from './image-fallback.directive';

@Directive({
  selector: '[appBackgroundImageFallback]',
  standalone: true,
})
export class BackgroundImageFallbackDirective implements OnChanges {
  @Input() appBackgroundImageFallback: string | null | undefined;
  @Input() backgroundImageFallback = EXPLORE_IMAGE_FALLBACK;
  @Input() backgroundImageFallbackSize = 'cover';
  @Input() backgroundImageFallbackPosition = '50% 50%';
  @Input() backgroundImageFallbackRepeat = 'no-repeat';

  private readonly elementRef = inject(ElementRef<HTMLElement>);
  private loadVersion = 0;

  ngOnChanges(_changes: SimpleChanges): void {
    this.applyImage();
  }

  private applyImage(): void {
    const element = this.elementRef.nativeElement;
    const source = this.appBackgroundImageFallback?.trim();
    this.loadVersion += 1;
    const version = this.loadVersion;

    element.style.backgroundSize = this.backgroundImageFallbackSize || 'cover';
    element.style.backgroundPosition = this.backgroundImageFallbackPosition || '50% 50%';
    element.style.backgroundRepeat = this.backgroundImageFallbackRepeat || 'no-repeat';

    if (!source) {
      element.style.backgroundImage = 'none';
      return;
    }

    this.loadIntoBackground(source, version, () => {
      this.loadIntoBackground(this.backgroundImageFallback || EXPLORE_IMAGE_FALLBACK, version, () => {
        this.setBackgroundImage(EMBEDDED_IMAGE_FALLBACK, version);
      });
    });
  }

  private loadIntoBackground(source: string, version: number, onError: () => void): void {
    const image = new Image();
    image.onload = () => this.setBackgroundImage(source, version);
    image.onerror = onError;
    image.src = source;
  }

  private setBackgroundImage(source: string, version: number): void {
    if (version !== this.loadVersion) {
      return;
    }

    const escaped = source.replace(/"/g, '\\"');
    this.elementRef.nativeElement.style.backgroundImage = `url("${escaped}")`;
  }
}
