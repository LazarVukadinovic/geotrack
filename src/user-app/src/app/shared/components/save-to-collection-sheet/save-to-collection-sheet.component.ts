import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { finalize, map, switchMap } from 'rxjs';
import { SaveToCollectionService, SaveToCollectionTarget } from '../../../core/services/save-to-collection.service';
import { Collection, CollectionPlace, CollectionsService } from '../../../features/profile/services/collections';

@Component({
  selector: 'app-save-to-collection-sheet',
  standalone: true,
  templateUrl: './save-to-collection-sheet.component.html',
  styleUrl: './save-to-collection-sheet.component.scss',
})
export class SaveToCollectionSheetComponent {
  private readonly flow = inject(SaveToCollectionService);
  private readonly collectionsService = inject(CollectionsService);

  protected readonly target = this.flow.target;
  protected readonly collections = this.collectionsService.collections;
  protected readonly listLoadState = this.collectionsService.listLoadState;

  protected readonly createMode = signal(false);
  protected readonly newCollectionName = signal('');
  protected readonly isSaving = signal(false);
  protected readonly busyCollectionId = signal<number | null>(null);
  protected readonly feedback = signal('');
  protected readonly error = signal('');

  private hydratedKey = '';

  protected readonly isBusy = computed(() => this.isSaving() || this.busyCollectionId() != null);

  protected readonly savedCollectionCount = computed(() =>
    this.collections().filter(collection => this.isAlreadySaved(collection)).length,
  );

  protected readonly canCreateAndSave = computed(() =>
    !!this.target() &&
    this.newCollectionName().trim().length > 0 &&
    !this.isBusy(),
  );

  constructor() {
    effect(() => {
      const target = this.target();
      if (!target) return;

      untracked(() => {
        this.resetSheetState();
        this.collectionsService.refreshMembershipDetails();
      });
    });

    effect(() => {
      const target = this.target();
      const collections = this.collections();
      if (!target) return;

      const key = `${target.entityType}:${target.entityId}:${collections
        .map(collection => {
          const status = collection.places == null ? 'unknown' : collection.places
            .map(item => `${item.entityType}:${item.entityId}`)
            .sort()
            .join('|');
          return `${collection.id}:${collection.itemsCount}:${status}`;
        })
        .join(',')}`;
      if (key === this.hydratedKey) return;
      this.hydratedKey = key;

      untracked(() => {
        for (const collection of collections) {
          const state = this.collectionsService.getDetailLoadState(collection.id);
          if (collection.places == null && state !== 'loading') {
            this.collectionsService.loadDetails(collection.id);
          }
        }
      });
    });
  }

  protected close(): void {
    if (this.isBusy()) return;
    this.flow.close();
  }

  protected startCreate(): void {
    if (this.isBusy()) return;
    this.error.set('');
    this.feedback.set('');
    this.createMode.set(true);
  }

  protected cancelCreate(): void {
    if (this.isBusy()) return;
    this.createMode.set(false);
    this.newCollectionName.set('');
  }

  protected onNameInput(event: Event): void {
    this.newCollectionName.set((event.target as HTMLInputElement).value);
  }

  protected retryCollections(): void {
    this.collectionsService.refreshMembershipDetails();
  }

  protected toggleCollectionMembership(collection: Collection): void {
    const target = this.target();
    if (!target || this.isBusy() || !this.isMembershipKnown(collection)) return;

    const savedItem = this.savedItemForCollection(collection);
    if (savedItem) {
      this.removeFromCollection(collection, savedItem, target);
      return;
    }

    this.addToCollection(collection, target);
  }

  private addToCollection(collection: Collection, target: SaveToCollectionTarget): void {
    this.error.set('');
    this.feedback.set('');
    this.busyCollectionId.set(collection.id);

    this.collectionsService.addItem(collection.id, this.toCollectionItem(target)).pipe(
      finalize(() => this.busyCollectionId.set(null)),
    ).subscribe({
      next: () => this.finishSuccessfulSave(collection.title),
      error: (err) => this.handleSaveError(err),
    });
  }

