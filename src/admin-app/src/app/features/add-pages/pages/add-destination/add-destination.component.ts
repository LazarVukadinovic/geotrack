import {
  AfterViewInit,
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  inject,
  NgZone,
  OnDestroy,
  OnInit,
  ViewChild,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router } from '@angular/router';
import { finalize, forkJoin, map, of, Subscription, switchMap } from 'rxjs';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { CoverImageCropperComponent } from '../../../../shared/components/cover-image-cropper/cover-image-cropper.component';
import { DashboardService } from '../../../../core/services/dashboard.service';
import { LightboxService } from '../../../../core/services/lightbox.service';
import { DestinationsListService } from '../../../../core/services/destinations-list.service';
import { RegionalMediaService } from '../../../../core/services/regional-media.service';
import { AuthService } from '../../../../core/services/auth.service';
import { AdminMapService } from '../../../../core/services/admin-map.service';
import { MapTileLayerService } from '../../../../core/services/map-tile-layer.service';
import { AdminFormDraftService } from '../../../../core/services/admin-form-draft.service';
import { COVER_IMAGE_ASPECT_RATIO } from '../../../../core/constants/cover-image.constants';
import { RegionalAssignedRegion } from '../../../../core/models/dashboard.model';
import { AdminMapViewport, AdminMapZone, RegionalScopeValidationResult } from '../../../../core/models/admin-map.model';
import {
  RegionalDestinationCreateRequest,
  RegionalDestinationTagDto,
  RegionalDestinationUpdateRequest,
} from '../../../../core/models/destination.model';
import { normalizeEditorDescriptionHtml } from '../../../../shared/utils/description-formatting';
import {
  applyAssignedRegionViewport,
  assignedRegionZone,
  createRegionalMapPinIcon,
  isPreciseAssignedRegionBoundary,
  renderAssignedRegionBoundary,
} from '../../shared/regional-creation-map';

declare const L: any;

interface ClassItem {
  tag: RegionalDestinationTagDto;
  checked: boolean;
}

interface DestinationFormDraft {
  activeRegionId: number | null;
  destinationName: string;
  summary: string;
  descriptionHtml: string;
  selectedTagIds: number[];
  centerLat: number | null;
  centerLng: number | null;
  zoneConfirmed: boolean;
  cardImageName: string;
  coverImageName: string;
  galleryImageNames: string[];
}

@Component({
  selector: 'app-add-destination',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent, CoverImageCropperComponent],
  templateUrl: './add-destination.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: [
    `
      .rte-field {
        position: relative;
      }
      .rte-ph {
        position: absolute;
        top: 0;
        left: 0;
        right: 0;
        padding: 0.5rem 0.75rem;
        color: #9ca3af;
        font-size: 0.875rem;
        pointer-events: none;
        user-select: none;
        line-height: 1.5rem;
      }
    `,
  ],
})
export class AddDestinationComponent implements OnInit, AfterViewInit, OnDestroy {
  private static readonly activeRegionStorageKey = 'geotrack_admin_active_region_id';
  private static readonly initialVisibleClassificationTags = 5;
  private readonly mapTileLayer = inject(MapTileLayerService);
  readonly coverImageAspectRatio = COVER_IMAGE_ASPECT_RATIO;
  readonly coverImageAspectRatioCss = `${COVER_IMAGE_ASPECT_RATIO} / 1`;

  @ViewChild('editorRef') editorRef!: ElementRef<HTMLDivElement>;
  @ViewChild('miniMapContainer') miniMapContainer!: ElementRef<HTMLDivElement>;
  @ViewChild('modalMapContainer') modalMapContainerRef!: ElementRef<HTMLDivElement>;

  assignedRegions: RegionalAssignedRegion[] = [];
  activeRegionId: number | null = null;
  regionLoading = false;
  tagsLoading = false;
  tagSearchLoading = false;
  isSaving = false;
  isEditMode = false;
  errorMessage: string | null = null;
  feedbackMessage: string | null = null;
  regionBoundaryMissingMessage: string | null = null;
  scopeValidationMessage: string | null = null;
  scopeValidationTone: 'success' | 'warning' | 'error' | null = null;
  scopeValidationPending = false;

  destinationName = '';
  summary = '';
  tagInput = '';
  tagFilter = '';

  classItems: ClassItem[] = [];
  destinationTags: RegionalDestinationTagDto[] = [];
  additionalTagResults: RegionalDestinationTagDto[] = [];
  selectedTagIds = new Set<number>();
  classificationExpanded = false;

  isBold = false;
  isItalic = false;
  isUnderline = false;
  editorIsEmpty = true;

  cardImageUrl: string | null = null;
  cardImageName = '';
  coverImageUrl: string | null = null;
  coverImageName = '';
  coverCropFile: File | null = null;
  galleryImages: { url: string; name: string; file: File }[] = [];

  private cardImageFile: File | null = null;
  private coverImageFile: File | null = null;
  private coverPreviewObjectUrl: string | null = null;

  zoneModalOpen = false;
  zoneConfirmed = false;
  centerLat: number | null = null;
  centerLng: number | null = null;

  private miniMap: any = null;
  private modalMap: any = null;
  private miniRegionBoundaryLayer: any = null;
  private modalRegionBoundaryLayer: any = null;
  private miniMapCenterMarker: any = null;
  private modalCenterMarker: any = null;
  private assignedRegionsSubscription: Subscription | null = null;
  private regionMapSubscription: Subscription | null = null;
  private validationSubscription: Subscription | null = null;
  private tagsSubscription: Subscription | null = null;
  private tagSearchSubscription: Subscription | null = null;
  private createSubscription: Subscription | null = null;
  private editDestinationId: number | null = null;
  private originalLatitude: number | null = null;
  private originalLongitude: number | null = null;
  private tagSearchHandle: ReturnType<typeof setTimeout> | null = null;
  private regionViewport: AdminMapViewport | null = null;
  private regionBoundaryZone: AdminMapZone | null = null;
  private scopeValidationResult: RegionalScopeValidationResult | null = null;
  private unregisterDraftSource: (() => void) | null = null;

