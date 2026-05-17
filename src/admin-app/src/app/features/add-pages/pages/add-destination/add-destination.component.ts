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
import { ZoneDrawService } from '../../../../core/services/zone-draw.service';
import { DestinationsListService } from '../../../../core/services/destinations-list.service';
import { AdminMapService } from '../../../../core/services/admin-map.service';
import { RegionalMediaService } from '../../../../core/services/regional-media.service';
import { AuthService } from '../../../../core/services/auth.service';
import { RegionalAssignedRegion } from '../../../../core/models/dashboard.model';
import { AdminMapGeometry, AdminMapViewport, AdminMapZone } from '../../../../core/models/admin-map.model';
import {
  RegionalDestinationBoundaryDto,
  RegionalDestinationCreateRequest,
  RegionalDestinationDetailDto,
  RegionalDestinationTagDto,
  RegionalDestinationUpdateRequest,
} from '../../../../core/models/destination.model';
import {
  RegionalEntityImageDto,
  RegionalEntityImageInput,
  RegionalImageUploadResult,
} from '../../../../core/models/regional-image.model';

declare const L: any;

type DrawTool = 'polygon' | 'rectangle' | 'circle';
type BoundaryShapeType = 'Polygon' | 'Rectangle' | 'Circle';

interface ClassItem {
  tag: RegionalDestinationTagDto;
  checked: boolean;
}