  private removeFromCollection(
    collection: Collection,
    savedItem: CollectionPlace,
    target: SaveToCollectionTarget,
  ): void {
    this.error.set('');
    this.feedback.set('');
    this.busyCollectionId.set(collection.id);

    this.collectionsService.removeItem(collection.id, savedItem.itemId).pipe(
      finalize(() => this.busyCollectionId.set(null)),
    ).subscribe({
      next: () => {
        const remaining = this.collections().some(candidate =>
          candidate.id !== collection.id &&
          this.isAlreadySavedForTarget(candidate, target),
        );
        this.feedback.set(remaining
          ? `Removed from ${collection.title}.`
          : `Removed from ${collection.title}. This item is no longer saved.`);
        this.error.set('');
      },
      error: (err) => this.handleRemoveError(err),
    });
  }

  protected isCollectionBusy(collection: Collection): boolean {
    return this.busyCollectionId() === collection.id;
  }

  protected createAndSave(): void {
    const target = this.target();
    const name = this.newCollectionName().trim();
    if (!target || !name || !this.canCreateAndSave()) return;

    this.error.set('');
    this.feedback.set('');
    this.isSaving.set(true);

    this.collectionsService.createCollection(name).pipe(
      switchMap((collection) =>
        this.collectionsService.addItem(collection.id, this.toCollectionItem(target)).pipe(
          map(() => collection),
        ),
      ),
      finalize(() => this.isSaving.set(false)),
    ).subscribe({
      next: (collection) => {
        this.createMode.set(false);
        this.newCollectionName.set('');
        this.finishSuccessfulSave(collection.title);
      },
      error: (err) => this.handleSaveError(err),
    });
  }

  protected isAlreadySaved(collection: Collection): boolean {
    const target = this.target();
    return !!target && this.isAlreadySavedForTarget(collection, target);
  }

  protected savedItemForCollection(collection: Collection): CollectionPlace | undefined {
    const target = this.target();
    if (!target) return undefined;
    return (collection.places ?? []).find(item =>
      item.entityType === target.entityType && item.entityId === target.entityId,
    );
  }

  protected isMembershipLoading(collection: Collection): boolean {
    return collection.places == null && !this.isMembershipUnavailable(collection);
  }

  protected isMembershipUnavailable(collection: Collection): boolean {
    const state = this.collectionsService.getDetailLoadState(collection.id);
    return collection.places == null && (state === 'error' || state === 'notFound');
  }

  private isMembershipKnown(collection: Collection): boolean {
    return collection.places != null;
  }

  private isAlreadySavedForTarget(collection: Collection, target: SaveToCollectionTarget): boolean {
    return (collection.places ?? []).some(item =>
      item.entityType === target.entityType && item.entityId === target.entityId,
    );
  }

  private toCollectionItem(target: SaveToCollectionTarget) {
    return {
      entityType: target.entityType,
      entityId: target.entityId,
      title: target.title,
      image: target.imageUrl,
      analyticsEntityType: target.analyticsEntityType,
      surface: target.surface,
    };
  }

  private finishSuccessfulSave(collectionTitle: string): void {
    this.feedback.set(`Saved to ${collectionTitle}.`);
    this.error.set('');
  }

  private handleSaveError(err: unknown): void {
    const message = this.extractErrorMessage(err);
    if (message.toLowerCase().includes('already exists')) {
      this.feedback.set('Already saved in that collection.');
      this.error.set('');
      return;
    }

    this.feedback.set('');
    this.error.set(message || 'Could not save this item. Please try again.');
  }

  private handleRemoveError(err: unknown): void {
    const message = this.extractErrorMessage(err);
    this.feedback.set('');
    this.error.set(message || 'Could not remove this item. Please try again.');
  }

  private extractErrorMessage(err: unknown): string {
    if (typeof err === 'object' && err !== null && 'error' in err) {
      const body = (err as { error?: unknown }).error;
      if (typeof body === 'object' && body !== null && 'message' in body) {
        const message = (body as { message?: unknown }).message;
        return typeof message === 'string' ? message : '';
      }
      if (typeof body === 'object' && body !== null && 'error' in body) {
        const message = (body as { error?: unknown }).error;
        return typeof message === 'string' ? message : '';
      }
    }
    return '';
  }

  private resetSheetState(): void {
    this.createMode.set(false);
    this.newCollectionName.set('');
    this.isSaving.set(false);
    this.busyCollectionId.set(null);
    this.feedback.set('');
    this.error.set('');
    this.hydratedKey = '';
  }
}