  constructor(
    private readonly zone: NgZone,
    private readonly cdr: ChangeDetectorRef,
    private readonly router: Router,
    private readonly route: ActivatedRoute,
    private readonly dashboardService: DashboardService,
    private readonly lightboxService: LightboxService,
    private readonly destinationsService: DestinationsListService,
    private readonly regionalMedia: RegionalMediaService,
    private readonly auth: AuthService,
    private readonly adminMapService: AdminMapService,
    private readonly formDrafts: AdminFormDraftService,
  ) {}

  ngOnInit(): void {
    const editId = Number(this.route.snapshot.paramMap.get('id'));
    this.isEditMode = Number.isFinite(editId) && editId > 0;
    this.editDestinationId = this.isEditMode ? editId : null;

    this.loadAssignedRegions();
    this.loadDestinationTags();
    this.registerDraftSource();
  }

  ngAfterViewInit(): void {
    setTimeout(() => this.initMiniMap(), 0);
    setTimeout(() => this.restoreSavedDraftIfPresent(), 0);
  }

  ngOnDestroy(): void {
    this.assignedRegionsSubscription?.unsubscribe();
    this.regionMapSubscription?.unsubscribe();
    this.validationSubscription?.unsubscribe();
    this.tagsSubscription?.unsubscribe();
    this.tagSearchSubscription?.unsubscribe();
    this.createSubscription?.unsubscribe();
    this.unregisterDraftSource?.();

    if (this.tagSearchHandle) {
      clearTimeout(this.tagSearchHandle);
    }

    this.destroyModalMap();
    this.revokeCoverPreviewObjectUrl();
    this.lightboxService.close();
    if (this.miniMap) {
      this.miniMap.remove();
      this.miniMap = null;
    }
  }

  get currentUserName(): string {
    return this.auth.fullName || this.auth.getUser()?.email || '';
  }

  get activeRegion(): RegionalAssignedRegion | null {
    return this.assignedRegions.find((region) => region.id === this.activeRegionId) ?? null;
  }

  get regionDisplayName(): string {
    return this.activeRegion?.displayName ?? '';
  }

  get canPublish(): boolean {
    const mediaReady =
      this.isEditMode || (Boolean(this.cardImageFile) && Boolean(this.coverImageFile));
    return (
      Boolean(this.activeRegionId) &&
      this.destinationName.trim().length > 0 &&
      this.hasValidCenter() &&
      this.zoneConfirmed &&
      !this.regionBoundaryMissingMessage &&
      !this.isScopeValidationRejected &&
      mediaReady &&
      !this.regionLoading &&
      !this.tagsLoading &&
      !this.isSaving
    );
  }

  get selectedAdditionalTags(): RegionalDestinationTagDto[] {
    return this.destinationTags
      .filter((tag) => this.selectedTagIds.has(tag.id))
      .sort((a, b) => a.name.localeCompare(b.name));
  }

  get visibleClassItems(): ClassItem[] {
    const query = this.tagFilter.trim().toLocaleLowerCase();
    if (!query) return this.classItems;

    return this.classItems.filter((item) =>
      item.tag.name.toLocaleLowerCase().includes(query) ||
      item.tag.slug.toLocaleLowerCase().includes(query),
    );
  }

  get showClassificationToggle(): boolean {
    return false;
  }

  get visibleClassificationLimit(): number {
    return this.classificationExpanded
      ? this.classItems.length
      : AddDestinationComponent.initialVisibleClassificationTags;
  }

  get remainingClassificationCount(): number {
    return Math.max(0, this.classItems.length - this.visibleClassItems.length);
  }

  get createdDateDisplay(): string {
    return new Date().toLocaleDateString('en-US', {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
    });
  }

  get drawHint(): string {
    return this.hasValidCenter()
      ? `Selected center: ${this.centerLat!.toFixed(5)}, ${this.centerLng!.toFixed(5)}`
      : 'Click the map to place the destination center point. No polygon is drawn.';
  }

  get isScopeValidationRejected(): boolean {
    return this.scopeValidationResult?.isAllowed === false;
  }

  get canConfirmZone(): boolean {
    return this.hasValidCenter() &&
      !this.scopeValidationPending &&
      this.scopeValidationResult?.isAllowed === true;
  }

