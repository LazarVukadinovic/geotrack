import {
  AfterViewInit,
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  NgZone,
  OnDestroy,
  OnInit,
  ViewChild,
} from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router } from '@angular/router';
import { finalize, of, Subscription, switchMap } from 'rxjs';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { DashboardService } from '../../../../core/services/dashboard.service';
import { LightboxService } from '../../../../core/services/lightbox.service';
import { ZoneDrawService, DrawTool } from '../../../../core/services/zone-draw.service';
import { LocationsListService } from '../../../../core/services/locations-list.service';
import { AdminMapService } from '../../../../core/services/admin-map.service';
import { RegionalMediaService } from '../../../../core/services/regional-media.service';
import { AuthService } from '../../../../core/services/auth.service';
import { RegionalAssignedRegion } from '../../../../core/models/dashboard.model';
import { AdminMapGeometry, AdminMapViewport, AdminMapZone } from '../../../../core/models/admin-map.model';
import {
  RegionalLocationBoundaryDto,
  RegionalLocationCreateRequest,
  RegionalLocationDetailDto,
  RegionalLocationParentDestinationDto,
  RegionalLocationTagDto,
  RegionalLocationTypeDto,
  RegionalLocationUpdateRequest,
} from '../../../../core/models/location.model';
import {
  RegionalEntityImageDto,
  RegionalEntityImageInput,
  RegionalImageUploadResult,
} from '../../../../core/models/regional-image.model';

declare const L: any;

type BoundaryShapeType = 'Polygon' | 'Rectangle' | 'Circle';

interface ClassItem {
  tag: RegionalLocationTagDto;
  checked: boolean;
}

