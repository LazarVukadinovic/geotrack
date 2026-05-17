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
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router } from '@angular/router';
import { finalize, of, Subscription, switchMap } from 'rxjs';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { DashboardService } from '../../../../core/services/dashboard.service';
import { AuthService } from '../../../../core/services/auth.service';
import { LightboxService } from '../../../../core/services/lightbox.service';
import { AdminMapService } from '../../../../core/services/admin-map.service';
import { RegionalMediaService } from '../../../../core/services/regional-media.service';
import { RegionalAssignedRegion } from '../../../../core/models/dashboard.model';
import { AdminMapViewport } from '../../../../core/models/admin-map.model';
import {
  RegionalEventCreateRequest,
  RegionalEventDetailDto,
  RegionalEventParentDestinationDto,
  RegionalEventParentLocationDto,
  RegionalEventParentPlaceDto,
  RegionalEventTagDto,
  RegionalEventTypeDto,
  RegionalEventUpdateRequest,
} from '../../../../core/models/event.model';
import { EventsListService } from '../../../../core/services/events-list.service';
import {
  RegionalEntityImageDto,
  RegionalEntityImageInput,
  RegionalImageUploadResult,
} from '../../../../core/models/regional-image.model';

declare const L: any;

type EventParentType = 'location' | 'place' | 'destination';

interface ClassItem {
  tag: RegionalEventTagDto;
  checked: boolean;
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
  private static readonly expandedVisibleClassificationTags = 10;

  @ViewChild('editorRef') editorRef!: ElementRef<HTMLDivElement>;
  @ViewChild('miniMapContainer') miniMapContainer!: ElementRef<HTMLDivElement>;

  assignedRegions: RegionalAssignedRegion[] = [];
  activeRegionId: number | null = null;
  regionLoading = false;
  parentsLoading = false;
  eventTypesLoading = false;
  tagsLoading = false;
  tagSearchLoading = false;
  detailLoading = false;
  isSaving = false;
  errorMessage: string | null = null;
  feedbackMessage: string | null = null;

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
  internalReferenceId = '';
  eventStatus: 'Published' | 'Draft' | 'Archived' = 'Published';
  isEditMode = false;

  isBold = false;
  isItalic = false;
  isUnderline = false;
  editorIsEmpty = true;

  coverImageUrl: string | null = null;
  coverImageName = '';
  coverImageFile: File | null = null;
  galleryImages: { url: string; name: string; file?: File }[] = [];

  destinations: RegionalEventParentDestinationDto[] = [];
  locations: RegionalEventParentLocationDto[] = [];
  places: RegionalEventParentPlaceDto[] = [];
  eventTypes: RegionalEventTypeDto[] = [];
  eventTags: RegionalEventTagDto[] = [];
  classItems: ClassItem[] = [];
  selectedTagIds = new Set<number>();
  additionalTagResults: RegionalEventTagDto[] = [];
  tagInput = '';
  classificationExpanded = false;

  private miniMap: any = null;
  private miniMarker: any = null;
  private regionViewport: AdminMapViewport | null = null;
  private subscriptions: Subscription[] = [];
  private parentSubscription: Subscription | null = null;
  private tagSearchSubscription: Subscription | null = null;
  private createSubscription: Subscription | null = null;
  private detailSubscription: Subscription | null = null;
  private tagSearchHandle: ReturnType<typeof setTimeout> | null = null;
  private readonly knownTags = new Map<number, RegionalEventTagDto>();
  private editEventId: string | null = null;
  private pendingDescriptionHtml: string | null = null;
  private imagesTouched = false;

  constructor(
    private readonly zone: NgZone,
    private readonly cdr: ChangeDetectorRef,
    private readonly router: Router,
    private readonly route: ActivatedRoute,
    private readonly dashboardService: DashboardService,
    private readonly auth: AuthService,
    private readonly lightboxService: LightboxService,
    private readonly adminMapService: AdminMapService,
    private readonly eventsService: EventsListService,
    private readonly regionalMediaService: RegionalMediaService,
  ) {}

  ngOnInit(): void {
    this.editEventId = this.route.snapshot.paramMap.get('id');
    this.isEditMode = Boolean(this.editEventId);
    this.loadAssignedRegions();
    this.loadEventTypes();
    this.loadEventTags();
  }

  ngAfterViewInit(): void {
    setTimeout(() => {
      this.initMiniMap();
      this.applyPendingDescription();
    }, 0);
  }

