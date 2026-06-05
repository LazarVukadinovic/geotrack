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
import { AuthService } from '../../../../core/services/auth.service';
import { LightboxService } from '../../../../core/services/lightbox.service';
import { AdminMapService } from '../../../../core/services/admin-map.service';
import { MapTileLayerService } from '../../../../core/services/map-tile-layer.service';
import { RegionalAssignedRegion } from '../../../../core/models/dashboard.model';
import { AdminMapViewport, AdminMapZone, RegionalScopeValidationResult } from '../../../../core/models/admin-map.model';
import {
  RegionalEventCreateRequest,
  RegionalEventImageDto,
  RegionalEventImageInputDto,
  RegionalEventParentDestinationDto,
  RegionalEventParentLocationDto,
  RegionalEventParentPlaceDto,
  RegionalEventTagDto,
  RegionalEventTypeDto,
  RegionalEventUpdateRequest,
} from '../../../../core/models/event.model';
import { EventsListService } from '../../../../core/services/events-list.service';
import {
  applyAssignedRegionViewport,
  assignedRegionZone,
  createRegionalMapPinIcon,
  isPreciseAssignedRegionBoundary,
  renderAssignedRegionBoundary,
} from '../../shared/regional-creation-map';

declare const L: any;

type EventParentType = 'location' | 'place' | 'destination';

interface ClassItem {
  tag: RegionalEventTagDto;
  checked: boolean;
}

interface GalleryImageItem {
  url: string;
  name: string;
  file?: File;
}

