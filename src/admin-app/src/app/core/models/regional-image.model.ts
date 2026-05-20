export interface RegionalEntityImageDto {
  id: number;
  url: string;
  altText?: string | null;
  caption?: string | null;
  isMain: boolean;
  sortOrder: number;
}

export interface RegionalEntityImageInput {
  url: string;
  altText?: string | null;
  caption?: string | null;
  isMain: boolean;
  sortOrder: number;
}

export interface RegionalImageUploadResult {
  fileName: string;
  url: string;
  sizeBytes: number;
}

export interface RegionalImageUploadResponse {
  images: RegionalImageUploadResult[];
}
