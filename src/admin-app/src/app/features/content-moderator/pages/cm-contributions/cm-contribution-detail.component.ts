import { ChangeDetectionStrategy, ChangeDetectorRef, Component, OnDestroy, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, finalize, forkJoin, takeUntil } from 'rxjs';
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
  destinationChoice: DestinationChoice = 'new';
  selectedDestinationId: number | null = null;
  newDestinationName = '';
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

  readonly galleryLimit = 8;
  get userName(): string { return this.auth.fullName || 'Content Moderator'; }
  get userRole(): string { return this.auth.getUser()?.role || 'Content Moderator'; }

  /** True when contribution has no locationId and MUST be assigned before approval. */
  get requiresLocationAssignment(): boolean {
    return !!this.contribution && !this.contribution.locationId;
  }

  /** Use CM approve endpoint whenever the assignment form has a region selected. */
  get usesCmApprove(): boolean {
    return !!this.selectedRegionId;
  }

  get canApprove(): boolean {
    if (!this.contribution) return false;
    if (!this.usesCmApprove) return true;
    if (!this.selectedRegionId) return false;
    if (!this.newLocationName.trim()) return false;
    if (this.destinationChoice === 'existing') return !!this.selectedDestinationId;
    return !!this.newDestinationName.trim();
  }

  get isDescriptionLong(): boolean {
    return isLongDescription(this.contribution?.description);
  }

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
          this.loadRegions();
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
    this.destinationChoice = 'new';
    this.cdr.markForCheck();

    if (!this.selectedRegionId) return;

    this.destinationsLoading = true;
    this.cdr.markForCheck();

    this.service.getDestinationsForRegion(this.selectedRegionId)
      .pipe(takeUntil(this.destroy$), finalize(() => { this.destinationsLoading = false; this.cdr.markForCheck(); }))
      .subscribe({
        next: destinations => {
          this.destinations = destinations;
          this.destinationChoice = 'new';
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
    this.destinationChoice = 'new';
    this.cdr.markForCheck();
  }

  onDestinationChoiceChanged(choice: DestinationChoice): void {
    this.destinationChoice = choice;
    this.selectedDestinationId = null;
    this.locations = [];
    if (choice === 'existing') {
      this.destinationImageFile = null;
      this.destinationImagePreview = null;
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
          this.destinationChoice === 'existing' ? this.selectedDestinationId : null,
          this.destinationChoice === 'new' ? this.newDestinationName.trim() : null,
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
