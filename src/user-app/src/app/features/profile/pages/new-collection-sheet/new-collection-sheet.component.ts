import { Component, inject, signal, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CollectionsService } from '../../services/collections';

@Component({
  selector: 'app-new-collection-sheet',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './new-collection-sheet.component.html',
})
export class NewCollectionSheetComponent {
  private readonly collService = inject(CollectionsService);

  protected readonly isOpen = signal(false);
  protected readonly name   = signal('');

  @Output() created = new EventEmitter<string>();

  open(): void {
    this.name.set('');
    this.isOpen.set(true);
  }

  close(): void {
    this.isOpen.set(false);
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