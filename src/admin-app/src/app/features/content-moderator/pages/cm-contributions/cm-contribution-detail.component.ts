import { ChangeDetectionStrategy, ChangeDetectorRef, Component, ElementRef, NgZone, OnDestroy, OnInit, ViewChild, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, Subscription, finalize, forkJoin, takeUntil } from 'rxjs';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';
import { AuthService } from '../../../../core/services/auth.service';
import { LightboxService } from '../../../../core/services/lightbox.service';
import {
  ContentModeratorContributionsService,
  CmContributionDetail,
  CmRegionOption,
  CmDestinationOption,
  CmLocationOption,
} from '../../../../core/services/content-moderator/content-moderator-contributions.service';
import { DescriptionRichHtmlPipe } from '../../../../shared/pipes/description-rich-html.pipe';
import { isLongDescription } from '../../../../shared/utils/description-formatting';
import {
  OPEN_24_HOURS_LABEL,
  hasDisplayableOperatingHours,
  isOpen24HoursSchedule,
  isOpen24HoursText,
} from '../../../../shared/utils/opening-hours';
import { MapTileLayerService } from '../../../../core/services/map-tile-layer.service';
import { AdminMapViewport, AdminMapZone } from '../../../../core/models/admin-map.model';
import {
  applyAssignedRegionViewport,
  createRegionalMapPinIcon,
  renderAssignedRegionBoundary,
} from '../../../add-pages/shared/regional-creation-map';

declare const L: any;

type DestinationChoice = 'existing' | 'new';