  loadAssignedRegions(): void {
    this.regionLoading = true;
    this.errorMessage = null;
    this.notifyStateChanged();

    this.assignedRegionsSubscription?.unsubscribe();
    this.assignedRegionsSubscription = this.dashboardService
      .getAssignedRegions()
      .pipe(
        finalize(() => {
          this.regionLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: (regions) => {
          this.assignedRegions = regions ?? [];

          if (this.assignedRegions.length === 0) {
            this.activeRegionId = null;
            this.clearPersistedRegion();
            this.errorMessage = 'No active assigned region is available for this account.';
            this.notifyStateChanged();
            return;
          }

          const queryRegionId = Number(this.route.snapshot.queryParamMap.get('regionId'));
          const persistedRegionId = this.readPersistedRegionId();
          const stateRegionId = Number((history.state as { regionId?: number | null })?.regionId);
          const selectedRegion =
            this.assignedRegions.find((region) => region.id === queryRegionId) ??
            this.assignedRegions.find((region) => region.id === stateRegionId) ??
            this.assignedRegions.find((region) => region.id === persistedRegionId) ??
            this.assignedRegions[0];

          this.setActiveRegion(selectedRegion.id, false);
          this.loadAssignedRegionMap(selectedRegion.id);
          this.loadDestinationForEditIfNeeded();
        },
        error: (error) => this.handleLoadError(error, 'Unable to load assigned regions.'),
      });
  }

  onRegionSelected(regionId: number): void {
    if (regionId === this.activeRegionId) return;

    this.setActiveRegion(regionId, true);
    this.resetZone();
    this.loadAssignedRegionMap(regionId);
    this.feedbackMessage = 'Region changed. Destination center was cleared.';
    this.notifyStateChanged();
  }

  loadDestinationTags(): void {
    this.tagsLoading = true;
    this.notifyStateChanged();

    this.tagsSubscription?.unsubscribe();
    this.tagsSubscription = this.destinationsService
      .getDestinationTags()
      .pipe(
        finalize(() => {
          this.tagsLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: (tags) => {
          this.destinationTags = this.normalizeTags(tags);
          this.classItems = this.destinationTags.map((tag) => ({
            tag,
            checked: this.selectedTagIds.has(tag.id),
          }));
        },
        error: (error) => this.handleLoadError(error, 'Unable to load destination tags.'),
      });
  }

  private loadDestinationForEditIfNeeded(): void {
    if (!this.isEditMode || !this.editDestinationId || !this.activeRegionId) {
      return;
    }

    this.destinationsService.getDestination(this.activeRegionId, this.editDestinationId).subscribe({
      next: (dto) => {
        this.destinationName = dto.name ?? '';
        this.summary = dto.summary ?? '';
        this.originalLatitude = dto.latitude ?? null;
        this.originalLongitude = dto.longitude ?? null;
        if (this.hasValidCoordinatePair(dto.latitude, dto.longitude)) {
          this.centerLat = dto.latitude;
          this.centerLng = dto.longitude;
          this.zoneConfirmed = true;
          setTimeout(() => this.showZoneOnMiniMap(), 0);
        }

        this.cardImageUrl = dto.cardImageUrl ?? dto.imageUrl ?? this.cardImageUrl;
        this.cardImageName = this.cardImageUrl ? 'Existing card image' : this.cardImageName;
        this.revokeCoverPreviewObjectUrl();
        this.coverImageUrl = dto.coverImageUrl ?? dto.imageUrl ?? this.coverImageUrl;
        this.coverImageName = this.coverImageUrl ? 'Existing cover image' : this.coverImageName;
        this.galleryImages = (dto.galleryImageUrls ?? []).map((url, index) => ({
          url,
          name: `Existing gallery image ${index + 1}`,
          file: null as unknown as File,
        }));

        this.selectedTagIds = new Set((dto.tags ?? []).map((tag) => tag.id));
        this.classItems = this.classItems.map((item) => ({
          ...item,
          checked: this.selectedTagIds.has(item.tag.id),
        }));

        setTimeout(() => {
          if (this.editorRef?.nativeElement) {
            this.editorRef.nativeElement.innerHTML = normalizeEditorDescriptionHtml(dto.description);
            this.syncEmptyState();
            this.notifyStateChanged();
          }
          this.restoreSavedDraftIfPresent();
        }, 0);

        this.notifyStateChanged();
      },
      error: (error) => this.handleLoadError(error, 'Unable to load destination for editing.'),
    });
  }

  onClassItemChange(item: ClassItem): void {
    if (item.checked) {
      this.selectedTagIds.add(item.tag.id);
    } else {
      this.selectedTagIds.delete(item.tag.id);
    }
    this.notifyStateChanged();
  }

  toggleClassificationExpanded(): void {
    this.classificationExpanded = !this.classificationExpanded;
    this.notifyStateChanged();
  }

  onTagInputChange(): void {
    if (this.tagSearchHandle) {
      clearTimeout(this.tagSearchHandle);
    }

    this.additionalTagResults = [];
    this.tagSearchLoading = false;
    this.notifyStateChanged();
  }

  addTag(event: Event): void {
    event.preventDefault();
    const name = this.tagInput.trim();
    if (name.length < 2 || this.tagSearchLoading) return;

    const existingSelectedTag = this.destinationTags.find(
      tag => tag.name.trim().toLocaleLowerCase() === name.toLocaleLowerCase() && this.selectedTagIds.has(tag.id),
    );
    if (existingSelectedTag) {
      this.tagInput = '';
      this.notifyStateChanged();
      return;
    }

    const tag = this.findTagToAdd();
    if (tag) {
      this.selectAdditionalTag(tag);
      return;
    }

    this.tagSearchLoading = true;
    this.tagSearchSubscription?.unsubscribe();
    this.tagSearchSubscription = this.destinationsService
      .createDestinationTag(name)
      .pipe(
        finalize(() => {
          this.tagSearchLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: (createdTag) => this.selectAdditionalTag(createdTag),
        error: (error) => this.handleLoadError(error, 'Unable to create destination tag.'),
      });
  }

  selectAdditionalTag(tag: RegionalDestinationTagDto): void {
    this.upsertDestinationTag(tag);
    this.selectedTagIds.add(tag.id);
    this.upsertClassItem(tag, true);
    this.tagInput = '';
    this.additionalTagResults = [];
    this.notifyStateChanged();
  }

  removeTag(tag: RegionalDestinationTagDto): void {
    this.selectedTagIds.delete(tag.id);
    const classItem = this.classItems.find((item) => item.tag.id === tag.id);
    if (classItem) {
      classItem.checked = false;
    }
    this.notifyStateChanged();
  }

  onEditorKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter') {
      event.preventDefault();
      document.execCommand('insertLineBreak');
      this.syncToolbarState();
      this.syncEmptyState();
    }
  }

  execFormat(command: 'bold' | 'italic' | 'underline'): void {
    document.execCommand(command, false, undefined);
    this.editorRef.nativeElement.focus();
    this.syncToolbarState();
  }

  onEditorChange(): void {
    this.syncToolbarState();
    this.syncEmptyState();
  }

  onEditorFocus(): void {
    this.syncToolbarState();
    this.syncEmptyState();
  }

  onEditorPaste(event: ClipboardEvent): void {
    const clipboard = event.clipboardData;
    if (!clipboard) return;

    event.preventDefault();
    const html = clipboard.getData('text/html');
    const text = clipboard.getData('text/plain');
    document.execCommand('insertHTML', false, normalizeEditorDescriptionHtml(html || text));
    this.syncToolbarState();
    this.syncEmptyState();
  }

  onEditorBlur(): void {
    this.syncToolbarState();
    const el = this.editorRef?.nativeElement;
    if (
      el &&
      (el.innerHTML === '<br>' || el.innerHTML === '&nbsp;' || el.innerHTML.trim() === '')
    ) {
      el.innerHTML = '';
    } else if (el) {
      el.innerHTML = normalizeEditorDescriptionHtml(el.innerHTML);
    }
    this.syncEmptyState();
  }

  onCardImageSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file || !this.isAllowedImage(file)) {
      input.value = '';
      return;
    }

