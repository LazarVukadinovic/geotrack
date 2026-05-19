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
import { finalize, Subscription } from 'rxjs';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { DashboardService } from '../../../../core/services/dashboard.service';
import { LightboxService } from '../../../../core/services/lightbox.service';
import { DestinationsListService } from '../../../../core/services/destinations-list.service';
import { AuthService } from '../../../../core/services/auth.service';
import { RegionalAssignedRegion } from '../../../../core/models/dashboard.model';
import {
  RegionalDestinationCreateRequest,
  RegionalDestinationTagDto,
  RegionalDestinationUpdateRequest,
} from '../../../../core/models/destination.model';

declare const L: any;

interface ClassItem {
  tag: RegionalDestinationTagDto;
  checked: boolean;
}

@Component({
  selector: 'app-add-destination',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
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
  private static readonly expandedVisibleClassificationTags = 10;

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

  destinationName = '';
  summary = '';
  tagInput = '';

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
  galleryImages: { url: string; name: string; file: File }[] = [];

  private cardImageFile: File | null = null;
  private coverImageFile: File | null = null;

  zoneModalOpen = false;
  zoneConfirmed = false;
  centerLat: number | null = null;
  centerLng: number | null = null;

  private miniMap: any = null;
  private modalMap: any = null;
  private miniMapCenterMarker: any = null;
  private modalCenterMarker: any = null;
  private assignedRegionsSubscription: Subscription | null = null;
  private tagsSubscription: Subscription | null = null;
  private tagSearchSubscription: Subscription | null = null;
  private createSubscription: Subscription | null = null;
  private editDestinationId: number | null = null;
  private originalLatitude: number | null = null;
  private originalLongitude: number | null = null;
  private tagSearchHandle: ReturnType<typeof setTimeout> | null = null;

  constructor(
    private readonly zone: NgZone,
    private readonly cdr: ChangeDetectorRef,
    private readonly router: Router,
    private readonly route: ActivatedRoute,
    private readonly dashboardService: DashboardService,
    private readonly lightboxService: LightboxService,
    private readonly destinationsService: DestinationsListService,
    private readonly auth: AuthService,
  ) {}

  ngOnInit(): void {
    const editId = Number(this.route.snapshot.paramMap.get('id'));
    this.isEditMode = Number.isFinite(editId) && editId > 0;
    this.editDestinationId = this.isEditMode ? editId : null;

    this.loadAssignedRegions();
    this.loadDestinationTags();
  }

  ngAfterViewInit(): void {
    setTimeout(() => this.initMiniMap(), 0);
  }

  ngOnDestroy(): void {
    this.assignedRegionsSubscription?.unsubscribe();
    this.tagsSubscription?.unsubscribe();
    this.tagSearchSubscription?.unsubscribe();
    this.createSubscription?.unsubscribe();

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
      mediaReady &&
      !this.regionLoading &&
      !this.tagsLoading &&
      !this.isSaving
    );
  }

  get selectedAdditionalTags(): RegionalDestinationTagDto[] {
    const primaryIds = new Set(this.classItems.map((item) => item.tag.id));
    return this.destinationTags
      .filter((tag) => this.selectedTagIds.has(tag.id) && !primaryIds.has(tag.id))
      .sort((a, b) => a.name.localeCompare(b.name));
  }

  get visibleClassItems(): ClassItem[] {
    return this.classItems.slice(0, this.visibleClassificationLimit);
  }

  get showClassificationToggle(): boolean {
    return this.classItems.length > AddDestinationComponent.initialVisibleClassificationTags;
  }

  get visibleClassificationLimit(): number {
    return this.classificationExpanded
      ? AddDestinationComponent.expandedVisibleClassificationTags
      : AddDestinationComponent.initialVisibleClassificationTags;
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
          this.loadDestinationForEditIfNeeded();
        },
        error: (error) => this.handleLoadError(error, 'Unable to load assigned regions.'),
      });
  }

  onRegionSelected(regionId: number): void {
    if (regionId === this.activeRegionId) return;

    this.setActiveRegion(regionId, true);
    this.resetZone();
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
            this.editorRef.nativeElement.innerHTML = dto.description ?? '';
            this.syncEmptyState();
            this.notifyStateChanged();
          }
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
    const firstAvailable = this.additionalTagResults.find(
      (tag) => !this.selectedTagIds.has(tag.id),
    );
    if (firstAvailable) {
      this.selectAdditionalTag(firstAvailable);
    }
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
    if (
      el &&
      (el.innerHTML === '<br>' || el.innerHTML === '&nbsp;' || el.innerHTML.trim() === '')
    ) {
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
    this.showCenterOnModalMap();
    this.removeMiniMapCenterMarker();
    this.notifyStateChanged();
  }

  confirmZone(): void {
    if (!this.hasValidCenter()) {
      this.errorMessage = 'Choose a destination center point before confirming.';
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
    this.createSubscription = this.destinationsService
      .updateDestination(regionId, destinationId, payload)
      .pipe(
        finalize(() => {
          this.isSaving = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: (updated) => {
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

    const descriptionHtml = this.editorRef?.nativeElement?.innerHTML?.trim() ?? '';

    return {
      regionId,
      name,
      summary: normalizedSummary || null,
      description: descriptionHtml || null,
      status: 'Published',
      latitude: this.centerLat,
      longitude: this.centerLng,
      tagIds: [...this.selectedTagIds],
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

    const descriptionHtml = this.editorRef?.nativeElement?.innerHTML?.trim() ?? '';

    return {
      name,
      summary: normalizedSummary || null,
      description: descriptionHtml || null,
      latitude: this.centerLat,
      longitude: this.centerLng,
      tagIds: [...this.selectedTagIds],
    };
  }

  private loadMapCenter(): [number, number] {
    return [44.0, 20.9];
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
    }).setView(this.loadMapCenter(), 8);

    L.tileLayer('https://tiles.stadiamaps.com/tiles/alidade_smooth/{z}/{x}/{y}{r}.png', {
      maxZoom: 19,
    }).addTo(this.miniMap);
  }

  private initModalMap(el: HTMLDivElement): void {
    if (this.modalMap || typeof L === 'undefined') return;

    const initialCenter = this.hasValidCenter()
      ? [this.centerLat as number, this.centerLng as number]
      : this.loadMapCenter();

    this.modalMap = L.map(el, {
      zoomControl: true,
      attributionControl: false,
    }).setView(initialCenter, this.hasValidCenter() ? 12 : 9);

    L.tileLayer('https://tiles.stadiamaps.com/tiles/alidade_smooth/{z}/{x}/{y}{r}.png', {
      maxZoom: 19,
    }).addTo(this.modalMap);

    this.modalMap.on('click', (event: any) => {
      this.zone.run(() => {
        this.centerLat = event.latlng.lat;
        this.centerLng = event.latlng.lng;
        this.zoneConfirmed = false;
        this.errorMessage = null;
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
    }
  }

  private showZoneOnMiniMap(): void {
    if (!this.miniMap) this.initMiniMap();
    if (!this.miniMap || !this.hasValidCenter()) return;

    this.removeMiniMapCenterMarker();

    this.miniMapCenterMarker = L.marker([this.centerLat as number, this.centerLng as number]);
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

    this.modalCenterMarker = L.marker([this.centerLat as number, this.centerLng as number]);
    this.modalCenterMarker.addTo(this.modalMap);
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
    this.removeMiniMapCenterMarker();
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

  private notifyStateChanged(): void {
    this.cdr.markForCheck();
  }
}
