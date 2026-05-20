import { Component, inject, signal, Output, EventEmitter, NgZone, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CollectionsService } from '../../services/collections';

const DISMISS_THRESHOLD = 120;

@Component({
  selector: 'app-new-collection-sheet',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './new-collection-sheet.component.html',
  styleUrl: './new-collection-sheet.component.css',
})
export class NewCollectionSheetComponent implements OnDestroy {
  private readonly collService = inject(CollectionsService);
  private readonly ngZone = inject(NgZone);

  protected readonly isOpen = signal(false);
  protected readonly name   = signal('');
  protected readonly dragOffset = signal(0);
  protected readonly isDragging = signal(false);

  private touchStartY = 0;
  private readonly boundMove = this.onDocTouchMove.bind(this);
  private readonly boundEnd  = this.onDocTouchEnd.bind(this);

  @Output() created = new EventEmitter<number>();

  open(): void {
    this.name.set('');
    this.dragOffset.set(0);
    this.isOpen.set(true);
  }

  close(): void {
    this.dragOffset.set(0);
    this.isOpen.set(false);
  }

  ngOnDestroy(): void {
    this.detachDocListeners();
  }

  protected onSheetTouchStart(e: TouchEvent): void {
    this.touchStartY = e.touches[0].clientY;
    this.isDragging.set(true);
    this.ngZone.runOutsideAngular(() => {
      document.addEventListener('touchmove', this.boundMove, { passive: false });
      document.addEventListener('touchend',  this.boundEnd,  { passive: false });
    });
  }

  private onDocTouchMove(e: TouchEvent): void {
    const delta = e.touches[0].clientY - this.touchStartY;
    if (delta <= 0) return;
    e.preventDefault();
    this.ngZone.run(() => this.dragOffset.set(delta));
  }

  private onDocTouchEnd(): void {
    this.detachDocListeners();
    this.ngZone.run(() => {
      this.isDragging.set(false);
      if (this.dragOffset() >= DISMISS_THRESHOLD) {
        this.close();
      } else {
        this.dragOffset.set(0);
      }
    });
  }

  private detachDocListeners(): void {
    document.removeEventListener('touchmove', this.boundMove);
    document.removeEventListener('touchend',  this.boundEnd);
  }

  protected onInput(e: Event): void {
    this.name.set((e.target as HTMLInputElement).value);
  }

  protected clearName(): void {
    this.name.set('');
  }

  protected onSave(): void {
    const trimmed = this.name().trim();
    if (!trimmed) return;
    this.collService.addCollection(trimmed, (id) => {
      this.created.emit(id);
      this.close();
    });
  }

  protected onCancel(): void {
    this.close();
  }
}