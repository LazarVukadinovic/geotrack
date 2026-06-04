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
import { finalize, forkJoin, Subscription } from 'rxjs';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { DashboardService } from '../../../../core/services/dashboard.service';
import { LightboxService } from '../../../../core/services/lightbox.service';
import { ZoneDrawService, DrawTool } from '../../../../core/services/zone-draw.service';
import { LocationsListService } from '../../../../core/services/locations-list.service';
import { AuthService } from '../../../../core/services/auth.service';
import { AdminMapService } from '../../../../core/services/admin-map.service';
import { MapTileLayerService } from '../../../../core/services/map-tile-layer.service';
import { AdminFormDraftService } from '../../../../core/services/admin-form-draft.service';
import { RegionalAssignedRegion } from '../../../../core/models/dashboard.model';
import { AdminMapViewport, AdminMapZone, RegionalScopeValidationResult } from '../../../../core/models/admin-map.model';
import {
  RegionalLocationBoundaryDto,
  RegionalLocationCreateRequest,
  RegionalLocationParentDestinationDto,
  RegionalLocationTagDto,
  RegionalLocationTypeDto,
  RegionalLocationUpdateRequest,
} from '../../../../core/models/location.model';
import {
  applyAssignedRegionViewport,
  assignedRegionZone,
  createRegionalMapPinIcon,
  isPreciseAssignedRegionBoundary,
  renderAssignedRegionBoundary,
} from '../../shared/regional-creation-map';

declare const L: any;

type BoundaryShapeType = 'Polygon' | 'Rectangle' | 'Circle';

interface ClassItem {
  tag: RegionalLocationTagDto;
  checked: boolean;
}