  ngOnDestroy(): void {
    this.subscriptions.forEach(subscription => subscription.unsubscribe());
    this.parentSubscription?.unsubscribe();
    this.tagSearchSubscription?.unsubscribe();
    this.createSubscription?.unsubscribe();
    this.detailSubscription?.unsubscribe();
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
    const limit = this.classificationExpanded
      ? NewEventComponent.expandedVisibleClassificationTags
      : NewEventComponent.initialVisibleClassificationTags;
    return this.classItems.slice(0, limit);
  }

  get canToggleClassification(): boolean {
    return this.classItems.length > NewEventComponent.initialVisibleClassificationTags;
  }

  get selectedTags(): RegionalEventTagDto[] {
    return [...this.selectedTagIds]
      .map(tagId => this.knownTags.get(tagId))
      .filter((tag): tag is RegionalEventTagDto => Boolean(tag));
  }

  get canPublish(): boolean {
    return !this.isSaving &&
      !this.detailLoading &&
      Boolean(this.activeRegionId) &&
      Boolean(this.eventTitle.trim()) &&
      Boolean(this.summary.trim()) &&
      Boolean(this.eventTypeId) &&
      Boolean(this.parentDestination) &&
      this.hasSelectedParent();
  }

  private get selectedParentGeo(): { latitude?: number | null; longitude?: number | null } | null {
    if (this.venueType === 'place') return this.selectedPlace;
    if (this.venueType === 'destination') return this.selectedDestination;
    return this.selectedLocation;
  }

