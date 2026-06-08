import { Component, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ContributionDraft } from '../../submit-contribution.component';
import { BackgroundImageFallbackDirective } from '../../../../../../shared/directives/background-image-fallback.directive';
import { getContributionAmenityLabel } from '../../../../services/contribution-amenities';
import { PhotoGalleryComponent } from '../../../../../../shared/components/photo-gallery/photo-gallery.component';
import type { EntityImage } from '../../../../../../core/models/entity-image.model';
import { DescriptionRichHtmlPipe } from '../../../../../../shared/pipes/description-rich-html.pipe';
import { isLongDescription } from '../../../../../../shared/utils/description-formatting';

@Component({
  selector: 'app-step-preview',
  standalone: true,
  imports: [CommonModule, BackgroundImageFallbackDirective, PhotoGalleryComponent, DescriptionRichHtmlPipe],
  templateUrl: './step-preview.component.html',
  styleUrl: './step-preview.component.css',
})
export class StepPreviewComponent {
  draft = input.required<ContributionDraft>();
  isAccommodation = input(false);
  confirmLabel = input('Submit');
  confirmed = output<void>();
  back = output<void>();
  readonly galleryLightboxIndex = signal<number | null>(null);
  readonly galleryLightboxImages = signal<EntityImage[]>([]);
  readonly aboutExpanded = signal(false);

  amenityMap: Record<string, { label: string }> = {
    pool:      { label: 'Swimming Pool' },
    wifi:      { label: 'High-speed Wi-Fi' },
    breakfast: { label: 'Breakfast Included' },
    parking:   { label: 'Free Parking' },
    spa:       { label: 'Spa & Wellness' },
    gym:       { label: 'Fitness Center' },
    dining:    { label: 'On-site Dining' },
    ac:        { label: 'Air Conditioning' },
    outdoor:   { label: 'Outdoor Seating' },
    sea:       { label: 'Sea View' },
    family:    { label: 'Family Friendly' },
  };

  partnerMap: Record<string, { label: string }> = {
    booking: { label: 'Booking.com' },
    airbnb:  { label: 'Airbnb' },
    hotel:   { label: 'Hotel Website' },
  };

getAmenityLabel(id: string): string { return this.amenityMap[id]?.label ?? getContributionAmenityLabel(id); }
getPartnerLabel(id: string): string { return this.partnerMap[id]?.label ?? id; }

get cardImageUrl(): string {
  return this.draft().basics?.cardImageUrl || '';
}

get coverImageUrl(): string {
  return this.draft().basics?.imageUrl || '';
}

get galleryImageUrls(): string[] {
  return this.draft().basics?.galleryImageUrls?.filter(Boolean) ?? [];
}

get visibleGalleryImageUrls(): string[] {
  return this.galleryImageUrls.slice(0, 4);
}

get hiddenGalleryCount(): number {
  return Math.max(0, this.galleryImageUrls.length - this.visibleGalleryImageUrls.length);
}

get hasOpeningHours(): boolean {
  const details = this.draft().details;
  return !!details?.openingHoursEnabled &&
    (!!details.openingHours?.trim() || (details.operatingHours?.length ?? 0) > 0);
}

hasLongAbout(description: string | null | undefined): boolean {
  return isLongDescription(description);
}

openSingleImage(url: string, caption: string): void {
  this.galleryLightboxImages.set([this.toGalleryImage(url, 0, caption)]);
  this.galleryLightboxIndex.set(0);
}

openGallery(index: number): void {
  this.galleryLightboxImages.set(this.galleryImageUrls.map((url, imageIndex) =>
    this.toGalleryImage(url, imageIndex, ''),
  ));
  this.galleryLightboxIndex.set(index);
}

closeGallery(): void {
  this.galleryLightboxIndex.set(null);
}

private toGalleryImage(url: string, index: number, caption: string): EntityImage {
  const label = caption || `Photo ${index + 1}`;

  return {
    id: `${label.toLowerCase().replace(/\s+/g, '-')}-${index}`,
    url,
    altText: label,
    caption,
    isMain: index === 0,
    imageRole: 'gallery',
    sortOrder: index,
    imagePosition: '50% 50%',
    imageSize: 'cover',
  };
}

}