    const reader = new FileReader();
    reader.onload = () => {
      this.zone.run(() => {
        this.cardImageUrl = reader.result as string;
        this.cardImageName = file.name;
        this.cardImageFile = file;
        this.notifyStateChanged();
      });
    };
    reader.readAsDataURL(file);
    input.value = '';
  }

  removeCardImage(): void {
    this.cardImageUrl = null;
    this.cardImageName = '';
    this.cardImageFile = null;
    this.notifyStateChanged();
  }

  onCoverImageSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file || !this.isAllowedImage(file)) {
      input.value = '';
      return;
    }

    this.coverCropFile = file;
    input.value = '';
    this.notifyStateChanged();
  }

  onCoverCropConfirmed(file: File): void {
    this.revokeCoverPreviewObjectUrl();
    const previewUrl = URL.createObjectURL(file);
    this.coverPreviewObjectUrl = previewUrl;
    this.coverImageUrl = previewUrl;
    this.coverImageName = file.name;
    this.coverImageFile = file;
    this.coverCropFile = null;
    this.notifyStateChanged();
  }

  onCoverCropCancelled(): void {
    this.coverCropFile = null;
    this.notifyStateChanged();
  }

  removeCoverImage(): void {
    this.revokeCoverPreviewObjectUrl();
    this.coverImageUrl = null;
    this.coverImageName = '';
    this.coverImageFile = null;
    this.coverCropFile = null;
    this.notifyStateChanged();
  }

  onGalleryImagesSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []).filter((file) => this.isAllowedImage(file));
    for (const file of files) {
      const reader = new FileReader();
      reader.onload = () => {
        this.zone.run(() => {
          this.galleryImages = [
            ...this.galleryImages,
            { url: reader.result as string, name: file.name, file },
          ];
          this.notifyStateChanged();
        });
      };
      reader.readAsDataURL(file);
    }
    input.value = '';
  }

  removeGalleryImage(index: number): void {
    this.galleryImages = this.galleryImages.filter((_, currentIndex) => currentIndex !== index);
    this.notifyStateChanged();
  }

  openLightbox(images: { url: string; name: string }[], index: number): void {
    this.lightboxService.open(images, index);
  }

  openZoneModal(): void {
    this.zoneModalOpen = true;
    this.notifyStateChanged();
    setTimeout(() => this.initModalMap(this.modalMapContainerRef.nativeElement), 50);
    if (this.hasValidCenter()) {
      this.validateSelectedPoint(this.centerLat as number, this.centerLng as number);
    }
  }

  closeZoneModal(): void {
    this.zoneModalOpen = false;
    this.destroyModalMap();
    this.notifyStateChanged();
  }

  onModalBackdropClick(_event: MouseEvent): void {
    this.closeZoneModal();
  }

  clearZone(): void {
    this.centerLat = null;
    this.centerLng = null;
    this.zoneConfirmed = false;
    this.errorMessage = null;
    this.clearScopeValidation();
    this.showCenterOnModalMap();
    this.removeMiniMapCenterMarker();
    this.applyRegionViewportToMiniMap();
    this.notifyStateChanged();
  }

  confirmZone(): void {
    if (!this.hasValidCenter()) {
      this.errorMessage = 'Choose a destination center point before confirming.';
      this.notifyStateChanged();
      return;
    }

    if (this.isScopeValidationRejected) {
      this.errorMessage = this.scopeValidationMessage ?? 'Selected location is outside your allowed regional scope.';
      this.notifyStateChanged();
      return;
    }

    if (this.scopeValidationPending || this.scopeValidationResult?.isAllowed !== true) {
      this.errorMessage = this.scopeValidationPending
        ? 'Location validation is still running.'
        : this.scopeValidationMessage ?? 'Selected location could not be validated against your assigned regional scope.';
      this.notifyStateChanged();
      return;
    }

    this.zoneConfirmed = true;
    this.errorMessage = null;
    this.showZoneOnMiniMap();
    this.closeZoneModal();
  }

  publish(): void {
    if (this.isSaving) return;

    if (this.isEditMode) {
      this.updateExistingDestination();
      return;
    }

    const payload = this.buildCreatePayload();
    if (!payload) {
      this.notifyStateChanged();
      return;
    }

    this.isSaving = true;
    this.errorMessage = null;
    this.feedbackMessage = null;
    this.notifyStateChanged();

    this.createSubscription?.unsubscribe();
    this.createSubscription = this.destinationsService
      .createDestination(payload)
      .pipe(
        finalize(() => {
          this.isSaving = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: (created) => {
          this.formDrafts.clearDraft(this.draftFormType());
          this.feedbackMessage = 'Destination published.';
          this.router.navigate(['/destinations'], {
            queryParams: { regionId: created.regionId },
            state: { feedbackMessage: 'Destination published.' },
          });
        },
        error: (error) => this.handleActionError(error),
      });
  }

  private updateExistingDestination(): void {
    const regionId = this.activeRegionId;
    const destinationId = this.editDestinationId;
    const payload = this.buildUpdatePayload();
    if (!regionId || !destinationId || !payload) {
      this.notifyStateChanged();
      return;
    }

    this.isSaving = true;
    this.errorMessage = null;
    this.feedbackMessage = null;
    this.notifyStateChanged();

    this.createSubscription?.unsubscribe();
    const payload$ = this.coverImageFile
      ? this.regionalMedia.uploadImages(regionId, [this.coverImageFile]).pipe(
          map(response => {
            const uploadedCover = response.images[0]?.url;
            if (!uploadedCover) {
              throw new Error('Cover image upload did not return a URL.');
            }

            return { ...payload, coverImageUrl: uploadedCover };
          }),
        )
      : of(payload);

    this.createSubscription = payload$
      .pipe(
        switchMap(preparedPayload => this.destinationsService.updateDestination(regionId, destinationId, preparedPayload)),
      )
      .pipe(
        finalize(() => {
          this.isSaving = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: (updated) => {
          this.formDrafts.clearDraft(this.draftFormType());
          this.feedbackMessage = 'Destination updated.';
          this.router.navigate(['/destinations/detail', updated.id], {
            queryParams: { regionId: updated.regionId },
            state: { regionId: updated.regionId, feedbackMessage: 'Destination updated.' },
          });
        },
        error: (error) => this.handleActionError(error),
      });
  }

  private buildCreatePayload(): RegionalDestinationCreateRequest | null {
    const regionId = this.activeRegionId;
    if (!regionId) {
      this.errorMessage = 'Select an assigned region before publishing.';
      return null;
    }

    const name = this.destinationName.trim();
    if (!name) {
      this.errorMessage = 'Destination name is required.';
      return null;
    }

    const normalizedSummary = this.summary.trim();
    if (normalizedSummary.length > 160) {
      this.errorMessage = 'Summary must be 160 characters or less.';
      return null;
    }

    if (!this.cardImageFile) {
      this.errorMessage = 'Card image is required.';
      return null;
    }

    if (!this.coverImageFile) {
      this.errorMessage = 'Cover image is required.';
      return null;
    }

    if (!this.hasValidCenter()) {
      this.errorMessage = 'Choose a destination center point before publishing.';
      return null;
    }

    if (!this.zoneConfirmed) {
      this.errorMessage = 'Confirm the destination center point before publishing.';
      return null;
    }

    if (this.scopeValidationPending || this.scopeValidationResult?.isAllowed !== true) {
      this.errorMessage = this.scopeValidationPending
        ? 'Location validation is still running.'
        : this.scopeValidationMessage ?? 'Selected location could not be validated against your assigned regional scope.';
      return null;
    }

    const descriptionHtml = normalizeEditorDescriptionHtml(this.editorRef?.nativeElement?.innerHTML);

    return {
      regionId,
      name,
      summary: normalizedSummary || null,
      description: descriptionHtml || null,
      status: 'Published',
      latitude: this.centerLat,
      longitude: this.centerLng,
      tagIds: this.selectedTagIdList(),
      cardImageFile: this.cardImageFile,
      coverImageFile: this.coverImageFile,
      galleryImageFiles: this.galleryImages.map((image) => image.file),
    };
  }

  private buildUpdatePayload(): RegionalDestinationUpdateRequest | null {
    const name = this.destinationName.trim();
    if (!name) {
      this.errorMessage = 'Destination name is required.';
      return null;
    }

    const normalizedSummary = this.summary.trim();
    if (normalizedSummary.length > 160) {
      this.errorMessage = 'Summary must be 160 characters or less.';
      return null;
    }

    if (!this.hasValidCenter()) {
      this.errorMessage = 'Choose a destination center point before updating.';
      return null;
    }

    if (!this.zoneConfirmed) {
      this.errorMessage = 'Confirm the destination center point before updating.';
      return null;
    }

    if (this.scopeValidationPending || this.isScopeValidationRejected) {
      this.errorMessage = this.scopeValidationPending
        ? 'Location validation is still running.'
        : this.scopeValidationMessage ?? 'Selected location is outside your allowed regional scope.';
      return null;
    }

    const descriptionHtml = normalizeEditorDescriptionHtml(this.editorRef?.nativeElement?.innerHTML);

    return {
      name,
      summary: normalizedSummary || null,
      description: descriptionHtml || null,
      latitude: this.centerLat,
      longitude: this.centerLng,
      tagIds: this.selectedTagIdList(),
    };
  }

  private loadMapCenter(): [number, number] {
    return [44.0, 20.9];
  }

  private loadAssignedRegionMap(regionId: number): void {
    this.regionMapSubscription?.unsubscribe();
    this.regionBoundaryMissingMessage = null;
    this.regionViewport = null;
    this.regionBoundaryZone = null;
    this.clearScopeValidation();

    this.regionMapSubscription = forkJoin({
      viewport: this.adminMapService.getViewport(regionId),
      zones: this.adminMapService.getZones(regionId),
    }).subscribe({
      next: ({ viewport, zones }) => {
        this.regionViewport = viewport;
        this.regionBoundaryZone = assignedRegionZone(zones);
        this.regionBoundaryMissingMessage = isPreciseAssignedRegionBoundary(this.regionBoundaryZone)
          ? null
          : 'Assigned region boundary is missing. Please contact an administrator.';
        this.refreshRegionBoundaryLayers();
        this.applyRegionViewportToMiniMap();
        this.notifyStateChanged();
      },
      error: () => {
        this.regionViewport = null;
        this.regionBoundaryZone = null;
        this.regionBoundaryMissingMessage = 'Assigned region boundary is missing. Please contact an administrator.';
        this.refreshRegionBoundaryLayers();
        this.applyRegionViewportToMiniMap();
        this.notifyStateChanged();
      },
    });
  }

  private initMiniMap(): void {
    if (this.miniMap) return;
    const el = this.miniMapContainer?.nativeElement;
    if (!el || typeof L === 'undefined') return;

    this.miniMap = L.map(el, {
      zoomControl: false,
      dragging: false,
      scrollWheelZoom: false,
      doubleClickZoom: false,
      touchZoom: false,
      attributionControl: false,
    });

    this.mapTileLayer.addTo(L, this.miniMap);

    this.refreshMiniRegionBoundaryLayer();
    this.applyRegionViewportToMiniMap();
  }

  private initModalMap(el: HTMLDivElement): void {
    if (this.modalMap || typeof L === 'undefined') return;

    this.modalMap = L.map(el, {
      zoomControl: true,
      attributionControl: false,
    });

    this.mapTileLayer.addTo(L, this.modalMap);

    this.refreshModalRegionBoundaryLayer();
    applyAssignedRegionViewport(this.modalMap, this.regionViewport, 9, 11);

    this.modalMap.on('click', (event: any) => {
      this.zone.run(() => {
        this.centerLat = event.latlng.lat;
        this.centerLng = event.latlng.lng;
        this.zoneConfirmed = false;
        this.errorMessage = null;
        this.validateSelectedPoint(event.latlng.lat, event.latlng.lng);
        this.showCenterOnModalMap();
        this.notifyStateChanged();
      });
    });

    setTimeout(() => this.modalMap?.invalidateSize(), 200);
    this.showCenterOnModalMap();
  }

  private destroyModalMap(): void {
    if (this.modalMap) {
      this.modalMap.remove();
      this.modalMap = null;
      this.modalCenterMarker = null;
      this.modalRegionBoundaryLayer = null;
    }
  }

  private refreshRegionBoundaryLayers(): void {
    this.refreshMiniRegionBoundaryLayer();
    this.refreshModalRegionBoundaryLayer();
  }

  private refreshMiniRegionBoundaryLayer(): void {
    this.miniRegionBoundaryLayer = renderAssignedRegionBoundary(
      typeof L === 'undefined' ? null : L,
      this.miniMap,
      this.miniRegionBoundaryLayer,
      this.regionBoundaryZone,
    );
  }

  private refreshModalRegionBoundaryLayer(): void {
    this.modalRegionBoundaryLayer = renderAssignedRegionBoundary(
      typeof L === 'undefined' ? null : L,
      this.modalMap,
      this.modalRegionBoundaryLayer,
      this.regionBoundaryZone,
    );
  }

  private applyRegionViewportToMiniMap(): void {
    if (!this.miniMap || this.hasValidCenter()) return;
    applyAssignedRegionViewport(this.miniMap, this.regionViewport, 8, 11);
  }

  private showZoneOnMiniMap(): void {
    if (!this.miniMap) this.initMiniMap();
    if (!this.miniMap || !this.hasValidCenter()) return;

    this.removeMiniMapCenterMarker();

    this.miniMapCenterMarker = L.marker([this.centerLat as number, this.centerLng as number], {
      icon: this.makePinIcon(),
    });
    this.miniMapCenterMarker.addTo(this.miniMap);
    this.miniMap.setView([this.centerLat as number, this.centerLng as number], 12);
  }

  private showCenterOnModalMap(): void {
    if (!this.modalMap || typeof L === 'undefined') return;

    if (this.modalCenterMarker) {
      this.modalMap.removeLayer(this.modalCenterMarker);
      this.modalCenterMarker = null;
    }

    if (!this.hasValidCenter()) return;

    this.modalCenterMarker = L.marker([this.centerLat as number, this.centerLng as number], {
      icon: this.makePinIcon(),
    });
    this.modalCenterMarker.addTo(this.modalMap);
  }

  private makePinIcon(): any {
    return createRegionalMapPinIcon(L);
  }

  private removeMiniMapCenterMarker(): void {
    if (this.miniMapCenterMarker && this.miniMap) {
      this.miniMap.removeLayer(this.miniMapCenterMarker);
      this.miniMapCenterMarker = null;
    }
  }

  private hasValidCenter(): boolean {
    return this.hasValidCoordinatePair(this.centerLat, this.centerLng);
  }

  private hasValidCoordinatePair(
    latitude: number | null | undefined,
    longitude: number | null | undefined,
  ): boolean {
    return (
      typeof latitude === 'number' &&
      typeof longitude === 'number' &&
      Number.isFinite(latitude) &&
      Number.isFinite(longitude) &&
      latitude >= -90 &&
      latitude <= 90 &&
      longitude >= -180 &&
      longitude <= 180 &&
      (latitude !== 0 || longitude !== 0)
    );
  }

  private resetZone(): void {
    this.zoneConfirmed = false;
    this.centerLat = null;
    this.centerLng = null;
    this.clearScopeValidation();
    this.removeMiniMapCenterMarker();
    this.applyRegionViewportToMiniMap();
  }

  private validateSelectedPoint(latitude: number, longitude: number): void {
    if (!this.activeRegionId || !this.hasValidCoordinatePair(latitude, longitude)) {
      this.clearScopeValidation();
      return;
    }

    this.validationSubscription?.unsubscribe();
    this.scopeValidationResult = null;
    this.scopeValidationTone = null;
    this.scopeValidationMessage = null;
    this.scopeValidationPending = true;
    this.notifyStateChanged();
    this.validationSubscription = this.adminMapService.validateEntityLocation({
      regionId: this.activeRegionId,
      entityType: 'Destination',
      operationType: this.isEditMode ? 'Update' : 'Create',
      latitude,
      longitude,
    }).subscribe({
      next: result => {
        this.scopeValidationPending = false;
        this.applyScopeValidationResult(result);
        this.notifyStateChanged();
      },
      error: error => {
        this.scopeValidationPending = false;
        this.scopeValidationResult = null;
        this.scopeValidationTone = 'error';
        this.scopeValidationMessage = this.extractErrorMessage(
          error,
          'Unable to validate this location against your assigned region.',
        );
        this.notifyStateChanged();
      },
    });
  }

  private applyScopeValidationResult(result: RegionalScopeValidationResult): void {
    this.scopeValidationResult = result;

    if (result.isAllowed) {
      if (
        result.validationMode === 'AllowedWithinToleranceInsideAssignedCountry' ||
        result.validationMode === 'AllowedWithinToleranceOutsideRegion'
      ) {
        this.scopeValidationTone = 'success';
        this.scopeValidationMessage = result.message ||
          'This location is slightly outside your assigned region but remains inside your assigned country/territory and does not belong to another region, so it is allowed.';
        return;
      }

      this.scopeValidationTone = null;
      this.scopeValidationMessage = null;
      return;
    }

    if (result.validationMode === 'RejectedInsideAnotherRegion') {
      this.scopeValidationTone = 'error';
      this.scopeValidationMessage = result.message ||
        'This location belongs to another region and cannot be managed from your account.';
      return;
    }

    if (result.validationMode === 'RejectedOutsideAssignedCountry') {
      this.scopeValidationTone = 'error';
      this.scopeValidationMessage = result.message || 'This location is outside your assigned country/territory.';
      return;
    }

    if (!result.isAllowed) {
      this.scopeValidationTone = 'error';
      this.scopeValidationMessage = result.message || 'This location is outside your assigned region.';
      return;
    }

    this.scopeValidationTone = null;
    this.scopeValidationMessage = null;
  }

  private clearScopeValidation(): void {
    this.scopeValidationResult = null;
    this.scopeValidationMessage = null;
    this.scopeValidationTone = null;
    this.scopeValidationPending = false;
    this.validationSubscription?.unsubscribe();
    this.validationSubscription = null;
  }

  private searchTags(query: string): void {
    this.tagSearchLoading = true;
    this.notifyStateChanged();

    this.tagSearchSubscription?.unsubscribe();
    this.tagSearchSubscription = this.destinationsService
      .searchDestinationTags(query)
      .pipe(
        finalize(() => {
          this.tagSearchLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: (tags) => {
          const normalized = this.normalizeTags(tags).filter(
            (tag) => !this.selectedTagIds.has(tag.id),
          );
          normalized.forEach((tag) => this.upsertDestinationTag(tag));
          this.additionalTagResults = normalized;
        },
        error: (error) => this.handleLoadError(error, 'Unable to search destination tags.'),
      });
  }

  private findTagToAdd(): RegionalDestinationTagDto | null {
    const query = this.tagInput.trim().toLocaleLowerCase();
    const exactMatch = this.destinationTags.find(
      (tag) => tag.name.trim().toLocaleLowerCase() === query && !this.selectedTagIds.has(tag.id),
    );
    return exactMatch ?? null;
  }

  private normalizeTags(
    tags: RegionalDestinationTagDto[] | null | undefined,
  ): RegionalDestinationTagDto[] {
    const seen = new Set<number>();
    return (tags ?? [])
      .filter((tag) => {
        if (!tag || seen.has(tag.id)) return false;
        seen.add(tag.id);
        return true;
      })
      .sort((a, b) => a.name.localeCompare(b.name));
  }

  private upsertDestinationTag(tag: RegionalDestinationTagDto): void {
    if (this.destinationTags.some((existing) => existing.id === tag.id)) return;
    this.destinationTags = [...this.destinationTags, tag].sort((a, b) =>
      a.name.localeCompare(b.name),
    );
  }

  private upsertClassItem(tag: RegionalDestinationTagDto, checked: boolean): void {
    const existing = this.classItems.find((item) => item.tag.id === tag.id);
    if (existing) {
      existing.checked = checked || existing.checked;
      return;
    }

    this.classItems = [...this.classItems, { tag, checked }].sort((a, b) =>
      a.tag.name.localeCompare(b.tag.name),
    );
  }

  private selectedTagIdList(): number[] {
    return [...new Set(this.selectedTagIds)].sort((a, b) => a - b);
  }

  private setActiveRegion(regionId: number, updateUrl: boolean): void {
    this.activeRegionId = regionId;
    this.persistRegion(regionId);

    if (updateUrl) {
      this.router.navigate([], {
        relativeTo: this.route,
        queryParams: { regionId },
        queryParamsHandling: 'merge',
        replaceUrl: true,
      });
    }
  }

  private readPersistedRegionId(): number | null {
    try {
      const value = sessionStorage.getItem(AddDestinationComponent.activeRegionStorageKey);
      const parsed = value ? Number(value) : NaN;
      return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
    } catch {
      return null;
    }
  }

  private persistRegion(regionId: number): void {
    try {
      sessionStorage.setItem(AddDestinationComponent.activeRegionStorageKey, String(regionId));
    } catch {
      // Session persistence is a convenience only.
    }
  }

  private clearPersistedRegion(): void {
    try {
      sessionStorage.removeItem(AddDestinationComponent.activeRegionStorageKey);
    } catch {
      // Session persistence is a convenience only.
    }
  }

  private isAllowedImage(file: File): boolean {
    const allowed = new Set(['image/png', 'image/jpeg', 'image/webp']);
    if (!allowed.has(file.type)) {
      this.errorMessage = 'Image must be PNG, JPG, or WEBP.';
      this.notifyStateChanged();
      return false;
    }

    const maxSizeBytes = 8 * 1024 * 1024;
    if (file.size > maxSizeBytes) {
      this.errorMessage = 'Image must be 8MB or smaller.';
      this.notifyStateChanged();
      return false;
    }

    this.errorMessage = null;
    return true;
  }

  private syncEmptyState(): void {
    const el = this.editorRef?.nativeElement;
    if (!el) return;
    const text = el.innerText ?? el.textContent ?? '';
    this.editorIsEmpty = text.trim() === '' && !el.querySelector('img');
    this.notifyStateChanged();
  }

  private syncToolbarState(): void {
    this.isBold = document.queryCommandState('bold');
    this.isItalic = document.queryCommandState('italic');
    this.isUnderline = document.queryCommandState('underline');
    this.notifyStateChanged();
  }

  private extractErrorMessage(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse) {
      return error.error?.message ?? fallback;
    }

    return fallback;
  }

  private handleLoadError(error: unknown, fallback: string): void {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 401) {
        this.errorMessage =
          'Sign in with an authenticated Regional Manager account to create destinations.';
        this.notifyStateChanged();
        return;
      }

      if (error.status === 403) {
        this.errorMessage = 'This account is not authorized for the selected region.';
        this.notifyStateChanged();
        return;
      }

      this.errorMessage = error.error?.message ?? fallback;
      this.notifyStateChanged();
      return;
    }

    this.errorMessage = fallback;
    this.notifyStateChanged();
  }

  private handleActionError(error: unknown): void {
    if (error instanceof HttpErrorResponse) {
      this.errorMessage = error.error?.message ?? 'Unable to publish destination.';
      this.notifyStateChanged();
      return;
    }

    this.errorMessage = 'Unable to publish destination.';
    this.notifyStateChanged();
  }

  private revokeCoverPreviewObjectUrl(): void {
    if (!this.coverPreviewObjectUrl) return;

    URL.revokeObjectURL(this.coverPreviewObjectUrl);
    this.coverPreviewObjectUrl = null;
  }

  private notifyStateChanged(): void {
    this.cdr.markForCheck();
  }

  private registerDraftSource(): void {
    this.unregisterDraftSource = this.formDrafts.register({
      formType: this.draftFormType(),
      snapshot: () => this.buildDraftSnapshot(),
    });
  }

  private draftFormType(): string {
    return this.isEditMode ? 'destination-edit' : 'destination-new';
  }

  private buildDraftSnapshot(): DestinationFormDraft | null {
    const descriptionHtml = this.editorRef?.nativeElement?.innerHTML?.trim() ?? '';
    const hasDraft =
      Boolean(this.destinationName.trim()) ||
      Boolean(this.summary.trim()) ||
      Boolean(descriptionHtml) ||
      this.selectedTagIds.size > 0 ||
      this.hasValidCenter() ||
      Boolean(this.cardImageName) ||
      Boolean(this.coverImageName) ||
      this.galleryImages.length > 0;

    if (!hasDraft) return null;

    return {
      activeRegionId: this.activeRegionId,
      destinationName: this.destinationName,
      summary: this.summary,
      descriptionHtml,
      selectedTagIds: [...this.selectedTagIds],
      centerLat: this.centerLat,
      centerLng: this.centerLng,
      zoneConfirmed: this.zoneConfirmed,
      cardImageName: this.cardImageName,
      coverImageName: this.coverImageName,
      galleryImageNames: this.galleryImages.map(image => image.name),
    };
  }

  private restoreSavedDraftIfPresent(): void {
    const draft = this.formDrafts.loadDraft<DestinationFormDraft>(this.draftFormType());
    const payload = draft?.payload;
    if (!payload) return;

    if (typeof payload.activeRegionId === 'number') {
      this.setActiveRegion(payload.activeRegionId, false);
      this.loadAssignedRegionMap(payload.activeRegionId);
    }

    this.destinationName = payload.destinationName ?? this.destinationName;
    this.summary = payload.summary ?? this.summary;
    this.centerLat = payload.centerLat;
    this.centerLng = payload.centerLng;
    this.zoneConfirmed = Boolean(payload.zoneConfirmed && this.hasValidCenter());
    this.selectedTagIds = new Set(payload.selectedTagIds ?? []);
    this.classItems = this.classItems.map(item => ({
      ...item,
      checked: this.selectedTagIds.has(item.tag.id),
    }));

    if (this.editorRef?.nativeElement) {
      this.editorRef.nativeElement.innerHTML = payload.descriptionHtml ?? '';
      this.syncEmptyState();
    }

    if (this.hasValidCenter()) {
      setTimeout(() => this.showZoneOnMiniMap(), 0);
    }

    this.feedbackMessage = 'Recovered a saved draft. Image files must be selected again.';
    this.notifyStateChanged();
  }
}
