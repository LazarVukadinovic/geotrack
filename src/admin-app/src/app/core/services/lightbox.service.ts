import { Injectable, NgZone } from '@angular/core';

export interface LightboxImage {
  url: string;
  name: string;
}

@Injectable({ providedIn: 'root' })
export class LightboxService {

  private lightboxEl: HTMLElement | null = null;
  private keyHandler: ((e: KeyboardEvent) => void) | null = null;
  private images: LightboxImage[] = [];
  private currentIndex = 0;

  constructor(private zone: NgZone) {}

  open(images: LightboxImage[], index: number): void {
    this.zone.run(() => {
      this.close();
      this.images = [...images];
      this.currentIndex = index;
      this._render();
      this.keyHandler = (e: KeyboardEvent) => {
        if (e.key === 'Escape')     { this.close(); }
        if (e.key === 'ArrowRight') { this.currentIndex = (this.currentIndex + 1) % this.images.length; this._render(); }
        if (e.key === 'ArrowLeft')  { this.currentIndex = (this.currentIndex - 1 + this.images.length) % this.images.length; this._render(); }
      };
      document.addEventListener('keydown', this.keyHandler);
      document.body.style.overflow = 'hidden';
    });
  }

  close(): void {
    if (this.lightboxEl) { document.body.removeChild(this.lightboxEl); this.lightboxEl = null; }
    if (this.keyHandler) { document.removeEventListener('keydown', this.keyHandler); this.keyHandler = null; }
    document.body.style.overflow = '';
  }

  private _render(): void {
    if (this.lightboxEl) document.body.removeChild(this.lightboxEl);

    const imgs = this.images;
    const idx = this.currentIndex;
    const single = imgs.length === 1;

    const overlay = document.createElement('div');
    overlay.style.cssText = 'position:fixed;inset:0;z-index:99999;background:rgba(0,0,0,0.92);display:flex;align-items:center;justify-content:center;';
    overlay.addEventListener('click', () => this.close());

    // Close
    const closeBtn = document.createElement('button');
    closeBtn.innerHTML = '&times;';
    closeBtn.style.cssText = 'position:absolute;top:16px;right:16px;width:40px;height:40px;border-radius:50%;background:rgba(255,255,255,0.15);border:none;color:white;font-size:24px;cursor:pointer;display:flex;align-items:center;justify-content:center;z-index:1;';
    closeBtn.addEventListener('click', (e) => { e.stopPropagation(); this.close(); });
    overlay.appendChild(closeBtn);

    if (!single) {
      // Counter
      const counter = document.createElement('div');
      counter.textContent = `${idx + 1} / ${imgs.length}`;
      counter.style.cssText = 'position:absolute;top:16px;left:50%;transform:translateX(-50%);color:white;font-size:14px;font-weight:500;background:rgba(0,0,0,0.4);padding:4px 12px;border-radius:20px;z-index:1;';
      overlay.appendChild(counter);

      // Prev
      const prev = document.createElement('button');
      prev.innerHTML = '&#8249;';
      prev.style.cssText = 'position:absolute;left:16px;top:50%;transform:translateY(-50%);width:44px;height:44px;border-radius:50%;background:rgba(255,255,255,0.15);border:none;color:white;font-size:28px;cursor:pointer;display:flex;align-items:center;justify-content:center;z-index:1;';
      prev.addEventListener('click', (e) => { e.stopPropagation(); this.currentIndex = (idx - 1 + imgs.length) % imgs.length; this._render(); });
      overlay.appendChild(prev);
    }

    // Main image
    const img = document.createElement('img');
    img.src = imgs[idx].url;
    img.alt = imgs[idx].name;
    img.style.cssText = 'max-width:88vw;max-height:80vh;object-fit:contain;border-radius:12px;box-shadow:0 25px 60px rgba(0,0,0,0.5);';
    img.addEventListener('click', (e) => e.stopPropagation());
    overlay.appendChild(img);

    if (!single) {
      // Next
      const next = document.createElement('button');
      next.innerHTML = '&#8250;';
      next.style.cssText = 'position:absolute;right:16px;top:50%;transform:translateY(-50%);width:44px;height:44px;border-radius:50%;background:rgba(255,255,255,0.15);border:none;color:white;font-size:28px;cursor:pointer;display:flex;align-items:center;justify-content:center;z-index:1;';
      next.addEventListener('click', (e) => { e.stopPropagation(); this.currentIndex = (idx + 1) % imgs.length; this._render(); });
      overlay.appendChild(next);

      // Thumbnails
      const thumbRow = document.createElement('div');
      thumbRow.style.cssText = 'position:absolute;bottom:16px;left:50%;transform:translateX(-50%);display:flex;gap:8px;z-index:1;';
      thumbRow.addEventListener('click', (e) => e.stopPropagation());
      imgs.forEach((im, ti) => {
        const thumb = document.createElement('div');
        thumb.style.cssText = `width:48px;height:48px;border-radius:8px;overflow:hidden;cursor:pointer;border:2px solid ${ti === idx ? 'white' : 'transparent'};opacity:${ti === idx ? '1' : '0.5'};transition:all 0.15s;`;
        const tImg = document.createElement('img');
        tImg.src = im.url;
        tImg.style.cssText = 'width:100%;height:100%;object-fit:cover;';
        tImg.addEventListener('click', (e) => { e.stopPropagation(); this.currentIndex = ti; this._render(); });
        thumb.appendChild(tImg);
        thumbRow.appendChild(thumb);
      });
      overlay.appendChild(thumbRow);
    }

    document.body.appendChild(overlay);
    this.lightboxEl = overlay;
  }
}