@Component({
  selector: 'app-add-location',
  standalone: true,
  imports: [CommonModule, FormsModule, DecimalPipe, SidebarComponent, TopbarComponent],
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
  detailLoading = false;
  isSaving = false;
  errorMessage: string | null = null;
  feedbackMessage: string | null = null;

  locationName = '';
  summary = '';
  parentDestinationId: number | null = null;
  locationTypeId: number | null = null;
  boundaryNote = '';
  tagInput = '';
  internalReferenceId = '';
  locationStatus: 'Published' | 'Draft' | 'Archived' = 'Published';

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

  coverImageUrl: string | null = null;
  coverImageName = '';
  coverImageFile: File | null = null;
  galleryImages: { url: string; name: string; file?: File }[] = [];

  zoneModalOpen = false;
  zoneConfirmed = false;
  zoneAreaKm2: number | null = null;
  drawTool: DrawTool = 'polygon';
  confirmedShapeType: BoundaryShapeType | null = null;
  zoneValidationMessage: string | null = null;

  isEditMode = false;

  private miniMap: any = null;
  private modalMap: any = null;
  private drawnLayer: any = null;
  private confirmedLayer: any = null;
  private drawControl: any = null;
  private regionViewport: AdminMapViewport | null = null;
  private mapZones: AdminMapZone[] = [];
  private assignedRegionsSubscription: Subscription | null = null;
  private parentDestinationsSubscription: Subscription | null = null;
  private locationTypesSubscription: Subscription | null = null;
  private tagsSubscription: Subscription | null = null;
  private tagSearchSubscription: Subscription | null = null;
  private createSubscription: Subscription | null = null;
  private viewportSubscription: Subscription | null = null;
  private zonesSubscription: Subscription | null = null;
  private detailSubscription: Subscription | null = null;
  private tagSearchHandle: ReturnType<typeof setTimeout> | null = null;
  private editLocationId: string | null = null;
  private pendingDescriptionHtml: string | null = null;
  private imagesTouched = false;

  constructor(
    private readonly zone: NgZone,
    private readonly cdr: ChangeDetectorRef,
    private readonly router: Router,
    private readonly route: ActivatedRoute,
    private readonly dashboardService: DashboardService,
    private readonly lightboxService: LightboxService,
    private readonly zoneDrawService: ZoneDrawService,
    private readonly locationsService: LocationsListService,
    private readonly adminMapService: AdminMapService,
    private readonly regionalMediaService: RegionalMediaService,
    private readonly auth: AuthService,
  ) {}

  ngOnInit(): void {
    this.editLocationId = this.route.snapshot.paramMap.get('id');
    this.isEditMode = Boolean(this.editLocationId);
    this.loadAssignedRegions();
    this.loadLocationTypes();
    this.loadLocationTags();
  }

  ngAfterViewInit(): void {
    setTimeout(() => {
      this.initMiniMap();
      this.applyPendingDescription();
    }, 0);
  }

  ngOnDestroy(): void {
    this.assignedRegionsSubscription?.unsubscribe();
    this.parentDestinationsSubscription?.unsubscribe();
    this.locationTypesSubscription?.unsubscribe();
    this.tagsSubscription?.unsubscribe();
    this.tagSearchSubscription?.unsubscribe();
    this.createSubscription?.unsubscribe();
    this.viewportSubscription?.unsubscribe();
    this.zonesSubscription?.unsubscribe();
    this.detailSubscription?.unsubscribe();

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
    return Boolean(this.activeRegionId) &&
      Boolean(this.parentDestinationId) &&
      Boolean(this.selectedParentDestinationZone) &&
      this.zoneConfirmed &&
      !this.zoneValidationMessage &&
      !this.regionLoading &&
      !this.parentDestinationsLoading &&
      !this.locationTypesLoading &&
      !this.tagsLoading &&
      !this.detailLoading &&
      !this.isSaving;
  }

  get selectedParentDestinationName(): string {
    if (!this.parentDestinationId) return '-';
    return this.parentDestinations.find(item => item.id === this.parentDestinationId)?.displayName ?? '-';
  }

  get selectedParentDestinationZone(): AdminMapZone | null {
    if (!this.parentDestinationId) return null;
    return this.mapZones.find(zone =>
      zone.entityType === 'Destination' &&
      zone.id === this.parentDestinationId &&
      !!zone.geometryGeoJson) ?? null;
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
          this.loadParentDestinations();
          if (this.isEditMode) {
            this.loadLocationForEdit(selectedRegion.id);
          }
        },
        error: error => this.handleLoadError(error, 'Unable to load assigned regions.'),
      });
  }

  onRegionSelected(regionId: number): void {
    if (regionId === this.activeRegionId) return;
    if (this.isEditMode) {
      this.feedbackMessage = 'The region is locked while editing this location.';
      this.notifyStateChanged();
      return;
    }

    this.setActiveRegion(regionId, true);
    this.parentDestinationId = null;
    this.parentDestinations = [];
    this.resetZone();
    this.loadParentDestinations();
    this.feedbackMessage = 'Region changed. Parent destination and boundary selection were cleared.';
    this.notifyStateChanged();
  }

  onParentDestinationChanged(): void {
    this.resetZone();
    this.zoneValidationMessage = null;
    this.renderParentBoundaryOnMaps(true);

    this.feedbackMessage = this.parentDestinationId && !this.selectedParentDestinationZone
      ? 'Select a parent destination with a defined boundary before drawing this location.'
      : null;

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
          this.renderParentBoundaryOnMaps(true);
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
        this.imagesTouched = true;
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
    this.imagesTouched = true;
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
          this.imagesTouched = true;
          this.notifyStateChanged();
        });
      };
      reader.readAsDataURL(file);
    }
    input.value = '';
  }

  removeGalleryImage(index: number): void {
    this.galleryImages = this.galleryImages.filter((_, currentIndex) => currentIndex !== index);
    this.imagesTouched = true;
    this.notifyStateChanged();
  }

  openLightbox(images: { url: string; name: string }[], index: number): void {
    this.lightboxService.open(images, index);
  }

  openZoneModal(): void {
    if (!this.parentDestinationId) {
      this.errorMessage = 'Parent destination is required.';
      this.notifyStateChanged();
      return;
    }

    if (!this.selectedParentDestinationZone) {
      this.errorMessage = 'Select a parent destination with a defined boundary before drawing this location.';
      this.notifyStateChanged();
      return;
    }

    this.zoneModalOpen = true;
    this.notifyStateChanged();
    setTimeout(() => this.initModalMap(this.modalMapContainerRef.nativeElement), 50);
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
    if (this.drawnLayer && this.modalMap) {
      this.modalMap.removeLayer(this.drawnLayer);
      this.drawnLayer = null;
    }
    if (this.drawControl) {
      this.drawControl.disable();
      this.drawControl = null;
    }
    this.zoneValidationMessage = null;
    this.activateDrawTool();
  }

  confirmZone(): void {
    if (!this.drawnLayer) {
      this.errorMessage = 'Draw a location zone before confirming.';
      this.notifyStateChanged();
      return;
    }

    const validationMessage = this.validateDrawnLayer(this.drawnLayer);
    if (validationMessage) {
      this.zoneValidationMessage = validationMessage;
      this.errorMessage = validationMessage;
      this.styleDrawnLayer(this.drawnLayer, false);
      this.notifyStateChanged();
      return;
    }

    this.zoneAreaKm2 = this.zoneDrawService.calculateAreaKm2(this.drawnLayer);
    this.zoneConfirmed = true;
    this.confirmedLayer = this.drawnLayer;
    this.confirmedShapeType = this.shapeTypeFromDrawTool(this.drawTool);
    this.zoneValidationMessage = null;
    this.errorMessage = null;

    this.showZoneOnMiniMap();
    this.closeZoneModal();
  }

  publish(): void {
    if (this.isSaving) return;

    const payload = this.buildPayload();
    if (!payload) {
      this.notifyStateChanged();
      return;
    }

    this.isSaving = true;
    this.errorMessage = null;
    this.feedbackMessage = null;
    this.notifyStateChanged();

    this.createSubscription?.unsubscribe();
    const saveRegionId = this.activeRegionId;
    const saveLocationId = this.editLocationId;
    this.createSubscription = of(payload)
      .pipe(
        switchMap(payloadWithoutImages => {
          const files = this.selectedImageFiles();
          if (files.length === 0) {
            const request = this.shouldSendImages(files)
              ? { ...payloadWithoutImages, images: this.buildImageInputs([]) }
              : payloadWithoutImages;
            return this.isEditMode && saveRegionId && saveLocationId
              ? this.locationsService.updateLocation(saveRegionId, saveLocationId, request)
              : this.locationsService.createLocation(request as RegionalLocationCreateRequest);
          }

          return this.regionalMediaService.uploadImages(this.activeRegionId!, files).pipe(
            switchMap(uploaded => {
              const request = {
                ...payloadWithoutImages,
                images: this.buildImageInputs(uploaded.images),
              };
              return this.isEditMode && saveRegionId && saveLocationId
                ? this.locationsService.updateLocation(saveRegionId, saveLocationId, request)
                : this.locationsService.createLocation(request as RegionalLocationCreateRequest);
            }),
          );
        }),
        finalize(() => {
          this.isSaving = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: saved => {
          const message = this.isEditMode ? 'Location saved.' : 'Location published.';
          this.feedbackMessage = message;
          this.router.navigate(['/locations'], {
            queryParams: { regionId: saved.regionId },
            state: { feedbackMessage: message },
          });
        },
        error: error => this.handleActionError(error),
      });
  }

  private buildPayload(): (RegionalLocationCreateRequest | RegionalLocationUpdateRequest) | null {
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

    if (!this.zoneConfirmed || !this.confirmedLayer) {
      this.errorMessage = 'Draw a location zone before publishing.';
      return null;
    }

    if (this.zoneValidationMessage) {
      this.errorMessage = this.zoneValidationMessage;
      return null;
    }

    if (!this.selectedParentDestinationZone) {
      this.errorMessage = 'Select a parent destination with a defined boundary before drawing this location.';
      return null;
    }

    const descriptionHtml = this.editorRef?.nativeElement?.innerHTML?.trim() ?? '';
    const { zone, centerLat, centerLng } = this.zoneDrawService.extractZoneResult(this.confirmedLayer);
    const boundary = this.createBoundaryPayload(zone);
    if (!boundary) {
      this.errorMessage = 'Boundary geometry is invalid.';
      return null;
    }

    return {
      ...(this.isEditMode ? {} : { regionId }),
      destinationId,
      name,
      summary: normalizedSummary || null,
      description: descriptionHtml || null,
      status: this.isEditMode ? this.locationStatus : 'Published',
      locationTypeId: this.locationTypeId,
      latitude: centerLat,
      longitude: centerLng,
      boundaryNote: this.boundaryNote.trim() || null,
      boundary,
      tagIds: [...this.selectedTagIds],
    };
  }

  private loadLocationForEdit(regionId: number): void {
    const locationId = this.editLocationId;
    if (!locationId) return;

    this.detailLoading = true;
    this.errorMessage = null;
    this.notifyStateChanged();

    this.detailSubscription?.unsubscribe();
    this.detailSubscription = this.locationsService
      .getLocation(regionId, locationId)
      .pipe(
        finalize(() => {
          this.detailLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: location => this.applyLocationDetail(location),
        error: error => this.handleLoadError(error, 'Unable to load location details.'),
      });
  }

  private applyLocationDetail(location: RegionalLocationDetailDto): void {
    this.locationName = location.name ?? '';
    this.summary = location.summary ?? '';
    this.parentDestinationId = location.destinationId ?? null;
    this.locationTypeId = location.locationTypeId ?? null;
    this.boundaryNote = location.boundaryNote ?? '';
    this.internalReferenceId = location.internalReferenceId ?? '';
    this.locationStatus = this.normalizeEditableStatus(location.status);
    this.confirmedShapeType = this.normalizeShapeType(location.boundaryShapeType);
    this.selectedTagIds = new Set((location.tags ?? []).map(tag => tag.id));
    (location.tags ?? []).forEach(tag => this.upsertLocationTag(tag));
    this.classItems = this.locationTags.map(tag => ({
      tag,
      checked: this.selectedTagIds.has(tag.id),
    }));
    this.applyEntityImages(location.images ?? [], location.imageUrl ?? null, location.name);
    this.setDescriptionHtml(location.description ?? '');
    this.renderParentBoundaryOnMaps(true);
    this.applyEditBoundaryFromZones(location);
    this.notifyStateChanged();
  }

  private createBoundaryPayload(zone: [number, number][]): RegionalLocationBoundaryDto | null {
    const shapeType = this.confirmedShapeType;
    if (!shapeType || zone.length < 3) return null;

    return {
      shapeType,
      coordinates: zone.map(([latitude, longitude]) => ({ latitude, longitude })),
    };
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

    L.tileLayer('https://tiles.stadiamaps.com/tiles/alidade_smooth/{z}/{x}/{y}{r}.png', {
      maxZoom: 19,
    }).addTo(this.miniMap);

    this.renderParentBoundaryOnMaps(true);
  }

  private initModalMap(el: HTMLDivElement): void {
    if (this.modalMap || typeof L === 'undefined') return;

    this.modalMap = L.map(el, {
      zoomControl: true,
      attributionControl: false,
    });

    L.tileLayer('https://tiles.stadiamaps.com/tiles/alidade_smooth/{z}/{x}/{y}{r}.png', {
      maxZoom: 19,
    }).addTo(this.modalMap);

    setTimeout(() => this.modalMap?.invalidateSize(), 200);
    this.renderParentBoundaryOnMaps(false);

    if (this.confirmedLayer) {
      const layerCopy = this.zoneDrawService.cloneLayer(this.confirmedLayer);
      if (layerCopy) {
        layerCopy.addTo(this.modalMap);
        this.drawnLayer = layerCopy;
        this.modalMap.fitBounds(layerCopy.getBounds(), { padding: [30, 30] });
      }
    } else {
      this.fitContext(this.modalMap, [30, 30]);
    }

    this.activateDrawTool();
  }

  private destroyModalMap(): void {
    if (this.modalMap) {
      this.modalMap.remove();
      this.modalMap = null;
      this.drawnLayer = null;
      this.drawControl = null;
    }
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
        this.zoneValidationMessage = this.validateDrawnLayer(this.drawnLayer);
        this.styleDrawnLayer(this.drawnLayer, !this.zoneValidationMessage);
        this.notifyStateChanged();
      },
    );
  }

  private showZoneOnMiniMap(): void {
    if (!this.miniMap) this.initMiniMap();
    if (!this.miniMap || !this.confirmedLayer) return;

    this.miniMap.eachLayer((layer: any) => {
      if (layer._isConfirmedZone) this.miniMap.removeLayer(layer);
    });

    const clone = this.zoneDrawService.cloneLayer(this.confirmedLayer);
    if (!clone) return;

    clone._isConfirmedZone = true;
    clone.addTo(this.miniMap);

    try {
      this.miniMap.fitBounds(clone.getBounds(), { padding: [10, 10] });
    } catch {
      // Fitting can fail while Leaflet is still settling container dimensions.
    }
  }

  private resetZone(): void {
    this.zoneConfirmed = false;
    this.zoneAreaKm2 = null;
    this.confirmedLayer = null;
    this.confirmedShapeType = null;

    if (this.miniMap) {
      this.miniMap.eachLayer((layer: any) => {
        if (layer._isConfirmedZone) this.miniMap.removeLayer(layer);
      });
      this.fitContext(this.miniMap, [10, 10]);
    }
    this.zoneValidationMessage = null;
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
    this.loadRegionViewport(regionId);
    this.loadMapZones(regionId);

    if (updateUrl) {
      this.router.navigate([], {
        relativeTo: this.route,
        queryParams: { regionId },
        queryParamsHandling: 'merge',
        replaceUrl: true,
      });
    }
  }

  private loadRegionViewport(regionId: number): void {
    this.viewportSubscription?.unsubscribe();
    this.viewportSubscription = this.adminMapService.getViewport(regionId)
      .subscribe({
        next: viewport => {
          this.regionViewport = viewport;
          this.fitContext(this.miniMap, [10, 10]);
          if (this.modalMap && !this.drawnLayer) {
            this.fitContext(this.modalMap, [30, 30]);
          }
          this.notifyStateChanged();
        },
        error: () => {
          this.regionViewport = null;
          this.fitContext(this.miniMap, [10, 10]);
          this.notifyStateChanged();
        },
      });
  }

  private loadMapZones(regionId: number): void {
    this.zonesSubscription?.unsubscribe();
    this.zonesSubscription = this.adminMapService.getZones(regionId)
      .subscribe({
        next: zones => {
          this.mapZones = zones ?? [];
          this.renderParentBoundaryOnMaps(true);
          if (this.isEditMode) {
            this.applyEditBoundaryFromZones();
          }
          this.notifyStateChanged();
        },
        error: () => {
          this.mapZones = [];
          this.renderParentBoundaryOnMaps(true);
          this.notifyStateChanged();
        },
      });
  }

  private renderParentBoundaryOnMaps(fitMiniMap: boolean): void {
    this.renderParentBoundaryOnMap(this.miniMap, fitMiniMap, [10, 10]);
    this.renderParentBoundaryOnMap(this.modalMap, true, [30, 30]);
  }

  private renderParentBoundaryOnMap(map: any, fit: boolean, padding: [number, number]): void {
    if (!map || typeof L === 'undefined') return;

    this.removeFlaggedLayers(map, '_isParentDestinationBoundary');

    const geometry = this.selectedParentDestinationZone?.geometryGeoJson;
    if (!geometry) {
      if (fit) this.fitContext(map, padding);
      return;
    }

    const layer = L.geoJSON(geometry, {
      interactive: false,
      style: {
        color: '#1d4ed8',
        weight: 2,
        opacity: 0.9,
        fillColor: '#93c5fd',
        fillOpacity: 0.14,
        dashArray: '6 4',
      },
    });
    layer._isParentDestinationBoundary = true;
    layer.addTo(map);

    if (fit) {
      try {
        map.fitBounds(layer.getBounds(), { padding, animate: false });
      } catch {
        this.applyRegionViewport(map, padding);
      }
    }
  }

  private fitContext(map: any, padding: [number, number]): void {
    if (!map || typeof L === 'undefined') return;

    const selectedZone = this.selectedParentDestinationZone;
    if (selectedZone?.geometryGeoJson) {
      this.renderParentBoundaryOnMap(map, true, padding);
      return;
    }

    this.applyRegionViewport(map, padding);
  }

  private applyRegionViewport(map: any, padding: [number, number]): void {
    if (!map || typeof L === 'undefined') return;

    const viewport = this.regionViewport;
    try {
      if (viewport?.bounds) {
        map.fitBounds(
          [
            [viewport.bounds.south, viewport.bounds.west],
            [viewport.bounds.north, viewport.bounds.east],
          ],
          { padding, animate: false },
        );
        return;
      }

      if (
        typeof viewport?.centerLatitude === 'number' &&
        typeof viewport.centerLongitude === 'number' &&
        typeof viewport.zoom === 'number'
      ) {
        map.setView([viewport.centerLatitude, viewport.centerLongitude], viewport.zoom, { animate: false });
        return;
      }

      map.fitWorld({ animate: false });
    } catch {
      // Leaflet can throw while the container is hidden; a later invalidateSize will settle the view.
    }
  }

  private removeFlaggedLayers(map: any, flag: string): void {
    map.eachLayer((layer: any) => {
      if (layer?.[flag]) {
        map.removeLayer(layer);
      }
    });
  }

  private validateDrawnLayer(layer: any): string | null {
    if (!this.selectedParentDestinationZone?.geometryGeoJson) {
      return 'Select a parent destination with a defined boundary before drawing this location.';
    }

    try {
      const { zone } = this.zoneDrawService.extractZoneResult(layer);
      if (!this.isZoneCoveredByParent(zone, this.selectedParentDestinationZone.geometryGeoJson)) {
        return 'This location boundary is partially outside the destination.';
      }
    } catch {
      return 'Boundary geometry is invalid.';
    }

    return null;
  }

  private styleDrawnLayer(layer: any, valid: boolean): void {
    if (typeof layer?.setStyle !== 'function') return;
    layer.setStyle(valid
      ? { color: '#2563eb', fillColor: '#60a5fa', fillOpacity: 0.18, weight: 2 }
      : { color: '#dc2626', fillColor: '#fecaca', fillOpacity: 0.28, weight: 2 });
  }

  private isZoneCoveredByParent(zone: [number, number][], geometry: AdminMapGeometry): boolean {
    const ring = this.closeRing(zone.map(([latitude, longitude]) => [longitude, latitude] as [number, number]));
    if (ring.length < 4) return false;

    const checkPoints: [number, number][] = [];
    for (let index = 0; index < ring.length - 1; index += 1) {
      const current = ring[index];
      const next = ring[index + 1];
      checkPoints.push(current);
      checkPoints.push([(current[0] + next[0]) / 2, (current[1] + next[1]) / 2]);
    }

    return this.parentPolygons(geometry).some(polygon =>
      checkPoints.every(point => this.pointCoveredByPolygon(point, polygon)),
    );
  }

  private parentPolygons(geometry: AdminMapGeometry): [number, number][][][] {
    if (geometry.type === 'Polygon') {
      return [geometry.coordinates as [number, number][][]];
    }

    return geometry.coordinates as [number, number][][][];
  }

  private pointCoveredByPolygon(point: [number, number], rings: [number, number][][]): boolean {
    const exterior = rings[0] ?? [];
    if (!this.pointInRingOrBoundary(point, exterior)) return false;

    return rings.slice(1).every(hole => !this.pointInRingStrict(point, hole));
  }

  private pointInRingOrBoundary(point: [number, number], ring: [number, number][]): boolean {
    if (this.pointOnRingBoundary(point, ring)) return true;
    return this.pointInRingStrict(point, ring);
  }

  private pointInRingStrict(point: [number, number], ring: [number, number][]): boolean {
    let inside = false;
    for (let i = 0, j = ring.length - 1; i < ring.length; j = i, i += 1) {
      const xi = ring[i][0];
      const yi = ring[i][1];
      const xj = ring[j][0];
      const yj = ring[j][1];
      const intersects = yi > point[1] !== yj > point[1] &&
        point[0] < ((xj - xi) * (point[1] - yi)) / (yj - yi) + xi;
      if (intersects) inside = !inside;
    }
    return inside;
  }

  private pointOnRingBoundary(point: [number, number], ring: [number, number][]): boolean {
    for (let index = 0; index < ring.length - 1; index += 1) {
      if (this.pointOnSegment(point, ring[index], ring[index + 1])) {
        return true;
      }
    }
    return false;
  }

  private pointOnSegment(point: [number, number], start: [number, number], end: [number, number]): boolean {
    const cross = (point[1] - start[1]) * (end[0] - start[0]) -
      (point[0] - start[0]) * (end[1] - start[1]);
    if (Math.abs(cross) > 1e-9) return false;

    const dot = (point[0] - start[0]) * (end[0] - start[0]) +
      (point[1] - start[1]) * (end[1] - start[1]);
    if (dot < -1e-9) return false;

    const lengthSquared = (end[0] - start[0]) ** 2 + (end[1] - start[1]) ** 2;
    return dot <= lengthSquared + 1e-9;
  }

  private closeRing(points: [number, number][]): [number, number][] {
    if (points.length === 0) return points;
    const first = points[0];
    const last = points[points.length - 1];
    if (Math.abs(first[0] - last[0]) < 1e-9 && Math.abs(first[1] - last[1]) < 1e-9) {
      return points;
    }
    return [...points, first];
  }

  private selectedImageFiles(): File[] {
    const files: File[] = [];
    if (this.coverImageFile) {
      files.push(this.coverImageFile);
    }

    files.push(...this.galleryImages
      .map(image => image.file)
      .filter((file): file is File => !!file));
    return files;
  }

  private shouldSendImages(files: File[]): boolean {
    return !this.isEditMode || this.imagesTouched || files.length > 0;
  }

  private buildImageInputs(uploaded: RegionalImageUploadResult[]): RegionalEntityImageInput[] {
    const images: RegionalEntityImageInput[] = [];
    let uploadIndex = 0;
    const fallbackName = this.locationName.trim() || 'Location image';

    if (this.coverImageUrl) {
      const uploadedCover = this.coverImageFile ? uploaded[uploadIndex++] : null;
      images.push({
        url: uploadedCover?.url ?? this.coverImageUrl,
        altText: `${fallbackName} cover`,
        caption: null,
        isMain: true,
        sortOrder: 0,
      });
    }

    for (const image of this.galleryImages) {
      const uploadedGalleryImage = image.file ? uploaded[uploadIndex++] : null;
      images.push({
        url: uploadedGalleryImage?.url ?? image.url,
        altText: image.name || fallbackName,
        caption: null,
        isMain: false,
        sortOrder: images.length,
      });
    }

    return images.map((image, index) => ({
      ...image,
      isMain: index === 0,
      sortOrder: index,
    }));
  }

  private applyEntityImages(images: RegionalEntityImageDto[], fallbackImageUrl: string | null, fallbackName: string): void {
    const orderedImages = [...images]
      .sort((a, b) => Number(b.isMain) - Number(a.isMain) || a.sortOrder - b.sortOrder || a.id - b.id);

    if (orderedImages.length === 0 && fallbackImageUrl) {
      this.coverImageUrl = fallbackImageUrl;
      this.coverImageName = fallbackName;
      this.coverImageFile = null;
      this.galleryImages = [];
      this.imagesTouched = false;
      return;
    }

    const [cover, ...gallery] = orderedImages;
    this.coverImageUrl = cover?.url ?? null;
    this.coverImageName = cover?.altText ?? fallbackName;
    this.coverImageFile = null;
    this.galleryImages = gallery.map(image => ({
      url: image.url,
      name: image.altText ?? fallbackName,
    }));
    this.imagesTouched = false;
  }

  private setDescriptionHtml(html: string): void {
    this.pendingDescriptionHtml = html;
    this.applyPendingDescription();
  }

  private applyPendingDescription(): void {
    if (this.pendingDescriptionHtml === null || !this.editorRef?.nativeElement) return;
    this.editorRef.nativeElement.innerHTML = this.pendingDescriptionHtml;
    this.pendingDescriptionHtml = null;
    this.syncEmptyState();
  }

  private applyEditBoundaryFromZones(location?: RegionalLocationDetailDto): void {
    const id = Number(location?.id ?? this.editLocationId);
    if (!Number.isFinite(id) || id <= 0) return;

    const zone = this.mapZones.find(item => item.entityType === 'Location' && item.id === id);
    const layer = this.layerFromGeometry(zone?.geometryGeoJson ?? null);
    if (!layer) return;

    this.confirmedLayer = layer;
    this.drawnLayer = layer;
    this.zoneConfirmed = true;
    if (location?.boundaryShapeType) {
      this.confirmedShapeType = this.normalizeShapeType(location.boundaryShapeType);
    } else if (!this.confirmedShapeType) {
      this.confirmedShapeType = 'Polygon';
    }
    this.zoneAreaKm2 = this.zoneDrawService.calculateAreaKm2(layer);
    this.zoneValidationMessage = this.validateDrawnLayer(layer);
    this.styleDrawnLayer(layer, !this.zoneValidationMessage);
    this.showZoneOnMiniMap();
  }

  private layerFromGeometry(geometry: AdminMapGeometry | null): any {
    if (!geometry || typeof L === 'undefined') return null;

    const exteriorRing = geometry.type === 'Polygon'
      ? (geometry.coordinates as [number, number][][])[0]
      : ((geometry.coordinates as [number, number][][][])[0]?.[0] ?? []);

    const points = exteriorRing
      .map(([longitude, latitude]) => [latitude, longitude] as [number, number])
      .filter(([latitude, longitude]) => Number.isFinite(latitude) && Number.isFinite(longitude));

    if (points.length > 1) {
      const first = points[0];
      const last = points[points.length - 1];
      if (Math.abs(first[0] - last[0]) < 1e-9 && Math.abs(first[1] - last[1]) < 1e-9) {
        points.pop();
      }
    }

    return points.length >= 3 ? L.polygon(points, this.zoneDrawService.cloneStyle) : null;
  }

  private normalizeShapeType(value?: string | null): BoundaryShapeType {
    return value === 'Rectangle' || value === 'Circle' ? value : 'Polygon';
  }

  private normalizeEditableStatus(value?: string | null): 'Published' | 'Draft' | 'Archived' {
    const normalized = (value ?? '').toLowerCase();
    if (normalized === 'draft') return 'Draft';
    if (normalized === 'archived') return 'Archived';
    return 'Published';
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
      this.errorMessage = error.error?.message ?? 'Unable to publish location.';
      this.notifyStateChanged();
      return;
    }

    this.errorMessage = 'Unable to publish location.';
    this.notifyStateChanged();
  }

  private notifyStateChanged(): void {
    this.cdr.markForCheck();
  }
}