@Component({
  selector: 'app-cm-contribution-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent, DescriptionRichHtmlPipe],
  templateUrl: './cm-contribution-detail.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CmContributionDetailComponent implements OnInit, OnDestroy {
  private readonly auth = inject(AuthService);
  private readonly service = inject(ContentModeratorContributionsService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly lightbox = inject(LightboxService);
  private readonly mapTileLayer = inject(MapTileLayerService);
  private readonly zone = inject(NgZone);
  private readonly destroy$ = new Subject<void>();

  contribution: CmContributionDetail | null = null;
  isLoading = true;
  notFound = false;
  errorMessage: string | null = null;
  processing = false;
  rejectModal = false;
  reviewerNote = '';
  descriptionExpanded = false;

  // Region/destination assignment
  regions: CmRegionOption[] = [];
  regionsLoading = false;
  selectedRegionId: number | null = null;
  regionSearch = '';

  destinations: CmDestinationOption[] = [];
  destinationsLoading = false;
  destinationChoice: DestinationChoice = 'existing';
  selectedDestinationId: number | null = null;
  newDestinationName = '';
  newDestinationLatitude: number | null = null;
  newDestinationLongitude: number | null = null;
  destinationCenterConfirmed = false;
  destinationCenterValidationPending = false;
  destinationCenterMessage: string | null = null;
  destinationRegionBoundaryMessage: string | null = null;
  destinationCenterModalOpen = false;
  destinationCenterCandidateAllowed = false;
  locations: CmLocationOption[] = [];
  locationsLoading = false;
  newLocationName = '';

  // Destination card image (for new destinations)
  destinationImageFile: File | null = null;
  destinationImagePreview: string | null = null;
  destinationImageError = '';

  // Location card image (for new locations)
  locationImageFile: File | null = null;
  locationImagePreview: string | null = null;
  locationImageError = '';

  private destinationCenterMap: any = null;
  private destinationCenterMarker: any = null;
  private destinationRegionBoundaryLayer: any = null;
  private destinationCenterModalMap: any = null;
  private destinationCenterModalMarker: any = null;
  private destinationModalRegionBoundaryLayer: any = null;
  private destinationCenterCandidateLatitude: number | null = null;
  private destinationCenterCandidateLongitude: number | null = null;
  private destinationRegionViewport: AdminMapViewport | null = null;
  private destinationRegionZone: AdminMapZone | null = null;
  private destinationCenterValidationSubscription: Subscription | null = null;
  private destinationRegionMapSubscription: Subscription | null = null;

  @ViewChild('destinationCenterMap')
  set destinationCenterMapElement(element: ElementRef<HTMLDivElement> | undefined) {
    if (!element) {
      this.destroyDestinationCenterMap();
      return;
    }
    setTimeout(() => this.initDestinationCenterMap(element.nativeElement), 0);
  }

  @ViewChild('destinationCenterModalMap')
  destinationCenterModalMapElement?: ElementRef<HTMLDivElement>;

  readonly galleryLimit = 8;
  get userName(): string { return this.auth.fullName || 'Content Moderator'; }
  get userRole(): string { return this.auth.getUser()?.role || 'Content Moderator'; }

  /** True when contribution has no locationId and MUST be assigned before approval. */
  get requiresLocationAssignment(): boolean {
    return !!this.contribution && !this.contribution.locationId;
  }

  /**
   * Use the CM approve endpoint whenever the assignment form has a region selected.
   * Ownership claims target an existing place (no assignment form is shown), so they always
   * go through the plain approve endpoint even though a suggested region is auto-selected.
   */
  get usesCmApprove(): boolean {
    return !this.isOwnershipClaim && this.requiresLocationAssignment && !!this.selectedRegionId;
  }

  get canApprove(): boolean {
    if (!this.contribution) return false;
    if (this.isOwnershipClaim || !this.requiresLocationAssignment) return true;
    if (!this.selectedRegionId) return false;
    if (!this.newLocationName.trim()) return false;
    return !!this.selectedDestinationId;
  }

  get isDescriptionLong(): boolean {
    return isLongDescription(this.contribution?.description);
  }

  get hasDisplayableOperatingHours(): boolean {
    return hasDisplayableOperatingHours(this.contribution?.operatingHours ?? []);
  }

  get showsOpen24Hours(): boolean {
    const hours = this.contribution?.operatingHours ?? [];
    return isOpen24HoursSchedule(hours) || isOpen24HoursText(this.contribution?.openingHours);
  }

  readonly open24HoursLabel = OPEN_24_HOURS_LABEL;

  get locationDisplay(): string {
    if (!this.contribution) return '';
    const parts = [this.contribution.city, this.contribution.region, this.contribution.country].filter(Boolean);
    return parts.length ? parts.join(', ') : (this.contribution.location || 'Outside mapped regions');
  }

  get selectedRegion(): CmRegionOption | null {
    return this.regions.find(region => region.id === this.selectedRegionId) ?? null;
  }

  get suggestedRegion(): CmRegionOption | null {
    const suggestedId = this.contribution?.suggestedRegionId;
    if (!suggestedId) return null;
    return this.regions.find(region => region.id === suggestedId) ?? null;
  }

  get filteredRegions(): CmRegionOption[] {
    const term = this.regionSearch.trim().toLowerCase();
    if (!term) {
      const suggestion = this.suggestedRegion;
      return suggestion ? [suggestion] : [];
    }
    return this.regions
      .filter(region => this.regionDisplayName(region).toLowerCase().includes(term))
      .slice(0, 8);
  }

  get filteredLocations(): CmLocationOption[] {
    const term = this.newLocationName.trim().toLowerCase();
    const locations = term
      ? this.locations.filter(l => l.name.toLowerCase().includes(term) && l.name.toLowerCase() !== term)
      : this.locations;
    return locations.slice(0, 8);
  }

  get canUseMapLocationSuggestion(): boolean {
    const suggestion = this.contribution?.suggestedLocationName?.trim();
    if (!suggestion) return false;
    return suggestion.toLowerCase() !== this.newLocationName.trim().toLowerCase();
  }

  get allPhotos(): { url: string; name: string }[] {
    if (!this.contribution) return [];
    const result: { url: string; name: string }[] = [];
    if (this.contribution.cardImageUrl) result.push({ url: this.contribution.cardImageUrl, name: 'card' });
    for (const url of this.contribution.galleryImageUrls) result.push({ url, name: 'gallery' });
    return result;
  }

  get photosPreview(): { url: string; name: string }[] {
    return this.allPhotos.slice(0, this.galleryLimit);
  }

  get photosOverflow(): number {
    return Math.max(0, this.allPhotos.length - this.galleryLimit);
  }

  ngOnInit(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    if (!id) { this.notFound = true; this.isLoading = false; this.cdr.markForCheck(); return; }
    this.load(id);
  }

  ngOnDestroy(): void {
    this.destinationCenterValidationSubscription?.unsubscribe();
    this.destinationRegionMapSubscription?.unsubscribe();
    this.destroyDestinationCenterModalMap();
    this.destroyDestinationCenterMap();
    this.destroy$.next();
    this.destroy$.complete();
  }

  private load(id: number): void {
    this.isLoading = true;
    this.cdr.markForCheck();

    this.service.getById(id)
      .pipe(takeUntil(this.destroy$), finalize(() => { this.isLoading = false; this.cdr.markForCheck(); }))
      .subscribe({
        next: c => {
          this.contribution = c;
          this.newDestinationName = c.suggestedDestinationName || c.city || '';
          this.newLocationName = c.suggestedLocationName || '';
          if (!c.locationId) this.loadRegions();
          this.cdr.markForCheck();
        },
        error: err => {
          if (err?.status === 404) this.notFound = true;
          else this.errorMessage = 'Unable to load contribution details.';
          this.cdr.markForCheck();
        },
      });
  }

  private loadRegions(): void {
    this.regionsLoading = true;
    this.cdr.markForCheck();

    this.service.getRegionOptions()
      .pipe(takeUntil(this.destroy$), finalize(() => { this.regionsLoading = false; this.cdr.markForCheck(); }))
      .subscribe({
        next: regions => {
          this.regions = regions;
          if (!this.selectedRegionId && this.contribution?.suggestedRegionId) {
            const suggested = regions.find(region => region.id === this.contribution?.suggestedRegionId);
            if (suggested) {
              this.selectedRegionId = suggested.id;
              this.regionSearch = this.regionDisplayName(suggested);
              this.onRegionSelected();
            }
          }
          this.cdr.markForCheck();
        },
        error: () => { this.cdr.markForCheck(); },
      });
  }

  onRegionSelected(): void {
    this.selectedDestinationId = null;
    this.destinations = [];
    this.locations = [];
    this.destinationChoice = 'existing';
    this.clearDestinationCenter();
    this.loadDestinationRegionMapContext();
    this.cdr.markForCheck();

    if (!this.selectedRegionId) return;

    this.destinationsLoading = true;
    this.cdr.markForCheck();

    this.service.getDestinationsForRegion(this.selectedRegionId)
      .pipe(takeUntil(this.destroy$), finalize(() => { this.destinationsLoading = false; this.cdr.markForCheck(); }))
      .subscribe({
        next: destinations => {
          this.destinations = destinations;
          this.destinationChoice = 'existing';
          this.cdr.markForCheck();
        },
        error: () => { this.cdr.markForCheck(); },
      });
  }

  selectRegion(region: CmRegionOption): void {
    this.selectedRegionId = region.id;
    this.regionSearch = this.regionDisplayName(region);
    this.onRegionSelected();
  }

  clearRegionSelection(): void {
    this.selectedRegionId = null;
    this.regionSearch = '';
    this.destinations = [];
    this.locations = [];
    this.selectedDestinationId = null;
    this.destinationChoice = 'existing';
    this.clearDestinationCenter();
    this.clearDestinationRegionMapContext();
    this.cdr.markForCheck();
  }

  onDestinationChoiceChanged(choice: DestinationChoice): void {
    this.destinationChoice = choice;
    this.selectedDestinationId = null;
    this.locations = [];
    if (choice === 'existing') {
      this.destinationImageFile = null;
      this.destinationImagePreview = null;
      this.clearDestinationCenter();
    }
    this.locationImageFile = null;
    this.locationImagePreview = null;
    this.cdr.markForCheck();
  }

  onDestinationSelected(): void {
    this.locations = [];
    if (!this.selectedDestinationId) { this.cdr.markForCheck(); return; }

    this.locationsLoading = true;
    this.cdr.markForCheck();

    this.service.getLocationsForDestination(this.selectedDestinationId)
      .pipe(takeUntil(this.destroy$), finalize(() => { this.locationsLoading = false; this.cdr.markForCheck(); }))
      .subscribe({
        next: locations => { this.locations = locations; this.cdr.markForCheck(); },
        error: () => { this.cdr.markForCheck(); },
      });
  }

  selectLocation(location: CmLocationOption): void {
    this.newLocationName = location.name;
    this.cdr.markForCheck();
  }

  useSuggestedLocation(): void {
    const suggestion = this.contribution?.suggestedLocationName?.trim();
    if (!suggestion) return;
    this.newLocationName = suggestion;
    this.cdr.markForCheck();
  }

  openLightbox(index: number): void {
    this.lightbox.open(this.allPhotos, index);
  }

  onDestinationImageSelected(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    (event.target as HTMLInputElement).value = '';
    if (!file) return;
    this.destinationImageError = '';
    this.destinationImageFile = file;
    const reader = new FileReader();
    reader.onload = () => { this.destinationImagePreview = reader.result as string; this.cdr.markForCheck(); };
    reader.readAsDataURL(file);
  }

  clearDestinationImage(): void {
    this.destinationImageFile = null;
    this.destinationImagePreview = null;
    this.destinationImageError = '';
    this.cdr.markForCheck();
  }

  onLocationImageSelected(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    (event.target as HTMLInputElement).value = '';
    if (!file) return;
    this.locationImageError = '';
    this.locationImageFile = file;
    const reader = new FileReader();
    reader.onload = () => { this.locationImagePreview = reader.result as string; this.cdr.markForCheck(); };
    reader.readAsDataURL(file);
  }

  clearLocationImage(): void {
    this.locationImageFile = null;
    this.locationImagePreview = null;
    this.locationImageError = '';
    this.cdr.markForCheck();
  }

  approve(): void {
    if (!this.contribution || this.processing || !this.canApprove) return;
    this.processing = true;
    this.errorMessage = null;
    this.cdr.markForCheck();

    const obs = this.usesCmApprove
      ? this.service.approveWithRegion(
          this.contribution.id,
          this.selectedRegionId!,
          this.selectedDestinationId,
          null,
          null,
          null,
          this.newLocationName.trim(),
          this.reviewerNote,
        )
      : this.service.approve(this.contribution.id, this.reviewerNote);

    obs.pipe(finalize(() => { this.processing = false; this.cdr.markForCheck(); }))
      .subscribe({
        next: (result) => {
          const destId = result?.createdDestinationId;
          const locId = result?.createdLocationId;
          const destImage = this.destinationImageFile;
          const locImage = this.locationImageFile;

          const uploads: ReturnType<typeof this.service.uploadDestinationCardImage>[] = [];
          if (destId && destImage) uploads.push(this.service.uploadDestinationCardImage(destId, destImage));
          if (locId && locImage) uploads.push(this.service.uploadLocationCardImage(locId, locImage));

          if (uploads.length === 0) {
            this.router.navigate(['/content-moderator/contributions'], {
              state: { feedbackMessage: 'Contribution approved.' }
            });
            return;
          }

          forkJoin(uploads).subscribe({
            next: () => this.router.navigate(['/content-moderator/contributions'], {
              state: { feedbackMessage: 'Contribution approved.' }
            }),
            error: () => this.router.navigate(['/content-moderator/contributions'], {
              state: { feedbackMessage: 'Contribution approved. One or more image uploads failed.' }
            }),
          });
        },
        error: err => {
          this.errorMessage = err?.error?.message || 'Unable to approve contribution.';
          this.cdr.markForCheck();
        },
      });
  }

  openReject(): void { this.rejectModal = true; this.cdr.markForCheck(); }
  cancelReject(): void { this.rejectModal = false; this.cdr.markForCheck(); }

  confirmReject(): void {
    if (!this.contribution || this.processing) return;
    this.rejectModal = false;
    this.processing = true;
    this.cdr.markForCheck();

    this.service.reject(this.contribution.id, this.reviewerNote)
      .pipe(finalize(() => { this.processing = false; this.cdr.markForCheck(); }))
      .subscribe({
        next: () => this.router.navigate(['/content-moderator/contributions'], {
          state: { feedbackMessage: 'Contribution rejected.' }
        }),
        error: err => {
          this.errorMessage = err?.error?.message || 'Unable to reject contribution.';
          this.cdr.markForCheck();
        },
      });
  }

  goBack(): void { this.router.navigate(['/content-moderator/contributions']); }

  get isOwnershipClaim(): boolean {
    return (this.contribution?.type ?? '').toLowerCase() === 'claimownership';
  }

  clearDestinationCenter(): void {
    this.destinationCenterValidationSubscription?.unsubscribe();
    this.destinationCenterValidationSubscription = null;
    this.newDestinationLatitude = null;
    this.newDestinationLongitude = null;
    this.destinationCenterConfirmed = false;
    this.destinationCenterValidationPending = false;
    this.destinationCenterMessage = null;
    this.destinationCenterCandidateLatitude = null;
    this.destinationCenterCandidateLongitude = null;
    this.destinationCenterCandidateAllowed = false;
    if (this.destinationCenterMarker && this.destinationCenterMap) {
      this.destinationCenterMap.removeLayer(this.destinationCenterMarker);
      this.destinationCenterMarker = null;
    }
    this.removeDestinationCenterModalMarker();
    this.cdr.markForCheck();
  }

  openDestinationCenterModal(): void {
    if (!this.selectedRegionId || this.destinationRegionBoundaryMessage) return;

    this.destinationCenterCandidateLatitude = this.newDestinationLatitude;
    this.destinationCenterCandidateLongitude = this.newDestinationLongitude;
    this.destinationCenterCandidateAllowed = this.destinationCenterConfirmed;
    this.destinationCenterMessage = null;
    this.destinationCenterModalOpen = true;
    this.cdr.markForCheck();
    setTimeout(() => {
      const element = this.destinationCenterModalMapElement?.nativeElement;
      if (element) this.initDestinationCenterModalMap(element);
    }, 50);
  }

  closeDestinationCenterModal(): void {
    this.destinationCenterModalOpen = false;
    this.destinationCenterValidationSubscription?.unsubscribe();
    this.destinationCenterValidationSubscription = null;
    this.destinationCenterValidationPending = false;
    this.destinationCenterMessage = null;
    this.destroyDestinationCenterModalMap();
    this.cdr.markForCheck();
  }

  confirmDestinationCenter(): void {
    if (
      !this.destinationCenterCandidateAllowed ||
      this.destinationCenterCandidateLatitude == null ||
      this.destinationCenterCandidateLongitude == null
    ) return;

    this.newDestinationLatitude = this.destinationCenterCandidateLatitude;
    this.newDestinationLongitude = this.destinationCenterCandidateLongitude;
    this.destinationCenterConfirmed = true;
    this.destinationCenterMessage = 'Destination centre is inside the selected region.';
    this.showDestinationCenterMarker();
    this.destinationCenterMap?.setView(
      [this.newDestinationLatitude, this.newDestinationLongitude],
      12,
      { animate: false },
    );
    this.closeDestinationCenterModal();
  }

  private loadDestinationRegionMapContext(): void {
    this.clearDestinationRegionMapContext();
    if (!this.selectedRegionId) return;

    const requestedRegionId = this.selectedRegionId;
    this.destinationRegionBoundaryMessage = 'Loading selected region boundary...';
    this.destinationRegionMapSubscription = this.service.getRegionMapContext(requestedRegionId)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: context => {
          if (context.regionId !== this.selectedRegionId) return;

          this.destinationRegionViewport = context.viewport;
          this.destinationRegionZone = context.hasBoundary && context.geometryGeoJson
            ? {
                id: context.regionId,
                entityType: 'Region',
                name: context.regionName,
                displayName: context.displayName,
                geometryGeoJson: context.geometryGeoJson,
                isGeometryFallback: false,
              }
            : null;
          this.destinationRegionBoundaryMessage = context.hasBoundary
            ? null
            : context.message || 'The selected region does not have a usable boundary.';
          this.refreshDestinationRegionBoundary();
          this.applyDestinationRegionViewport();
          this.cdr.markForCheck();
        },
        error: err => {
          if (requestedRegionId !== this.selectedRegionId) return;
          this.destinationRegionBoundaryMessage = err?.error?.message ||
            'Unable to load the selected region boundary.';
          this.cdr.markForCheck();
        },
      });
  }

  private clearDestinationRegionMapContext(): void {
    this.destinationRegionMapSubscription?.unsubscribe();
    this.destinationRegionMapSubscription = null;
    this.destinationRegionViewport = null;
    this.destinationRegionZone = null;
    this.destinationRegionBoundaryMessage = null;
    this.refreshDestinationRegionBoundary();
  }

  private refreshDestinationRegionBoundary(): void {
    this.destinationRegionBoundaryLayer = renderAssignedRegionBoundary(
      typeof L === 'undefined' ? null : L,
      this.destinationCenterMap,
      this.destinationRegionBoundaryLayer,
      this.destinationRegionZone,
    );
    this.destinationModalRegionBoundaryLayer = renderAssignedRegionBoundary(
      typeof L === 'undefined' ? null : L,
      this.destinationCenterModalMap,
      this.destinationModalRegionBoundaryLayer,
      this.destinationRegionZone,
    );
  }

  private applyDestinationRegionViewport(): void {
    if (this.destinationRegionViewport && !this.destinationRegionViewport.isEmpty) {
      applyAssignedRegionViewport(this.destinationCenterMap, this.destinationRegionViewport, 9, 11);
      applyAssignedRegionViewport(this.destinationCenterModalMap, this.destinationRegionViewport, 9, 11);
      return;
    }

    const fallbackLatitude = this.contribution?.latitude ?? 44.0165;
    const fallbackLongitude = this.contribution?.longitude ?? 21.0059;
    for (const map of [this.destinationCenterMap, this.destinationCenterModalMap]) {
      map?.setView(
        [fallbackLatitude, fallbackLongitude],
        this.contribution?.latitude != null ? 13 : 7,
        { animate: false },
      );
    }
  }

  private validateDestinationCenter(latitude: number, longitude: number): void {
    if (!this.selectedRegionId || this.destinationCenterValidationPending) return;

    this.destinationCenterValidationSubscription?.unsubscribe();
    this.destinationCenterValidationPending = true;
    this.destinationCenterCandidateAllowed = false;
    this.destinationCenterMessage = 'Checking destination centre...';
    this.cdr.markForCheck();

    this.destinationCenterValidationSubscription = this.service
      .validateDestinationCenter(this.selectedRegionId, latitude, longitude)
      .pipe(takeUntil(this.destroy$), finalize(() => {
        this.destinationCenterValidationPending = false;
        this.cdr.markForCheck();
      }))
      .subscribe({
        next: result => {
          this.destinationCenterMessage = result.message;
          if (!result.isAllowed) {
            this.destinationCenterCandidateLatitude = null;
            this.destinationCenterCandidateLongitude = null;
            this.destinationCenterCandidateAllowed = false;
            this.removeDestinationCenterModalMarker();
            return;
          }

          this.destinationCenterCandidateLatitude = latitude;
          this.destinationCenterCandidateLongitude = longitude;
          this.destinationCenterCandidateAllowed = true;
          this.showDestinationCenterModalMarker();
          this.destinationCenterModalMap?.setView([latitude, longitude], 12, { animate: true });
        },
        error: err => {
          this.destinationCenterMessage = err?.error?.message ||
            'Unable to validate the destination centre.';
          this.destinationCenterCandidateAllowed = false;
        },
      });
  }

  private initDestinationCenterMap(element: HTMLDivElement): void {
    if (this.destinationCenterMap || typeof L === 'undefined') return;

    this.destinationCenterMap = L.map(element, { zoomControl: true, attributionControl: false });
    this.mapTileLayer.addTo(L, this.destinationCenterMap);
    this.refreshDestinationRegionBoundary();
    this.applyDestinationRegionViewport();
    setTimeout(() => this.destinationCenterMap?.invalidateSize(), 100);
    this.showDestinationCenterMarker();
  }

  private initDestinationCenterModalMap(element: HTMLDivElement): void {
    if (this.destinationCenterModalMap || typeof L === 'undefined') return;

    this.destinationCenterModalMap = L.map(element, { zoomControl: true, attributionControl: false });
    this.mapTileLayer.addTo(L, this.destinationCenterModalMap);
    this.refreshDestinationRegionBoundary();
    this.applyDestinationRegionViewport();
    this.destinationCenterModalMap.on('click', (event: any) => {
      this.zone.run(() => this.validateDestinationCenter(event.latlng.lat, event.latlng.lng));
    });
    setTimeout(() => this.destinationCenterModalMap?.invalidateSize(), 100);
    this.showDestinationCenterModalMarker();
  }

  private showDestinationCenterMarker(): void {
    if (!this.destinationCenterMap || this.newDestinationLatitude == null || this.newDestinationLongitude == null) return;
    const point: [number, number] = [this.newDestinationLatitude, this.newDestinationLongitude];
    if (this.destinationCenterMarker) {
      this.destinationCenterMarker.setLatLng(point);
    } else {
      this.destinationCenterMarker = L.marker(point, {
        icon: createRegionalMapPinIcon(L),
      }).addTo(this.destinationCenterMap);
    }
  }

  private showDestinationCenterModalMarker(): void {
    if (
      !this.destinationCenterModalMap ||
      this.destinationCenterCandidateLatitude == null ||
      this.destinationCenterCandidateLongitude == null
    ) return;

    const point: [number, number] = [
      this.destinationCenterCandidateLatitude,
      this.destinationCenterCandidateLongitude,
    ];
    if (this.destinationCenterModalMarker) {
      this.destinationCenterModalMarker.setLatLng(point);
    } else {
      this.destinationCenterModalMarker = L.marker(point, {
        icon: createRegionalMapPinIcon(L),
      }).addTo(this.destinationCenterModalMap);
    }
  }

  private removeDestinationCenterModalMarker(): void {
    if (this.destinationCenterModalMarker && this.destinationCenterModalMap) {
      this.destinationCenterModalMap.removeLayer(this.destinationCenterModalMarker);
    }
    this.destinationCenterModalMarker = null;
  }

  private destroyDestinationCenterModalMap(): void {
    this.destinationCenterModalMap?.remove();
    this.destinationCenterModalMap = null;
    this.destinationCenterModalMarker = null;
    this.destinationModalRegionBoundaryLayer = null;
  }

  private destroyDestinationCenterMap(): void {
    this.destinationCenterMap?.remove();
    this.destinationCenterMap = null;
    this.destinationCenterMarker = null;
    this.destinationRegionBoundaryLayer = null;
  }

  typeLabel(type: string): string {
    switch ((type ?? '').toLowerCase()) {
      case 'updateplace': return 'Update Place';
      case 'claimownership': return 'Claim Ownership';
      default: return 'New Place';
    }
  }

  regionDisplayName(region: CmRegionOption): string {
    return region.territoryName ? `${region.name} (${region.territoryName})` : region.name;
  }

  partnerDisplayName(id: string): string {
    switch (id.toLowerCase()) {
      case 'booking': return 'Booking.com';
      case 'airbnb': return 'Airbnb';
      case 'hotel': return 'Official Website';
      default: return id;
    }
  }
}