@Component({
  selector: 'app-add-destination',
  standalone: true,
  imports: [CommonModule, FormsModule, DecimalPipe, SidebarComponent, TopbarComponent],
  templateUrl: './add-destination.component.html',
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
  `]
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
  detailLoading = false;
  isSaving = false;
  errorMessage: string | null = null;
  feedbackMessage: string | null = null;

  destinationName = '';
  summary = '';
  boundaryNote = '';
  tagInput = '';
  internalReferenceId = '';
  destinationStatus: 'Published' | 'Draft' | 'Archived' = 'Published';

  classItems: ClassItem[] = [];
  destinationTags: RegionalDestinationTagDto[] = [];
  additionalTagResults: RegionalDestinationTagDto[] = [];
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
  isEditMode = false;

  private miniMap: any = null;
  private modalMap: any = null;
  private drawnLayer: any = null;
  private confirmedLayer: any = null;
  private drawControl: any = null;
  private regionViewport: AdminMapViewport | null = null;
  private mapZones: AdminMapZone[] = [];
  private assignedRegionsSubscription: Subscription | null = null;
  private tagsSubscription: Subscription | null = null;
  private tagSearchSubscription: Subscription | null = null;
  private createSubscription: Subscription | null = null;
  private viewportSubscription: Subscription | null = null;
  private zonesSubscription: Subscription | null = null;
  private detailSubscription: Subscription | null = null;
  private tagSearchHandle: ReturnType<typeof setTimeout> | null = null;
  private editDestinationId: string | null = null;
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
    private readonly destinationsService: DestinationsListService,
    private readonly adminMapService: AdminMapService,
    private readonly regionalMediaService: RegionalMediaService,
    private readonly auth: AuthService,
  ) {}

  ngOnInit(): void {
    this.editDestinationId = this.route.snapshot.paramMap.get('id');
    this.isEditMode = Boolean(this.editDestinationId);
    this.loadAssignedRegions();
    this.loadDestinationTags();
  }

  ngAfterViewInit(): void {
    setTimeout(() => {
      this.initMiniMap();
      this.applyPendingDescription();
    }, 0);
  }

  ngOnDestroy(): void {
    this.assignedRegionsSubscription?.unsubscribe();
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
    return Boolean(this.activeRegionId) && !this.regionLoading && !this.tagsLoading && !this.detailLoading && !this.isSaving;
  }

  get selectedAdditionalTags(): RegionalDestinationTagDto[] {
    const primaryIds = new Set(this.classItems.map(item => item.tag.id));
    return this.destinationTags
      .filter(tag => this.selectedTagIds.has(tag.id) && !primaryIds.has(tag.id))
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
          if (this.isEditMode) {
            this.loadDestinationForEdit(selectedRegion.id);
          }
        },
        error: error => this.handleLoadError(error, 'Unable to load assigned regions.'),
      });
  }

  onRegionSelected(regionId: number): void {
    if (regionId === this.activeRegionId) return;
    if (this.isEditMode) {
      this.feedbackMessage = 'The region is locked while editing this destination.';
      this.notifyStateChanged();
      return;
    }

    this.setActiveRegion(regionId, true);
    this.resetZone();
    this.feedbackMessage = 'Region changed. Boundary selection was cleared.';
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
        next: tags => {
          this.destinationTags = this.normalizeTags(tags);
          this.classItems = this.destinationTags
            .map(tag => ({ tag, checked: this.selectedTagIds.has(tag.id) }));
        },
        error: error => this.handleLoadError(error, 'Unable to load destination tags.'),
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
    this.activateDrawTool();
  }

  confirmZone(): void {
    if (!this.drawnLayer) {
      this.errorMessage = 'Draw a destination zone before confirming.';
      this.notifyStateChanged();
      return;
    }

    this.zoneAreaKm2 = this.zoneDrawService.calculateAreaKm2(this.drawnLayer);
    this.zoneConfirmed = true;
    this.confirmedLayer = this.drawnLayer;
    this.confirmedShapeType = this.shapeTypeFromDrawTool(this.drawTool);
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
    const saveDestinationId = this.editDestinationId;
    this.createSubscription = of(payload)
      .pipe(
        switchMap(payloadWithoutImages => {
          const files = this.selectedImageFiles();
          if (files.length === 0) {
            const request = this.shouldSendImages(files)
              ? { ...payloadWithoutImages, images: this.buildImageInputs([]) }
              : payloadWithoutImages;
            return this.isEditMode && saveRegionId && saveDestinationId
              ? this.destinationsService.updateDestination(saveRegionId, saveDestinationId, request)
              : this.destinationsService.createDestination(request as RegionalDestinationCreateRequest);
          }

          return this.regionalMediaService.uploadImages(this.activeRegionId!, files).pipe(
            switchMap(uploaded => {
              const request = {
              ...payloadWithoutImages,
                images: this.buildImageInputs(uploaded.images),
              };
              return this.isEditMode && saveRegionId && saveDestinationId
                ? this.destinationsService.updateDestination(saveRegionId, saveDestinationId, request)
                : this.destinationsService.createDestination(request as RegionalDestinationCreateRequest);
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
          const message = this.isEditMode ? 'Destination saved.' : 'Destination published.';
          this.feedbackMessage = message;
          this.router.navigate(['/destinations'], {
            queryParams: { regionId: saved.regionId },
            state: { feedbackMessage: message },
          });
        },
        error: error => this.handleActionError(error),
      });
  }

  private buildPayload(): (RegionalDestinationCreateRequest | RegionalDestinationUpdateRequest) | null {
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

    if (!this.zoneConfirmed || !this.confirmedLayer) {
      this.errorMessage = 'Draw a destination zone before publishing.';
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
      name,
      summary: normalizedSummary || null,
      description: descriptionHtml || null,
      status: this.isEditMode ? this.destinationStatus : 'Published',
      latitude: centerLat,
      longitude: centerLng,
      boundaryNote: this.boundaryNote.trim() || null,
      boundary,
      tagIds: [...this.selectedTagIds],
    };
  }

  private loadDestinationForEdit(regionId: number): void {
    const destinationId = this.editDestinationId;
    if (!destinationId) return;

    this.detailLoading = true;
    this.errorMessage = null;
    this.notifyStateChanged();

    this.detailSubscription?.unsubscribe();
    this.detailSubscription = this.destinationsService
      .getDestination(regionId, destinationId)
      .pipe(
        finalize(() => {
          this.detailLoading = false;
          this.notifyStateChanged();
        }),
      )
      .subscribe({
        next: destination => this.applyDestinationDetail(destination),
        error: error => this.handleLoadError(error, 'Unable to load destination details.'),
      });
  }

  private applyDestinationDetail(destination: RegionalDestinationDetailDto): void {
    this.destinationName = destination.name ?? '';
    this.summary = destination.summary ?? '';
    this.boundaryNote = destination.boundaryNote ?? '';
    this.internalReferenceId = destination.internalReferenceId ?? '';
    this.destinationStatus = this.normalizeEditableStatus(destination.status);
    this.confirmedShapeType = this.normalizeShapeType(destination.boundaryShapeType);
    this.selectedTagIds = new Set((destination.tags ?? []).map(tag => tag.id));
    (destination.tags ?? []).forEach(tag => this.upsertDestinationTag(tag));
    this.classItems = this.destinationTags.map(tag => ({
      tag,
      checked: this.selectedTagIds.has(tag.id),
    }));
    this.applyEntityImages(destination.images ?? [], destination.imageUrl ?? null, destination.name);
    this.setDescriptionHtml(destination.description ?? '');
    this.applyEditBoundaryFromZones(destination);
    this.notifyStateChanged();
  }

  private createBoundaryPayload(zone: [number, number][]): RegionalDestinationBoundaryDto | null {
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

    this.applyViewportToMap(this.miniMap, [10, 10]);
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

    if (this.confirmedLayer) {
      const layerCopy = this.zoneDrawService.cloneLayer(this.confirmedLayer);
      if (layerCopy) {
        layerCopy.addTo(this.modalMap);
        this.drawnLayer = layerCopy;
        this.modalMap.fitBounds(layerCopy.getBounds(), { padding: [30, 30] });
      }
    } else {
      this.applyViewportToMap(this.modalMap, [30, 30]);
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
      this.applyViewportToMap(this.miniMap, [10, 10]);
    }
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
        next: tags => {
          const normalized = this.normalizeTags(tags)
            .filter(tag => !this.selectedTagIds.has(tag.id));
          normalized.forEach(tag => this.upsertDestinationTag(tag));
          this.additionalTagResults = normalized;
        },
        error: error => this.handleLoadError(error, 'Unable to search destination tags.'),
      });
  }

  private normalizeTags(tags: RegionalDestinationTagDto[] | null | undefined): RegionalDestinationTagDto[] {
    const seen = new Set<number>();
    return (tags ?? [])
      .filter(tag => {
        if (!tag || seen.has(tag.id)) return false;
        seen.add(tag.id);
        return true;
      })
      .sort((a, b) => a.name.localeCompare(b.name));
  }

  private upsertDestinationTag(tag: RegionalDestinationTagDto): void {
    if (this.destinationTags.some(existing => existing.id === tag.id)) return;
    this.destinationTags = [...this.destinationTags, tag].sort((a, b) => a.name.localeCompare(b.name));
  }

  private upsertClassItem(tag: RegionalDestinationTagDto, checked: boolean): void {
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
          this.applyViewportToMap(this.miniMap, [10, 10]);
          if (this.modalMap && !this.drawnLayer) {
            this.applyViewportToMap(this.modalMap, [30, 30]);
          }
          this.notifyStateChanged();
        },
        error: () => {
          this.regionViewport = null;
          this.applyViewportToMap(this.miniMap, [10, 10]);
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
          if (this.isEditMode) {
            this.applyEditBoundaryFromZones();
          }
          this.notifyStateChanged();
        },
        error: () => {
          this.mapZones = [];
          this.notifyStateChanged();
        },
      });
  }

  private applyViewportToMap(map: any, padding: [number, number]): void {
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
    const fallbackName = this.destinationName.trim() || 'Destination image';

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

  private applyEditBoundaryFromZones(destination?: RegionalDestinationDetailDto): void {
    const id = Number(destination?.id ?? this.editDestinationId);
    if (!Number.isFinite(id) || id <= 0) return;

    const zone = this.mapZones.find(item => item.entityType === 'Destination' && item.id === id);
    const layer = this.layerFromGeometry(zone?.geometryGeoJson ?? null);
    if (!layer) return;

    this.confirmedLayer = layer;
    this.drawnLayer = layer;
    this.zoneConfirmed = true;
    if (destination?.boundaryShapeType) {
      this.confirmedShapeType = this.normalizeShapeType(destination.boundaryShapeType);
    } else if (!this.confirmedShapeType) {
      this.confirmedShapeType = 'Polygon';
    }
    this.zoneAreaKm2 = this.zoneDrawService.calculateAreaKm2(layer);
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
        this.errorMessage = 'Sign in with an authenticated Regional Manager account to create destinations.';
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
