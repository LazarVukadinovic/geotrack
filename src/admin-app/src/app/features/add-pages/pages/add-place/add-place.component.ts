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
import { CommonModule, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { ActivatedRoute, Router } from '@angular/router';
import { finalize, forkJoin, map, of, Subscription, switchMap } from 'rxjs';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { CoverImageCropperComponent } from '../../../../shared/components/cover-image-cropper/cover-image-cropper.component';
import { DashboardService } from '../../../../core/services/dashboard.service';
import { LightboxService } from '../../../../core/services/lightbox.service';
import { PlacesListService } from '../../../../core/services/places-list.service';
import { RegionalMediaService } from '../../../../core/services/regional-media.service';
import { AuthService } from '../../../../core/services/auth.service';
import { AdminMapService } from '../../../../core/services/admin-map.service';
import { MapTileLayerService } from '../../../../core/services/map-tile-layer.service';
import { AdminFormDraftService } from '../../../../core/services/admin-form-draft.service';
import {
  AdminFormTransientFileStoreService,
  AdminTransientFileItem,
} from '../../../../core/services/admin-form-transient-media.service';
import { COVER_IMAGE_ASPECT_RATIO } from '../../../../core/constants/cover-image.constants';
import { RegionalAssignedRegion } from '../../../../core/models/dashboard.model';
import { AdminMapViewport, AdminMapZone, RegionalScopeValidationResult } from '../../../../core/models/admin-map.model';
import {
  RegionalPlaceCreateRequest,
  RegionalPlaceDetailDto,
  RegionalPlaceOperatingHourDto,
  RegionalPlaceParentDestinationDto,
  RegionalPlaceParentLocationDto,
  RegionalPlaceTagDto,
  RegionalPlaceTypeDto,
  RegionalStayTypeDto,
  RegionalPlaceUpdateRequest,
} from '../../../../core/models/place.model';
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
  tag: RegionalPlaceTagDto;
  checked: boolean;
}

interface DayHours {
  name: string;
  open: boolean;
  openTime: string;
  closeTime: string;
}

interface PlaceFormDraft {
  activeRegionId: number | null;
  parentDestinationId: number | null;
  placeName: string;
  summary: string;
  parentLocationId: number | null;
  placeTypeId: number | null;
  stayTypeId: number | null;
  bestTimeToVisit: string;
  visitorTips: string;
  accessNotes: string;
  accessibilityNotes: string;
  website: string;
  contactPhone: string;
  admissionPrice: string;
  bookingLink: string;
  opsEnabled: boolean;
  open24Hours: boolean;
  days: DayHours[];
  streetAddress: string;
  pinLat: string;
  pinLng: string;
  modalPinLat: number | null;
  modalPinLng: number | null;
  pinConfirmed: boolean;
  descriptionHtml: string;
  selectedTagIds: number[];
  cardImageName: string;
  coverImageName: string;
  galleryImageNames: string[];
}

interface CreateDestinationReturnState {
  id: number;
  name: string;
}

interface CreateLocationReturnState {
  id: number;
  name: string;
  destinationId: number | null;
}

interface AddPlaceReturnFlowState {
  restoredDraft?: PlaceFormDraft | null;
  newlyCreatedDestination?: CreateDestinationReturnState | null;
  newlyCreatedLocation?: CreateLocationReturnState | null;
}

type TimeField = 'openTime' | 'closeTime';

interface MapReverseGeocodeDto {
  address: string;
  city: string;
  region: string;
  country: string;
  displayLabel: string;
}

