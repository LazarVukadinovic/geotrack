import {
  AfterViewInit,
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  HostListener,
  Input,
  OnChanges,
  OnDestroy,
  Output,
  SimpleChanges,
  ViewChild,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  COVER_IMAGE_ASPECT_RATIO,
  COVER_IMAGE_ASPECT_RATIO_LABEL,
  COVER_IMAGE_MAX_ZOOM,
  COVER_IMAGE_MIN_OUTPUT_WIDTH,
  COVER_IMAGE_MIN_ZOOM,
  COVER_IMAGE_OUTPUT_MAX_WIDTH,
} from '../../../core/constants/cover-image.constants';

@Component({
  selector: 'app-cover-image-cropper',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './cover-image-cropper.component.html',
  styleUrl: './cover-image-cropper.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CoverImageCropperComponent implements AfterViewInit, OnChanges, OnDestroy {
  @Input({ required: true }) imageFile!: File;
  @Input() aspectRatio = COVER_IMAGE_ASPECT_RATIO;
  @Input() minZoom = COVER_IMAGE_MIN_ZOOM;
  @Input() maxZoom = COVER_IMAGE_MAX_ZOOM;
  @Input() previewLabel = 'Cover preview';

  @Output() cropped = new EventEmitter<File>();
  @Output() cancelled = new EventEmitter<void>();

  @ViewChild('cropFrame') private cropFrame?: ElementRef<HTMLDivElement>;
  @ViewChild('dialogPanel') private dialogPanel?: ElementRef<HTMLElement>;
  @ViewChild('sourceImage') private sourceImage?: ElementRef<HTMLImageElement>;

  imageUrl: string | null = null;
  imageLoaded = false;
  isProcessing = false;
  errorMessage: string | null = null;
  zoom = COVER_IMAGE_MIN_ZOOM;
  translateX = 0;
  translateY = 0;
  baseWidth = 0;
  baseHeight = 0;

  private naturalWidth = 0;
  private naturalHeight = 0;
  private objectUrl: string | null = null;
  private resizeObserver: ResizeObserver | null = null;
  private isDragging = false;
  private dragStartX = 0;
  private dragStartY = 0;
  private dragOriginX = 0;
  private dragOriginY = 0;
  private returnFocusElement: HTMLElement | null = null;

  constructor(private readonly cdr: ChangeDetectorRef) {}

  get aspectRatioCss(): string {
    return `${this.aspectRatio} / 1`;
  }

  get imageLayerTransform(): string {
    return `translate3d(${this.translateX}px, ${this.translateY}px, 0)`;
  }

  get imageTransform(): string {
    return `scale(${this.zoom})`;
  }

  get zoomPercent(): number {
    return Math.round(this.zoom * 100);
  }

  get ratioLabel(): string {
    return this.isDefaultCoverRatio ? COVER_IMAGE_ASPECT_RATIO_LABEL : `${this.aspectRatio.toFixed(2)}:1`;
  }

  private get isDefaultCoverRatio(): boolean {
    return Math.abs(this.aspectRatio - COVER_IMAGE_ASPECT_RATIO) < 0.001;
  }

  ngAfterViewInit(): void {
    this.returnFocusElement = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    this.resizeObserver = new ResizeObserver(() => this.recalculateLayout(false));
    if (this.cropFrame?.nativeElement) {
      this.resizeObserver.observe(this.cropFrame.nativeElement);
    }

    setTimeout(() => this.dialogPanel?.nativeElement.focus(), 0);
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['imageFile']) {
      this.loadObjectUrl();
    }
  }

  ngOnDestroy(): void {
    this.resizeObserver?.disconnect();
    this.revokeObjectUrl();
    this.restoreFocus();
  }

  onImageLoaded(): void {
    const image = this.sourceImage?.nativeElement;
    if (!image?.naturalWidth || !image.naturalHeight) {
      this.errorMessage = 'This image could not be loaded.';
      this.imageLoaded = false;
      this.cdr.markForCheck();
      return;
    }

    this.naturalWidth = image.naturalWidth;
    this.naturalHeight = image.naturalHeight;
    this.imageLoaded = true;
    this.errorMessage = null;
    this.zoom = this.normalizedMinZoom();
    this.translateX = 0;
    this.translateY = 0;
    this.recalculateLayout(true);
    this.cdr.markForCheck();
  }

  onImageError(): void {
    this.imageLoaded = false;
    this.errorMessage = 'This image could not be loaded.';
    this.cdr.markForCheck();
  }

  onPointerDown(event: PointerEvent): void {
    if (!this.imageLoaded || this.isProcessing) return;

    event.preventDefault();
    this.isDragging = true;
    this.dragStartX = event.clientX;
    this.dragStartY = event.clientY;
    this.dragOriginX = this.translateX;
    this.dragOriginY = this.translateY;
    this.cropFrame?.nativeElement.setPointerCapture?.(event.pointerId);
  }

  @HostListener('document:pointermove', ['$event'])
  onPointerMove(event: PointerEvent): void {
    if (!this.isDragging) return;

    this.translateX = this.dragOriginX + event.clientX - this.dragStartX;
    this.translateY = this.dragOriginY + event.clientY - this.dragStartY;
    this.constrainPan();
    this.cdr.markForCheck();
  }

  @HostListener('document:pointerup')
  onPointerUp(): void {
    this.isDragging = false;
  }

  onFrameKeydown(event: KeyboardEvent): void {
    if (!this.imageLoaded || this.isProcessing) return;

    const panStep = event.shiftKey ? 36 : 12;
    let handled = true;

    switch (event.key) {
      case 'ArrowLeft':
        this.translateX -= panStep;
        break;
      case 'ArrowRight':
        this.translateX += panStep;
        break;
      case 'ArrowUp':
        this.translateY -= panStep;
        break;
      case 'ArrowDown':
        this.translateY += panStep;
        break;
      case '+':
      case '=':
        this.zoomBy(0.1);
        return;
      case '-':
      case '_':
        this.zoomBy(-0.1);
        return;
      case '0':
        this.resetCrop();
        return;
      default:
        handled = false;
        break;
    }

    if (handled) {
      event.preventDefault();
      this.constrainPan();
      this.cdr.markForCheck();
    }
  }

  onDialogKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape' && !this.isProcessing) {
      event.preventDefault();
      this.cancel();
      return;
    }

    if (event.key !== 'Tab') return;

    const panel = this.dialogPanel?.nativeElement;
    if (!panel) return;

    const focusable = Array.from(
      panel.querySelectorAll<HTMLElement>(
        'button:not([disabled]), [href], input:not([disabled]), [tabindex]:not([tabindex="-1"])',
      ),
    ).filter(element => !element.hasAttribute('disabled') && element.offsetParent !== null);

    if (focusable.length === 0) return;

    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    const active = document.activeElement;

    if (event.shiftKey && active === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && active === last) {
      event.preventDefault();
      first.focus();
    }
  }

  onZoomInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.setZoom(Number(input.value));
  }

  zoomBy(delta: number): void {
    this.setZoom(this.zoom + delta);
  }

  resetCrop(): void {
    this.zoom = this.normalizedMinZoom();
    this.translateX = 0;
    this.translateY = 0;
    this.constrainPan();
    this.cdr.markForCheck();
  }

  cancel(): void {
    if (this.isProcessing) return;
    this.cancelled.emit();
  }

  async confirmCrop(): Promise<void> {
    if (!this.imageLoaded || this.isProcessing) return;

    const image = this.sourceImage?.nativeElement;
    const frame = this.cropFrame?.nativeElement;
    if (!image || !frame) {
      this.errorMessage = 'Crop preview is not ready yet.';
      this.cdr.markForCheck();
      return;
    }

    this.isProcessing = true;
    this.errorMessage = null;
    this.cdr.markForCheck();

    try {
      const crop = this.visibleSourceRect(frame);
      const outputWidth = Math.max(
        COVER_IMAGE_MIN_OUTPUT_WIDTH,
        Math.min(COVER_IMAGE_OUTPUT_MAX_WIDTH, Math.round(crop.width)),
      );
      const outputHeight = Math.round(outputWidth / this.aspectRatio);
      const canvas = document.createElement('canvas');
      canvas.width = outputWidth;
      canvas.height = outputHeight;

      const context = canvas.getContext('2d', { alpha: false });
      if (!context) {
        throw new Error('Canvas rendering is not available.');
      }

      context.fillStyle = '#f8fafc';
      context.fillRect(0, 0, outputWidth, outputHeight);
      context.imageSmoothingEnabled = true;
      context.imageSmoothingQuality = 'high';
      context.drawImage(
        image,
        crop.x,
        crop.y,
        crop.width,
        crop.height,
        0,
        0,
        outputWidth,
        outputHeight,
      );

      const blob = await this.canvasToBlob(canvas);
      const croppedFile = new File([blob], this.croppedFileName(this.imageFile.name), {
        type: blob.type,
        lastModified: Date.now(),
      });
      this.cropped.emit(croppedFile);
    } catch (error) {
      this.errorMessage = error instanceof Error ? error.message : 'Cover crop could not be generated.';
      this.isProcessing = false;
      this.cdr.markForCheck();
    }
  }

  private loadObjectUrl(): void {
    this.revokeObjectUrl();
    this.imageLoaded = false;
    this.errorMessage = null;
    this.naturalWidth = 0;
    this.naturalHeight = 0;
    this.baseWidth = 0;
    this.baseHeight = 0;
    this.zoom = this.normalizedMinZoom();
    this.translateX = 0;
    this.translateY = 0;

    if (this.imageFile) {
      this.objectUrl = URL.createObjectURL(this.imageFile);
      this.imageUrl = this.objectUrl;
    } else {
      this.imageUrl = null;
    }

    this.cdr.markForCheck();
  }

  private recalculateLayout(resetPan: boolean): void {
    if (!this.imageLoaded || !this.cropFrame?.nativeElement || !this.naturalWidth || !this.naturalHeight) return;

    const frame = this.cropFrame.nativeElement;
    const frameWidth = frame.clientWidth;
    const frameHeight = frame.clientHeight;
    if (frameWidth <= 0 || frameHeight <= 0) return;

    const baseScale = Math.max(frameWidth / this.naturalWidth, frameHeight / this.naturalHeight);
    this.baseWidth = this.naturalWidth * baseScale;
    this.baseHeight = this.naturalHeight * baseScale;

    if (resetPan) {
      this.translateX = 0;
      this.translateY = 0;
    }

    this.constrainPan();
    this.cdr.markForCheck();
  }

  private setZoom(value: number): void {
    const nextZoom = this.clamp(value, this.normalizedMinZoom(), this.normalizedMaxZoom());
    if (Math.abs(nextZoom - this.zoom) < 0.001) return;

    this.zoom = nextZoom;
    this.constrainPan();
    this.cdr.markForCheck();
  }

  private constrainPan(): void {
    const frame = this.cropFrame?.nativeElement;
    if (!frame || !this.baseWidth || !this.baseHeight) return;

    const renderedWidth = this.baseWidth * this.zoom;
    const renderedHeight = this.baseHeight * this.zoom;
    const maxX = Math.max(0, (renderedWidth - frame.clientWidth) / 2);
    const maxY = Math.max(0, (renderedHeight - frame.clientHeight) / 2);

    this.translateX = this.clamp(this.translateX, -maxX, maxX);
    this.translateY = this.clamp(this.translateY, -maxY, maxY);
  }

  private visibleSourceRect(frame: HTMLDivElement): { x: number; y: number; width: number; height: number } {
    const renderedScale = (this.baseWidth * this.zoom) / this.naturalWidth;
    const renderedWidth = this.baseWidth * this.zoom;
    const renderedHeight = this.baseHeight * this.zoom;
    const imageLeft = frame.clientWidth / 2 + this.translateX - renderedWidth / 2;
    const imageTop = frame.clientHeight / 2 + this.translateY - renderedHeight / 2;

    const x = this.clamp(-imageLeft / renderedScale, 0, this.naturalWidth);
    const y = this.clamp(-imageTop / renderedScale, 0, this.naturalHeight);
    const width = this.clamp(frame.clientWidth / renderedScale, 1, this.naturalWidth - x);
    const height = this.clamp(frame.clientHeight / renderedScale, 1, this.naturalHeight - y);

    return { x, y, width, height };
  }

  private canvasToBlob(canvas: HTMLCanvasElement): Promise<Blob> {
    return new Promise((resolve, reject) => {
      canvas.toBlob(
        blob => {
          if (blob) {
            resolve(blob);
          } else {
            reject(new Error('Cover crop could not be generated.'));
          }
        },
        'image/jpeg',
        0.9,
      );
    });
  }

  private croppedFileName(fileName: string): string {
    const baseName = (fileName || 'cover-image')
      .replace(/\.[^.]+$/, '')
      .replace(/[^a-z0-9_-]+/gi, '-')
      .replace(/^-+|-+$/g, '')
      .slice(0, 80) || 'cover-image';

    return `${baseName}-cover-crop.jpg`;
  }

  private normalizedMinZoom(): number {
    return Math.max(1, Number.isFinite(this.minZoom) ? this.minZoom : COVER_IMAGE_MIN_ZOOM);
  }

  private normalizedMaxZoom(): number {
    return Math.max(this.normalizedMinZoom(), Number.isFinite(this.maxZoom) ? this.maxZoom : COVER_IMAGE_MAX_ZOOM);
  }

  private clamp(value: number, min: number, max: number): number {
    return Math.min(Math.max(value, min), max);
  }

  private revokeObjectUrl(): void {
    if (this.objectUrl) {
      URL.revokeObjectURL(this.objectUrl);
      this.objectUrl = null;
    }
  }

  private restoreFocus(): void {
    this.returnFocusElement?.focus?.();
    this.returnFocusElement = null;
  }
}
