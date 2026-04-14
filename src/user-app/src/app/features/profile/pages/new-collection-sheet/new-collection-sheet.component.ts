import { Component, inject, signal, Output, EventEmitter } from '@angular/core';
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
export class NewCollectionSheetComponent {
  private readonly collService = inject(CollectionsService);

  protected readonly isOpen = signal(false);
  protected readonly name   = signal('');
  protected readonly dragOffset = signal(0);
  protected readonly isDragging = signal(false);

  private touchStartY = 0;

  @Output() created = new EventEmitter<string>();

  open(): void {
    this.name.set('');
    this.dragOffset.set(0);
    this.isOpen.set(true);
  }

  close(): void {
     this.dragOffset.set(0);
    this.isOpen.set(false);
  }
  protected onTouchStart(e: TouchEvent): void {
    this.touchStartY = e.touches[0].clientY;
    this.isDragging.set(true);
  }

  protected onTouchMove(e: TouchEvent): void {
    const delta = e.touches[0].clientY - this.touchStartY;
    if (delta > 0) {
      this.dragOffset.set(delta);
    }
  }

  protected onTouchEnd(): void {
    this.isDragging.set(false);
    if (this.dragOffset() >= DISMISS_THRESHOLD) {
      this.close();
    } else {
      this.dragOffset.set(0);
    }
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
    this.collService.addCollection(trimmed);
    const newId = this.collService.collections().at(-1)!.id;
    this.created.emit(newId);
    this.close();
  }

  protected onCancel(): void {
    this.close();
  }
}