interface LocationFormDraft {
  activeRegionId: number | null;
  locationName: string;
  summary: string;
  parentDestinationId: number | null;
  locationTypeId: number | null;
  boundaryNote: string;
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
  selector: 'app-add-location',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './add-location.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: [`
    .rte-field { position: relative; }
    .rte-ph {
      position: absolute;
      top: 0; left: 0; right: 0;
      padding: 0.5rem 0.75rem;
      color: #9ca3af;
      font-size: 0.875rem;
      pointer-events: none;
      user-select: none;
      line-height: 1.5rem;
    }
  `],
})
export class NewLocationComponent implements OnInit, AfterViewInit, OnDestroy {
  private static readonly activeRegionStorageKey = 'geotrack_admin_active_region_id';
  private static readonly initialVisibleClassificationTags = 5;
  private static readonly expandedVisibleClassificationTags = 10;
  private readonly mapTileLayer = inject(MapTileLayerService);

  @ViewChild('editorRef') editorRef!: ElementRef<HTMLDivElement>;
  @ViewChild('miniMapContainer') miniMapContainer!: ElementRef<HTMLDivElement>;
  @ViewChild('modalMapContainer') modalMapContainerRef!: ElementRef<HTMLDivElement>;

  assignedRegions: RegionalAssignedRegion[] = [];
  activeRegionId: number | null = null;
  regionLoading = false;
  parentDestinationsLoading = false;
  locationTypesLoading = false;
  tagsLoading = false;
  tagSearchLoading = false;
  isSaving = false;
  errorMessage: string | null = null;
  feedbackMessage: string | null = null;
  regionBoundaryMissingMessage: string | null = null;
  scopeValidationMessage: string | null = null;
  scopeValidationTone: 'success' | 'warning' | 'error' | null = null;
  scopeValidationPending = false;

  locationName = '';
  summary = '';
  parentDestinationId: number | null = null;
  locationTypeId: number | null = null;
  boundaryNote = '';
  tagInput = '';

  parentDestinations: RegionalLocationParentDestinationDto[] = [];
  locationTypes: RegionalLocationTypeDto[] = [];
  classItems: ClassItem[] = [];
  locationTags: RegionalLocationTagDto[] = [];
  additionalTagResults: RegionalLocationTagDto[] = [];
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
  galleryImages: { url: string; name: string; file: File }[] = [];

  private cardImageFile: File | null = null;
  private coverImageFile: File | null = null;

  zoneModalOpen = false;
  zoneConfirmed = false;
  zoneAreaKm2: number | null = null;
  centerLat: number | null = null;
  centerLng: number | null = null;
  drawTool: DrawTool = 'polygon';
  confirmedShapeType: BoundaryShapeType | null = null;

  isEditMode = false;

  private editLocationId: number | null = null;

  private miniMap: any = null;
  private modalMap: any = null;
  private miniRegionBoundaryLayer: any = null;
  private modalRegionBoundaryLayer: any = null;
  private drawnLayer: any = null;
  private confirmedLayer: any = null;
  private drawControl: any = null;
  private assignedRegionsSubscription: Subscription | null = null;
  private regionMapSubscription: Subscription | null = null;
  private validationSubscription: Subscription | null = null;
  private parentDestinationsSubscription: Subscription | null = null;
  private locationTypesSubscription: Subscription | null = null;
  private tagsSubscription: Subscription | null = null;
  private tagSearchSubscription: Subscription | null = null;
  private createSubscription: Subscription | null = null;
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
    private readonly zoneDrawService: ZoneDrawService,
    private readonly locationsService: LocationsListService,
    private readonly auth: AuthService,
    private readonly adminMapService: AdminMapService,
    private readonly formDrafts: AdminFormDraftService,
  ) {}

  ngOnInit(): void {
    const editId = Number(this.route.snapshot.paramMap.get('id'));
    this.isEditMode = Number.isFinite(editId) && editId > 0;
    this.editLocationId = this.isEditMode ? editId : null;

    this.loadAssignedRegions();
    this.loadLocationTypes();
    this.loadLocationTags();
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
    this.parentDestinationsSubscription?.unsubscribe();
    this.locationTypesSubscription?.unsubscribe();
    this.tagsSubscription?.unsubscribe();
    this.tagSearchSubscription?.unsubscribe();
    this.createSubscription?.unsubscribe();
    this.unregisterDraftSource?.();

    if (this.tagSearchHandle) {
      clearTimeout(this.tagSearchHandle);
    }

    this.destroyModalMap();
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
    return this.assignedRegions.find(region => region.id === this.activeRegionId) ?? null;
  }

  get regionDisplayName(): string {
    return this.activeRegion?.displayName ?? '';
  }

  get canPublish(): boolean {
    const mediaReady = this.isEditMode || (Boolean(this.cardImageFile) && Boolean(this.coverImageFile));
    return Boolean(this.activeRegionId) &&
      Boolean(this.parentDestinationId) &&
      this.locationName.trim().length > 0 &&
      this.zoneConfirmed &&
      !this.regionBoundaryMissingMessage &&
      !this.isScopeValidationRejected &&
      mediaReady &&
      !this.regionLoading &&
      !this.parentDestinationsLoading &&
      !this.locationTypesLoading &&
      !this.tagsLoading &&
      !this.isSaving;
  }

  get selectedParentDestinationName(): string {
    if (!this.parentDestinationId) return '-';
    return this.parentDestinations.find(item => item.id === this.parentDestinationId)?.displayName ?? '-';
  }

  get selectedAdditionalTags(): RegionalLocationTagDto[] {
    const primaryIds = new Set(this.classItems.map(item => item.tag.id));
    return this.locationTags
      .filter(tag => this.selectedTagIds.has(tag.id) && !primaryIds.has(tag.id))
      .sort((a, b) => a.name.localeCompare(b.name));
  }

  get visibleClassItems(): ClassItem[] {
    return this.classItems.slice(0, this.visibleClassificationLimit);
  }

  get showClassificationToggle(): boolean {
    return this.classItems.length > NewLocationComponent.initialVisibleClassificationTags;
  }

  get visibleClassificationLimit(): number {
    return this.classificationExpanded
      ? NewLocationComponent.expandedVisibleClassificationTags
      : NewLocationComponent.initialVisibleClassificationTags;
  }

  get createdDateDisplay(): string {
    return new Date().toLocaleDateString('en-US', {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
    });
  }

  get drawHint(): string {
    return this.zoneDrawService.drawHints[this.drawTool];
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
        next: regions => {
          this.assignedRegions = regions ?? [];

          if (this.assignedRegions.length === 0) {
            this.activeRegionId = null;
            this.parentDestinationId = null;
            this.parentDestinations = [];
            this.clearPersistedRegion();
            this.errorMessage = 'No active assigned region is available for this account.';
            this.notifyStateChanged();
            return;
          }

          const queryRegionId = Number(this.route.snapshot.queryParamMap.get('regionId'));
          const persistedRegionId = this.readPersistedRegionId();
          const stateRegionId = Number((history.state as { regionId?: number | null })?.regionId);
          const selectedRegion =
            this.assignedRegions.find(region => region.id === queryRegionId) ??
            this.assignedRegions.find(region => region.id === stateRegionId) ??
            this.assignedRegions.find(region => region.id === persistedRegionId) ??
            this.assignedRegions[0];

          this.setActiveRegion(selectedRegion.id, false);
          this.loadAssignedRegionMap(selectedRegion.id);
          this.loadParentDestinations();
          this.loadLocationForEditIfNeeded();
        },
        error: error => this.handleLoadError(error, 'Unable to load assigned regions.'),
      });
  }

  onRegionSelected(regionId: number): void {
    if (regionId === this.activeRegionId) return;

    this.setActiveRegion(regionId, true);
    this.parentDestinationId = null;
    this.parentDestinations = [];
    this.resetZone();
    this.loadAssignedRegionMap(regionId);
    this.loadParentDestinations();
    this.feedbackMessage = 'Region changed. Parent destination and center selection were cleared.';
    this.notifyStateChanged();
  }

  loadParentDestinations(): void {
    if (!this.activeRegionId) {
      this.parentDestinations = [];
      this.parentDestinationId = null;
      this.notifyStateChanged();
      return;
    }

    this.parentDestinationsLoading = true;
    this.notifyStateChanged();

    this.parentDestinationsSubscription?.unsubscribe();
    this.parentDestinationsSubscription = this.locationsService
      .getParentDestinations(this.activeRegionId)
      .pipe(
        finalize(() => {
          this.parentDestinationsLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: destinations => {
          this.parentDestinations = this.normalizeParentDestinations(destinations);
          if (
            this.parentDestinationId &&
            !this.parentDestinations.some(item => item.id === this.parentDestinationId)
          ) {
            this.parentDestinationId = null;
          }
        },
        error: error => this.handleLoadError(error, 'Unable to load parent destinations.'),
      });
  }

  loadLocationTypes(): void {
    this.locationTypesLoading = true;
    this.notifyStateChanged();

    this.locationTypesSubscription?.unsubscribe();
    this.locationTypesSubscription = this.locationsService
      .getLocationTypes()
      .pipe(
        finalize(() => {
          this.locationTypesLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: types => {
          this.locationTypes = this.normalizeLocationTypes(types);
          if (
            this.locationTypeId &&
            !this.locationTypes.some(type => type.id === this.locationTypeId)
          ) {
            this.locationTypeId = null;
          }
        },
        error: error => this.handleLoadError(error, 'Unable to load location types.'),
      });
  }

  loadLocationTags(): void {
    this.tagsLoading = true;
    this.notifyStateChanged();

    this.tagsSubscription?.unsubscribe();
    this.tagsSubscription = this.locationsService
      .getLocationTags()
      .pipe(
        finalize(() => {
          this.tagsLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: tags => {
          this.locationTags = this.normalizeTags(tags);
          this.classItems = this.locationTags.map(tag => ({
            tag,
            checked: this.selectedTagIds.has(tag.id),
          }));
        },
        error: error => this.handleLoadError(error, 'Unable to load location tags.'),
      });
  }

  private loadLocationForEditIfNeeded(): void {
    if (!this.isEditMode || !this.editLocationId || !this.activeRegionId) {
      return;
    }

    this.locationsService.getLocation(this.activeRegionId, this.editLocationId).subscribe({
      next: dto => {
        this.locationName = dto.name ?? '';
        this.summary = dto.summary ?? '';
        this.parentDestinationId = dto.destinationId ?? null;
        this.locationTypeId = dto.locationTypeId ?? null;
        this.boundaryNote = dto.boundaryNote ?? '';
        this.centerLat = dto.latitude ?? null;
        this.centerLng = dto.longitude ?? null;
        this.zoneConfirmed = this.hasValidCenter();

        this.cardImageUrl = dto.cardImageUrl ?? dto.imageUrl ?? null;
        this.cardImageName = this.cardImageUrl ? 'Existing card image' : '';
        this.coverImageUrl = dto.coverImageUrl ?? dto.imageUrl ?? null;
        this.coverImageName = this.coverImageUrl ? 'Existing cover image' : '';
        this.galleryImages = (dto.galleryImageUrls ?? []).map((url, index) => ({
          url,
          name: `Existing gallery image ${index + 1}`,
          file: null as unknown as File,
        }));

        this.selectedTagIds = new Set((dto.tags ?? []).map(tag => tag.id));
        for (const tag of dto.tags ?? []) {
          this.upsertLocationTag(tag);
          this.upsertClassItem(tag, true);
        }
        this.classItems = this.classItems.map(item => ({
          ...item,
          checked: this.selectedTagIds.has(item.tag.id),
        }));

        setTimeout(() => {
          if (this.editorRef?.nativeElement) {
            this.editorRef.nativeElement.innerHTML = dto.description ?? '';
            this.syncEmptyState();
            this.notifyStateChanged();
          }
          this.restoreSavedDraftIfPresent();
        }, 0);

        setTimeout(() => this.showZoneOnMiniMap(), 0);
        this.notifyStateChanged();
      },
      error: error => this.handleLoadError(error, 'Unable to load location for editing.'),
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

    const query = this.tagInput.trim();
    if (!query) {
      this.additionalTagResults = [];
      this.tagSearchLoading = false;
      this.notifyStateChanged();
      return;
    }

    this.tagSearchHandle = setTimeout(() => this.searchTags(query), 250);
  }

  addTag(event: Event): void {
    event.preventDefault();
    const firstAvailable = this.additionalTagResults.find(tag => !this.selectedTagIds.has(tag.id));
    if (firstAvailable) {
      this.selectAdditionalTag(firstAvailable);
    }
  }

  selectAdditionalTag(tag: RegionalLocationTagDto): void {
    this.upsertLocationTag(tag);
    this.selectedTagIds.add(tag.id);
    this.upsertClassItem(tag, true);
    this.tagInput = '';
    this.additionalTagResults = [];
    this.notifyStateChanged();
  }

  removeTag(tag: RegionalLocationTagDto): void {
    this.selectedTagIds.delete(tag.id);
    const classItem = this.classItems.find(item => item.tag.id === tag.id);
    if (classItem) {
      classItem.checked = false;
    }
    this.notifyStateChanged();
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

  onEditorBlur(): void {
    this.syncToolbarState();
    const el = this.editorRef?.nativeElement;
    if (el && (el.innerHTML === '<br>' || el.innerHTML === '&nbsp;' || el.innerHTML.trim() === '')) {
      el.innerHTML = '';
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

    const reader = new FileReader();
    reader.onload = () => {
      this.zone.run(() => {
        this.coverImageUrl = reader.result as string;
        this.coverImageName = file.name;
        this.coverImageFile = file;
        this.notifyStateChanged();
      });
    };
    reader.readAsDataURL(file);
    input.value = '';
  }

  removeCoverImage(): void {
    this.coverImageUrl = null;
    this.coverImageName = '';
    this.coverImageFile = null;
    this.notifyStateChanged();
  }

  onGalleryImagesSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []).filter(file => this.isAllowedImage(file));
    for (const file of files) {
      const reader = new FileReader();
      reader.onload = () => {
        this.zone.run(() => {
          this.galleryImages = [...this.galleryImages, { url: reader.result as string, name: file.name, file }];
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

  setDrawTool(tool: DrawTool): void {
    this.drawTool = tool;
    if (this.modalMap) {
      this.clearZone();
      this.activateDrawTool();
    }
    this.notifyStateChanged();
  }

  clearZone(): void {
    this.centerLat = null;
    this.centerLng = null;
    this.zoneConfirmed = false;
    this.clearScopeValidation();
    this.showCenterOnModalMap();
    this.applyRegionViewportToMiniMap();
    this.notifyStateChanged();
  }

  confirmZone(): void {
    if (!this.hasValidCenter()) {
      this.errorMessage = 'Choose a location center point before confirming.';
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
      this.updateExistingLocation();
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
    this.createSubscription = this.locationsService
      .createLocation(payload)
      .pipe(
        finalize(() => {
          this.isSaving = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: created => {
          this.formDrafts.clearDraft(this.draftFormType());
          this.feedbackMessage = 'Location published.';
          this.router.navigate(['/locations'], {
            queryParams: { regionId: created.regionId },
            state: { feedbackMessage: 'Location published.' },
          });
        },
        error: error => this.handleActionError(error),
      });
  }

  private updateExistingLocation(): void {
    const regionId = this.activeRegionId;
    const locationId = this.editLocationId;
    const payload = this.buildUpdatePayload();
    if (!regionId || !locationId || !payload) {
      this.notifyStateChanged();
      return;
    }

    this.isSaving = true;
    this.errorMessage = null;
    this.feedbackMessage = null;
    this.notifyStateChanged();

    this.createSubscription?.unsubscribe();
    this.createSubscription = this.locationsService
      .updateLocation(regionId, locationId, payload)
      .pipe(
        finalize(() => {
          this.isSaving = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: updated => {
          this.formDrafts.clearDraft(this.draftFormType());
          this.feedbackMessage = 'Location updated.';
          this.router.navigate(['/locations/detail', updated.id], {
            queryParams: { regionId: updated.regionId },
            state: { regionId: updated.regionId, feedbackMessage: 'Location updated.' },
          });
        },
        error: error => this.handleActionError(error),
      });
  }

  private buildCreatePayload(): RegionalLocationCreateRequest | null {
    const regionId = this.activeRegionId;
    if (!regionId) {
      this.errorMessage = 'Select an assigned region before publishing.';
      return null;
    }

    const destinationId = this.parentDestinationId;
    if (!destinationId) {
      this.errorMessage = 'Parent destination is required.';
      return null;
    }

    const name = this.locationName.trim();
    if (!name) {
      this.errorMessage = 'Location name is required.';
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

    if (!this.zoneConfirmed || !this.hasValidCenter()) {
      this.errorMessage = 'Choose a location center point before publishing.';
      return null;
    }

    if (this.scopeValidationPending || this.scopeValidationResult?.isAllowed !== true) {
      this.errorMessage = this.scopeValidationPending
        ? 'Location validation is still running.'
        : this.scopeValidationMessage ?? 'Selected location could not be validated against your assigned regional scope.';
      return null;
    }

    const descriptionHtml = this.editorRef?.nativeElement?.innerHTML?.trim() ?? '';
    const centerLat = this.centerLat as number;
    const centerLng = this.centerLng as number;

    return {
      regionId,
      destinationId,
      name,
      summary: normalizedSummary || null,
      description: descriptionHtml || null,
      status: 'Published',
      locationTypeId: this.locationTypeId,
      latitude: centerLat,
      longitude: centerLng,
      tagIds: [...this.selectedTagIds],
      cardImageFile: this.cardImageFile,
      coverImageFile: this.coverImageFile,
      galleryImageFiles: this.galleryImages.map(image => image.file),
    };
  }

  private buildUpdatePayload(): RegionalLocationUpdateRequest | null {
    const destinationId = this.parentDestinationId;
    if (!destinationId) {
      this.errorMessage = 'Parent destination is required.';
      return null;
    }

    const name = this.locationName.trim();
    if (!name) {
      this.errorMessage = 'Location name is required.';
      return null;
    }

    const normalizedSummary = this.summary.trim();
    if (normalizedSummary.length > 160) {
      this.errorMessage = 'Summary must be 160 characters or less.';
      return null;
    }

    if (!this.zoneConfirmed || !this.hasValidCenter()) {
      this.errorMessage = 'Choose a location center point before updating.';
      return null;
    }

    if (this.scopeValidationPending || this.isScopeValidationRejected) {
      this.errorMessage = this.scopeValidationPending
        ? 'Location validation is still running.'
        : this.scopeValidationMessage ?? 'Selected location is outside your allowed regional scope.';
      return null;
    }

    const descriptionHtml = this.editorRef?.nativeElement?.innerHTML?.trim() ?? '';

    return {
      destinationId,
      name,
      summary: normalizedSummary || null,
      description: descriptionHtml || null,
      locationTypeId: this.locationTypeId,
      latitude: this.centerLat,
      longitude: this.centerLng,
      tagIds: [...this.selectedTagIds],
    };
  }

  private createBoundaryPayload(zone: [number, number][]): RegionalLocationBoundaryDto | null {
    const shapeType = this.confirmedShapeType;
    if (!shapeType || zone.length < 3) return null;

    return {
      shapeType,
      coordinates: zone.map(([latitude, longitude]) => ({ latitude, longitude })),
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
      this.drawnLayer = null;
      this.drawControl = null;
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

  private activateDrawTool(): void {
    if (!this.modalMap || typeof L === 'undefined') return;

    if (this.drawControl) {
      this.drawControl.disable();
      this.drawControl = null;
    }

    this.drawControl = this.zoneDrawService.activateDrawTool(
      this.modalMap,
      this.drawTool,
      layer => {
        if (this.drawnLayer) {
          this.modalMap.removeLayer(this.drawnLayer);
        }
        this.drawnLayer = layer;
        this.modalMap.addLayer(this.drawnLayer);
        this.notifyStateChanged();
      },
    );
  }

  private showZoneOnMiniMap(): void {
    if (!this.miniMap) this.initMiniMap();
    if (!this.miniMap || !this.hasValidCenter()) return;

    this.miniMap.eachLayer((layer: any) => {
      if (layer._isConfirmedCenter) this.miniMap.removeLayer(layer);
    });

    const lat = this.centerLat as number;
    const lng = this.centerLng as number;
    const marker = L.marker([lat, lng], {
      icon: this.makePinIcon(),
    });
    marker._isConfirmedCenter = true;
    marker.addTo(this.miniMap);
    this.miniMap.setView([lat, lng], 12);
  }

  private showCenterOnModalMap(): void {
    if (!this.modalMap || typeof L === 'undefined') return;

    this.modalMap.eachLayer((layer: any) => {
      if (layer._isDraftCenter) this.modalMap.removeLayer(layer);
    });

    if (!this.hasValidCenter()) return;

    const marker = L.marker([this.centerLat as number, this.centerLng as number], {
      icon: this.makePinIcon(),
    });
    marker._isDraftCenter = true;
    marker.addTo(this.modalMap);
  }

  private makePinIcon(): any {
    return createRegionalMapPinIcon(L);
  }

  private hasValidCenter(): boolean {
    return this.hasValidCoordinatePair(this.centerLat, this.centerLng);
  }

  private hasValidCoordinatePair(
    latitude: number | null | undefined,
    longitude: number | null | undefined,
  ): boolean {
    return typeof latitude === 'number' &&
      typeof longitude === 'number' &&
      Number.isFinite(latitude) &&
      Number.isFinite(longitude) &&
      latitude >= -90 &&
      latitude <= 90 &&
      longitude >= -180 &&
      longitude <= 180 &&
      (latitude !== 0 || longitude !== 0);
  }

  private resetZone(): void {
    this.zoneConfirmed = false;
    this.zoneAreaKm2 = null;
    this.centerLat = null;
    this.centerLng = null;
    this.confirmedLayer = null;
    this.confirmedShapeType = null;
    this.clearScopeValidation();

    if (this.miniMap) {
      this.miniMap.eachLayer((layer: any) => {
        if (layer._isConfirmedCenter) this.miniMap.removeLayer(layer);
      });
    }

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
      entityType: 'Location',
      operationType: this.isEditMode ? 'Update' : 'Create',
      latitude,
      longitude,
      destinationId: this.parentDestinationId,
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
    this.tagSearchSubscription = this.locationsService
      .searchLocationTags(query)
      .pipe(
        finalize(() => {
          this.tagSearchLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: tags => {
          const normalized = this.normalizeTags(tags)
            .filter(tag => !this.selectedTagIds.has(tag.id));
          normalized.forEach(tag => this.upsertLocationTag(tag));
          this.additionalTagResults = normalized;
        },
        error: error => this.handleLoadError(error, 'Unable to search location tags.'),
      });
  }

  private normalizeParentDestinations(
    destinations: RegionalLocationParentDestinationDto[] | null | undefined,
  ): RegionalLocationParentDestinationDto[] {
    const seen = new Set<number>();
    return (destinations ?? [])
      .filter(item => {
        if (!item || seen.has(item.id)) return false;
        seen.add(item.id);
        return true;
      })
      .sort((a, b) => a.displayName.localeCompare(b.displayName));
  }

  private normalizeLocationTypes(types: RegionalLocationTypeDto[] | null | undefined): RegionalLocationTypeDto[] {
    const seen = new Set<number>();
    return (types ?? [])
      .filter(type => {
        if (!type || seen.has(type.id)) return false;
        seen.add(type.id);
        return true;
      })
      .sort((a, b) => a.name.localeCompare(b.name));
  }

  private normalizeTags(tags: RegionalLocationTagDto[] | null | undefined): RegionalLocationTagDto[] {
    const seen = new Set<number>();
    return (tags ?? [])
      .filter(tag => {
        if (!tag || seen.has(tag.id)) return false;
        seen.add(tag.id);
        return true;
      })
      .sort((a, b) => a.name.localeCompare(b.name));
  }

  private upsertLocationTag(tag: RegionalLocationTagDto): void {
    if (this.locationTags.some(existing => existing.id === tag.id)) return;
    this.locationTags = [...this.locationTags, tag].sort((a, b) => a.name.localeCompare(b.name));
  }

  private upsertClassItem(tag: RegionalLocationTagDto, checked: boolean): void {
    const existing = this.classItems.find(item => item.tag.id === tag.id);
    if (existing) {
      existing.checked = checked || existing.checked;
      return;
    }

    this.classItems = [...this.classItems, { tag, checked }]
      .sort((a, b) => a.tag.name.localeCompare(b.tag.name));
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
      const value = sessionStorage.getItem(NewLocationComponent.activeRegionStorageKey);
      const parsed = value ? Number(value) : NaN;
      return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
    } catch {
      return null;
    }
  }

  private persistRegion(regionId: number): void {
    try {
      sessionStorage.setItem(NewLocationComponent.activeRegionStorageKey, String(regionId));
    } catch {
      // Session persistence is a convenience only.
    }
  }

  private clearPersistedRegion(): void {
    try {
      sessionStorage.removeItem(NewLocationComponent.activeRegionStorageKey);
    } catch {
      // Session persistence is a convenience only.
    }
  }

  private shapeTypeFromDrawTool(tool: DrawTool): BoundaryShapeType {
    switch (tool) {
      case 'rectangle':
        return 'Rectangle';
      case 'circle':
        return 'Circle';
      default:
        return 'Polygon';
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
        this.errorMessage = 'Sign in with an authenticated Regional Manager account to create locations.';
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
      this.errorMessage = error.error?.message ?? (this.isEditMode ? 'Unable to update location.' : 'Unable to publish location.');
      this.notifyStateChanged();
      return;
    }

    this.errorMessage = this.isEditMode ? 'Unable to update location.' : 'Unable to publish location.';
    this.notifyStateChanged();
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
    return this.isEditMode ? 'location-edit' : 'location-new';
  }

  private buildDraftSnapshot(): LocationFormDraft | null {
    const descriptionHtml = this.editorRef?.nativeElement?.innerHTML?.trim() ?? '';
    const hasDraft =
      Boolean(this.locationName.trim()) ||
      Boolean(this.summary.trim()) ||
      Boolean(this.boundaryNote.trim()) ||
      Boolean(descriptionHtml) ||
      Boolean(this.parentDestinationId) ||
      Boolean(this.locationTypeId) ||
      this.selectedTagIds.size > 0 ||
      this.hasValidCenter() ||
      Boolean(this.cardImageName) ||
      Boolean(this.coverImageName) ||
      this.galleryImages.length > 0;

    if (!hasDraft) return null;

    return {
      activeRegionId: this.activeRegionId,
      locationName: this.locationName,
      summary: this.summary,
      parentDestinationId: this.parentDestinationId,
      locationTypeId: this.locationTypeId,
      boundaryNote: this.boundaryNote,
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
    const draft = this.formDrafts.loadDraft<LocationFormDraft>(this.draftFormType());
    const payload = draft?.payload;
    if (!payload) return;

    if (typeof payload.activeRegionId === 'number') {
      this.setActiveRegion(payload.activeRegionId, false);
      this.loadAssignedRegionMap(payload.activeRegionId);
      this.loadParentDestinations();
    }

    this.locationName = payload.locationName ?? this.locationName;
    this.summary = payload.summary ?? this.summary;
    this.parentDestinationId = payload.parentDestinationId;
    this.locationTypeId = payload.locationTypeId;
    this.boundaryNote = payload.boundaryNote ?? this.boundaryNote;
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