@Component({
  selector: 'app-new-place',
  standalone: true,
  imports: [CommonModule, FormsModule, DecimalPipe, SidebarComponent, TopbarComponent, CoverImageCropperComponent],
  templateUrl: './add-place.component.html',
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
export class NewPlaceComponent implements OnInit, AfterViewInit, OnDestroy {
  private static readonly activeRegionStorageKey = 'geotrack_admin_active_region_id';
  private static readonly initialVisibleClassificationTags = 5;
  private readonly mapTileLayer = inject(MapTileLayerService);
  readonly coverImageAspectRatio = COVER_IMAGE_ASPECT_RATIO;
  readonly coverImageAspectRatioCss = `${COVER_IMAGE_ASPECT_RATIO} / 1`;
  readonly createDestinationOptionValue = '__create_new_destination__';
  readonly createLocationOptionValue = '__create_new_location__';

  @ViewChild('editorRef') editorRef!: ElementRef<HTMLDivElement>;
  @ViewChild('miniMapContainer') miniMapContainer!: ElementRef<HTMLDivElement>;
  @ViewChild('modalMapContainer') modalMapContainerRef!: ElementRef<HTMLDivElement>;

  assignedRegions: RegionalAssignedRegion[] = [];
  activeRegionId: number | null = null;
  regionLoading = false;
  parentDestinationsLoading = false;
  parentLocationsLoading = false;
  placeTypesLoading = false;
  hasLinkedContribution = false;
  tagsLoading = false;
  tagSearchLoading = false;
  isSaving = false;
  isGeocodingAddress = false;
  errorMessage: string | null = null;
  feedbackMessage: string | null = null;
  regionBoundaryMissingMessage: string | null = null;
  scopeValidationMessage: string | null = null;
  scopeValidationTone: 'success' | 'warning' | 'error' | null = null;
  scopeValidationPending = false;

  isEditMode = false;
  editPlaceId: string | null = null;
  private editLoadedDestination: RegionalPlaceParentDestinationDto | null = null;
  private editLoadedLocation: RegionalPlaceParentLocationDto | null = null;

  placeName = '';
  summary = '';
  parentDestinationId: number | null = null;
  parentLocationId: number | null = null;
  placeTypeId: number | null = null;
  stayTypeId: number | null = null;
  bestTimeToVisit = '';
  visitorTips = '';
  accessNotes = '';
  accessibilityNotes = '';
  website = '';
  contactPhone = '';
  admissionPrice = '';
  bookingLink = '';
  opsEnabled = false;
  open24Hours = false;
  tagInput = '';
  tagFilter = '';

  parentDestinations: RegionalPlaceParentDestinationDto[] = [];
  parentLocations: RegionalPlaceParentLocationDto[] = [];
  placeTypes: RegionalPlaceTypeDto[] = [];
  stayTypes: RegionalStayTypeDto[] = [];
  placeTags: RegionalPlaceTagDto[] = [];
  classItems: ClassItem[] = [];
  additionalTagResults: RegionalPlaceTagDto[] = [];
  selectedTagIds = new Set<number>();
  classificationExpanded = false;

  isBold = false;
  isItalic = false;
  isUnderline = false;
  editorIsEmpty = true;
  readonly descriptionMaxLength = 2000;
  descriptionLimitReached = false;

  cardImageUrl: string | null = null;
  cardImageName = '';
  coverImageUrl: string | null = null;
  coverImageName = '';
  coverCropFile: File | null = null;
  galleryImages: { url: string; name: string; file?: File }[] = [];

  private cardImageFile: File | null = null;
  private coverImageFile: File | null = null;
  private cardPreviewObjectUrl: string | null = null;
  private coverPreviewObjectUrl: string | null = null;
  private galleryPreviewObjectUrls: string[] = [];

  days: DayHours[] = [
    { name: 'Monday', open: true, openTime: '09:00', closeTime: '17:00' },
    { name: 'Tuesday', open: true, openTime: '09:00', closeTime: '17:00' },
    { name: 'Wednesday', open: true, openTime: '09:00', closeTime: '17:00' },
    { name: 'Thursday', open: true, openTime: '09:00', closeTime: '17:00' },
    { name: 'Friday', open: true, openTime: '09:00', closeTime: '17:00' },
    { name: 'Saturday', open: true, openTime: '10:00', closeTime: '20:00' },
    { name: 'Sunday', open: false, openTime: '09:00', closeTime: '17:00' },
  ];
  activeTimePicker: { day: DayHours; field: TimeField } | null = null;
  timePickerMode: 'hour' | 'minute' = 'hour';
  isDraggingMinute = false;
  readonly minuteHandRadius = 138;
  readonly clockOuterHours = [12, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];
  readonly clockInnerHours = [0, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23];
  readonly clockMinutes = Array.from({ length: 12 }, (_, index) => index * 5);

  pinModalOpen = false;
  pinConfirmed = false;
  pinLat = '';
  pinLng = '';
  streetAddress = '';
  modalPinLat: number | null = null;
  modalPinLng: number | null = null;

  private miniMap: any = null;
  private modalMap: any = null;
  private miniRegionBoundaryLayer: any = null;
  private modalRegionBoundaryLayer: any = null;
  private miniMarker: any = null;
  private modalMarker: any = null;
  private assignedRegionsSubscription: Subscription | null = null;
  private parentDestinationsSubscription: Subscription | null = null;
  private parentLocationsSubscription: Subscription | null = null;
  private placeTypesSubscription: Subscription | null = null;
  private tagsSubscription: Subscription | null = null;
  private tagSearchSubscription: Subscription | null = null;
  private createSubscription: Subscription | null = null;
  private reverseGeocodeSubscription: Subscription | null = null;
  private viewportSubscription: Subscription | null = null;
  private validationSubscription: Subscription | null = null;
  private tagSearchHandle: ReturnType<typeof setTimeout> | null = null;
  private geocodeDebounceHandle: ReturnType<typeof setTimeout> | null = null;
  private regionViewport: AdminMapViewport | null = null;
  private regionBoundaryZone: AdminMapZone | null = null;
  private scopeValidationResult: RegionalScopeValidationResult | null = null;
  private lastScopeValidationKey: string | null = null;
  private unregisterDraftSource: (() => void) | null = null;
  private restoredDraftFromNavigation: PlaceFormDraft | null = null;
  private pendingNewlyCreatedDestination: CreateDestinationReturnState | null = null;
  private pendingNewlyCreatedLocation: CreateLocationReturnState | null = null;

  constructor(
    private readonly zone: NgZone,
    private readonly cdr: ChangeDetectorRef,
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly dashboardService: DashboardService,
    private readonly lightboxService: LightboxService,
    private readonly placesService: PlacesListService,
    private readonly regionalMedia: RegionalMediaService,
    private readonly auth: AuthService,
    private readonly http: HttpClient,
    private readonly adminMapService: AdminMapService,
    private readonly formDrafts: AdminFormDraftService,
    private readonly transientMedia: AdminFormTransientFileStoreService,
  ) {}

  ngOnInit(): void {
    this.editPlaceId = this.route.snapshot.paramMap.get('id');
    this.isEditMode = Boolean(this.editPlaceId);
    this.captureReturnFlowState();

    this.loadAssignedRegions();
    this.applyInitialCoordinatesFromRoute();
    this.loadPlaceTypes();
    this.loadStayTypes();
    this.loadPlaceTags();
    this.registerDraftSource();
  }

  ngAfterViewInit(): void {
    setTimeout(() => {
      this.initMiniMap();
      const coordinates = this.readPinnedCoordinates();
      if (coordinates && this.pinConfirmed) {
        this.updateMiniMapPin(coordinates.latitude, coordinates.longitude);
      }
      const restoredFromNavigation = this.restoreDraftFromNavigationIfPresent();
      if (!restoredFromNavigation) {
        this.restoreSavedDraftIfPresent();
      }
    }, 0);
  }

  ngOnDestroy(): void {
    this.assignedRegionsSubscription?.unsubscribe();
    this.parentDestinationsSubscription?.unsubscribe();
    this.parentLocationsSubscription?.unsubscribe();
    this.placeTypesSubscription?.unsubscribe();
    this.tagsSubscription?.unsubscribe();
    this.tagSearchSubscription?.unsubscribe();
    this.createSubscription?.unsubscribe();
    this.reverseGeocodeSubscription?.unsubscribe();
    this.viewportSubscription?.unsubscribe();
    this.validationSubscription?.unsubscribe();
    this.unregisterDraftSource?.();

    if (this.tagSearchHandle) {
      clearTimeout(this.tagSearchHandle);
    }
    if (this.geocodeDebounceHandle) {
      clearTimeout(this.geocodeDebounceHandle);
    }

    this.destroyModalMap();
    this.revokeCardPreviewObjectUrl();
    this.revokeCoverPreviewObjectUrl();
    this.revokeGalleryPreviewObjectUrls();
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

  get selectedParentDestination(): RegionalPlaceParentDestinationDto | null {
    if (!this.parentDestinationId) return null;

    return this.parentDestinations.find(destination => destination.id === this.parentDestinationId)
      ?? (this.selectedParentLocation?.destinationId === this.parentDestinationId
        ? {
            id: this.parentDestinationId,
            name: this.selectedParentLocation.destinationName,
            displayName: this.selectedParentLocation.destinationName,
            status: '',
          }
        : null);
  }

  get selectedParentLocation(): RegionalPlaceParentLocationDto | null {
    return this.parentLocations.find(location => location.id === this.parentLocationId) ?? null;
  }

  get selectedParentDestinationName(): string {
    return this.selectedParentDestination?.displayName
      ?? this.selectedParentDestination?.name
      ?? this.selectedParentLocation?.destinationName
      ?? '';
  }

  get selectedPlaceTypeName(): string {
    return this.placeTypes.find(type => type.id === this.placeTypeId)?.name ?? '';
  }

  get isStayType(): boolean {
    const name = (this.selectedPlaceTypeName ?? '').toLowerCase().trim();
    return name === 'stay' || name === 'stays' || name === 'hotel' || name === 'hotels' ||
      name === 'accommodation' || name === 'accommodations';
  }

  onPlaceTypeSelection(value: number | null): void {
    this.placeTypeId = value;

    if (this.isStayType) {
      if (!this.stayTypeId) {
        this.stayTypeId = this.stayTypes[0]?.id ?? null;
      }
      return;
    }

    this.stayTypeId = null;
    this.bookingLink = '';
  }

  get selectedStayTypeName(): string {
    return this.stayTypes.find(t => t.id === this.stayTypeId)?.name ?? '';
  }

  get selectedAdditionalTags(): RegionalPlaceTagDto[] {
    return this.placeTags
      .filter(tag => this.selectedTagIds.has(tag.id))
      .sort((a, b) => a.name.localeCompare(b.name));
  }

  get visibleClassItems(): ClassItem[] {
    const query = this.tagFilter.trim().toLocaleLowerCase();
    if (!query) return this.classItems;

    return this.classItems.filter(item =>
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
      : NewPlaceComponent.initialVisibleClassificationTags;
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

  get canPublish(): boolean {
    const mediaReady = this.isEditMode || (Boolean(this.cardImageFile) && Boolean(this.coverImageFile));
    return Boolean(this.activeRegionId) &&
      Boolean(this.parentDestinationId) &&
      Boolean(this.parentLocationId) &&
      Boolean(this.placeTypeId) &&
      this.placeName.trim().length > 0 &&
      this.summary.trim().length > 0 &&
      (this.isEditMode || this.pinConfirmed) &&
      !this.regionBoundaryMissingMessage &&
      !this.scopeValidationPending &&
      !this.isScopeValidationRejected &&
      this.scopeValidationResult?.isAllowed === true &&
      mediaReady &&
      !this.regionLoading &&
      !this.parentDestinationsLoading &&
      !this.parentLocationsLoading &&
      !this.placeTypesLoading &&
      !this.tagsLoading &&
      !this.isSaving;
  }

  get isScopeValidationRejected(): boolean {
    return this.scopeValidationResult?.isAllowed === false;
  }

  get canConfirmPin(): boolean {
    return this.modalPinLat !== null &&
      this.modalPinLng !== null &&
      this.hasValidCoordinates(this.modalPinLat, this.modalPinLng) &&
      !this.scopeValidationPending &&
      this.scopeValidationResult?.isAllowed === true;
  }

  loadAssignedRegions(): void {
    this.regionLoading = true;
    if (!this.isEditMode) {
      this.errorMessage = null;
    }
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
            this.parentLocationId = null;
            this.parentDestinations = [];
            this.parentLocations = [];
            this.clearPersistedRegion();
            this.errorMessage = 'No active assigned region is available for this account.';
            this.notifyStateChanged();
            return;
          }

          const queryRegionId = Number(this.route.snapshot.queryParamMap.get('regionId'));
          const stateRegionId = Number((history.state as { regionId?: number | null })?.regionId);
          const persistedRegionId = this.readPersistedRegionId();
          const selectedRegion =
            this.assignedRegions.find(region => region.id === queryRegionId) ??
            this.assignedRegions.find(region => region.id === stateRegionId) ??
            this.assignedRegions.find(region => region.id === persistedRegionId) ??
            this.assignedRegions[0];

          this.setActiveRegion(selectedRegion.id, false);
          this.loadParentDestinations();
          this.loadRegionViewport(selectedRegion.id);
          this.loadPlaceForEditIfNeeded();
        },
        error: error => this.handleLoadError(error, 'Unable to load assigned regions.'),
      });
  }

  onRegionSelected(regionId: number): void {
    if (regionId === this.activeRegionId) return;

    this.editLoadedDestination = null;
    this.editLoadedLocation = null;
    this.setActiveRegion(regionId, true);
    this.parentDestinationId = null;
    this.parentLocationId = null;
    this.parentDestinations = [];
    this.parentLocations = [];
    this.resetPin();
    this.loadParentDestinations();
    this.loadRegionViewport(regionId);
    this.feedbackMessage = 'Region changed. Parent destination, parent location and map pin were cleared.';
    this.notifyStateChanged();
  }

  loadParentDestinations(): void {
    if (!this.activeRegionId) {
      this.parentDestinations = [];
      this.parentDestinationId = null;
      this.parentLocations = [];
      this.parentLocationId = null;
      this.notifyStateChanged();
      return;
    }

    this.parentDestinationsLoading = true;
    this.notifyStateChanged();

    this.parentDestinationsSubscription?.unsubscribe();
    this.parentDestinationsSubscription = this.placesService
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
          if (this.pendingNewlyCreatedDestination) {
            this.parentDestinations = this.normalizeParentDestinations([
              ...this.parentDestinations,
              {
                id: this.pendingNewlyCreatedDestination.id,
                name: this.pendingNewlyCreatedDestination.name,
                displayName: this.pendingNewlyCreatedDestination.name,
                status: 'Published',
              },
            ]);
            this.parentDestinationId = this.pendingNewlyCreatedDestination.id;
            this.pendingNewlyCreatedDestination = null;
          }
          this.ensureLoadedParentSelectionsPresent();
          if (
            this.parentDestinationId &&
            !this.parentDestinations.some(destination => destination.id === this.parentDestinationId)
          ) {
            this.parentDestinationId = null;
            this.parentLocationId = null;
            this.parentLocations = [];
          }
        },
        error: error => this.handleLoadError(error, 'Unable to load parent destinations.'),
      });
  }

  loadParentLocations(): void {
    if (!this.activeRegionId || !this.parentDestinationId) {
      this.parentLocations = [];
      this.parentLocationId = null;
      this.notifyStateChanged();
      return;
    }

    this.parentLocationsLoading = true;
    this.notifyStateChanged();

    this.parentLocationsSubscription?.unsubscribe();
    this.parentLocationsSubscription = this.placesService
      .getParentLocations(this.activeRegionId, null, 50, this.parentDestinationId)
      .pipe(
        finalize(() => {
          this.parentLocationsLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: locations => {
          this.parentLocations = this.normalizeParentLocations(locations)
            .filter(location => location.destinationId === this.parentDestinationId);
          if (
            this.pendingNewlyCreatedLocation &&
            this.pendingNewlyCreatedLocation.destinationId === this.parentDestinationId
          ) {
            this.parentLocations = this.normalizeParentLocations([
              ...this.parentLocations,
              {
                id: this.pendingNewlyCreatedLocation.id,
                name: this.pendingNewlyCreatedLocation.name,
                displayName: this.pendingNewlyCreatedLocation.name,
                destinationId: this.pendingNewlyCreatedLocation.destinationId ?? this.parentDestinationId ?? 0,
                destinationName: this.selectedParentDestinationName,
                status: 'Published',
              },
            ]);
            this.parentLocationId = this.pendingNewlyCreatedLocation.id;
            this.pendingNewlyCreatedLocation = null;
            setTimeout(() => this.revalidatePinnedCoordinatesIfPresent(), 0);
          }
          this.ensureLoadedParentSelectionsPresent();
          if (
            this.parentLocationId &&
            !this.parentLocations.some(location => location.id === this.parentLocationId) &&
            !this.pendingNewlyCreatedLocation
          ) {
            this.parentLocationId = null;
          }
        },
        error: error => this.handleLoadError(error, 'Unable to load parent locations.'),
      });
  }

  loadRegionViewport(regionId: number): void {
    this.viewportSubscription?.unsubscribe();
    this.regionBoundaryMissingMessage = null;
    this.regionViewport = null;
    this.regionBoundaryZone = null;
    this.clearScopeValidation();

    this.viewportSubscription = forkJoin({
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

  loadPlaceTypes(): void {
    this.placeTypesLoading = true;
    this.notifyStateChanged();

    this.placeTypesSubscription?.unsubscribe();
    this.placeTypesSubscription = this.placesService
      .getPlaceTypes()
      .pipe(
        finalize(() => {
          this.placeTypesLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: types => {
          this.placeTypes = this.normalizePlaceTypes(types);
          if (
            this.placeTypeId &&
            !this.placeTypes.some(type => type.id === this.placeTypeId)
          ) {
            this.placeTypeId = null;
          }
        },
        error: error => this.handleLoadError(error, 'Unable to load place types.'),
      });
  }

  loadStayTypes(): void {
    this.placesService.getStayTypes().subscribe({
      next: types => {
        this.stayTypes = Array.isArray(types) ? types : [];
        if (this.stayTypeId && !this.stayTypes.some(t => t.id === this.stayTypeId)) {
          this.stayTypeId = this.stayTypes[0]?.id ?? null;
        }
        this.notifyStateChanged();
      },
      error: () => {
        this.stayTypes = [];
        this.notifyStateChanged();
      },
    });
  }

  loadPlaceTags(): void {
    this.tagsLoading = true;
    this.notifyStateChanged();

    this.tagsSubscription?.unsubscribe();
    this.tagsSubscription = this.placesService
      .getPlaceTags()
      .pipe(
        finalize(() => {
          this.tagsLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: tags => {
          this.placeTags = this.normalizeTags(tags);
          this.syncSelectedTagsWithAvailableTags();
          this.classItems = this.placeTags.map(tag => ({
            tag,
            checked: this.selectedTagIds.has(tag.id),
          }));
        },
        error: error => this.handleLoadError(error, 'Unable to load place tags.'),
      });
  }

  private loadPlaceForEditIfNeeded(): void {
    if (!this.isEditMode || !this.editPlaceId || !this.activeRegionId) {
      return;
    }

    this.placesService.getPlace(this.activeRegionId, this.editPlaceId).subscribe({
      next: dto => {
        const wasLinked = this.hasLinkedContribution;
        this.hasLinkedContribution = dto.hasLinkedContribution ?? false;
        if (this.hasLinkedContribution !== wasLinked) {
          this.loadPlaceTypes();
        }
        this.placeName = dto.name ?? '';
        this.summary = dto.summary ?? dto.subtitle ?? '';
        this.parentDestinationId = dto.destinationId ?? null;
        this.parentLocationId = dto.locationId ?? null;
        this.captureLoadedParentSelections(dto);
        this.ensureLoadedParentSelectionsPresent();
        if (this.parentDestinationId) {
          this.loadParentLocations();
        }
        this.placeTypeId = dto.placeTypeId ?? null;
        this.stayTypeId = dto.stayTypeId ?? null;
        this.bestTimeToVisit = dto.bestTimeToVisit ?? '';
        this.visitorTips = dto.visitorTips ?? '';
        this.accessNotes = dto.accessNotes ?? '';
        this.accessibilityNotes = dto.accessibilityNotes ?? '';
        this.website = dto.website ?? '';
        this.contactPhone = dto.phoneNumber ?? dto.contactPhone ?? '';
        this.admissionPrice = dto.admissionPriceRange ?? '';
        this.bookingLink = dto.bookingExternalLink ?? '';
        this.streetAddress = dto.address ?? '';
        this.opsEnabled = Boolean(dto.operatingHoursEnabled);
        this.applyOperatingHours(dto.operatingHours ?? []);

        if (typeof dto.latitude === 'number' && typeof dto.longitude === 'number') {
          this.pinLat = dto.latitude.toFixed(6);
          this.pinLng = dto.longitude.toFixed(6);
          this.modalPinLat = dto.latitude;
          this.modalPinLng = dto.longitude;
          this.pinConfirmed = this.hasValidCoordinates(dto.latitude, dto.longitude);
          this.validateSelectedPoint(dto.latitude, dto.longitude);
          setTimeout(() => this.updateMiniMapPin(dto.latitude, dto.longitude), 0);
        }

        this.cardImageUrl = dto.cardImageUrl ?? dto.imageUrl ?? null;
        this.cardImageName = this.cardImageUrl ? 'Existing card image' : '';
        this.revokeCoverPreviewObjectUrl();
        this.coverImageUrl = dto.coverImageUrl ?? dto.imageUrl ?? null;
        this.coverImageName = this.coverImageUrl ? 'Existing cover image' : '';
        this.galleryImages = (dto.galleryImageUrls ?? []).map((url, index) => ({
          url,
          name: `Existing gallery image ${index + 1}`,
        }));

        this.selectedTagIds = new Set((dto.tags ?? []).map(tag => tag.id));
        this.syncSelectedTagsWithAvailableTags();
        this.classItems = this.classItems.map(item => ({
          ...item,
          checked: this.selectedTagIds.has(item.tag.id),
        }));

        setTimeout(() => {
          if (this.editorRef?.nativeElement) {
            this.setEditorHtml(dto.description);
            this.syncEmptyState();
            this.notifyStateChanged();
          }
          this.restoreSavedDraftIfPresent();
        }, 0);

        this.notifyStateChanged();
      },
      error: error => this.handleLoadError(error, 'Unable to load place for editing.'),
    });
  }

  onParentDestinationChanged(): void {
    this.editLoadedLocation = null;
    if (this.editLoadedDestination && this.editLoadedDestination.id !== this.parentDestinationId) {
      this.editLoadedDestination = null;
    }
    this.clearScopeValidation();
    this.parentLocationId = null;
    this.parentLocations = [];
    this.errorMessage = null;
    this.loadParentLocations();
    this.notifyStateChanged();
  }

  onParentLocationChanged(): void {
    this.errorMessage = null;
    this.notifyStateChanged();
  }

  onParentDestinationSelection(value: number | string | null): void {
    if (value === this.createDestinationOptionValue) {
      this.openCreateDestinationFlow();
      return;
    }

    this.parentDestinationId = typeof value === 'number' ? value : null;
    this.onParentDestinationChanged();
  }

  onParentLocationSelection(value: number | string | null): void {
    if (value === this.createLocationOptionValue) {
      if (!this.parentDestinationId) {
        this.errorMessage = 'Please select a destination before creating a new location.';
        this.notifyStateChanged();
        return;
      }

      this.openCreateLocationFlow();
      return;
    }

    this.parentLocationId = typeof value === 'number' ? value : null;
    this.onParentLocationChanged();
    this.revalidatePinnedCoordinatesIfPresent();
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

    const existingSelectedTag = this.placeTags.find(
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
    this.tagSearchSubscription = this.placesService
      .createPlaceTag(name)
      .pipe(
        finalize(() => {
          this.tagSearchLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: tag => this.selectAdditionalTag(tag),
        error: error => this.handleLoadError(error, 'Unable to create place tag.'),
      });
  }

  selectAdditionalTag(tag: RegionalPlaceTagDto): void {
    this.upsertPlaceTag(tag);
    this.selectedTagIds.add(tag.id);
    this.upsertClassItem(tag, true);
    this.tagInput = '';
    this.additionalTagResults = [];
    this.notifyStateChanged();
  }

  removeTag(tag: RegionalPlaceTagDto): void {
    this.selectedTagIds.delete(tag.id);
    const classItem = this.classItems.find(item => item.tag.id === tag.id);
    if (classItem) {
      classItem.checked = false;
    }
    this.notifyStateChanged();
  }

  openTimePicker(day: DayHours, field: TimeField): void {
    this.activeTimePicker = { day, field };
    this.timePickerMode = 'hour';
    this.notifyStateChanged();
  }

  closeTimePicker(): void {
    this.activeTimePicker = null;
    this.notifyStateChanged();
  }

  setTimePickerMode(mode: 'hour' | 'minute'): void {
    this.timePickerMode = mode;
    this.notifyStateChanged();
  }

  onTimeInput(day: DayHours, field: TimeField, value: string): void {
    day[field] = value.replace(/[^\d:]/g, '').slice(0, 5);
    this.notifyStateChanged();
  }

  onTimeBlur(day: DayHours, field: TimeField): void {
    const normalized = this.normalizeTimeInput(day[field]);
    if (normalized) {
      day[field] = normalized;
      this.notifyStateChanged();
    }
  }

  selectClockHour(hour: number): void {
    const picker = this.activeTimePicker;
    if (!picker) return;

    const [, minutes] = picker.day[picker.field].split(':');
    picker.day[picker.field] = `${this.padTimePart(hour)}:${minutes || '00'}`;
    this.timePickerMode = 'minute';
    this.notifyStateChanged();
  }

  minuteCircleStyle(): Record<string, string> {
    const minute = this.currentMinute();
    const angle = (minute / 60) * 360 - 90;
    const radius = 138;
    const x = Math.cos(angle * Math.PI / 180) * radius;
    const y = Math.sin(angle * Math.PI / 180) * radius;
    return {
      left: `calc(50% + ${x}px)`,
      top: `calc(50% + ${y}px)`,
      transform: 'translate(-50%, -50%)',
    };
  }

  minuteHandAngle(): number {
    const picker = this.activeTimePicker;
    if (!picker) return 0;
    return (Number(picker.day[picker.field].slice(3, 5)) || 0) / 60 * 360;
  }

  currentMinute(): number {
    const picker = this.activeTimePicker;
    if (!picker) return 0;
    return Number(picker.day[picker.field].slice(3, 5)) || 0;
  }

  onMinutePointerDown(event: MouseEvent | TouchEvent, el: HTMLElement): void {
    if (this.timePickerMode !== 'minute') return;
    this.isDraggingMinute = true;
    this.applyMinuteFromEvent(event, el);
    event.preventDefault();
  }

  onMinutePointerMove(event: MouseEvent | TouchEvent, el: HTMLElement): void {
    if (!this.isDraggingMinute) return;
    this.applyMinuteFromEvent(event, el);
    event.preventDefault();
  }

  onMinutePointerUp(): void {
    this.isDraggingMinute = false;
  }

  private applyMinuteFromEvent(event: MouseEvent | TouchEvent, el: HTMLElement): void {
    const rect = el.getBoundingClientRect();
    const cx = rect.left + rect.width / 2;
    const cy = rect.top + rect.height / 2;
    let clientX: number, clientY: number;
    if (window.TouchEvent && event instanceof TouchEvent) {
      if (!event.touches.length) return;
      clientX = event.touches[0].clientX;
      clientY = event.touches[0].clientY;
    } else {
      clientX = (event as MouseEvent).clientX;
      clientY = (event as MouseEvent).clientY;
    }
    const angle = Math.atan2(clientY - cy, clientX - cx) * 180 / Math.PI + 90;
    const minute = Math.round(((angle % 360) + 360) % 360 / 360 * 60) % 60;
    const picker = this.activeTimePicker;
    if (!picker) return;
    const [hours] = picker.day[picker.field].split(':');
    picker.day[picker.field] = `${hours || '00'}:${this.padTimePart(minute)}`;
    this.notifyStateChanged();
  }

  selectClockMinute(minute: number): void {
    const picker = this.activeTimePicker;
    if (!picker) return;

    const [hours] = picker.day[picker.field].split(':');
    picker.day[picker.field] = `${hours || '00'}:${this.padTimePart(minute)}`;
    this.notifyStateChanged();
  }

  clockHourStyle(hour: number, ring: 'outer' | 'inner'): Record<string, string> {
    const arr = ring === 'outer' ? this.clockOuterHours : this.clockInnerHours;
    const index = arr.indexOf(hour);
    const angle = (index / 12) * 360 - 90;
    const radius = ring === 'outer' ? 138 : 95;
    const x = Math.cos(angle * Math.PI / 180) * radius;
    const y = Math.sin(angle * Math.PI / 180) * radius;
    return {
      left: `calc(50% + ${x}px)`,
      top: `calc(50% + ${y}px)`,
      transform: 'translate(-50%, -50%)',
    };
  }

  clockButtonStyle(value: number, total: number): Record<string, string> {
    const angle = (value / total) * 360 - 90;
    const radius = 138;
    const x = Math.cos(angle * Math.PI / 180) * radius;
    const y = Math.sin(angle * Math.PI / 180) * radius;
    return {
      left: `calc(50% + ${x}px)`,
      top: `calc(50% + ${y}px)`,
      transform: 'translate(-50%, -50%)',
    };
  }

  isSelectedClockHour(hour: number): boolean {
    const picker = this.activeTimePicker;
    return !!picker && Number(picker.day[picker.field].slice(0, 2)) === hour;
  }

  isSelectedClockMinute(minute: number): boolean {
    const picker = this.activeTimePicker;
    return !!picker && Number(picker.day[picker.field].slice(3, 5)) === minute;
  }

  formatClockPart(value: number): string {
    return this.padTimePart(value);
  }

  onEditorKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter') {
      event.preventDefault();
      if (this.editorTextLength() - this.selectedEditorTextLength() >= this.descriptionMaxLength) {
        this.descriptionLimitReached = true;
        this.notifyStateChanged();
        return;
      }
      document.execCommand('insertLineBreak');
      this.syncToolbarState();
      this.syncEmptyState();
    }
  }

  onEditorBeforeInput(event: InputEvent): void {
    if (event.inputType.startsWith('delete') || event.inputType.startsWith('format')) return;

    const insertionLength = event.data?.length ??
      (event.inputType === 'insertParagraph' || event.inputType === 'insertLineBreak' ? 1 : 0);
    if (insertionLength === 0) return;

    if (this.editorTextLength() - this.selectedEditorTextLength() + insertionLength > this.descriptionMaxLength) {
      event.preventDefault();
      this.descriptionLimitReached = true;
      this.notifyStateChanged();
    }
  }

  execFormat(command: 'bold' | 'italic' | 'underline'): void {
    document.execCommand(command, false, undefined);
    this.editorRef.nativeElement.focus();
    this.syncToolbarState();
  }

  onEditorChange(): void {
    this.syncToolbarState();
    this.syncDescriptionLimitState();
    this.syncEmptyState();
  }

  onEditorPaste(event: ClipboardEvent): void {
    const clipboard = event.clipboardData;
    if (!clipboard) return;

    event.preventDefault();
    const html = clipboard.getData('text/html');
    const text = clipboard.getData('text/plain');
    const remaining = Math.max(0, this.descriptionMaxLength - (this.editorTextLength() - this.selectedEditorTextLength()));
    const allowedText = text.slice(0, remaining);
    const content = normalizeEditorDescriptionHtml(html || this.escapeHtml(allowedText).replace(/\r\n|\r|\n/g, '<br>'));
    document.execCommand('insertHTML', false, content);
    this.descriptionLimitReached = text.length > remaining || this.editorTextLength() >= this.descriptionMaxLength;
    this.syncToolbarState();
    this.syncDescriptionLimitState();
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
    } else if (el) {
      el.innerHTML = this.normalizeEditorHtml(el.innerHTML);
    }
    this.syncDescriptionLimitState();
    this.syncEmptyState();
  }

  onCardImageSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file || !this.isAllowedImage(file)) {
      input.value = '';
      return;
    }

    this.revokeCardPreviewObjectUrl();
    this.cardPreviewObjectUrl = URL.createObjectURL(file);
    this.cardImageUrl = this.cardPreviewObjectUrl;
    this.cardImageName = file.name;
    this.cardImageFile = file;
    input.value = '';
    this.notifyStateChanged();
  }

  removeCardImage(): void {
    this.revokeCardPreviewObjectUrl();
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
    const files = Array.from(input.files ?? []).filter(file => this.isAllowedImage(file));
    for (const file of files) {
      const previewUrl = URL.createObjectURL(file);
      this.galleryPreviewObjectUrls = [...this.galleryPreviewObjectUrls, previewUrl];
      this.galleryImages = [...this.galleryImages, { url: previewUrl, name: file.name, file }];
      this.notifyStateChanged();
    }
    input.value = '';
  }

  removeGalleryImage(index: number): void {
    const removed = this.galleryImages[index];
    this.revokeObjectUrlIfNeeded(removed?.url ?? null);
    this.galleryPreviewObjectUrls = this.galleryPreviewObjectUrls.filter(url => url !== removed?.url);
    this.galleryImages = this.galleryImages.filter((_, currentIndex) => currentIndex !== index);
    this.notifyStateChanged();
  }

  openLightbox(images: { url: string; name: string }[], index: number): void {
    this.lightboxService.open(images, index);
  }

  openPinModal(): void {
    this.pinModalOpen = true;

    const coordinates = this.readPinnedCoordinates();
    this.modalPinLat = coordinates?.latitude ?? null;
    this.modalPinLng = coordinates?.longitude ?? null;

    this.notifyStateChanged();
    setTimeout(() => this.initModalMap(this.modalMapContainerRef.nativeElement), 50);
    if (this.modalPinLat !== null && this.modalPinLng !== null) {
      this.validateSelectedPoint(this.modalPinLat, this.modalPinLng);
    }
  }

  closePinModal(): void {
    this.pinModalOpen = false;
    this.destroyModalMap();
    this.notifyStateChanged();
  }

  onPinModalBackdropClick(_event: MouseEvent): void {
    this.closePinModal();
  }

  confirmPin(): void {
    if (this.modalPinLat === null || this.modalPinLng === null) {
      this.errorMessage = 'Click the map to place a pin before confirming.';
      this.notifyStateChanged();
      return;
    }

    if (!this.hasValidCoordinates(this.modalPinLat, this.modalPinLng)) {
      this.errorMessage = 'Pinned coordinates are invalid.';
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

    this.pinLat = this.modalPinLat.toFixed(6);
    this.pinLng = this.modalPinLng.toFixed(6);
    this.pinConfirmed = true;
    this.errorMessage = null;

    this.updateMiniMapPin(this.modalPinLat, this.modalPinLng);
    this.reverseGeocode(this.modalPinLat, this.modalPinLng);
    this.closePinModal();
  }

  onCoordInputChange(): void {
    const coordinates = this.readPinnedCoordinates();
    if (!coordinates) {
      this.pinConfirmed = false;
      this.notifyStateChanged();
      return;
    }

    this.pinConfirmed = true;
    this.updateMiniMapPin(coordinates.latitude, coordinates.longitude);
    this.validateSelectedPoint(coordinates.latitude, coordinates.longitude);

    if (this.geocodeDebounceHandle) {
      clearTimeout(this.geocodeDebounceHandle);
    }
    this.geocodeDebounceHandle = setTimeout(
      () => this.reverseGeocode(coordinates.latitude, coordinates.longitude),
      300,
    );
  }

  publish(): void {
    if (this.isSaving) return;

    if (this.isEditMode) {
      this.updateExistingPlace();
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
    this.createSubscription = this.placesService
      .createPlace(payload)
      .pipe(
        finalize(() => {
          this.isSaving = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: created => {
          this.formDrafts.clearDraft(this.draftFormType());
          this.transientMedia.clear(this.draftFormType());
          this.feedbackMessage = 'Place published.';
          this.router.navigate(['/places'], {
            queryParams: { regionId: created.regionId },
            state: { feedbackMessage: 'Place published.' },
          });
        },
        error: error => this.handleActionError(error),
      });
  }

  private updateExistingPlace(): void {
    const regionId = this.activeRegionId;
    const placeId = this.editPlaceId;
    const payload = this.buildUpdatePayload();
    if (!regionId || !placeId || !payload) {
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
        switchMap(preparedPayload => this.placesService.updatePlace(regionId, placeId, preparedPayload)),
      )
      .pipe(
        finalize(() => {
          this.isSaving = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: updated => {
          this.formDrafts.clearDraft(this.draftFormType());
          this.feedbackMessage = 'Place updated.';
          this.router.navigate(['/places/detail', updated.id], {
            queryParams: { regionId: updated.regionId },
            state: { regionId: updated.regionId, feedbackMessage: 'Place updated.' },
          });
        },
        error: error => this.handleActionError(error),
      });
  }

  private buildCreatePayload(): RegionalPlaceCreateRequest | null {
    if (this.isEditMode) {
      this.errorMessage = 'Place editing is not available from this create form.';
      return null;
    }

    const regionId = this.activeRegionId;
    if (!regionId) {
      this.errorMessage = 'Select an assigned region before publishing.';
      return null;
    }

    if (!this.parentDestinationId) {
      this.errorMessage = 'Parent destination is required.';
      return null;
    }

    const locationId = this.parentLocationId;
    if (!locationId) {
      this.errorMessage = 'Parent location is required.';
      return null;
    }

    const selectedLocation = this.selectedParentLocation;

    if (!selectedLocation) {
      this.errorMessage = 'Selected parent location is still loading. Please wait and try again.';
      return null;
    }

    if (selectedLocation.destinationId !== this.parentDestinationId) {
      this.errorMessage = 'Parent location must belong to the selected destination.';
      return null;
    }

    const placeTypeId = this.placeTypeId;
    if (!placeTypeId) {
      this.errorMessage = 'Place type is required.';
      return null;
    }

    const name = this.placeName.trim();
    if (!name) {
      this.errorMessage = 'Place name is required.';
      return null;
    }

    const normalizedSummary = this.summary.trim();
    if (!normalizedSummary) {
      this.errorMessage = 'Summary is required.';
      return null;
    }
    if (normalizedSummary.length > 160) {
      this.errorMessage = 'Summary must be 160 characters or less.';
      return null;
    }

    const coordinates = this.readPinnedCoordinates();
    if (!coordinates || !this.pinConfirmed) {
      this.errorMessage = 'Pin the place on the map before publishing.';
      return null;
    }

    const currentValidationKey = this.scopeValidationKey(coordinates.latitude, coordinates.longitude);
    if (this.lastScopeValidationKey !== currentValidationKey) {
      this.validateSelectedPoint(coordinates.latitude, coordinates.longitude);
      this.errorMessage = 'Location validation is still running.';
      return null;
    }

    if (this.scopeValidationPending || this.scopeValidationResult?.isAllowed !== true) {
      this.errorMessage = this.scopeValidationPending
        ? 'Location validation is still running.'
        : this.scopeValidationMessage ?? 'Selected location could not be validated against your assigned regional scope.';
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

    const website = this.normalizeUrl(this.website, 'Website URL is invalid.');
    if (website === false) return null;

    const bookingLink = this.normalizeUrl(this.bookingLink, 'Booking / external link is invalid.');
    if (bookingLink === false) return null;

    const contactPhone = this.normalizePhone(this.contactPhone);
    if (contactPhone === false) return null;

    const operatingHours = this.buildOperatingHoursPayload();
    if (!operatingHours) return null;

    const descriptionHtml = this.getEditorHtml();

    return {
      regionId,
      locationId,
      placeTypeId,
      stayTypeId: this.isStayType ? (this.stayTypeId ?? null) : null,
      name,
      summary: normalizedSummary,
      description: descriptionHtml || null,
      status: 'Published',
      latitude: coordinates.latitude,
      longitude: coordinates.longitude,
      streetAddress: this.streetAddress.trim() || null,
      bestTimeToVisit: this.bestTimeToVisit.trim() || null,
      visitorTips: this.visitorTips.trim() || null,
      accessNotes: this.accessNotes.trim() || null,
      accessibilityNotes: this.accessibilityNotes.trim() || null,
      website,
      contactPhone,
      admissionPriceRange: this.admissionPrice.trim() || null,
      bookingExternalLink: this.isStayType ? bookingLink : null,
      operatingHoursEnabled: this.opsEnabled,
      operatingHours,
      tagIds: this.selectedTagIdList(),
      cardImageFile: this.cardImageFile,
      coverImageFile: this.coverImageFile,
      galleryImageFiles: this.galleryImages
        .filter((image): image is { url: string; name: string; file: File } => image.file != null)
        .map(image => image.file),
    };
  }

  private buildUpdatePayload(): RegionalPlaceUpdateRequest | null {
    if (!this.parentDestinationId) {
      this.errorMessage = 'Parent destination is required.';
      return null;
    }

    const locationId = this.parentLocationId;
    if (!locationId) {
      this.errorMessage = 'Parent location is required.';
      return null;
    }

  const selectedLocation = this.selectedParentLocation;

  if (!selectedLocation) {
    this.errorMessage = 'Selected parent location is still loading. Please wait and try again.';
    return null;
  }

  if (selectedLocation.destinationId !== this.parentDestinationId) {
    this.errorMessage = 'Parent location must belong to the selected destination.';
    return null;
  }

    const placeTypeId = this.placeTypeId;
    if (!placeTypeId) {
      this.errorMessage = 'Place type is required.';
      return null;
    }

    const name = this.placeName.trim();
    if (!name) {
      this.errorMessage = 'Place name is required.';
      return null;
    }

    const normalizedSummary = this.summary.trim();
    if (!normalizedSummary) {
      this.errorMessage = 'Summary is required.';
      return null;
    }
    if (normalizedSummary.length > 160) {
      this.errorMessage = 'Summary must be 160 characters or less.';
      return null;
    }

    const coordinates = this.readPinnedCoordinates();
    if (!coordinates) {
      this.errorMessage = 'Place coordinates are required before updating.';
      return null;
    }

    // In edit mode an existing valid pin is already confirmed by the loaded data.
    // Do not force the regional manager to click the map again just to save text/type changes.
    if (!this.pinConfirmed) {
      this.pinConfirmed = true;
    }

    const currentValidationKey = this.scopeValidationKey(coordinates.latitude, coordinates.longitude);
    if (this.lastScopeValidationKey !== currentValidationKey) {
      this.validateSelectedPoint(coordinates.latitude, coordinates.longitude);
      this.errorMessage = 'Location validation is still running.';
      return null;
    }

    if (this.scopeValidationPending || this.scopeValidationResult?.isAllowed !== true) {
      this.errorMessage = this.scopeValidationPending
        ? 'Location validation is still running.'
        : this.scopeValidationMessage ?? 'Selected location could not be validated against your assigned regional scope.';
      return null;
    }

    const website = this.normalizeUrl(this.website, 'Website URL is invalid.');
    if (website === false) return null;

    const bookingLink = this.normalizeUrl(this.bookingLink, 'Booking / external link is invalid.');
    if (bookingLink === false) return null;

    const contactPhone = this.normalizePhone(this.contactPhone);
    if (contactPhone === false) return null;

    const operatingHours = this.buildOperatingHoursPayload();
    if (!operatingHours) return null;

    const descriptionHtml = this.getEditorHtml();

    return {
      locationId,
      placeTypeId,
      stayTypeId: this.isStayType ? (this.stayTypeId ?? null) : null,
      name,
      subtitle: normalizedSummary,
      description: descriptionHtml || null,
      address: this.streetAddress.trim() || null,
      latitude: coordinates.latitude,
      longitude: coordinates.longitude,
      bestTimeToVisit: this.bestTimeToVisit.trim() || null,
      visitorTips: this.visitorTips.trim() || null,
      accessNotes: this.accessNotes.trim() || null,
      accessibilityNotes: this.accessibilityNotes.trim() || null,
      website,
      phoneNumber: contactPhone,
      admissionPriceRange: this.admissionPrice.trim() || null,
      bookingExternalLink: this.isStayType ? bookingLink : null,
      operatingHoursEnabled: this.opsEnabled,
      operatingHours,
      tagIds: this.selectedTagIdList(),
      galleryImageUrls: this.galleryImages.filter(image => !image.file).map(image => image.url),
      galleryImageFiles: this.galleryImages
        .filter((image): image is { url: string; name: string; file: File } => image.file != null)
        .map(image => image.file),
    };
  }

  private buildOperatingHoursPayload(): RegionalPlaceOperatingHourDto[] | null {
    if (!this.opsEnabled) return [];

    if (this.open24Hours) {
      return this.days.map(day => ({
        dayOfWeek: day.name,
        isOpen: true,
        openTime: '00:00',
        closeTime: '00:00',
      }));
    }

    const hours: RegionalPlaceOperatingHourDto[] = [];
    for (const day of this.days) {
      if (!day.open) {
        hours.push({ dayOfWeek: day.name, isOpen: false });
        continue;
      }

      if (day.openTime === day.closeTime) {
        this.errorMessage = `${day.name} opening time and closing time cannot be the same.`;
        return null;
      }

      if (!this.isValidClockTime(day.openTime) || !this.isValidClockTime(day.closeTime)) {
        this.errorMessage = `${day.name} opening hours must use 24-hour HH:mm format.`;
        return null;
      }

      hours.push({
        dayOfWeek: day.name,
        isOpen: true,
        openTime: day.openTime,
        closeTime: day.closeTime,
      });
    }

    return hours;
  }

  private applyOperatingHours(hours: RegionalPlaceOperatingHourDto[]): void {
    if (!hours.length) {
      this.open24Hours = false;
      return;
    }

    this.open24Hours = this.isOpen24HoursSchedule(hours);

    const byDay = new Map(hours.map(hour => [hour.dayOfWeek.toLowerCase(), hour]));
    this.days = this.days.map(day => {
      const incoming = byDay.get(day.name.toLowerCase());
      if (!incoming) return day;

      return {
        ...day,
        open: incoming.isOpen,
        openTime: incoming.openTime ?? day.openTime,
        closeTime: incoming.closeTime ?? day.closeTime,
      };
    });
  }

  private isOpen24HoursSchedule(hours: RegionalPlaceOperatingHourDto[]): boolean {
    if (hours.length !== this.days.length) return false;

    const byDay = new Map(hours.map(hour => [hour.dayOfWeek.toLowerCase(), hour]));
    return this.days.every(day => {
      const hour = byDay.get(day.name.toLowerCase());
      return hour?.isOpen === true && hour.openTime === '00:00' && hour.closeTime === '00:00';
    });
  }

  private normalizeTimeInput(value: string): string | null {
    const trimmed = value.trim();
    const match = trimmed.match(/^(\d{1,2})(?::?(\d{0,2}))?$/);
    if (!match) return null;

    const hours = Number(match[1]);
    const minutes = Number((match[2] || '0').padEnd(2, '0'));
    if (!Number.isInteger(hours) || !Number.isInteger(minutes) || hours < 0 || hours > 23 || minutes < 0 || minutes > 59) {
      return null;
    }

    return `${this.padTimePart(hours)}:${this.padTimePart(minutes)}`;
  }

  private isValidClockTime(value: string): boolean {
    return /^([01]\d|2[0-3]):[0-5]\d$/.test(value);
  }

  private padTimePart(value: number): string {
    return value.toString().padStart(2, '0');
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
    const coordinates = this.readPinnedCoordinates();
    if (coordinates && this.pinConfirmed) {
      this.updateMiniMapPin(coordinates.latitude, coordinates.longitude);
    } else {
      this.applyRegionViewportToMiniMap();
    }
  }

  private initModalMap(el: HTMLDivElement): void {
    if (this.modalMap || typeof L === 'undefined') return;

    this.modalMap = L.map(el, {
      zoomControl: true,
      attributionControl: false,
    });

    this.mapTileLayer.addTo(L, this.modalMap);

    this.refreshModalRegionBoundaryLayer();
    setTimeout(() => this.modalMap?.invalidateSize(), 200);

    if (this.modalPinLat !== null && this.modalPinLng !== null) {
      applyAssignedRegionViewport(this.modalMap, this.regionViewport, 9, 11);
      this.modalMarker = L.marker([this.modalPinLat, this.modalPinLng], {
        icon: this.makePinIcon(),
        draggable: true,
      }).addTo(this.modalMap);

      this.modalMarker.on('dragend', (event: any) => this.onModalMarkerMoved(event.target.getLatLng()));
    } else {
      applyAssignedRegionViewport(this.modalMap, this.regionViewport, 9, 11);
    }

    this.modalMap.on('click', (event: any) => {
      this.zone.run(() => {
        this.modalPinLat = event.latlng.lat;
        this.modalPinLng = event.latlng.lng;
        this.validateSelectedPoint(event.latlng.lat, event.latlng.lng);
        this.notifyStateChanged();
      });

      if (this.modalMarker) {
        this.modalMarker.setLatLng(event.latlng);
      } else {
        this.modalMarker = L.marker(event.latlng, {
          icon: this.makePinIcon(),
          draggable: true,
        }).addTo(this.modalMap);

        this.modalMarker.on('dragend', (ev: any) => this.onModalMarkerMoved(ev.target.getLatLng()));
      }
    });
  }

  private onModalMarkerMoved(position: { lat: number; lng: number }): void {
    this.zone.run(() => {
      this.modalPinLat = position.lat;
      this.modalPinLng = position.lng;
      this.validateSelectedPoint(position.lat, position.lng);
      this.notifyStateChanged();
    });
  }

  private destroyModalMap(): void {
    if (this.modalMap) {
      this.modalMap.remove();
      this.modalMap = null;
      this.modalMarker = null;
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

  private makePinIcon(): any {
    return createRegionalMapPinIcon(L);
  }

  private updateMiniMapPin(lat: number, lng: number): void {
    if (!this.miniMap) this.initMiniMap();
    if (!this.miniMap || typeof L === 'undefined') return;

    if (this.miniMarker) {
      this.miniMarker.setLatLng([lat, lng]);
    } else {
      this.miniMarker = L.marker([lat, lng], {
        icon: this.makePinIcon(),
        draggable: false,
      }).addTo(this.miniMap);
    }

    this.miniMap.setView([lat, lng], 14);
  }

  private reverseGeocode(lat: number, lng: number): void {
    if (!this.hasValidCoordinates(lat, lng)) return;

    this.isGeocodingAddress = true;
    this.notifyStateChanged();

    const params = new HttpParams()
      .set('latitude', String(lat))
      .set('longitude', String(lng));

    this.reverseGeocodeSubscription?.unsubscribe();
    this.reverseGeocodeSubscription = this.http
      .get<MapReverseGeocodeDto>('/api/map/reverse', { params })
      .pipe(
        finalize(() => {
          this.isGeocodingAddress = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: result => {
          if (!this.streetAddress.trim()) {
            this.streetAddress = result.displayLabel || this.formatReverseGeocodeAddress(result);
          }
        },
        error: () => undefined,
      });
  }

  private searchTags(query: string): void {
    this.tagSearchLoading = true;
    this.notifyStateChanged();

    this.tagSearchSubscription?.unsubscribe();
    this.tagSearchSubscription = this.placesService
      .searchPlaceTags(query)
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
          normalized.forEach(tag => this.upsertPlaceTag(tag));
          this.additionalTagResults = normalized;
        },
        error: error => this.handleLoadError(error, 'Unable to search place tags.'),
      });
  }

  private findTagToAdd(): RegionalPlaceTagDto | null {
    const query = this.tagInput.trim().toLocaleLowerCase();
    const exactMatch = this.placeTags.find(
      tag => tag.name.trim().toLocaleLowerCase() === query && !this.selectedTagIds.has(tag.id),
    );
    return exactMatch ?? null;
  }

  private captureLoadedParentSelections(dto: RegionalPlaceDetailDto): void {
    this.editLoadedDestination = dto.destinationId
      ? {
          id: dto.destinationId,
          name: dto.destinationName ?? '',
          displayName: dto.destinationName ?? '',
          status: '',
        }
      : null;
    this.editLoadedLocation = dto.locationId
      ? {
          id: dto.locationId,
          name: dto.locationName ?? '',
          displayName: dto.locationName ?? '',
          destinationId: dto.destinationId ?? 0,
          destinationName: dto.destinationName ?? '',
          status: '',
        }
      : null;
  }

  // The parent destination/location selectors are populated from the region's
  // catalogue, which may not yet list the entity attached to a place that was
  // approved through the contribution flow. Inject the place's own parent
  // selections so the edit form and its preview always reflect them.
  private ensureLoadedParentSelectionsPresent(): void {
    if (
      this.editLoadedDestination &&
      this.editLoadedDestination.id === this.parentDestinationId &&
      !this.parentDestinations.some(destination => destination.id === this.editLoadedDestination!.id)
    ) {
      this.parentDestinations = this.normalizeParentDestinations([
        ...this.parentDestinations,
        this.editLoadedDestination,
      ]);
    }

    if (
      this.editLoadedLocation &&
      this.editLoadedLocation.id === this.parentLocationId &&
      this.editLoadedLocation.destinationId === this.parentDestinationId &&
      !this.parentLocations.some(location => location.id === this.editLoadedLocation!.id)
    ) {
      this.parentLocations = this.normalizeParentLocations([
        ...this.parentLocations,
        this.editLoadedLocation,
      ]);
    }
  }

  private normalizeParentDestinations(
    destinations: RegionalPlaceParentDestinationDto[] | null | undefined,
  ): RegionalPlaceParentDestinationDto[] {
    const seen = new Set<number>();
    return (destinations ?? [])
      .filter(destination => {
        if (!destination || seen.has(destination.id)) return false;
        seen.add(destination.id);
        return true;
      })
      .sort((a, b) => (a.displayName || a.name).localeCompare(b.displayName || b.name));
  }

  private normalizeParentLocations(
    locations: RegionalPlaceParentLocationDto[] | null | undefined,
  ): RegionalPlaceParentLocationDto[] {
    const seen = new Set<number>();
    return (locations ?? [])
      .filter(location => {
        if (!location || seen.has(location.id)) return false;
        seen.add(location.id);
        return true;
      })
      .sort((a, b) => a.displayName.localeCompare(b.displayName));
  }

  private openCreateDestinationFlow(): void {
    this.captureTransientMedia();
    this.router.navigate(['/destinations/new'], {
      queryParams: {
        source: 'add-place',
        returnFlow: 'create-place',
        regionId: this.activeRegionId,
      },
      state: {
        regionId: this.activeRegionId,
        returnUrl: '/places/new',
        placeDraft: this.buildPlaceDraftPayload(),
        coordinates: this.readPinnedCoordinates(),
      },
    });
  }

  private openCreateLocationFlow(): void {
    this.captureTransientMedia();
    this.router.navigate(['/locations/new'], {
      queryParams: {
        source: 'add-place',
        returnFlow: 'create-place',
        regionId: this.activeRegionId,
      },
      state: {
        regionId: this.activeRegionId,
        returnUrl: '/places/new',
        placeDraft: this.buildPlaceDraftPayload(),
        selectedDestinationId: this.parentDestinationId,
        coordinates: this.readPinnedCoordinates(),
      },
    });
  }

  private static readonly CONTRIBUTION_INELIGIBLE_PLACE_TYPES = new Set(['beach', 'nature', 'shopping']);

  private normalizePlaceTypes(types: RegionalPlaceTypeDto[] | null | undefined): RegionalPlaceTypeDto[] {
    const seen = new Set<number>();
    return (types ?? [])
      .filter(type => {
        if (!type || seen.has(type.id)) return false;
        seen.add(type.id);
        if (this.hasLinkedContribution &&
            NewPlaceComponent.CONTRIBUTION_INELIGIBLE_PLACE_TYPES.has(type.name.toLowerCase().trim())) {
          return false;
        }
        return true;
      })
      .sort((a, b) => a.name.localeCompare(b.name));
  }

  private normalizeTags(tags: RegionalPlaceTagDto[] | null | undefined): RegionalPlaceTagDto[] {
    const seen = new Set<number>();
    return (tags ?? [])
      .filter(tag => {
        if (!tag || seen.has(tag.id)) return false;
        seen.add(tag.id);
        return true;
      })
      .sort((a, b) => a.name.localeCompare(b.name));
  }

  private upsertPlaceTag(tag: RegionalPlaceTagDto): void {
    if (this.placeTags.some(existing => existing.id === tag.id)) return;
    this.placeTags = [...this.placeTags, tag].sort((a, b) => a.name.localeCompare(b.name));
  }

  private upsertClassItem(tag: RegionalPlaceTagDto, checked: boolean): void {
    const existing = this.classItems.find(item => item.tag.id === tag.id);
    if (existing) {
      existing.checked = checked || existing.checked;
      return;
    }

    this.classItems = [...this.classItems, { tag, checked }]
      .sort((a, b) => a.tag.name.localeCompare(b.tag.name));
  }

  private selectedTagIdList(): number[] {
    const allowedIds = this.availableTagIdSet();
    return [...new Set(this.selectedTagIds)]
      .filter(id => allowedIds.size === 0 || allowedIds.has(id))
      .sort((a, b) => a - b);
  }

  private syncSelectedTagsWithAvailableTags(): void {
    const allowedIds = this.availableTagIdSet();
    if (allowedIds.size === 0) return;

    this.selectedTagIds = new Set([...this.selectedTagIds].filter(id => allowedIds.has(id)));
  }

  private availableTagIdSet(): Set<number> {
    return new Set(this.placeTags.map(tag => tag.id));
  }

  private applyInitialCoordinatesFromRoute(): void {
    const queryParams = this.route.snapshot.queryParamMap;
    const state = history.state as {
      droppedPin?: { latitude?: number | string | null; longitude?: number | string | null } | null;
      latitude?: number | string | null;
      longitude?: number | string | null;
    };

    const latitude = this.readRouteNumber(queryParams.get('latitude')) ??
      this.readRouteNumber(state?.droppedPin?.latitude) ??
      this.readRouteNumber(state?.latitude);
    const longitude = this.readRouteNumber(queryParams.get('longitude')) ??
      this.readRouteNumber(state?.droppedPin?.longitude) ??
      this.readRouteNumber(state?.longitude);

    if (latitude === null || longitude === null || !this.hasValidCoordinates(latitude, longitude)) {
      return;
    }

    this.pinLat = latitude.toFixed(6);
    this.pinLng = longitude.toFixed(6);
    this.modalPinLat = latitude;
    this.modalPinLng = longitude;
    this.pinConfirmed = true;
    this.feedbackMessage = queryParams.get('source') === 'map' ? 'Map pin selected.' : this.feedbackMessage;
    this.updateMiniMapPin(latitude, longitude);
    this.reverseGeocode(latitude, longitude);
    this.notifyStateChanged();
  }

  private readRouteNumber(value: number | string | null | undefined): number | null {
    if (value === null || value === undefined || value === '') return null;

    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
  }

  private readPinnedCoordinates(): { latitude: number; longitude: number } | null {
    const latitude = Number(this.pinLat);
    const longitude = Number(this.pinLng);
    if (!this.hasValidCoordinates(latitude, longitude)) return null;

    return { latitude, longitude };
  }

  private revalidatePinnedCoordinatesIfPresent(): void {
    const coordinates = this.readPinnedCoordinates();

    if (!coordinates || !this.pinConfirmed || !this.activeRegionId || !this.parentLocationId) {
      return;
    }

    this.validateSelectedPoint(coordinates.latitude, coordinates.longitude);
  }

  private scopeValidationKey(latitude: number, longitude: number): string {
    return [
      this.activeRegionId ?? 'none',
      this.parentLocationId ?? 'none',
      latitude.toFixed(6),
      longitude.toFixed(6),
      this.isEditMode ? 'Update' : 'Create',
    ].join('|');
  }

  private hasValidCoordinates(latitude: number, longitude: number): boolean {
    return Number.isFinite(latitude) &&
      Number.isFinite(longitude) &&
      latitude >= -90 &&
      latitude <= 90 &&
      longitude >= -180 &&
      longitude <= 180 &&
      !(latitude === 0 && longitude === 0);
  }

  private applyRegionViewportToMiniMap(): void {
    if (!this.miniMap || (this.pinConfirmed && this.readPinnedCoordinates())) return;
    applyAssignedRegionViewport(this.miniMap, this.regionViewport, 8, 11);
  }

  private applyViewportToMap(map: any, viewport: AdminMapViewport | null, fallbackZoom: number): void {
    if (!map) return;

    if (viewport && !viewport.isEmpty) {
      if (viewport.bounds) {
        map.fitBounds(
          [
            [viewport.bounds.south, viewport.bounds.west],
            [viewport.bounds.north, viewport.bounds.east],
          ],
          {
            padding: [24, 24],
            maxZoom: 14,
            animate: false,
          },
        );
        return;
      }

      if (
        viewport.centerLatitude !== null &&
        viewport.centerLatitude !== undefined &&
        viewport.centerLongitude !== null &&
        viewport.centerLongitude !== undefined
      ) {
        map.setView(
          [viewport.centerLatitude, viewport.centerLongitude],
          viewport.zoom ?? fallbackZoom,
          { animate: false },
        );
        return;
      }
    }

    map.fitWorld({ animate: false });
  }

  private resetPin(): void {
    this.pinConfirmed = false;
    this.pinLat = '';
    this.pinLng = '';
    this.streetAddress = '';
    this.modalPinLat = null;
    this.modalPinLng = null;
    this.clearScopeValidation();

    if (this.miniMarker && this.miniMap) {
      this.miniMap.removeLayer(this.miniMarker);
      this.miniMarker = null;
    }

    this.applyRegionViewportToMiniMap();
  }

  private validateSelectedPoint(latitude: number, longitude: number): void {
    if (!this.activeRegionId || !this.parentLocationId || !this.hasValidCoordinates(latitude, longitude)) {
      this.clearScopeValidation();
      return;
    }

    const validationKey = this.scopeValidationKey(latitude, longitude);
    this.validationSubscription?.unsubscribe();
    this.lastScopeValidationKey = validationKey;
    this.scopeValidationResult = null;
    this.scopeValidationTone = null;
    this.scopeValidationMessage = null;
    this.scopeValidationPending = true;
    if (!this.pinModalOpen) {
      this.pinConfirmed = false;
    }
    this.notifyStateChanged();
    this.validationSubscription = this.adminMapService.validateEntityLocation({
      regionId: this.activeRegionId,
      entityType: 'Place',
      operationType: this.isEditMode ? 'Update' : 'Create',
      latitude,
      longitude,
      locationId: this.parentLocationId,
    }).subscribe({
      next: result => {
        if (this.lastScopeValidationKey !== validationKey) {
          return;
        }
        this.scopeValidationPending = false;
        this.applyScopeValidationResult(result);
        this.notifyStateChanged();
      },
      error: error => {
        if (this.lastScopeValidationKey !== validationKey) {
          return;
        }
        this.scopeValidationPending = false;
        if (!this.pinModalOpen) {
          this.pinConfirmed = false;
        }
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
      if (!this.pinModalOpen) {
        this.pinConfirmed = true;
      }

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

    if (!this.pinModalOpen) {
      this.pinConfirmed = false;
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
    this.lastScopeValidationKey = null;
    this.validationSubscription?.unsubscribe();
    this.validationSubscription = null;
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
      const value = sessionStorage.getItem(NewPlaceComponent.activeRegionStorageKey);
      const parsed = value ? Number(value) : NaN;
      return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
    } catch {
      return null;
    }
  }

  private persistRegion(regionId: number): void {
    try {
      sessionStorage.setItem(NewPlaceComponent.activeRegionStorageKey, String(regionId));
    } catch {
      // Session persistence is a convenience only.
    }
  }

  private clearPersistedRegion(): void {
    try {
      sessionStorage.removeItem(NewPlaceComponent.activeRegionStorageKey);
    } catch {
      // Session persistence is a convenience only.
    }
  }

  private normalizeUrl(value: string, invalidMessage: string): string | null | false {
    const normalized = value.trim();
    if (!normalized) return null;

    try {
      const url = new URL(normalized);
      if ((url.protocol !== 'http:' && url.protocol !== 'https:') || url.username || url.password) {
        this.errorMessage = invalidMessage;
        return false;
      }
      return url.toString();
    } catch {
      this.errorMessage = invalidMessage;
      return false;
    }
  }

  private normalizePhone(value: string): string | null | false {
    const normalized = value.trim();
    if (!normalized) return null;

    if (
      normalized.length < 3 ||
      normalized.length > 50 ||
      [...normalized].some(char => !/[0-9+\-().\s]/.test(char))
    ) {
      this.errorMessage = 'Contact phone is invalid.';
      return false;
    }

    return normalized;
  }

  private formatReverseGeocodeAddress(result: MapReverseGeocodeDto): string {
    return [result.address, result.city, result.region, result.country]
      .filter(part => part?.trim())
      .join(', ');
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

  private syncDescriptionLimitState(): void {
    this.descriptionLimitReached = this.editorTextLength() >= this.descriptionMaxLength;
  }

  private editorTextLength(): number {
    return (this.editorRef?.nativeElement?.innerText ?? this.editorRef?.nativeElement?.textContent ?? '').length;
  }

  private selectedEditorTextLength(): number {
    const editor = this.editorRef?.nativeElement;
    const selection = globalThis.getSelection?.();
    if (!editor || !selection || selection.rangeCount === 0) return 0;
    const range = selection.getRangeAt(0);
    if (!editor.contains(range.commonAncestorContainer)) return 0;
    return selection.toString().length;
  }

  private setEditorHtml(value: string | null | undefined): void {
    const el = this.editorRef?.nativeElement;
    if (!el) return;

    el.innerHTML = this.normalizeEditorHtml(value ?? '');
    this.syncDescriptionLimitState();
  }

  private getEditorHtml(): string {
    return this.normalizeEditorHtml(this.editorRef?.nativeElement?.innerHTML ?? '');
  }

  private normalizeEditorHtml(value: string): string {
    return normalizeEditorDescriptionHtml(value);
  }

  private escapeHtml(value: string): string {
    return value
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
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
        this.errorMessage = 'Sign in with an authenticated Regional Manager account to create places.';
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
      this.errorMessage = error.error?.message ?? (this.isEditMode ? 'Unable to update place.' : 'Unable to publish place.');
      this.notifyStateChanged();
      return;
    }

    this.errorMessage = this.isEditMode ? 'Unable to update place.' : 'Unable to publish place.';
    this.notifyStateChanged();
  }

  private revokeCoverPreviewObjectUrl(): void {
    if (!this.coverPreviewObjectUrl) return;

    URL.revokeObjectURL(this.coverPreviewObjectUrl);
    this.coverPreviewObjectUrl = null;
  }

  private revokeCardPreviewObjectUrl(): void {
    if (!this.cardPreviewObjectUrl) return;

    URL.revokeObjectURL(this.cardPreviewObjectUrl);
    this.cardPreviewObjectUrl = null;
  }

  private revokeGalleryPreviewObjectUrls(): void {
    for (const url of this.galleryPreviewObjectUrls) {
      this.revokeObjectUrlIfNeeded(url);
    }

    this.galleryPreviewObjectUrls = [];
  }

  private revokeObjectUrlIfNeeded(url: string | null | undefined): void {
    if (!url || !url.startsWith('blob:')) {
      return;
    }

    URL.revokeObjectURL(url);
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
    return this.isEditMode ? 'place-edit' : 'place-new';
  }

  private buildDraftSnapshot(): PlaceFormDraft | null {
    const snapshot = this.buildPlaceDraftPayload();
    const hasDraft =
      Boolean(snapshot.placeName.trim()) ||
      Boolean(snapshot.summary.trim()) ||
      Boolean(snapshot.bestTimeToVisit.trim()) ||
      Boolean(snapshot.visitorTips.trim()) ||
      Boolean(snapshot.accessNotes.trim()) ||
      Boolean(snapshot.accessibilityNotes.trim()) ||
      Boolean(snapshot.website.trim()) ||
      Boolean(snapshot.contactPhone.trim()) ||
      Boolean(snapshot.admissionPrice.trim()) ||
      Boolean(snapshot.bookingLink.trim()) ||
      Boolean(snapshot.streetAddress.trim()) ||
      Boolean(snapshot.descriptionHtml) ||
      Boolean(snapshot.parentDestinationId) ||
      Boolean(snapshot.parentLocationId) ||
      Boolean(snapshot.placeTypeId) ||
      Boolean(snapshot.stayTypeId) ||
      snapshot.selectedTagIds.length > 0 ||
      snapshot.pinConfirmed ||
      Boolean(snapshot.cardImageName) ||
      Boolean(snapshot.coverImageName) ||
      snapshot.galleryImageNames.length > 0;

    if (!hasDraft) return null;

    return snapshot;
  }

  private buildPlaceDraftPayload(): PlaceFormDraft {
    return {
      activeRegionId: this.activeRegionId,
      parentDestinationId: this.parentDestinationId,
      placeName: this.placeName,
      summary: this.summary,
      parentLocationId: this.parentLocationId,
      placeTypeId: this.placeTypeId,
      stayTypeId: this.stayTypeId,
      bestTimeToVisit: this.bestTimeToVisit,
      visitorTips: this.visitorTips,
      accessNotes: this.accessNotes,
      accessibilityNotes: this.accessibilityNotes,
      website: this.website,
      contactPhone: this.contactPhone,
      admissionPrice: this.admissionPrice,
      bookingLink: this.bookingLink,
      opsEnabled: this.opsEnabled,
      open24Hours: this.open24Hours,
      days: this.days.map(day => ({ ...day })),
      streetAddress: this.streetAddress,
      pinLat: this.pinLat,
      pinLng: this.pinLng,
      modalPinLat: this.modalPinLat,
      modalPinLng: this.modalPinLng,
      pinConfirmed: this.pinConfirmed,
      descriptionHtml: normalizeEditorDescriptionHtml(this.editorRef?.nativeElement?.innerHTML ?? ''),
      selectedTagIds: [...this.selectedTagIds],
      cardImageName: this.cardImageName,
      coverImageName: this.coverImageName,
      galleryImageNames: this.galleryImages.map(image => image.name),
    };
  }

  private restoreSavedDraftIfPresent(): void {
    const draft = this.formDrafts.loadDraft<PlaceFormDraft>(this.draftFormType());
    const payload = draft?.payload;
    if (!payload) return;

    this.applyDraftPayload(payload);
    const restoredTransientMedia = this.restoreTransientMediaIfPresent();
    this.feedbackMessage = restoredTransientMedia
      ? 'Recovered a saved draft.'
      : 'Recovered a saved draft. Image files must be selected again.';

    const coordinates = this.readPinnedCoordinates();
    if (coordinates && this.pinConfirmed && this.activeRegionId && this.parentLocationId) {
      setTimeout(() => this.revalidatePinnedCoordinatesIfPresent(), 0);
    }

    this.notifyStateChanged();
  }

  private captureReturnFlowState(): void {
    const state = history.state as AddPlaceReturnFlowState | null;
    this.restoredDraftFromNavigation = state?.restoredDraft ?? null;
    this.pendingNewlyCreatedDestination = state?.newlyCreatedDestination ?? null;
    this.pendingNewlyCreatedLocation = state?.newlyCreatedLocation ?? null;
  }

  private restoreDraftFromNavigationIfPresent(): boolean {
    if (!this.restoredDraftFromNavigation && !this.pendingNewlyCreatedDestination && !this.pendingNewlyCreatedLocation) {
      return false;
    }

    if (this.restoredDraftFromNavigation) {
      this.applyDraftPayload(this.restoredDraftFromNavigation);
    }

    if (this.pendingNewlyCreatedDestination) {
      this.parentDestinationId = this.pendingNewlyCreatedDestination.id;
      this.parentLocationId = null;
      this.feedbackMessage = `Destination "${this.pendingNewlyCreatedDestination.name}" selected.`;
    }

    if (this.pendingNewlyCreatedLocation) {
      this.parentDestinationId = this.pendingNewlyCreatedLocation.destinationId ?? this.parentDestinationId;
      this.parentLocationId = this.pendingNewlyCreatedLocation.id;
      this.feedbackMessage = `Location "${this.pendingNewlyCreatedLocation.name}" selected.`;
    }

    if (this.activeRegionId) {
      this.loadParentDestinations();
      if (this.parentDestinationId) {
        this.loadParentLocations();
      }
      this.loadRegionViewport(this.activeRegionId);
    }

    const restoredTransientMedia = this.restoreTransientMediaIfPresent();

    const coordinates = this.readPinnedCoordinates();
    if (coordinates && this.pinConfirmed && this.activeRegionId && this.parentLocationId) {
      setTimeout(() => this.revalidatePinnedCoordinatesIfPresent(), 0);
    }

    if (restoredTransientMedia) {
      this.feedbackMessage = this.feedbackMessage
        ? `${this.feedbackMessage} Gallery images restored.`
        : 'Draft restored from previous step.';
    }

    this.notifyStateChanged();
    return true;
  }

  private applyDraftPayload(payload: PlaceFormDraft): void {
    this.clearScopeValidation();
    this.revokeCardPreviewObjectUrl();
    this.revokeCoverPreviewObjectUrl();
    this.revokeGalleryPreviewObjectUrls();
    this.cardImageFile = null;
    this.cardImageUrl = null;
    this.cardImageName = '';
    this.coverImageFile = null;
    this.coverImageUrl = null;
    this.coverImageName = '';
    this.galleryImages = [];
    this.parentDestinationId = typeof payload.parentDestinationId === 'number' ? payload.parentDestinationId : null;
    this.parentLocationId = payload.parentLocationId ?? null;

    if (typeof payload.activeRegionId === 'number') {
      this.setActiveRegion(payload.activeRegionId, false);
      this.loadParentDestinations();
      if (this.parentDestinationId) {
        this.loadParentLocations();
      }
      this.loadRegionViewport(payload.activeRegionId);
    }

    this.placeName = payload.placeName ?? this.placeName;
    this.summary = payload.summary ?? this.summary;
    this.placeTypeId = payload.placeTypeId;
    this.stayTypeId = payload.stayTypeId ?? null;
    this.bestTimeToVisit = payload.bestTimeToVisit ?? this.bestTimeToVisit;
    this.visitorTips = payload.visitorTips ?? this.visitorTips;
    this.accessNotes = payload.accessNotes ?? this.accessNotes;
    this.accessibilityNotes = payload.accessibilityNotes ?? this.accessibilityNotes;
    this.website = payload.website ?? this.website;
    this.contactPhone = payload.contactPhone ?? this.contactPhone;
    this.admissionPrice = payload.admissionPrice ?? this.admissionPrice;
    this.bookingLink = payload.bookingLink ?? this.bookingLink;
    this.opsEnabled = Boolean(payload.opsEnabled);
    this.open24Hours = Boolean(payload.open24Hours);
    this.days = Array.isArray(payload.days) ? payload.days.map(day => ({ ...day })) : this.days;
    this.streetAddress = payload.streetAddress ?? this.streetAddress;
    this.pinLat = payload.pinLat ?? this.pinLat;
    this.pinLng = payload.pinLng ?? this.pinLng;
    this.modalPinLat = payload.modalPinLat;
    this.modalPinLng = payload.modalPinLng;
    this.pinConfirmed = Boolean(payload.pinConfirmed);
    this.selectedTagIds = new Set(payload.selectedTagIds ?? []);
    this.syncSelectedTagsWithAvailableTags();
    this.classItems = this.classItems.map(item => ({
      ...item,
      checked: this.selectedTagIds.has(item.tag.id),
    }));

    if (this.editorRef?.nativeElement) {
      this.editorRef.nativeElement.innerHTML = normalizeEditorDescriptionHtml(payload.descriptionHtml ?? '');
      this.syncEmptyState();
    }

    const coordinates = this.readPinnedCoordinates();
    if (coordinates && this.pinConfirmed) {
      setTimeout(() => this.updateMiniMapPin(coordinates.latitude, coordinates.longitude), 0);
    }
  }

  private captureTransientMedia(): void {
    const galleryImages = this.galleryImages
      .filter((image): image is { url: string; name: string; file: File } => image.file != null)
      .map(image => ({
        file: image.file,
        name: image.name,
      }));

    this.transientMedia.save(this.draftFormType(), {
      cardImage: this.cardImageFile && this.cardImageUrl
        ? {
            file: this.cardImageFile,
            name: this.cardImageName || this.cardImageFile.name,
          }
        : null,
      coverImage: this.coverImageFile
        ? {
            file: this.coverImageFile,
            name: this.coverImageName || this.coverImageFile.name,
          }
        : null,
      galleryImages,
    });
  }

  private restoreTransientMediaIfPresent(): boolean {
    const media = this.transientMedia.peek(this.draftFormType());
    if (!media) {
      return false;
    }

    this.revokeCardPreviewObjectUrl();
    this.revokeCoverPreviewObjectUrl();
    this.revokeGalleryPreviewObjectUrls();

    if (media.cardImage) {
      this.cardImageFile = media.cardImage.file;
      this.cardImageName = media.cardImage.name;
      this.cardPreviewObjectUrl = URL.createObjectURL(media.cardImage.file);
      this.cardImageUrl = this.cardPreviewObjectUrl;
    }

    if (media.coverImage) {
      this.coverImageFile = media.coverImage.file;
      this.coverImageName = media.coverImage.name;
      this.coverPreviewObjectUrl = URL.createObjectURL(media.coverImage.file);
      this.coverImageUrl = this.coverPreviewObjectUrl;
    }

    if (media.galleryImages?.length) {
      this.galleryImages = media.galleryImages.map((image: AdminTransientFileItem) => ({
        url: URL.createObjectURL(image.file),
        name: image.name,
        file: image.file,
      }));
      this.galleryPreviewObjectUrls = this.galleryImages.map(image => image.url);
    }

    this.transientMedia.clear(this.draftFormType());
    return true;
  }
}