  onRegionSelected(regionId: number): void {
    if (regionId === this.activeRegionId) return;
    if (this.isEditMode) {
      this.feedbackMessage = 'The region is locked while editing this event.';
      this.cdr.markForCheck();
      return;
    }
    this.activeRegionId = regionId;
    sessionStorage.setItem(NewEventComponent.activeRegionStorageKey, String(regionId));
    this.loadRegionViewport(regionId);
    this.resetParentSelection();
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
        this.imagesTouched = true;
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
    this.imagesTouched = true;
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
          this.imagesTouched = true;
          this.cdr.markForCheck();
        });
      };
      reader.readAsDataURL(file);
    }
    if (input) input.value = '';
  }

  removeGalleryImage(index: number): void {
    this.galleryImages = this.galleryImages.filter((_, currentIndex) => currentIndex !== index);
    this.imagesTouched = true;
    this.cdr.markForCheck();
  }

  openLightbox(images: { url: string; name: string }[], index: number): void {
    this.lightboxService.open(images, index);
  }

  onClassItemChange(item: ClassItem): void {
    if (item.checked) {
      this.selectedTagIds.add(item.tag.id);
    } else {
      this.selectedTagIds.delete(item.tag.id);
    }
  }

  removeSelectedTag(tagId: number): void {
    this.selectedTagIds.delete(tagId);
    this.classItems = this.classItems.map(item =>
      item.tag.id === tagId ? { ...item, checked: false } : item);
    this.cdr.markForCheck();
  }

  toggleAdditionalTag(tag: RegionalEventTagDto): void {
    this.knownTags.set(tag.id, tag);
    if (this.selectedTagIds.has(tag.id)) {
      this.selectedTagIds.delete(tag.id);
    } else {
      this.selectedTagIds.add(tag.id);
    }
    this.classItems = this.classItems.map(item =>
      item.tag.id === tag.id ? { ...item, checked: this.selectedTagIds.has(tag.id) } : item);
    this.cdr.markForCheck();
  }

  onTagSearchInput(): void {
    if (this.tagSearchHandle) clearTimeout(this.tagSearchHandle);
    const query = this.tagInput.trim();
    if (query.length < 2) {
      this.additionalTagResults = [];
      this.cdr.markForCheck();
      return;
    }

    this.tagSearchHandle = setTimeout(() => this.searchTags(query), 250);
  }

  toggleClassificationExpanded(): void {
    this.classificationExpanded = !this.classificationExpanded;
  }

  publish(): void {
    const payload = this.buildPayload();
    if (!payload) {
      this.cdr.markForCheck();
      return;
    }

    this.isSaving = true;
    this.errorMessage = null;
    this.feedbackMessage = null;
    this.createSubscription?.unsubscribe();
    const saveRegionId = this.activeRegionId;
    const saveEventId = this.editEventId;
    this.createSubscription = of(payload)
      .pipe(
        switchMap(payloadWithoutImages => {
          const files = this.selectedImageFiles();
          if (files.length === 0) {
            const request = this.shouldSendImages(files)
              ? { ...payloadWithoutImages, images: this.buildImageInputs([]) }
              : payloadWithoutImages;
            return this.isEditMode && saveRegionId && saveEventId
              ? this.eventsService.updateEvent(saveRegionId, saveEventId, request)
              : this.eventsService.createEvent(request as RegionalEventCreateRequest);
          }

          return this.regionalMediaService.uploadImages(this.activeRegionId!, files).pipe(
            switchMap(uploaded => {
              const request = {
                ...payloadWithoutImages,
                images: this.buildImageInputs(uploaded.images),
              };
              return this.isEditMode && saveRegionId && saveEventId
                ? this.eventsService.updateEvent(saveRegionId, saveEventId, request)
                : this.eventsService.createEvent(request as RegionalEventCreateRequest);
            }),
          );
        }),
        finalize(() => {
          this.isSaving = false;
          this.cdr.markForCheck();
        }),
      )
      .subscribe({
        next: saved => {
          const message = this.isEditMode ? 'Event saved.' : 'Event published.';
          this.feedbackMessage = message;
          this.router.navigate(['/events'], {
            queryParams: { regionId: saved.regionId },
            state: { feedbackMessage: message },
          });
        },
        error: error => {
          this.errorMessage = this.extractErrorMessage(
            error,
            this.isEditMode ? 'Event could not be saved.' : 'Event could not be published.',
          );
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
            this.loadRegionViewport(this.activeRegionId);
            this.loadParentOptions();
            if (this.isEditMode) {
              this.loadEventForEdit(this.activeRegionId);
            }
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
          if (!this.eventTypeId) {
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
          this.eventTags = Array.isArray(tags) ? tags : [];
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
          this.additionalTagResults = (Array.isArray(tags) ? tags : [])
            .filter(tag => !this.eventTags.some(existing => existing.id === tag.id));
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

  private buildPayload(): (RegionalEventCreateRequest | RegionalEventUpdateRequest) | null {
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

    if (!this.hasSelectedParent() || !this.parentDestination) {
      this.errorMessage = 'Select a valid event parent in the active region.';
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

    const attendanceLimit = this.attendanceLimit.trim()
      ? Number(this.attendanceLimit.trim())
      : null;
    if (attendanceLimit !== null && (!Number.isInteger(attendanceLimit) || attendanceLimit <= 0)) {
      this.errorMessage = 'Attendance limit must be a positive whole number.';
      return null;
    }

    return {
      ...(this.isEditMode ? {} : { regionId: this.activeRegionId }),
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
      status: this.isEditMode ? this.eventStatus : 'Published',
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
      tagIds: [...this.selectedTagIds],
    };
  }

  private loadEventForEdit(regionId: number): void {
    const eventId = this.editEventId;
    if (!eventId) return;

    this.detailLoading = true;
    this.errorMessage = null;
    this.cdr.markForCheck();

    this.detailSubscription?.unsubscribe();
    this.detailSubscription = this.eventsService
      .getEvent(regionId, eventId)
      .pipe(finalize(() => {
        this.detailLoading = false;
        this.cdr.markForCheck();
      }))
      .subscribe({
        next: event => this.applyEventDetail(event),
        error: error => {
          this.errorMessage = this.extractErrorMessage(error, 'Event details could not be loaded.');
          this.cdr.markForCheck();
        },
      });
  }

  private applyEventDetail(event: RegionalEventDetailDto): void {
    this.eventTitle = event.name ?? '';
    this.summary = event.summary ?? '';
    this.eventTypeId = event.eventTypeId ?? null;
    this.customVenueName = event.venueName ?? '';
    this.allDay = Boolean(event.isAllDay);
    this.recurring = Boolean(event.isRecurring);
    this.freeEvent = event.isFree ?? true;
    this.ticketPrice = event.ticketPrice ?? '';
    this.externalTicketLink = event.externalTicketLink ?? '';
    this.organizerName = event.hostedBy ?? '';
    this.contactEmail = event.contactEmail ?? '';
    this.attendanceLimit = event.attendanceLimit ? String(event.attendanceLimit) : '';
    this.ageRestriction = event.ageRestriction ?? '';
    this.internalReferenceId = event.internalReferenceId ?? '';
    this.eventStatus = this.normalizeEditableStatus(event.status);
    this.venueType = this.normalizeParentType(event.parentEntityType);
    this.selectedDestinationId = event.destinationId ?? null;
    this.selectedLocationId = event.locationId ?? null;
    this.selectedPlaceId = event.placeId ?? null;
    this.applyDateTimeRange(event.startsAt, event.endsAt, this.allDay);

    this.selectedTagIds = new Set((event.tags ?? []).map(tag => tag.id));
    (event.tags ?? []).forEach(tag => this.knownTags.set(tag.id, tag));
    this.classItems = this.eventTags.map(tag => ({
      tag,
      checked: this.selectedTagIds.has(tag.id),
    }));
    this.applyEntityImages(event.images ?? [], event.imageUrl ?? null, event.name);
    this.setDescriptionHtml(event.description ?? '');
    this.loadParentOptions();
    this.updateMiniMapFromSelection();
    this.cdr.markForCheck();
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
    this.updateMiniMapFromSelection();
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
    const fallbackName = this.eventTitle.trim() || 'Event image';

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

  private applyDateTimeRange(startsAt?: string | null, endsAt?: string | null, isAllDay = false): void {
    const start = startsAt ? new Date(startsAt) : null;
    const end = endsAt ? new Date(endsAt) : null;
    if (start && !Number.isNaN(start.getTime())) {
      this.startDate = this.toDateInput(start);
      this.startTime = this.toTimeInput(start);
    }
    if (end && !Number.isNaN(end.getTime())) {
      this.endDate = this.toDateInput(end);
      this.endTime = isAllDay ? this.endTime : this.toTimeInput(end);
    }
  }

  private toDateInput(date: Date): string {
    return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
  }

  private toTimeInput(date: Date): string {
    return `${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`;
  }

  private normalizeParentType(value?: string | null): EventParentType {
    const normalized = (value ?? '').toLowerCase();
    if (normalized === 'place') return 'place';
    if (normalized === 'destination') return 'destination';
    return 'location';
  }

  private normalizeEditableStatus(value?: string | null): 'Published' | 'Draft' | 'Archived' {
    const normalized = (value ?? '').toLowerCase();
    if (normalized === 'draft') return 'Draft';
    if (normalized === 'archived') return 'Archived';
    return 'Published';
  }

  private hasSelectedParent(): boolean {
    if (this.venueType === 'place') return Boolean(this.selectedPlaceId);
    if (this.venueType === 'destination') return Boolean(this.selectedDestinationId);
    return Boolean(this.selectedLocationId);
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
    L.tileLayer('https://tiles.stadiamaps.com/tiles/alidade_smooth/{z}/{x}/{y}{r}.png', { maxZoom: 19 }).addTo(this.miniMap);
    this.updateMiniMapFromSelection();
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
      this.applyRegionViewportToMiniMap();
      return;
    }

    this.miniMarker = L.marker([latitude, longitude], { draggable: false }).addTo(this.miniMap);
    this.miniMap.setView([latitude, longitude], this.miniMap.getZoom(), { animate: false });
  }

  private loadRegionViewport(regionId: number): void {
    const subscription = this.adminMapService.getViewport(regionId)
      .subscribe({
        next: viewport => {
          this.regionViewport = viewport;
          if (this.selectedParentLatitude === null || this.selectedParentLongitude === null) {
            this.applyRegionViewportToMiniMap();
          }
          this.cdr.markForCheck();
        },
        error: () => {
          this.regionViewport = null;
          this.applyRegionViewportToMiniMap();
          this.cdr.markForCheck();
        },
      });
    this.subscriptions.push(subscription);
  }

  private applyRegionViewportToMiniMap(): void {
    if (!this.miniMap || typeof L === 'undefined') return;

    const viewport = this.regionViewport;
    try {
      if (viewport?.bounds) {
        this.miniMap.fitBounds(
          [
            [viewport.bounds.south, viewport.bounds.west],
            [viewport.bounds.north, viewport.bounds.east],
          ],
          { padding: [10, 10], animate: false },
        );
        return;
      }

      if (
        typeof viewport?.centerLatitude === 'number' &&
        typeof viewport.centerLongitude === 'number' &&
        typeof viewport.zoom === 'number'
      ) {
        this.miniMap.setView([viewport.centerLatitude, viewport.centerLongitude], viewport.zoom, { animate: false });
        return;
      }

      this.miniMap.fitWorld({ animate: false });
    } catch {
      // Leaflet can throw while hidden; the next render/update will retry.
    }
  }

  private extractErrorMessage(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse) {
      const message = typeof error.error?.message === 'string' ? error.error.message : null;
      return message || fallback;
    }

    return fallback;
  }
}
