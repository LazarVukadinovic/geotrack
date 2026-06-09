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
import { finalize, forkJoin, Subscription } from 'rxjs';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { DashboardService } from '../../../../core/services/dashboard.service';
import { LightboxService } from '../../../../core/services/lightbox.service';
import { PlacesListService } from '../../../../core/services/places-list.service';
import { AuthService } from '../../../../core/services/auth.service';
import { AdminMapService } from '../../../../core/services/admin-map.service';
import { MapTileLayerService } from '../../../../core/services/map-tile-layer.service';
import { RegionalAssignedRegion } from '../../../../core/models/dashboard.model';
import { AdminMapViewport, AdminMapZone, RegionalScopeValidationResult } from '../../../../core/models/admin-map.model';
import {
  RegionalPlaceCreateRequest,
  RegionalPlaceOperatingHourDto,
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
  imports: [CommonModule, FormsModule, DecimalPipe, SidebarComponent, TopbarComponent],
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

  @ViewChild('editorRef') editorRef!: ElementRef<HTMLDivElement>;
  @ViewChild('miniMapContainer') miniMapContainer!: ElementRef<HTMLDivElement>;
  @ViewChild('modalMapContainer') modalMapContainerRef!: ElementRef<HTMLDivElement>;

  assignedRegions: RegionalAssignedRegion[] = [];
  activeRegionId: number | null = null;
  regionLoading = false;
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

  placeName = '';
  summary = '';
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
  tagInput = '';
  tagFilter = '';

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

  cardImageUrl: string | null = null;
  cardImageName = '';
  coverImageUrl: string | null = null;
  coverImageName = '';
  galleryImages: { url: string; name: string; file: File }[] = [];

  private cardImageFile: File | null = null;
  private coverImageFile: File | null = null;

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

  constructor(
    private readonly zone: NgZone,
    private readonly cdr: ChangeDetectorRef,
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly dashboardService: DashboardService,
    private readonly lightboxService: LightboxService,
    private readonly placesService: PlacesListService,
    private readonly auth: AuthService,
    private readonly http: HttpClient,
    private readonly adminMapService: AdminMapService,
  ) {}

  ngOnInit(): void {
    this.editPlaceId = this.route.snapshot.paramMap.get('id');
    this.isEditMode = Boolean(this.editPlaceId);

    this.loadAssignedRegions();
    this.applyInitialCoordinatesFromRoute();
    this.loadPlaceTypes();
    this.loadStayTypes();
    this.loadPlaceTags();
  }

  ngAfterViewInit(): void {
    setTimeout(() => {
      this.initMiniMap();
      const coordinates = this.readPinnedCoordinates();
      if (coordinates && this.pinConfirmed) {
        this.updateMiniMapPin(coordinates.latitude, coordinates.longitude);
      }
    }, 0);
  }

  ngOnDestroy(): void {
    this.assignedRegionsSubscription?.unsubscribe();
    this.parentLocationsSubscription?.unsubscribe();
    this.placeTypesSubscription?.unsubscribe();
    this.tagsSubscription?.unsubscribe();
    this.tagSearchSubscription?.unsubscribe();
    this.createSubscription?.unsubscribe();
    this.reverseGeocodeSubscription?.unsubscribe();
    this.viewportSubscription?.unsubscribe();
    this.validationSubscription?.unsubscribe();

    if (this.tagSearchHandle) {
      clearTimeout(this.tagSearchHandle);
    }
    if (this.geocodeDebounceHandle) {
      clearTimeout(this.geocodeDebounceHandle);
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

  get selectedParentLocation(): RegionalPlaceParentLocationDto | null {
    return this.parentLocations.find(location => location.id === this.parentLocationId) ?? null;
  }

  get selectedParentDestinationName(): string {
    return this.selectedParentLocation?.destinationName ?? '';
  }

  get selectedPlaceTypeName(): string {
    return this.placeTypes.find(type => type.id === this.placeTypeId)?.name ?? '';
  }

  get isStayType(): boolean {
    const name = (this.selectedPlaceTypeName ?? '').toLowerCase().trim();
    return name === 'stay' || name === 'stays' || name === 'hotel' || name === 'hotels' ||
      name === 'accommodation' || name === 'accommodations';
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
      Boolean(this.parentLocationId) &&
      Boolean(this.placeTypeId) &&
      this.placeName.trim().length > 0 &&
      this.summary.trim().length > 0 &&
      this.pinConfirmed &&
      !this.regionBoundaryMissingMessage &&
      !this.scopeValidationPending &&
      !this.isScopeValidationRejected &&
      mediaReady &&
      !this.regionLoading &&
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
            this.parentLocationId = null;
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
          this.loadParentLocations();
          this.loadRegionViewport(selectedRegion.id);
          this.loadPlaceForEditIfNeeded();
        },
        error: error => this.handleLoadError(error, 'Unable to load assigned regions.'),
      });
  }

  onRegionSelected(regionId: number): void {
    if (regionId === this.activeRegionId) return;

    this.setActiveRegion(regionId, true);
    this.parentLocationId = null;
    this.parentLocations = [];
    this.resetPin();
    this.loadParentLocations();
    this.loadRegionViewport(regionId);
    this.feedbackMessage = 'Region changed. Parent location and map pin were cleared.';
    this.notifyStateChanged();
  }

  loadParentLocations(): void {
    if (!this.activeRegionId) {
      this.parentLocations = [];
      this.parentLocationId = null;
      this.notifyStateChanged();
      return;
    }

    this.parentLocationsLoading = true;
    this.notifyStateChanged();

    this.parentLocationsSubscription?.unsubscribe();
    this.parentLocationsSubscription = this.placesService
      .getParentLocations(this.activeRegionId)
      .pipe(
        finalize(() => {
          this.parentLocationsLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: locations => {
          this.parentLocations = this.normalizeParentLocations(locations);
          if (
            this.parentLocationId &&
            !this.parentLocations.some(location => location.id === this.parentLocationId)
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
        this.parentLocationId = dto.locationId ?? null;
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
          setTimeout(() => this.updateMiniMapPin(dto.latitude, dto.longitude), 0);
        }

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
          this.upsertPlaceTag(tag);
          this.upsertClassItem(tag, true);
        }
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
        }, 0);

        this.notifyStateChanged();
      },
      error: error => this.handleLoadError(error, 'Unable to load place for editing.'),
    });
  }

  onParentLocationChanged(): void {
    this.notifyStateChanged();
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

  onEditorPaste(event: ClipboardEvent): void {
    const clipboard = event.clipboardData;
    if (!clipboard) return;

    event.preventDefault();
    const html = clipboard.getData('text/html');
    const text = clipboard.getData('text/plain');
    const content = html || this.escapeHtml(text).replace(/\r\n|\r|\n/g, '<br>');
    document.execCommand('insertHTML', false, this.normalizeEditorHtml(content));
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
    } else if (el) {
      el.innerHTML = this.normalizeEditorHtml(el.innerHTML);
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
    this.createSubscription = this.placesService
      .updatePlace(regionId, placeId, payload)
      .pipe(
        finalize(() => {
          this.isSaving = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: updated => {
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

    const locationId = this.parentLocationId;
    if (!locationId) {
      this.errorMessage = 'Parent location is required.';
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
      bookingExternalLink: bookingLink,
      operatingHoursEnabled: this.opsEnabled,
      operatingHours,
      tagIds: this.selectedTagIdList(),
      cardImageFile: this.cardImageFile,
      coverImageFile: this.coverImageFile,
      galleryImageFiles: this.galleryImages.map(image => image.file),
    };
  }

  private buildUpdatePayload(): RegionalPlaceUpdateRequest | null {
    const locationId = this.parentLocationId;
    if (!locationId) {
      this.errorMessage = 'Parent location is required.';
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
      this.errorMessage = 'Pin the place on the map before updating.';
      return null;
    }

    if (this.scopeValidationPending || this.isScopeValidationRejected) {
      this.errorMessage = this.scopeValidationPending
        ? 'Location validation is still running.'
        : this.scopeValidationMessage ?? 'Selected location is outside your allowed regional scope.';
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
      bookingExternalLink: bookingLink,
      operatingHoursEnabled: this.opsEnabled,
      operatingHours,
      tagIds: this.selectedTagIdList(),
    };
  }

  private buildOperatingHoursPayload(): RegionalPlaceOperatingHourDto[] | null {
    if (!this.opsEnabled) return [];

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
      return;
    }

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
    return [...new Set(this.selectedTagIds)].sort((a, b) => a - b);
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
    if (!this.activeRegionId || !this.hasValidCoordinates(latitude, longitude)) {
      this.clearScopeValidation();
      return;
    }

    this.validationSubscription?.unsubscribe();
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
        this.scopeValidationPending = false;
        this.applyScopeValidationResult(result);
        this.notifyStateChanged();
      },
      error: error => {
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

  private setEditorHtml(value: string | null | undefined): void {
    const el = this.editorRef?.nativeElement;
    if (!el) return;

    el.innerHTML = this.normalizeEditorHtml(value ?? '');
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

  private notifyStateChanged(): void {
    this.cdr.markForCheck();
  }
}
