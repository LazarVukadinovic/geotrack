import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  Output,
  SimpleChanges,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { finalize, forkJoin, Subscription } from 'rxjs';
import { EntityProfileService } from '../../../core/services/entity-profile.service';
import { EntityProfileDto, EntityProfileEntityType, EntityProfileTagDto } from '../../../core/models/entity-profile.model';

export interface EntityProfileEditContext {
  entityType: EntityProfileEntityType;
  entityId: number;
  entityName: string;
}

interface TagGroup {
  typeName: string;
  tags: SelectableTag[];
}

interface SelectableTag {
  tag: EntityProfileTagDto;
  selected: boolean;
}

@Component({
  selector: 'app-entity-profile-edit-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './entity-profile-edit-modal.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EntityProfileEditModalComponent implements OnChanges, OnDestroy {
  @Input() context: EntityProfileEditContext | null = null;
  @Input() isOpen = false;
  @Output() closed = new EventEmitter<void>();
  @Output() saved = new EventEmitter<EntityProfileDto>();

  isLoading = false;
  isSaving = false;
  isUploadingCard = false;
  isUploadingCover = false;
  isUploadingGallery = false;
  errorMessage: string | null = null;
  successMessage: string | null = null;

  description = '';
  cardImageUrl: string | null = null;
  coverImageUrl: string | null = null;
  galleryImageUrls: string[] = [];
  tagGroups: TagGroup[] = [];

  private sub: Subscription | null = null;

  readonly MAX_GALLERY = 10;

  constructor(
    private readonly svc: EntityProfileService,
    private readonly cdr: ChangeDetectorRef,
  ) {}

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['isOpen'] && this.isOpen && this.context) {
      this.loadProfile();
    }
    if (changes['isOpen'] && !this.isOpen) {
      this.resetState();
    }
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
  }

  close(): void {
    this.closed.emit();
  }

  onBackdropClick(event: MouseEvent): void {
    if ((event.target as HTMLElement).classList.contains('modal-backdrop')) {
      this.close();
    }
  }

  onCardImageSelected(event: Event): void {
    const file = this.getSingleFile(event);
    if (!file) return;
    this.uploadSingle(file, 'card');
  }

  onCoverImageSelected(event: Event): void {
    const file = this.getSingleFile(event);
    if (!file) return;
    this.uploadSingle(file, 'cover');
  }

  onGalleryImagesSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    if (!files.length) return;
    const remaining = this.MAX_GALLERY - this.galleryImageUrls.length;
    const toUpload = files.slice(0, remaining);
    if (!toUpload.length) {
      this.errorMessage = `Gallery is full (max ${this.MAX_GALLERY} images).`;
      this.cdr.markForCheck();
      return;
    }
    this.uploadGallery(toUpload);
    input.value = '';
  }

  removeCard(): void {
    this.cardImageUrl = null;
    this.cdr.markForCheck();
  }

  removeCover(): void {
    this.coverImageUrl = null;
    this.cdr.markForCheck();
  }

  removeGalleryImage(index: number): void {
    this.galleryImageUrls = this.galleryImageUrls.filter((_, i) => i !== index);
    this.cdr.markForCheck();
  }

  toggleTag(tag: SelectableTag): void {
    tag.selected = !tag.selected;
    this.cdr.markForCheck();
  }

  get selectedTagIds(): number[] {
    return this.tagGroups.flatMap(g => g.tags.filter(t => t.selected).map(t => t.tag.id));
  }

  save(): void {
    if (!this.context || this.isSaving) return;

    this.isSaving = true;
    this.errorMessage = null;
    this.successMessage = null;
    this.cdr.markForCheck();

    const request = {
      description: this.description.trim() || null,
      cardImageUrl: this.cardImageUrl,
      coverImageUrl: this.coverImageUrl,
      galleryImageUrls: [...this.galleryImageUrls],
      tagIds: this.selectedTagIds,
    };

    const save$ = this.context.entityType === 'region'
      ? this.svc.updateRegionProfile$(this.context.entityId, request)
      : this.svc.updateTerritoryProfile$(this.context.entityId, request);

    this.sub?.unsubscribe();
    this.sub = save$.pipe(finalize(() => {
      this.isSaving = false;
      this.cdr.markForCheck();
    })).subscribe({
      next: (result) => {
        this.successMessage = 'Profile saved successfully.';
        this.saved.emit(result);
        this.cdr.markForCheck();
        setTimeout(() => {
          this.successMessage = null;
          this.cdr.markForCheck();
        }, 2500);
      },
      error: () => {
        this.errorMessage = 'Failed to save profile. Please try again.';
        this.cdr.markForCheck();
      },
    });
  }

  get isAnyUploading(): boolean {
    return this.isUploadingCard || this.isUploadingCover || this.isUploadingGallery;
  }

  private loadProfile(): void {
    if (!this.context) return;

    this.isLoading = true;
    this.errorMessage = null;
    this.cdr.markForCheck();

    const entityId = this.context.entityId;
    const entityType = this.context.entityType;

    const profile$ = entityType === 'region'
      ? this.svc.getRegionProfile$(entityId)
      : this.svc.getTerritoryProfile$(entityId);

    const tags$ = entityType === 'region'
      ? this.svc.getRegionTags$(entityId)
      : this.svc.getTerritoryTags$(entityId);

    this.sub?.unsubscribe();
    this.sub = forkJoin({ profile: profile$, availableTags: tags$ }).pipe(finalize(() => {
      this.isLoading = false;
      this.cdr.markForCheck();
    })).subscribe({
      next: ({ profile, availableTags }) => {
        this.description = profile.description ?? '';
        this.cardImageUrl = profile.cardImageUrl;
        this.coverImageUrl = profile.coverImageUrl;
        this.galleryImageUrls = [...(profile.galleryImageUrls ?? [])];

        const selectedIds = new Set((profile.tags ?? []).map(t => t.id));
        this.tagGroups = this.buildTagGroups(availableTags, selectedIds);
        this.cdr.markForCheck();
      },
      error: () => {
        this.errorMessage = 'Failed to load profile data.';
        this.cdr.markForCheck();
      },
    });
  }

  private buildTagGroups(tags: EntityProfileTagDto[], selectedIds: Set<number>): TagGroup[] {
    const groupMap = new Map<string, SelectableTag[]>();

    for (const tag of tags) {
      const groupName = tag.tagTypeName ?? 'Other';
      if (!groupMap.has(groupName)) {
        groupMap.set(groupName, []);
      }
      groupMap.get(groupName)!.push({ tag, selected: selectedIds.has(tag.id) });
    }

    return Array.from(groupMap.entries()).map(([typeName, tags]) => ({ typeName, tags }));
  }

  private uploadSingle(file: File, slot: 'card' | 'cover'): void {
    if (!this.context) return;

    if (slot === 'card') this.isUploadingCard = true;
    else this.isUploadingCover = true;
    this.errorMessage = null;
    this.cdr.markForCheck();

    const upload$ = this.context.entityType === 'region'
      ? this.svc.uploadRegionImages$(this.context.entityId, [file])
      : this.svc.uploadTerritoryImages$(this.context.entityId, [file]);

    upload$.pipe(finalize(() => {
      if (slot === 'card') this.isUploadingCard = false;
      else this.isUploadingCover = false;
      this.cdr.markForCheck();
    })).subscribe({
      next: (resp) => {
        const url = resp.images[0]?.url;
        if (url) {
          if (slot === 'card') this.cardImageUrl = url;
          else this.coverImageUrl = url;
          this.cdr.markForCheck();
        }
      },
      error: () => {
        this.errorMessage = `Failed to upload ${slot} image.`;
        this.cdr.markForCheck();
      },
    });
  }

  private uploadGallery(files: File[]): void {
    if (!this.context) return;

    this.isUploadingGallery = true;
    this.errorMessage = null;
    this.cdr.markForCheck();

    const upload$ = this.context.entityType === 'region'
      ? this.svc.uploadRegionImages$(this.context.entityId, files)
      : this.svc.uploadTerritoryImages$(this.context.entityId, files);

    upload$.pipe(finalize(() => {
      this.isUploadingGallery = false;
      this.cdr.markForCheck();
    })).subscribe({
      next: (resp) => {
        const newUrls = resp.images.map(i => i.url).filter(Boolean);
        this.galleryImageUrls = [...this.galleryImageUrls, ...newUrls].slice(0, this.MAX_GALLERY);
        this.cdr.markForCheck();
      },
      error: () => {
        this.errorMessage = 'Failed to upload gallery images.';
        this.cdr.markForCheck();
      },
    });
  }

  private getSingleFile(event: Event): File | null {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    input.value = '';
    return file;
  }

  private resetState(): void {
    this.sub?.unsubscribe();
    this.sub = null;
    this.isLoading = false;
    this.isSaving = false;
    this.isUploadingCard = false;
    this.isUploadingCover = false;
    this.isUploadingGallery = false;
    this.errorMessage = null;
    this.successMessage = null;
    this.description = '';
    this.cardImageUrl = null;
    this.coverImageUrl = null;
    this.galleryImageUrls = [];
    this.tagGroups = [];
  }
}
