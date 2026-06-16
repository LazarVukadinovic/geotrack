import { Injectable } from '@angular/core';

export interface AdminTransientFileItem {
  file: File;
  name: string;
}

export interface AdminTransientFormFiles {
  cardImage?: AdminTransientFileItem | null;
  coverImage?: AdminTransientFileItem | null;
  galleryImages?: AdminTransientFileItem[];
}

@Injectable({ providedIn: 'root' })
export class AdminFormTransientFileStoreService {
  private readonly mediaByFormType = new Map<string, AdminTransientFormFiles>();

  save(formType: string, media: AdminTransientFormFiles): void {
    this.mediaByFormType.set(formType, {
      cardImage: media.cardImage ? { ...media.cardImage } : null,
      coverImage: media.coverImage ? { ...media.coverImage } : null,
      galleryImages: [...(media.galleryImages ?? [])].map(image => ({ ...image })),
    });
  }

  peek(formType: string): AdminTransientFormFiles | null {
    return this.mediaByFormType.get(formType) ?? null;
  }

  consume(formType: string): AdminTransientFormFiles | null {
    const media = this.peek(formType);
    if (media) {
      this.mediaByFormType.delete(formType);
    }

    return media;
  }

  clear(formType: string): void {
    this.mediaByFormType.delete(formType);
  }
}

export { AdminFormTransientFileStoreService as AdminFormTransientMediaService };
export type AdminTransientMediaItem = AdminTransientFileItem;
export type AdminTransientFormMedia = AdminTransientFormFiles;
