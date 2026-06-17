export interface EntityProfileTagDto {
  id: number;
  name: string;
  slug: string;
  tagTypeId?: number | null;
  tagTypeName?: string | null;
  tagTypeSlug?: string | null;
}

export interface EntityProfileDto {
  description: string | null;
  cardImageUrl: string | null;
  coverImageUrl: string | null;
  galleryImageUrls: string[];
  tags: EntityProfileTagDto[];
}

export interface EntityProfileUpdateRequest {
  description: string | null;
  cardImageUrl: string | null;
  coverImageUrl: string | null;
  galleryImageUrls: string[];
  tagIds: number[];
}

export type EntityProfileEntityType = 'region' | 'territory';
