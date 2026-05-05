export interface EntityImage {
  id: string;
  url: string;
  altText: string;
  caption: string;
  isMain: boolean;
  sortOrder: number;
}

export interface ApiEntityImage {
  id?: number | string | null;
  url?: string | null;
  altText?: string | null;
  caption?: string | null;
  isMain?: boolean | null;
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
        sortOrder,
      };
    })
    .filter((image): image is EntityImage => image !== null)
    .sort((a, b) =>
      Number(b.isMain) - Number(a.isMain) ||
      a.sortOrder - b.sortOrder ||
      a.id.localeCompare(b.id, undefined, { numeric: true }),
    );
}

export function getMainImageUrl(images: EntityImage[], fallback?: string | null): string {
  return images[0]?.url ?? fallback?.trim() ?? '';
}
