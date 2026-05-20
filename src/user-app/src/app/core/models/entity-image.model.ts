export type EntityImageRole = 'card' | 'cover' | 'gallery';

export interface EntityImage {
  id: string;
  url: string;
  altText: string;
  caption: string;
  isMain: boolean;
  imageRole: EntityImageRole;
  sortOrder: number;
}

export interface ApiEntityImage {
  id?: number | string | null;
  url?: string | null;
  altText?: string | null;
  caption?: string | null;
  isMain?: boolean | null;
  imageRole?: string | null;
  sortOrder?: number | null;
}

export function normalizeEntityImages(images: ApiEntityImage[] | null | undefined): EntityImage[] {
  return (images ?? [])
    .map((image, index) => {
      const url = typeof image.url === 'string' ? image.url.trim() : '';
      if (!url) return null;

      const sortOrder = typeof image.sortOrder === 'number' && Number.isFinite(image.sortOrder)
        ? image.sortOrder
        : index;

      return {
        id: String(image.id ?? index),
        url,
        altText: typeof image.altText === 'string' ? image.altText.trim() : '',
        caption: typeof image.caption === 'string' ? image.caption.trim() : '',
        isMain: image.isMain === true,
        imageRole: normalizeImageRole(image.imageRole, image.isMain === true),
        sortOrder,
      };
    })
    .filter((image): image is EntityImage => image !== null)
    .sort((a, b) =>
      imageRoleSort(a.imageRole) - imageRoleSort(b.imageRole) ||
      a.sortOrder - b.sortOrder ||
      a.id.localeCompare(b.id, undefined, { numeric: true }),
    );
}

export function getCardImageUrl(images: EntityImage[], fallback?: string | null): string {
  return images.find(image => image.imageRole === 'card')?.url ?? fallback?.trim() ?? '';
}

export function getCoverImageUrl(images: EntityImage[], fallback?: string | null): string {
  return images.find(image => image.imageRole === 'cover')?.url ?? fallback?.trim() ?? '';
}

export function getGalleryImages(images: EntityImage[]): EntityImage[] {
  return images.filter(image => image.imageRole === 'gallery');
}

export function getMainImageUrl(images: EntityImage[], fallback?: string | null): string {
  return getCardImageUrl(images, fallback);
}

function normalizeImageRole(role: string | null | undefined, legacyIsMain: boolean): EntityImageRole {
  const normalized = role?.trim().toLowerCase();
  if (normalized === 'card' || normalized === 'cover' || normalized === 'gallery') return normalized;
  return legacyIsMain ? 'cover' : 'gallery';
}

function imageRoleSort(role: EntityImageRole): number {
  switch (role) {
    case 'cover': return 0;
    case 'card': return 1;
    case 'gallery': return 2;
  }
}
