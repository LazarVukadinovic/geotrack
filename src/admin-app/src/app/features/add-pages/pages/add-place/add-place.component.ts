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
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { ActivatedRoute, Router } from '@angular/router';
import { finalize, Subscription } from 'rxjs';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { DashboardService } from '../../../../core/services/dashboard.service';
import { LightboxService } from '../../../../core/services/lightbox.service';
import { PlacesListService } from '../../../../core/services/places-list.service';
import { AuthService } from '../../../../core/services/auth.service';
import { AdminMapService } from '../../../../core/services/admin-map.service';
import { RegionalAssignedRegion } from '../../../../core/models/dashboard.model';
import { AdminMapViewport } from '../../../../core/models/admin-map.model';
import {
  RegionalPlaceCreateRequest,
  RegionalPlaceOperatingHourDto,
  RegionalPlaceParentLocationDto,
  RegionalPlaceTagDto,
  RegionalPlaceTypeDto,
} from '../../../../core/models/place.model';

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
  private static readonly expandedVisibleClassificationTags = 10;

  @ViewChild('editorRef') editorRef!: ElementRef<HTMLDivElement>;
  @ViewChild('miniMapContainer') miniMapContainer!: ElementRef<HTMLDivElement>;
  @ViewChild('modalMapContainer') modalMapContainerRef!: ElementRef<HTMLDivElement>;

  assignedRegions: RegionalAssignedRegion[] = [];
  activeRegionId: number | null = null;
  regionLoading = false;
  parentLocationsLoading = false;
  placeTypesLoading = false;
  tagsLoading = false;
  tagSearchLoading = false;
  isSaving = false;
  isGeocodingAddress = false;
  errorMessage: string | null = null;
  feedbackMessage: string | null = null;

  isEditMode = false;
  editPlaceId: string | null = null;

  placeName = '';
  summary = '';
  parentLocationId: number | null = null;
  placeTypeId: number | null = null;
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

  parentLocations: RegionalPlaceParentLocationDto[] = [];
  placeTypes: RegionalPlaceTypeDto[] = [];
  placeTags: RegionalPlaceTagDto[] = [];
  classItems: ClassItem[] = [];
  additionalTagResults: RegionalPlaceTagDto[] = [];
  selectedTagIds = new Set<number>();
  classificationExpanded = false;

  isBold = false;
  isItalic = false;
  isUnderline = false;
  editorIsEmpty = true;

  coverImageUrl: string | null = null;
  coverImageName = '';
  galleryImages: { url: string; name: string }[] = [];

  allHours = Array.from({ length: 24 }, (_, hour) => `${hour.toString().padStart(2, '0')}:00`);
  days: DayHours[] = [
    { name: 'Monday', open: true, openTime: '09:00', closeTime: '17:00' },
    { name: 'Tuesday', open: true, openTime: '09:00', closeTime: '17:00' },
    { name: 'Wednesday', open: true, openTime: '09:00', closeTime: '17:00' },
    { name: 'Thursday', open: true, openTime: '09:00', closeTime: '17:00' },
    { name: 'Friday', open: true, openTime: '09:00', closeTime: '17:00' },
    { name: 'Saturday', open: true, openTime: '10:00', closeTime: '20:00' },
    { name: 'Sunday', open: false, openTime: '09:00', closeTime: '17:00' },
  ];

  pinModalOpen = false;
  pinConfirmed = false;
  pinLat = '';
  pinLng = '';
  streetAddress = '';
  modalPinLat: number | null = null;
  modalPinLng: number | null = null;

  private miniMap: any = null;
  private modalMap: any = null;
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
  private tagSearchHandle: ReturnType<typeof setTimeout> | null = null;
  private geocodeDebounceHandle: ReturnType<typeof setTimeout> | null = null;
  private regionViewport: AdminMapViewport | null = null;

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
    if (this.isEditMode) {
      this.errorMessage = 'Place editing is not available from this create form.';
    }

    this.loadAssignedRegions();
    this.applyInitialCoordinatesFromRoute();
    this.loadPlaceTypes();
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

  get selectedAdditionalTags(): RegionalPlaceTagDto[] {
    const primaryIds = new Set(this.classItems.map(item => item.tag.id));
    return this.placeTags
      .filter(tag => this.selectedTagIds.has(tag.id) && !primaryIds.has(tag.id))
      .sort((a, b) => a.name.localeCompare(b.name));
  }

  get visibleClassItems(): ClassItem[] {
    return this.classItems.slice(0, this.visibleClassificationLimit);
  }

  get showClassificationToggle(): boolean {
    return this.classItems.length > NewPlaceComponent.initialVisibleClassificationTags;
  }

  get visibleClassificationLimit(): number {
    return this.classificationExpanded
      ? NewPlaceComponent.expandedVisibleClassificationTags
      : NewPlaceComponent.initialVisibleClassificationTags;
  }

  get createdDateDisplay(): string {
    return new Date().toLocaleDateString('en-US', {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
    });
  }

  get canPublish(): boolean {
    return !this.isEditMode &&
      Boolean(this.activeRegionId) &&
      Boolean(this.parentLocationId) &&
      Boolean(this.placeTypeId) &&
      this.placeName.trim().length > 0 &&
      this.summary.trim().length > 0 &&
      this.pinConfirmed &&
      !this.regionLoading &&
      !this.parentLocationsLoading &&
      !this.placeTypesLoading &&
      !this.tagsLoading &&
      !this.isSaving;
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
    this.viewportSubscription = this.adminMapService
      .getViewport(regionId)
      .subscribe({
        next: viewport => {
          this.regionViewport = viewport;
          this.applyRegionViewportToMiniMap();
          this.notifyStateChanged();
        },
        error: () => {
          this.regionViewport = null;
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
        this.notifyStateChanged();
      });
    };
    reader.readAsDataURL(file);
    input.value = '';
  }

  removeCoverImage(): void {
    this.coverImageUrl = null;
    this.coverImageName = '';
    this.notifyStateChanged();
  }

  onGalleryImagesSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []).filter(file => this.isAllowedImage(file));
    for (const file of files) {
      const reader = new FileReader();
      reader.onload = () => {
        this.zone.run(() => {
          this.galleryImages = [...this.galleryImages, { url: reader.result as string, name: file.name }];
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

    const website = this.normalizeUrl(this.website, 'Website URL is invalid.');
    if (website === false) return null;

    const bookingLink = this.normalizeUrl(this.bookingLink, 'Booking / external link is invalid.');
    if (bookingLink === false) return null;

    const contactPhone = this.normalizePhone(this.contactPhone);
    if (contactPhone === false) return null;

    const operatingHours = this.buildOperatingHoursPayload();
    if (!operatingHours) return null;

    const descriptionHtml = this.editorRef?.nativeElement?.innerHTML?.trim() ?? '';

    return {
      regionId,
      locationId,
      placeTypeId,
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
      tagIds: [...this.selectedTagIds],
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

      if (this.toMinutes(day.openTime) >= this.toMinutes(day.closeTime)) {
        this.errorMessage = `${day.name} opening time must be before closing time.`;
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

    L.tileLayer('https://tiles.stadiamaps.com/tiles/alidade_smooth/{z}/{x}/{y}{r}.png', {
      maxZoom: 19,
    }).addTo(this.modalMap);

    setTimeout(() => this.modalMap?.invalidateSize(), 200);

    if (this.modalPinLat !== null && this.modalPinLng !== null) {
      this.modalMap.setView([this.modalPinLat, this.modalPinLng], 14);
      this.modalMarker = L.marker([this.modalPinLat, this.modalPinLng], {
        icon: this.makePinIcon(),
        draggable: true,
      }).addTo(this.modalMap);

      this.modalMarker.on('dragend', (event: any) => this.onModalMarkerMoved(event.target.getLatLng()));
    } else {
      this.applyViewportToMap(this.modalMap, this.regionViewport, 9);
    }

    this.modalMap.on('click', (event: any) => {
      this.zone.run(() => {
        this.modalPinLat = event.latlng.lat;
        this.modalPinLng = event.latlng.lng;
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
      this.notifyStateChanged();
    });
  }

  private destroyModalMap(): void {
    if (this.modalMap) {
      this.modalMap.remove();
      this.modalMap = null;
      this.modalMarker = null;
    }
  }

  private makePinIcon(): any {
    return L.divIcon({
      html: `<svg width="24" height="32" viewBox="0 0 24 32" xmlns="http://www.w3.org/2000/svg">
               <path d="M12 0C5.373 0 0 5.373 0 12c0 8 12 20 12 20S24 20 24 12C24 5.373 18.627 0 12 0z"
                     fill="#2563eb" stroke="white" stroke-width="1.5"/>
               <circle cx="12" cy="12" r="4" fill="white"/>
             </svg>`,
      iconSize: [24, 32],
      iconAnchor: [12, 32],
      className: '',
    });
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

  private normalizePlaceTypes(types: RegionalPlaceTypeDto[] | null | undefined): RegionalPlaceTypeDto[] {
    const seen = new Set<number>();
    return (types ?? [])
      .filter(type => {
        if (!type || seen.has(type.id)) return false;
        seen.add(type.id);
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
    this.applyViewportToMap(this.miniMap, this.regionViewport, 8);
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

    if (this.miniMarker && this.miniMap) {
      this.miniMap.removeLayer(this.miniMarker);
      this.miniMarker = null;
    }

    this.applyRegionViewportToMiniMap();
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

  private toMinutes(value: string): number {
    const [hours, minutes] = value.split(':').map(part => Number(part));
    return hours * 60 + minutes;
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
      this.errorMessage = error.error?.message ?? 'Unable to publish place.';
      this.notifyStateChanged();
      return;
    }

    this.errorMessage = 'Unable to publish place.';
    this.notifyStateChanged();
  }

  private notifyStateChanged(): void {
    this.cdr.markForCheck();
  }
}
