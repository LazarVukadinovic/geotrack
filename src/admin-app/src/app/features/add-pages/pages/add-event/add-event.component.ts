import { Component, ElementRef, ViewChild, NgZone, ChangeDetectorRef, AfterViewInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SidebarComponent } from '../../../../shared/components/sidebar/sidebar.component';
import { TopbarComponent } from '../../../../shared/components/topbar/topbar.component';

interface ClassItem {
  label: string;
  checked: boolean;
}

@Component({
  selector: 'app-new-event',
  standalone: true,
  imports: [CommonModule, FormsModule, SidebarComponent, TopbarComponent],
  templateUrl: './add-event.component.html',
  styleUrl: './add-event.component.css',
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
export class NewEventComponent implements AfterViewInit, OnDestroy {
 
  constructor(private zone: NgZone, private cdr: ChangeDetectorRef) {}
 
  @ViewChild('editorRef') editorRef!: ElementRef<HTMLDivElement>;
 
  // ── Basic fields ──────────────────────────────────────────────
  eventTitle = 'Summer Festival';
  venueType  = 'location';
  allDay     = false;
  freeEvent  = false;
  recurring  = false;
 
  // ── Rich text editor ─────────────────────────────────────────
  isBold        = false;
  isItalic      = false;
  isUnderline   = false;
  editorIsEmpty = true;
 
  execFormat(command: 'bold' | 'italic' | 'underline'): void {
    document.execCommand(command, false, undefined);
    this.editorRef.nativeElement.focus();
    this.syncToolbarState();
  }
 
  onEditorChange(): void { this.syncToolbarState(); this.syncEmptyState(); }
  onEditorFocus(): void  { this.syncToolbarState(); this.syncEmptyState(); }
  onEditorBlur(): void {
    this.syncToolbarState();
    const el = this.editorRef?.nativeElement;
    if (el && (el.innerHTML === '<br>' || el.innerHTML === '&nbsp;' || el.innerHTML.trim() === '')) {
      el.innerHTML = '';
    }
    this.syncEmptyState();
  }
 
  private syncEmptyState(): void {
    const el = this.editorRef?.nativeElement;
    if (!el) return;
    const text = el.innerText ?? el.textContent ?? '';
    this.editorIsEmpty = text.trim() === '' && el.innerHTML !== '<img' && !el.querySelector('img');
  }
 
  private syncToolbarState(): void {
    this.isBold      = document.queryCommandState('bold');
    this.isItalic    = document.queryCommandState('italic');
    this.isUnderline = document.queryCommandState('underline');
  }
 
  // ── Cover image ───────────────────────────────────────────────
  coverImageUrl : string | null = null;
  coverImageName: string        = '';
 
  onCoverImageSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file  = input.files?.[0];
    if (!file) return;
    this.coverImageUrl  = URL.createObjectURL(file);
    this.coverImageName = file.name;
    input.value = '';
  }
 
  removeCoverImage(): void {
    if (this.coverImageUrl) URL.revokeObjectURL(this.coverImageUrl);
    this.coverImageUrl  = null;
    this.coverImageName = '';
  }
 
  // ── Gallery ───────────────────────────────────────────────────
  galleryImages: { url: string; name: string }[] = [];
 
  onGalleryImagesSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    for (const file of files) {
      this.galleryImages.push({ url: URL.createObjectURL(file), name: file.name });
    }
    input.value = '';
  }
 
  removeGalleryImage(index: number): void {
    URL.revokeObjectURL(this.galleryImages[index].url);
    this.galleryImages.splice(index, 1);
  }
 
  // ── Lightbox ──────────────────────────────────────────────────
  private lightboxEl: HTMLElement | null = null;
  private _lightboxKeyHandler: ((e: KeyboardEvent) => void) | null = null;
  private _currentImages: { url: string; name: string }[] = [];
  private _currentIndex = 0;
 
  openLightbox(images: { url: string; name: string }[], index: number): void {
    this.zone.run(() => {
      this.closeLightbox();
      this._currentImages = [...images];
      this._currentIndex  = index;
      this._renderLightbox();
 
      this._lightboxKeyHandler = (e: KeyboardEvent) => {
        if (e.key === 'Escape')     { this.closeLightbox(); }
        if (e.key === 'ArrowRight') { this._currentIndex = (this._currentIndex + 1) % this._currentImages.length; this._renderLightbox(); }
        if (e.key === 'ArrowLeft')  { this._currentIndex = (this._currentIndex - 1 + this._currentImages.length) % this._currentImages.length; this._renderLightbox(); }
      };
      document.addEventListener('keydown', this._lightboxKeyHandler);
      document.body.style.overflow = 'hidden';
    });
  }
 
  private _renderLightbox(): void {
    if (this.lightboxEl) document.body.removeChild(this.lightboxEl);
    const imgs   = this._currentImages;
    const idx    = this._currentIndex;
    const single = imgs.length === 1;
 
    const overlay = document.createElement('div');
    overlay.style.cssText = 'position:fixed;inset:0;z-index:99999;background:rgba(0,0,0,0.92);display:flex;align-items:center;justify-content:center;';
    overlay.addEventListener('click', () => this.closeLightbox());
 
    const closeBtn = document.createElement('button');
    closeBtn.innerHTML = '&times;';
    closeBtn.style.cssText = 'position:absolute;top:16px;right:16px;width:40px;height:40px;border-radius:50%;background:rgba(255,255,255,0.15);border:none;color:white;font-size:24px;cursor:pointer;display:flex;align-items:center;justify-content:center;z-index:1;';
    closeBtn.addEventListener('click', (e) => { e.stopPropagation(); this.closeLightbox(); });
    overlay.appendChild(closeBtn);
 
    if (!single) {
      const counter = document.createElement('div');
      counter.textContent = `${idx + 1} / ${imgs.length}`;
      counter.style.cssText = 'position:absolute;top:16px;left:50%;transform:translateX(-50%);color:white;font-size:14px;font-weight:500;background:rgba(0,0,0,0.4);padding:4px 12px;border-radius:20px;z-index:1;';
      overlay.appendChild(counter);
 
      const prev = document.createElement('button');
      prev.innerHTML = '&#8249;';
      prev.style.cssText = 'position:absolute;left:16px;top:50%;transform:translateY(-50%);width:44px;height:44px;border-radius:50%;background:rgba(255,255,255,0.15);border:none;color:white;font-size:28px;cursor:pointer;display:flex;align-items:center;justify-content:center;z-index:1;';
      prev.addEventListener('click', (e) => { e.stopPropagation(); this._currentIndex = (idx - 1 + imgs.length) % imgs.length; this._renderLightbox(); });
      overlay.appendChild(prev);
    }
 
    const img = document.createElement('img');
    img.src = imgs[idx].url;
    img.alt = imgs[idx].name;
    img.style.cssText = 'max-width:88vw;max-height:80vh;object-fit:contain;border-radius:12px;box-shadow:0 25px 60px rgba(0,0,0,0.5);';
    img.addEventListener('click', (e) => e.stopPropagation());
    overlay.appendChild(img);
 
    if (!single) {
      const next = document.createElement('button');
      next.innerHTML = '&#8250;';
      next.style.cssText = 'position:absolute;right:16px;top:50%;transform:translateY(-50%);width:44px;height:44px;border-radius:50%;background:rgba(255,255,255,0.15);border:none;color:white;font-size:28px;cursor:pointer;display:flex;align-items:center;justify-content:center;z-index:1;';
      next.addEventListener('click', (e) => { e.stopPropagation(); this._currentIndex = (idx + 1) % imgs.length; this._renderLightbox(); });
      overlay.appendChild(next);
 
      const thumbRow = document.createElement('div');
      thumbRow.style.cssText = 'position:absolute;bottom:16px;left:50%;transform:translateX(-50%);display:flex;gap:8px;z-index:1;';
      thumbRow.addEventListener('click', (e) => e.stopPropagation());
      imgs.forEach((im, ti) => {
        const thumb = document.createElement('div');
        thumb.style.cssText = `width:48px;height:48px;border-radius:8px;overflow:hidden;cursor:pointer;border:2px solid ${ti === idx ? 'white' : 'transparent'};opacity:${ti === idx ? '1' : '0.5'};transition:all 0.15s;`;
        const tImg = document.createElement('img');
        tImg.src = im.url;
        tImg.style.cssText = 'width:100%;height:100%;object-fit:cover;';
        tImg.addEventListener('click', (e) => { e.stopPropagation(); this._currentIndex = ti; this._renderLightbox(); });
        thumb.appendChild(tImg);
        thumbRow.appendChild(thumb);
      });
      overlay.appendChild(thumbRow);
    }
 
    document.body.appendChild(overlay);
    this.lightboxEl = overlay;
  }
 
  closeLightbox(): void {
    if (this.lightboxEl) { document.body.removeChild(this.lightboxEl); this.lightboxEl = null; }
    if (this._lightboxKeyHandler) { document.removeEventListener('keydown', this._lightboxKeyHandler); this._lightboxKeyHandler = null; }
    document.body.style.overflow = '';
  }
 
  // ── Classification ────────────────────────────────────────────
  classItems: ClassItem[] = [
    { label: 'Music',           checked: true  },
    { label: 'Outdoor',         checked: true  },
    { label: 'Culture',         checked: false },
    { label: 'Family Friendly', checked: false },
  ];
 
  // ── Lifecycle ─────────────────────────────────────────────────
  ngAfterViewInit(): void {}
 
  ngOnDestroy(): void {
    this.closeLightbox();
    this.galleryImages.forEach(img => URL.revokeObjectURL(img.url));
    if (this.coverImageUrl) URL.revokeObjectURL(this.coverImageUrl);
  }
}