@Component({
  selector: 'app-new-event',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './add-event.component.html',
  styleUrl: './add-event.component.css',
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
export class NewEventComponent implements OnInit, AfterViewInit, OnDestroy {
  private static readonly activeRegionStorageKey = 'geotrack_admin_active_region_id';
  private static readonly initialVisibleClassificationTags = 5;
  private readonly mapTileLayer = inject(MapTileLayerService);

  @ViewChild('editorRef') editorRef!: ElementRef<HTMLDivElement>;
  @ViewChild('miniMapContainer') miniMapContainer!: ElementRef<HTMLDivElement>;

  assignedRegions: RegionalAssignedRegion[] = [];
  activeRegionId: number | null = null;
  regionLoading = false;
  parentsLoading = false;
  eventTypesLoading = false;
  tagsLoading = false;
  tagSearchLoading = false;
  isEditMode = false;
  editEventId: string | null = null;
  readonly internalReferenceId = '';
  isSaving = false;
  errorMessage: string | null = null;
  feedbackMessage: string | null = null;
  regionBoundaryMissingMessage: string | null = null;
  scopeValidationMessage: string | null = null;
  scopeValidationTone: 'success' | 'warning' | 'error' | null = null;
  scopeValidationPending = false;

  eventTitle = '';
  eventTypeId: number | null = null;
  summary = '';
  venueType: EventParentType = 'location';
  selectedDestinationId: number | null = null;
  selectedLocationId: number | null = null;
  selectedPlaceId: number | null = null;
  customVenueName = '';

  allDay = false;
  recurring = false;
  startDate = this.todayIso();
  endDate = this.todayIso();
  startTime = '09:00';
  endTime = '17:00';

  freeEvent = true;
  ticketPrice = '';
  externalTicketLink = '';
  organizerName = '';
  contactEmail = '';
  attendanceLimit = '';
  ageRestriction = '';

  isBold = false;
  isItalic = false;
  isUnderline = false;
  editorIsEmpty = true;

  cardImageUrl: string | null = null;
  cardImageName = '';
  coverImageUrl: string | null = null;
  coverImageName = '';
  galleryImages: GalleryImageItem[] = [];

  private cardImageFile: File | null = null;
  private coverImageFile: File | null = null;
  private existingImages: RegionalEventImageDto[] = [];

  destinations: RegionalEventParentDestinationDto[] = [];
  locations: RegionalEventParentLocationDto[] = [];
  places: RegionalEventParentPlaceDto[] = [];
  eventTypes: RegionalEventTypeDto[] = [];
  eventTags: RegionalEventTagDto[] = [];
  classItems: ClassItem[] = [];
  selectedTagIds = new Set<number>();
  additionalTagResults: RegionalEventTagDto[] = [];
  tagInput = '';
  tagFilter = '';
  classificationExpanded = false;

  private miniMap: any = null;
  private miniRegionBoundaryLayer: any = null;
  private miniMarker: any = null;
  private subscriptions: Subscription[] = [];
  private regionMapSubscription: Subscription | null = null;
  private validationSubscription: Subscription | null = null;
  private parentSubscription: Subscription | null = null;
  private tagSearchSubscription: Subscription | null = null;
  private createSubscription: Subscription | null = null;
  private tagSearchHandle: ReturnType<typeof setTimeout> | null = null;
  private readonly knownTags = new Map<number, RegionalEventTagDto>();
  private regionViewport: AdminMapViewport | null = null;
  private regionBoundaryZone: AdminMapZone | null = null;
  private scopeValidationResult: RegionalScopeValidationResult | null = null;

  constructor(
    private readonly zone: NgZone,
    private readonly cdr: ChangeDetectorRef,
    private readonly router: Router,
    private readonly route: ActivatedRoute,
    private readonly dashboardService: DashboardService,
    private readonly auth: AuthService,
    private readonly lightboxService: LightboxService,
    private readonly eventsService: EventsListService,
    private readonly adminMapService: AdminMapService,
  ) {}

  ngOnInit(): void {
    this.editEventId = this.route.snapshot.paramMap.get('id');
    this.isEditMode = Boolean(this.editEventId);
    this.loadAssignedRegions();
    this.loadEventTypes();
    this.loadEventTags();
  }

  ngAfterViewInit(): void {
    setTimeout(() => this.initMiniMap(), 0);
  }

  ngOnDestroy(): void {
    this.subscriptions.forEach(subscription => subscription.unsubscribe());
    this.regionMapSubscription?.unsubscribe();
    this.validationSubscription?.unsubscribe();
    this.parentSubscription?.unsubscribe();
    this.tagSearchSubscription?.unsubscribe();
    this.createSubscription?.unsubscribe();
    if (this.tagSearchHandle) clearTimeout(this.tagSearchHandle);
    this.lightboxService.close();
    if (this.miniMap) {
      this.miniMap.remove();
      this.miniMap = null;
    }
  }

  get currentUserName(): string {
    return this.auth.fullName || this.auth.getUser()?.email || '';
  }

  get createdDateDisplay(): string {
    return new Date().toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
  }

  get activeRegion(): RegionalAssignedRegion | null {
    return this.assignedRegions.find(region => region.id === this.activeRegionId) ?? null;
  }

  get regionDisplayName(): string {
    return this.activeRegion?.displayName ?? '';
  }

  get selectedParentDestinationName(): string {
    return this.parentDestination?.displayName ?? this.parentDestination?.name ?? '';
  }

  get parentDestination(): RegionalEventParentDestinationDto | null {
    if (this.venueType === 'destination') {
      return this.destinations.find(destination => destination.id === this.selectedDestinationId) ?? null;
    }

    const destinationId = this.venueType === 'place'
      ? this.selectedPlace?.destinationId
      : this.selectedLocation?.destinationId;

    if (!destinationId) return null;

    return this.destinations.find(destination => destination.id === destinationId)
      ?? {
        id: destinationId,
        name: this.venueType === 'place'
          ? this.selectedPlace?.destinationName ?? ''
          : this.selectedLocation?.destinationName ?? '',
        displayName: this.venueType === 'place'
          ? this.selectedPlace?.destinationName ?? ''
          : this.selectedLocation?.destinationName ?? '',
        status: '',
        latitude: null,
        longitude: null,
      };
  }

  get selectedLocation(): RegionalEventParentLocationDto | null {
    return this.locations.find(location => location.id === this.selectedLocationId) ?? null;
  }

  get selectedPlace(): RegionalEventParentPlaceDto | null {
    return this.places.find(place => place.id === this.selectedPlaceId) ?? null;
  }

  get selectedDestination(): RegionalEventParentDestinationDto | null {
    return this.destinations.find(destination => destination.id === this.selectedDestinationId) ?? null;
  }

  get selectedParentName(): string {
    if (this.venueType === 'place') return this.selectedPlace?.displayName ?? this.selectedPlace?.name ?? '';
    if (this.venueType === 'destination') return this.selectedDestination?.displayName ?? this.selectedDestination?.name ?? '';
    return this.selectedLocation?.displayName ?? this.selectedLocation?.name ?? '';
  }

  get selectedParentLatitude(): number | null {
    const parent = this.selectedParentGeo;
    return typeof parent?.latitude === 'number' ? parent.latitude : null;
  }

  get selectedParentLongitude(): number | null {
    const parent = this.selectedParentGeo;
    return typeof parent?.longitude === 'number' ? parent.longitude : null;
  }

  get visibleClassItems(): ClassItem[] {
    const query = this.tagFilter.trim().toLocaleLowerCase();
    if (!query) return this.classItems;

    return this.classItems.filter(item =>
      item.tag.name.toLocaleLowerCase().includes(query) ||
      item.tag.slug.toLocaleLowerCase().includes(query),
    );
  }

  get canToggleClassification(): boolean {
    return false;
  }

  get selectedTags(): RegionalEventTagDto[] {
    return [...this.selectedTagIds]
      .map(tagId => this.knownTags.get(tagId))
      .filter((tag): tag is RegionalEventTagDto => Boolean(tag))
      .sort((a, b) => a.name.localeCompare(b.name));
  }

  get visibleClassificationLimit(): number {
    return this.classificationExpanded
      ? this.classItems.length
      : NewEventComponent.initialVisibleClassificationTags;
  }

  get remainingClassificationCount(): number {
    return Math.max(0, this.classItems.length - this.visibleClassItems.length);
  }

  get canPublish(): boolean {
    if (this.isEditMode) {
      return !this.isSaving &&
        Boolean(this.eventTitle.trim()) &&
        Boolean(this.summary.trim()) &&
        Boolean(this.eventTypeId) &&
        this.hasSelectedParent() &&
        this.scopeValidationResult?.isAllowed !== false;
    }

    return !this.isSaving &&
      Boolean(this.activeRegionId) &&
      Boolean(this.eventTitle.trim()) &&
      Boolean(this.summary.trim()) &&
      Boolean(this.eventTypeId) &&
      Boolean(this.parentDestination) &&
      Boolean(this.cardImageFile) &&
      Boolean(this.coverImageFile) &&
      !this.regionBoundaryMissingMessage &&
      !this.scopeValidationPending &&
      this.scopeValidationResult?.isAllowed === true &&
      this.hasSelectedParent();
  }

  get saveButtonDisabled(): boolean {
    return this.isEditMode ? this.isSaving : !this.canPublish;
  }

  get isScopeValidationRejected(): boolean {
    return this.scopeValidationResult?.isAllowed === false;
  }

  private get selectedParentGeo(): { latitude?: number | null; longitude?: number | null } | null {
    if (this.venueType === 'place') return this.selectedPlace;
    if (this.venueType === 'destination') return this.selectedDestination;
    return this.selectedLocation;
  }

  onRegionSelected(regionId: number): void {
    if (regionId === this.activeRegionId) return;
    this.activeRegionId = regionId;
    sessionStorage.setItem(NewEventComponent.activeRegionStorageKey, String(regionId));
    this.resetParentSelection();
    this.loadAssignedRegionMap(regionId);
    this.loadParentOptions();
    this.cdr.markForCheck();
  }

  onVenueTypeChange(): void {
    this.resetParentSelection();
    this.loadParentOptions();
  }

  onParentSelectionChanged(): void {
    if (this.venueType === 'place') {
      const place = this.selectedPlace;
      this.selectedLocationId = place?.locationId ?? null;
      this.selectedDestinationId = place?.destinationId ?? null;
    } else if (this.venueType === 'location') {
      const location = this.selectedLocation;
      this.selectedDestinationId = location?.destinationId ?? null;
      this.selectedPlaceId = null;
    } else {
      this.selectedLocationId = null;
      this.selectedPlaceId = null;
    }

    this.updateMiniMapFromSelection();
    this.validateSelectedParentLocation();
    this.cdr.markForCheck();
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
    const input = event.target instanceof HTMLInputElement ? event.target : null;
    const file = input?.files?.[0];
    if (!file || !this.validateImageFile(file)) return;

    const reader = new FileReader();
    reader.onload = () => {
      this.zone.run(() => {
        this.cardImageUrl = reader.result as string;
        this.cardImageName = file.name;
        this.cardImageFile = file;
        this.cdr.markForCheck();
      });
    };
    reader.readAsDataURL(file);
    if (input) input.value = '';
  }

  removeCardImage(): void {
    this.cardImageUrl = null;
    this.cardImageName = '';
    this.cardImageFile = null;
    this.cdr.markForCheck();
  }

  onCoverImageSelected(event: Event): void {
    const input = event.target instanceof HTMLInputElement ? event.target : null;
    const file = input?.files?.[0];
    if (!file || !this.validateImageFile(file)) return;

    const reader = new FileReader();
    reader.onload = () => {
      this.zone.run(() => {
        this.coverImageUrl = reader.result as string;
        this.coverImageName = file.name;
        this.coverImageFile = file;
        this.cdr.markForCheck();
      });
    };
    reader.readAsDataURL(file);
    if (input) input.value = '';
  }

  removeCoverImage(): void {
    this.coverImageUrl = null;
    this.coverImageName = '';
    this.coverImageFile = null;
    this.cdr.markForCheck();
  }

  onGalleryImagesSelected(event: Event): void {
    const input = event.target instanceof HTMLInputElement ? event.target : null;
    const files = Array.from(input?.files ?? []).filter(file => this.validateImageFile(file));
    for (const file of files) {
      const reader = new FileReader();
      reader.onload = () => {
        this.zone.run(() => {
          this.galleryImages = [...this.galleryImages, { url: reader.result as string, name: file.name, file }];
          this.cdr.markForCheck();
        });
      };
      reader.readAsDataURL(file);
    }
    if (input) input.value = '';
  }

  removeGalleryImage(index: number): void {
    this.galleryImages = this.galleryImages.filter((_, currentIndex) => currentIndex !== index);
    this.cdr.markForCheck();
  }

  openLightbox(images: { url: string; name: string }[], index: number): void {
    this.lightboxService.open(images, index);
  }

  onClassItemChange(item: ClassItem): void {
    if (item.checked) {
      this.knownTags.set(item.tag.id, item.tag);
      this.selectedTagIds.add(item.tag.id);
    } else {
      this.selectedTagIds.delete(item.tag.id);
    }
    this.cdr.markForCheck();
  }

  removeSelectedTag(tagId: number): void {
    this.selectedTagIds.delete(tagId);
    this.classItems = this.classItems.map(item =>
      item.tag.id === tagId ? { ...item, checked: false } : item);
    this.cdr.markForCheck();
  }

  toggleAdditionalTag(tag: RegionalEventTagDto): void {
    this.upsertEventTag(tag);
    this.knownTags.set(tag.id, tag);
    if (this.selectedTagIds.has(tag.id)) {
      this.selectedTagIds.delete(tag.id);
    } else {
      this.selectedTagIds.add(tag.id);
    }
    this.classItems = this.classItems.map(item =>
      item.tag.id === tag.id ? { ...item, checked: this.selectedTagIds.has(tag.id) } : item);
    this.tagInput = '';
    this.additionalTagResults = [];
    this.cdr.markForCheck();
  }

  addTag(event: Event): void {
    event.preventDefault();
    const name = this.tagInput.trim();
    if (name.length < 2 || this.tagSearchLoading) return;

    const existingSelectedTag = this.eventTags.find(
      tag => tag.name.trim().toLocaleLowerCase() === name.toLocaleLowerCase() && this.selectedTagIds.has(tag.id),
    );
    if (existingSelectedTag) {
      this.tagInput = '';
      this.cdr.markForCheck();
      return;
    }

    const tag = this.findTagToAdd();
    if (tag) {
      this.toggleAdditionalTag(tag);
      this.tagInput = '';
      this.additionalTagResults = [];
      this.cdr.markForCheck();
      return;
    }

    this.tagSearchLoading = true;
    this.tagSearchSubscription?.unsubscribe();
    this.tagSearchSubscription = this.eventsService.createEventTag(name)
      .pipe(finalize(() => {
        this.tagSearchLoading = false;
        this.cdr.markForCheck();
      }))
      .subscribe({
        next: createdTag => {
          this.toggleAdditionalTag(createdTag);
          this.tagInput = '';
          this.additionalTagResults = [];
          this.cdr.markForCheck();
        },
        error: error => {
          this.errorMessage = this.extractErrorMessage(error, 'Event tag could not be created.');
          this.cdr.markForCheck();
        },
      });
  }

  onTagSearchInput(): void {
    if (this.tagSearchHandle) clearTimeout(this.tagSearchHandle);
    this.additionalTagResults = [];
    this.cdr.markForCheck();
  }

  toggleClassificationExpanded(): void {
    this.classificationExpanded = !this.classificationExpanded;
    this.cdr.markForCheck();
  }

  publish(): void {
    if (this.isSaving) return;

    if (this.isEditMode) {
      this.updateExistingEvent();
      return;
    }

    const payload = this.buildPayload();
    if (!payload) {
      this.cdr.markForCheck();
      return;
    }

    this.isSaving = true;
    this.errorMessage = null;
    this.feedbackMessage = null;
    this.createSubscription?.unsubscribe();
    this.createSubscription = this.eventsService.createEvent(payload)
      .pipe(finalize(() => {
        this.isSaving = false;
        this.cdr.markForCheck();
      }))
      .subscribe({
        next: created => {
          this.feedbackMessage = 'Event published.';
          this.router.navigate(['/events'], { queryParams: { regionId: created.regionId } });
        },
        error: error => {
          this.errorMessage = this.extractErrorMessage(error, 'Event could not be published.');
          this.cdr.markForCheck();
        },
      });
  }

  private updateExistingEvent(): void {
    const regionId = this.activeRegionId;
    const eventId = this.editEventId;
    if (!regionId) {
      this.errorMessage = 'Assigned region is still loading. Please try again in a moment.';
      this.feedbackMessage = null;
      this.cdr.markForCheck();
      return;
    }

    if (!eventId) {
      this.errorMessage = 'Event id is missing. Reopen the event from the events list and try again.';
      this.feedbackMessage = null;
      this.cdr.markForCheck();
      return;
    }

    const payload = this.buildUpdatePayload();
    if (!payload) {
      this.feedbackMessage = null;
      this.cdr.markForCheck();
      return;
    }

    this.isSaving = true;
    this.errorMessage = null;
    this.feedbackMessage = 'Saving changes...';
    this.cdr.markForCheck();

    const newGalleryFiles = this.galleryImages
      .filter((img): img is GalleryImageItem & { file: File } => img.file != null)
      .map(img => img.file);
    const filesToUpload = [
      ...(this.cardImageFile ? [this.cardImageFile] : []),
      ...(this.coverImageFile ? [this.coverImageFile] : []),
      ...newGalleryFiles,
    ];
    const existingCardUrl = this.existingImages.find(image => image.isMain)?.url ?? this.existingImages[0]?.url ?? null;
    const existingCoverUrl =
      this.existingImages.find(image => !image.isMain && image.sortOrder === 1)?.url ??
      existingCardUrl;

    const hasImageChanges =
      filesToUpload.length > 0 ||
      this.cardImageUrl !== existingCardUrl ||
      this.coverImageUrl !== existingCoverUrl ||
      this.galleryImages.length !== this.existingGalleryImages.length ||
      this.galleryImages.some((image, index) => image.file == null && image.url !== this.existingGalleryImages[index]?.url);

    if (!hasImageChanges) {
      this.saveUpdate(regionId, eventId, payload);
      return;
    }

    if (filesToUpload.length === 0) {
      payload.images = this.buildImagesArray({});
      this.saveUpdate(regionId, eventId, payload);
      return;
    }

    this.createSubscription?.unsubscribe();
    this.createSubscription = this.eventsService.uploadImages(regionId, filesToUpload)
      .subscribe({
        next: uploadedUrls => {
          let index = 0;
          const uploadedCardUrl = this.cardImageFile ? uploadedUrls[index++] : undefined;
          const uploadedCoverUrl = this.coverImageFile ? uploadedUrls[index++] : undefined;
          const uploadedGalleryUrls = uploadedUrls.slice(index);
          payload.images = this.buildImagesArray({
            cardUrl: uploadedCardUrl,
            coverUrl: uploadedCoverUrl,
            galleryUrls: uploadedGalleryUrls,
          });
          this.saveUpdate(regionId, eventId, payload);
        },
        error: error => {
          this.isSaving = false;
          this.errorMessage = this.extractErrorMessage(error, 'Gallery images could not be uploaded.');
          this.cdr.markForCheck();
        },
      });
  }

  private buildImagesArray(uploaded: { cardUrl?: string; coverUrl?: string; galleryUrls?: string[] }): RegionalEventImageInputDto[] {
    const existingCardImage = this.existingImages.find(image => image.isMain) ?? this.existingImages[0] ?? null;
    const existingCoverImage = this.existingImages.find(image => !image.isMain && image.sortOrder === 1) ?? null;
    const cardUrl = uploaded.cardUrl ?? existingCardImage?.url ?? this.cardImageUrl;
    const coverUrl = uploaded.coverUrl ?? existingCoverImage?.url ?? null;
    const images: RegionalEventImageInputDto[] = [];

    if (cardUrl) {
      images.push({
        url: cardUrl,
        isMain: true,
        sortOrder: 0,
        altText: (existingCardImage?.altText ?? this.eventTitle.trim()) || null,
        caption: existingCardImage?.caption ?? null,
      });
    }

    if (coverUrl && coverUrl !== cardUrl) {
      images.push({
        url: coverUrl,
        isMain: false,
        sortOrder: 1,
        altText: (existingCoverImage?.altText ?? this.eventTitle.trim()) || null,
        caption: existingCoverImage?.caption ?? null,
      });
    }

    const existingGallery = this.galleryImages
      .filter(img => img.file == null)
      .map((img, i) => ({ url: img.url, isMain: false, sortOrder: 2 + i, altText: null, caption: null }));

    const newGallery = (uploaded.galleryUrls ?? []).map((url, i) => ({
      url,
      isMain: false,
      sortOrder: 2 + existingGallery.length + i,
      altText: null,
      caption: null,
    }));

    return [...images, ...existingGallery, ...newGallery];
  }

  private saveUpdate(regionId: number, eventId: string, payload: RegionalEventUpdateRequest): void {
    this.createSubscription?.unsubscribe();
    this.createSubscription = this.eventsService.updateEvent(regionId, eventId, payload)
      .pipe(finalize(() => {
        this.isSaving = false;
        this.cdr.markForCheck();
      }))
      .subscribe({
        next: updated => {
          try {
            this.eventsService.updateItem(String(updated.id), this.eventsService.toListItem(updated));
          } catch {
          }
          this.router.navigate(['/events/detail', updated.id], {
            queryParams: { regionId: updated.regionId },
            state: { regionId: updated.regionId, feedbackMessage: 'Event updated.' },
          });
        },
        error: error => {
          this.errorMessage = this.extractErrorMessage(error, 'Event could not be updated.');
          this.cdr.markForCheck();
        },
      });
  }

  private loadAssignedRegions(): void {
    this.regionLoading = true;
    const subscription = this.dashboardService.getAssignedRegions()
      .pipe(finalize(() => {
        this.regionLoading = false;
        this.cdr.markForCheck();
      }))
      .subscribe({
        next: regions => {
          this.assignedRegions = Array.isArray(regions) ? regions : [];
          this.activeRegionId = this.resolveInitialRegionId(this.assignedRegions);
          if (this.activeRegionId) {
            sessionStorage.setItem(NewEventComponent.activeRegionStorageKey, String(this.activeRegionId));
            this.loadAssignedRegionMap(this.activeRegionId);
            this.loadParentOptions();
            this.loadEventForEditIfNeeded();
          } else {
            this.errorMessage = 'No assigned region is available for this account.';
          }
          this.cdr.markForCheck();
        },
        error: error => {
          this.assignedRegions = [];
          this.activeRegionId = null;
          this.errorMessage = this.extractErrorMessage(error, 'Assigned regions could not be loaded.');
          this.cdr.markForCheck();
        },
      });
    this.subscriptions.push(subscription);
  }

  private loadEventForEditIfNeeded(): void {
    if (!this.isEditMode || !this.editEventId || !this.activeRegionId) return;

    const subscription = this.eventsService.getEvent(this.activeRegionId, this.editEventId).subscribe({
      next: dto => {
        this.eventTitle = dto.name ?? '';
        this.summary = dto.summary ?? '';
        this.eventTypeId = dto.eventTypeId ?? null;
        this.customVenueName = dto.venueName ?? '';
        this.allDay = dto.isAllDay ?? false;
        this.recurring = dto.isRecurring ?? false;
        this.freeEvent = dto.isFree ?? true;
        this.ticketPrice = dto.ticketPrice ?? '';
        this.externalTicketLink = dto.externalTicketLink ?? '';
        this.organizerName = dto.hostedBy ?? '';
        this.contactEmail = dto.contactEmail ?? '';
        this.attendanceLimit = dto.attendanceLimit != null ? String(dto.attendanceLimit) : '';
        this.ageRestriction = dto.ageRestriction ?? '';

        const parentType = (dto.parentEntityType ?? '').toLowerCase();
        if (parentType === 'place') {
          this.venueType = 'place';
          this.selectedPlaceId = dto.placeId ?? null;
          this.selectedLocationId = dto.locationId ?? null;
          this.selectedDestinationId = dto.destinationId ?? null;
        } else if (parentType === 'destination') {
          this.venueType = 'destination';
          this.selectedDestinationId = dto.destinationId ?? null;
        } else {
          this.venueType = 'location';
          this.selectedLocationId = dto.locationId ?? null;
          this.selectedDestinationId = dto.destinationId ?? null;
        }

        if (dto.startsAt) {
          const start = new Date(dto.startsAt);
          this.startDate = this.toDateIso(start);
          this.startTime = this.toTimeHHmm(start);
        }
        if (dto.endsAt) {
          const end = new Date(dto.endsAt);
          this.endDate = this.toDateIso(end);
          this.endTime = this.toTimeHHmm(end);
        }

        this.existingImages = [...(dto.images ?? [])].sort((a, b) => a.sortOrder - b.sortOrder);
        const cardImage = this.existingImages.find(image => image.isMain) ?? this.existingImages[0] ?? null;
        const coverImage = this.existingImages.find(image => !image.isMain && image.sortOrder === 1) ?? cardImage;
        const galleryUrls = this.existingGalleryImages.map(image => image.url);
        const fallbackGalleryUrls = (dto.galleryImageUrls ?? []).filter(url => !galleryUrls.includes(url));

        this.cardImageUrl = cardImage?.url ?? dto.imageUrl ?? null;
        this.cardImageName = this.cardImageUrl ? 'Existing card image' : '';
        this.coverImageUrl = coverImage?.url ?? dto.imageUrl ?? null;
        this.coverImageName = this.coverImageUrl ? 'Existing cover image' : '';
        this.galleryImages = [
          ...this.existingGalleryImages.map((image, index) => ({
            url: image.url,
            name: image.altText || image.caption || `Gallery image ${index + 1}`,
          })),
          ...fallbackGalleryUrls.map((url, index) => ({
            url,
            name: `Gallery image ${this.existingGalleryImages.length + index + 1}`,
          })),
        ];

        this.selectedTagIds = new Set((dto.tags ?? []).map(tag => tag.id));
        for (const tag of dto.tags ?? []) {
          this.knownTags.set(tag.id, tag);
        }
        this.classItems = this.classItems.map(item => ({
          ...item,
          checked: this.selectedTagIds.has(item.tag.id),
        }));

        setTimeout(() => {
          if (this.editorRef?.nativeElement) {
            this.editorRef.nativeElement.innerHTML = dto.description ?? '';
            this.syncEmptyState();
            this.cdr.markForCheck();
          }
        }, 0);

        this.loadParentOptions();
        this.cdr.markForCheck();
      },
      error: error => {
        this.errorMessage = this.extractErrorMessage(error, 'Unable to load event for editing.');
        this.cdr.markForCheck();
      },
    });
    this.subscriptions.push(subscription);
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
        this.refreshMiniRegionBoundaryLayer();
        this.updateMiniMapFromSelection();
        this.cdr.markForCheck();
      },
      error: () => {
        this.regionViewport = null;
        this.regionBoundaryZone = null;
        this.regionBoundaryMissingMessage = 'Assigned region boundary is missing. Please contact an administrator.';
        this.refreshMiniRegionBoundaryLayer();
        this.updateMiniMapFromSelection();
        this.cdr.markForCheck();
      },
    });
  }

  private loadEventTypes(): void {
    this.eventTypesLoading = true;
    const subscription = this.eventsService.getEventTypes()
      .pipe(finalize(() => {
        this.eventTypesLoading = false;
        this.cdr.markForCheck();
      }))
      .subscribe({
        next: eventTypes => {
          this.eventTypes = Array.isArray(eventTypes) ? eventTypes : [];
          if (!this.isEditMode) {
            this.eventTypeId = this.eventTypes[0]?.id ?? null;
          }
          this.cdr.markForCheck();
        },
        error: error => {
          this.eventTypes = [];
          this.errorMessage = this.extractErrorMessage(error, 'Event categories could not be loaded.');
          this.cdr.markForCheck();
        },
      });
    this.subscriptions.push(subscription);
  }

  private loadEventTags(): void {
    this.tagsLoading = true;
    const subscription = this.eventsService.getEventTags()
      .pipe(finalize(() => {
        this.tagsLoading = false;
        this.cdr.markForCheck();
      }))
      .subscribe({
        next: tags => {
          this.eventTags = this.normalizeTags(tags);
          this.eventTags.forEach(tag => this.knownTags.set(tag.id, tag));
          this.classItems = this.eventTags.map(tag => ({
            tag,
            checked: this.selectedTagIds.has(tag.id),
          }));
          this.cdr.markForCheck();
        },
        error: error => {
          this.eventTags = [];
          this.classItems = [];
          this.errorMessage = this.extractErrorMessage(error, 'Event tags could not be loaded.');
          this.cdr.markForCheck();
        },
      });
    this.subscriptions.push(subscription);
  }

  private loadParentOptions(): void {
    if (!this.activeRegionId) return;

    this.parentsLoading = true;
    this.parentSubscription?.unsubscribe();

    const request = this.venueType === 'place'
      ? this.eventsService.getParentPlaces(this.activeRegionId)
      : this.venueType === 'destination'
        ? this.eventsService.getParentDestinations(this.activeRegionId)
        : this.eventsService.getParentLocations(this.activeRegionId);

    this.parentSubscription = request
      .pipe(finalize(() => {
        this.parentsLoading = false;
        this.cdr.markForCheck();
      }))
      .subscribe({
        next: results => {
          if (this.venueType === 'place') {
            this.places = Array.isArray(results) ? results as RegionalEventParentPlaceDto[] : [];
          } else if (this.venueType === 'destination') {
            this.destinations = Array.isArray(results) ? results as RegionalEventParentDestinationDto[] : [];
          } else {
            this.locations = Array.isArray(results) ? results as RegionalEventParentLocationDto[] : [];
          }

          this.loadDestinationsForDerivation();
          this.updateMiniMapFromSelection();
          if (this.isEditMode && this.hasSelectedParent()) {
            this.validateSelectedParentLocation();
          }
          this.cdr.markForCheck();
        },
        error: error => {
          this.errorMessage = this.extractErrorMessage(error, 'Parent options could not be loaded.');
          this.cdr.markForCheck();
        },
      });
  }

  private loadDestinationsForDerivation(): void {
    if (!this.activeRegionId) return;
    const subscription = this.eventsService.getParentDestinations(this.activeRegionId)
      .subscribe({
        next: destinations => {
          this.destinations = Array.isArray(destinations) ? destinations : [];
          this.cdr.markForCheck();
        },
      });
    this.subscriptions.push(subscription);
  }

  private searchTags(query: string): void {
    this.tagSearchLoading = true;
    this.tagSearchSubscription?.unsubscribe();
    this.tagSearchSubscription = this.eventsService.getEventTags(query)
      .pipe(finalize(() => {
        this.tagSearchLoading = false;
        this.cdr.markForCheck();
      }))
      .subscribe({
        next: tags => {
          this.additionalTagResults = this.normalizeTags(tags)
            .filter(tag => !this.selectedTagIds.has(tag.id));
          this.additionalTagResults.forEach(tag => this.knownTags.set(tag.id, tag));
          this.cdr.markForCheck();
        },
        error: error => {
          this.additionalTagResults = [];
          this.errorMessage = this.extractErrorMessage(error, 'Additional tags could not be searched.');
          this.cdr.markForCheck();
        },
      });
  }

  private findTagToAdd(): RegionalEventTagDto | null {
    const query = this.tagInput.trim().toLocaleLowerCase();
    const exactMatch = this.eventTags.find(
      tag => tag.name.trim().toLocaleLowerCase() === query && !this.selectedTagIds.has(tag.id),
    );
    return exactMatch ?? null;
  }

  private upsertEventTag(tag: RegionalEventTagDto): void {
    if (!this.eventTags.some(existing => existing.id === tag.id)) {
      this.eventTags = [...this.eventTags, tag].sort((a, b) => a.name.localeCompare(b.name));
    }

    const existingItem = this.classItems.find(item => item.tag.id === tag.id);
    if (existingItem) {
      existingItem.checked = true;
      return;
    }

    this.classItems = [...this.classItems, { tag, checked: true }]
      .sort((a, b) => a.tag.name.localeCompare(b.tag.name));
  }

  private normalizeTags(tags: RegionalEventTagDto[] | null | undefined): RegionalEventTagDto[] {
    const seen = new Set<number>();
    return (tags ?? [])
      .filter(tag => {
        if (!tag || seen.has(tag.id)) return false;
        seen.add(tag.id);
        return true;
      })
      .sort((a, b) => a.name.localeCompare(b.name));
  }

  private selectedTagIdList(): number[] {
    return [...new Set(this.selectedTagIds)].sort((a, b) => a - b);
  }

  private buildPayload(): RegionalEventCreateRequest | null {
    this.errorMessage = null;

    if (!this.activeRegionId) {
      this.errorMessage = 'Select an assigned region before publishing.';
      return null;
    }

    const name = this.eventTitle.trim();
    if (!name) {
      this.errorMessage = 'Event title is required.';
      return null;
    }

    const summary = this.summary.trim();
    if (!summary) {
      this.errorMessage = 'Short excerpt/summary is required.';
      return null;
    }

    if (summary.length > 160) {
      this.errorMessage = 'Short excerpt/summary must be 160 characters or fewer.';
      return null;
    }

    if (!this.eventTypeId) {
      this.errorMessage = 'Event category/type is required.';
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

    if (!this.hasSelectedParent() || !this.parentDestination) {
      this.errorMessage = 'Select a valid event parent in the active region.';
      return null;
    }

    if (this.isScopeValidationRejected) {
      this.errorMessage = this.scopeValidationMessage ?? 'Selected event parent is outside your allowed regional scope.';
      return null;
    }

    if (this.scopeValidationPending || this.scopeValidationResult?.isAllowed !== true) {
      this.errorMessage = this.scopeValidationPending
        ? 'Event parent validation is still running.'
        : this.scopeValidationMessage ?? 'Selected event parent could not be validated against your assigned regional scope.';
      return null;
    }

    const startsAt = this.combineDateTime(this.startDate, this.allDay ? '00:00' : this.startTime);
    const endsAt = this.combineDateTime(this.endDate, this.allDay ? '00:00' : this.endTime);
    if (!startsAt || !endsAt) {
      this.errorMessage = 'Start and end date/time are required.';
      return null;
    }

    if (!this.allDay && new Date(endsAt).getTime() <= new Date(startsAt).getTime()) {
      this.errorMessage = 'End time must be after start time.';
      return null;
    }

    if (this.allDay && new Date(this.endDate).getTime() < new Date(this.startDate).getTime()) {
      this.errorMessage = 'End date must be after start date.';
      return null;
    }

    if (!this.freeEvent && this.ticketPrice.trim()) {
      const price = Number(this.ticketPrice.trim().replace(/[$,]/g, ''));
      if (Number.isFinite(price) && price < 0) {
        this.errorMessage = 'Ticket price must be non-negative.';
        return null;
      }
    }

    const externalTicketLink = this.externalTicketLink.trim();
    if (externalTicketLink && !this.isSafeHttpUrl(externalTicketLink)) {
      this.errorMessage = 'External ticket link must be a valid http or https URL.';
      return null;
    }

    const contactEmail = this.contactEmail.trim();
    if (contactEmail && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(contactEmail)) {
      this.errorMessage = 'Contact email is invalid.';
      return null;
    }

    const attendanceLimitStr = String(this.attendanceLimit ?? '').trim();
    const attendanceLimit = attendanceLimitStr ? Number(attendanceLimitStr) : null;
    if (attendanceLimit !== null && (!Number.isInteger(attendanceLimit) || attendanceLimit <= 0)) {
      this.errorMessage = 'Attendance limit must be a positive whole number.';
      return null;
    }

    return {
      regionId: this.activeRegionId,
      parentEntityType: this.toBackendParentType(this.venueType),
      destinationId: this.parentDestination.id,
      locationId: this.venueType === 'location'
        ? this.selectedLocationId
        : this.venueType === 'place'
          ? this.selectedPlace?.locationId ?? null
          : null,
      placeId: this.venueType === 'place' ? this.selectedPlaceId : null,
      eventTypeId: this.eventTypeId,
      name,
      summary,
      description: this.editorRef?.nativeElement?.innerHTML?.trim() || null,
      status: 'Published',
      venueName: this.customVenueName.trim() || null,
      isAllDay: this.allDay,
      startsAt,
      endsAt,
      isRecurring: this.recurring,
      isFree: this.freeEvent,
      ticketPrice: this.freeEvent ? null : this.ticketPrice.trim() || null,
      externalTicketLink: externalTicketLink || null,
      organizerName: this.organizerName.trim() || null,
      contactEmail: contactEmail || null,
      attendanceLimit,
      ageRestriction: this.ageRestriction.trim() || null,
      tagIds: this.selectedTagIdList(),
      cardImageFile: this.cardImageFile,
      coverImageFile: this.coverImageFile,
      galleryImageFiles: this.galleryImages
        .filter((image): image is GalleryImageItem & { file: File } => image.file != null)
        .map(image => image.file),
    };
  }

  private resolveInitialRegionId(regions: RegionalAssignedRegion[]): number | null {
    if (regions.length === 0) return null;

    const queryRegion = Number(this.route.snapshot.queryParamMap.get('regionId'));
    if (regions.some(region => region.id === queryRegion)) return queryRegion;

    const state = history.state as { regionId?: number; activeRegionId?: number } | null;
    const stateRegion = Number(state?.regionId ?? state?.activeRegionId);
    if (regions.some(region => region.id === stateRegion)) return stateRegion;

    const storedRegion = Number(sessionStorage.getItem(NewEventComponent.activeRegionStorageKey));
    if (regions.some(region => region.id === storedRegion)) return storedRegion;

    return regions[0].id;
  }

  private resetParentSelection(): void {
    this.selectedDestinationId = null;
    this.selectedLocationId = null;
    this.selectedPlaceId = null;
    this.clearScopeValidation();
    this.updateMiniMapFromSelection();
  }

  private hasSelectedParent(): boolean {
    if (this.venueType === 'place') return Boolean(this.selectedPlaceId);
    if (this.venueType === 'destination') return Boolean(this.selectedDestinationId);
    return Boolean(this.selectedLocationId);
  }

  private get existingGalleryImages(): RegionalEventImageDto[] {
    return this.existingImages.filter(image => this.isGalleryImage(image));
  }

  private isGalleryImage(image: RegionalEventImageDto): boolean {
    return !image.isMain && image.sortOrder >= 2;
  }

  private toBackendParentType(parentType: EventParentType): 'Destination' | 'Location' | 'Place' {
    if (parentType === 'place') return 'Place';
    if (parentType === 'destination') return 'Destination';
    return 'Location';
  }

  private combineDateTime(date: string, time: string): string | null {
    if (!date || !time) return null;
    const value = new Date(`${date}T${time}:00`);
    return Number.isNaN(value.getTime()) ? null : value.toISOString();
  }

  private todayIso(): string {
    const now = new Date();
    return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
  }

  private isSafeHttpUrl(value: string): boolean {
    try {
      const url = new URL(value);
      return (url.protocol === 'http:' || url.protocol === 'https:') && !url.username && !url.password;
    } catch {
      return false;
    }
  }

  private validateImageFile(file: File): boolean {
    const allowedTypes = new Set(['image/png', 'image/jpeg', 'image/webp']);
    if (!allowedTypes.has(file.type)) {
      this.errorMessage = 'Images must be PNG, JPG, or WEBP.';
      this.cdr.markForCheck();
      return false;
    }

    if (file.size > 8 * 1024 * 1024) {
      this.errorMessage = 'Images must be 8 MB or smaller.';
      this.cdr.markForCheck();
      return false;
    }

    return true;
  }

  private syncEmptyState(): void {
    const el = this.editorRef?.nativeElement;
    if (!el) return;
    const text = el.innerText ?? el.textContent ?? '';
    this.editorIsEmpty = text.trim() === '' && el.innerHTML !== '<img' && !el.querySelector('img');
  }

  private syncToolbarState(): void {
    this.isBold = document.queryCommandState('bold');
    this.isItalic = document.queryCommandState('italic');
    this.isUnderline = document.queryCommandState('underline');
  }

  private initMiniMap(): void {
    if (this.miniMap || typeof L === 'undefined') return;
    const el = this.miniMapContainer?.nativeElement;
    if (!el) return;

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
    this.updateMiniMapFromSelection();
  }

  private refreshMiniRegionBoundaryLayer(): void {
    this.miniRegionBoundaryLayer = renderAssignedRegionBoundary(
      typeof L === 'undefined' ? null : L,
      this.miniMap,
      this.miniRegionBoundaryLayer,
      this.regionBoundaryZone,
    );
  }

  private updateMiniMapFromSelection(): void {
    if (!this.miniMap || typeof L === 'undefined') return;

    if (this.miniMarker) {
      this.miniMap.removeLayer(this.miniMarker);
      this.miniMarker = null;
    }

    const latitude = this.selectedParentLatitude;
    const longitude = this.selectedParentLongitude;
    if (latitude === null || longitude === null) {
      applyAssignedRegionViewport(this.miniMap, this.regionViewport, 7, 11);
      return;
    }

    this.miniMarker = L.marker([latitude, longitude], {
      draggable: false,
      icon: createRegionalMapPinIcon(L),
    }).addTo(this.miniMap);
    this.miniMap.setView([latitude, longitude], this.venueType === 'destination' ? 10 : 13);
  }

  private validateSelectedParentLocation(): void {
    const latitude = this.selectedParentLatitude;
    const longitude = this.selectedParentLongitude;
    if (!this.activeRegionId || latitude === null || longitude === null) {
      this.clearScopeValidation();
      return;
    }

    this.validationSubscription?.unsubscribe();
    this.scopeValidationResult = null;
    this.scopeValidationTone = null;
    this.scopeValidationMessage = null;
    this.scopeValidationPending = true;
    this.cdr.markForCheck();
    this.validationSubscription = this.adminMapService.validateEntityLocation({
      regionId: this.activeRegionId,
      entityType: 'Event',
      operationType: this.isEditMode ? 'Update' : 'Create',
      latitude,
      longitude,
      destinationId: this.parentDestination?.id ?? null,
      locationId: this.venueType === 'location'
        ? this.selectedLocationId
        : this.venueType === 'place'
          ? this.selectedPlace?.locationId ?? null
          : null,
      placeId: this.venueType === 'place' ? this.selectedPlaceId : null,
      parentEntityType: this.toBackendParentType(this.venueType),
    }).subscribe({
      next: result => {
        this.scopeValidationPending = false;
        this.applyScopeValidationResult(result);
        this.cdr.markForCheck();
      },
      error: error => {
        this.scopeValidationPending = false;
        this.scopeValidationResult = null;
        this.scopeValidationTone = 'error';
        this.scopeValidationMessage = this.extractErrorMessage(
          error,
          'Unable to validate this event parent against your assigned region.',
        );
        this.cdr.markForCheck();
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
      this.scopeValidationMessage = result.message || 'This event parent is outside your assigned region.';
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

  private buildUpdatePayload(): RegionalEventUpdateRequest | null {
    this.errorMessage = null;

    const name = this.eventTitle.trim();
    if (!name) {
      this.errorMessage = 'Event title is required.';
      return null;
    }

    const summary = this.summary.trim();
    if (!summary) {
      this.errorMessage = 'Short excerpt/summary is required.';
      return null;
    }

    if (summary.length > 160) {
      this.errorMessage = 'Short excerpt/summary must be 160 characters or fewer.';
      return null;
    }

    if (!this.eventTypeId) {
      this.errorMessage = 'Event category/type is required.';
      return null;
    }

    if (!this.cardImageUrl) {
      this.errorMessage = 'Card image is required.';
      return null;
    }

    if (!this.coverImageUrl) {
      this.errorMessage = 'Cover image is required.';
      return null;
    }

    if (!this.hasSelectedParent()) {
      this.errorMessage = 'Select a valid event parent in the active region.';
      return null;
    }

    // Derive destinationId without requiring parentDestination to be fully resolved (lists may still be loading).
    const destinationId =
      this.parentDestination?.id ??
      this.selectedDestinationId ??
      this.selectedLocation?.destinationId ??
      this.selectedPlace?.destinationId ??
      null;

    if (!destinationId) {
      this.errorMessage = 'Could not determine destination for the selected parent. Please re-select.';
      return null;
    }

    if (this.isScopeValidationRejected) {
      this.errorMessage = this.scopeValidationMessage ?? 'Selected event parent is outside your allowed regional scope.';
      return null;
    }

    if (this.scopeValidationPending) {
      this.errorMessage = 'Event parent validation is still running.';
      return null;
    }

    if (this.scopeValidationResult?.isAllowed === false) {
      this.errorMessage = this.scopeValidationMessage ?? 'Selected event parent is outside your allowed regional scope.';
      return null;
    }

    const startsAt = this.combineDateTime(this.startDate, this.allDay ? '00:00' : this.startTime);
    const endsAt = this.combineDateTime(this.endDate, this.allDay ? '00:00' : this.endTime);
    if (!startsAt || !endsAt) {
      this.errorMessage = 'Start and end date/time are required.';
      return null;
    }

    if (!this.allDay && new Date(endsAt).getTime() <= new Date(startsAt).getTime()) {
      this.errorMessage = 'End time must be after start time.';
      return null;
    }

    if (this.allDay && new Date(this.endDate).getTime() < new Date(this.startDate).getTime()) {
      this.errorMessage = 'End date must be after start date.';
      return null;
    }

    const externalTicketLink = this.externalTicketLink.trim();
    if (externalTicketLink && !this.isSafeHttpUrl(externalTicketLink)) {
      this.errorMessage = 'External ticket link must be a valid http or https URL.';
      return null;
    }

    const contactEmail = this.contactEmail.trim();
    if (contactEmail && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(contactEmail)) {
      this.errorMessage = 'Contact email is invalid.';
      return null;
    }

    const attendanceLimitStr = String(this.attendanceLimit ?? '').trim();
    const attendanceLimit = attendanceLimitStr ? Number(attendanceLimitStr) : null;
    if (attendanceLimit !== null && (!Number.isInteger(attendanceLimit) || attendanceLimit <= 0)) {
      this.errorMessage = 'Attendance limit must be a positive whole number.';
      return null;
    }

    return {
      parentEntityType: this.toBackendParentType(this.venueType),
      destinationId,
      locationId: this.venueType === 'location'
        ? this.selectedLocationId
        : this.venueType === 'place'
          ? this.selectedPlace?.locationId ?? null
          : null,
      placeId: this.venueType === 'place' ? this.selectedPlaceId : null,
      eventTypeId: this.eventTypeId,
      name,
      summary,
      description: this.editorRef?.nativeElement?.innerHTML?.trim() ?? '',
      venueName: this.customVenueName.trim(),
      isAllDay: this.allDay,
      startsAt,
      endsAt,
      isRecurring: this.recurring,
      isFree: this.freeEvent,
      ticketPrice: this.freeEvent ? '' : this.ticketPrice.trim(),
      externalTicketLink,
      organizerName: this.organizerName.trim(),
      contactEmail,
      attendanceLimit,
      clearAttendanceLimit: attendanceLimit === null,
      ageRestriction: this.ageRestriction.trim(),
      tagIds: this.selectedTagIdList(),
    };
  }

  private toDateIso(date: Date): string {
    const y = date.getFullYear();
    const m = String(date.getMonth() + 1).padStart(2, '0');
    const d = String(date.getDate()).padStart(2, '0');
    return `${y}-${m}-${d}`;
  }

  private toTimeHHmm(date: Date): string {
    return `${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`;
  }

  private extractErrorMessage(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse) {
      const message = typeof error.error?.message === 'string' ? error.error.message : null;
      return message || fallback;
    }

    return fallback;
  }
}
