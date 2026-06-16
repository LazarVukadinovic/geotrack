import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { StepBasicsComponent } from './step-basics.component';

describe('StepBasicsComponent', () => {
  let component: StepBasicsComponent;
  let fixture: ComponentFixture<StepBasicsComponent>;
  let httpTesting: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StepBasicsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    })
    .compileComponents();

    httpTesting = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(StepBasicsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    flushInitialCatalogRequests();
    await fixture.whenStable();
  });

  afterEach(() => {
    httpTesting.verify();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('requires a confirmed map location before continuing', () => {
    component.form.name = 'Validation Villa';
    component.form.city = 'Dubrovnik';
    component.form.country = 'Croatia';

    component.onContinue();

    expect(component.isValid()).toBe(false);
    expect(component.showLocationError()).toBe(true);
  });

  it('accepts valid confirmed coordinates', () => {
    component.form.name = 'Validation Villa';
    component.form.city = 'Dubrovnik';
    component.form.country = 'Croatia';
    component.form.imageUrl = '/media/uploads/contributions/cover.webp';
    component.form.cardImageUrl = '/media/uploads/contributions/card.webp';
    component.form.lat = 42.6507;
    component.form.lng = 18.0944;
    component.form.destinationId = 1;
    component.form.locationId = 1;

    expect(component.isValid()).toBe(true);
    expect(component.hasConfirmedLocation()).toBe(true);
  });

  for (const placeType of ['Stay', 'Restaurant', 'Cafe', 'Bar']) {
    it(`supports an optional proof upload for ${placeType}`, () => {
      component.form.placeType = placeType;

      expect(component.supportsOwnershipProof()).toBe(true);
    });
  }

  for (const placeType of ['Landmark', 'Nature', 'Beach']) {
    it(`does not show proof upload for ${placeType}`, () => {
      component.form.placeType = placeType;

      expect(component.supportsOwnershipProof()).toBe(false);
    });
  }

  for (const placeType of ['Stay', 'Restaurant', 'Cafe', 'Bar']) {
    it(`shows ownership warning without proof for ${placeType}`, () => {
      fillValidLocation();
      component.form.placeType = placeType;

      expect(component.isValid()).toBe(true);
      expect(component.showOwnershipWarning()).toBe(true);
    });
  }

  it('preserves an uploaded Restaurant proof in memory but omits it for an unrelated type', async () => {
    fillValidLocation();
    component.selectPlaceType({ id: 2, name: 'Restaurant', description: null, isOwnable: true });
    component.form.ownershipProofFile = makePdfFile();

    expect(component.isValid()).toBe(true);

    component.selectPlaceType({ id: 1, name: 'Landmark', description: null, isOwnable: false });

    expect(component.supportsOwnershipProof()).toBe(false);
    expect(component.form.ownershipProofFile).not.toBeNull();
    expect(component.isValid()).toBe(true);

    let savedProof: File | null | undefined;
    component.saved.subscribe(value => savedProof = value.ownershipProofFile);
    await component.onContinue();

    expect(savedProof).toBeNull();
  });

  it('keeps existing ownership proof locked for approved owner edits', () => {
    fixture.componentRef.setInput('ownershipProofLocked', true);
    component.form.ownershipProofDocumentUrl = '/media/uploads/contributions/existing-proof.pdf';
    component.form.ownershipProofOriginalFileName = 'existing-proof.pdf';

    const input = document.createElement('input');
    Object.defineProperty(input, 'files', { value: [makePdfFile()] });

    component.onOwnershipProofSelected({ target: input } as unknown as Event);
    component.selectPlaceType({ id: 1, name: 'Landmark', description: null, isOwnable: false });

    expect(component.form.ownershipProofFile).toBeNull();
    expect(component.form.ownershipProofDocumentUrl).toBe('/media/uploads/contributions/existing-proof.pdf');
    expect(component.form.ownershipProofOriginalFileName).toBe('existing-proof.pdf');
  });

  it('blocks adding gallery images after the 10 image limit', () => {
    const urls = Array.from({ length: 10 }, (_, index) => `/media/gallery-${index}.webp`);
    component.galleryPreviewUrls.set(urls);
    component.form.galleryImageUrls = [...urls];
    component.form.galleryImageFiles = urls.map(() => null);

    const input = document.createElement('input');
    Object.defineProperty(input, 'files', { value: [makeImageFile()] });

    component.onGalleryImagesSelected({ target: input } as unknown as Event);

    expect(component.galleryPreviewUrls().length).toBe(10);
    expect(component.galleryError()).toBe('You can add up to 10 gallery images.');
  });

  it('removes the active lightbox gallery image without removing previous images first', () => {
    const urls = Array.from({ length: 8 }, (_, index) => `/media/gallery-${index}.webp`);
    component.galleryPreviewUrls.set(urls);
    component.form.galleryImageUrls = [...urls];
    component.form.galleryImageFiles = urls.map(() => null);
    component.openGalleryLightbox(7);

    component.removeGalleryImage(7);

    expect(component.form.galleryImageUrls).toEqual(urls.slice(0, 7));
    expect(component.galleryLightboxIndex()).toBe(6);
  });

  function fillValidLocation(): void {
    component.form.name = 'Validation Place';
    component.form.city = 'Dubrovnik';
    component.form.country = 'Croatia';
    component.form.imageUrl = '/media/uploads/contributions/cover.webp';
    component.form.cardImageUrl = '/media/uploads/contributions/card.webp';
    component.form.lat = 42.6507;
    component.form.lng = 18.0944;
    component.form.destinationId = 1;
    component.form.locationId = 1;
  }

  function makePdfFile(): File {
    return new File(['%PDF-1.4\n% proof\n'], 'ownership-proof.pdf', { type: 'application/pdf' });
  }

  function makeImageFile(): File {
    return new File(['image'], 'gallery.webp', { type: 'image/webp' });
  }

  function flushInitialCatalogRequests(): void {
    httpTesting.expectOne('/api/contributions/place-types').flush([]);
    httpTesting.expectOne('/api/contributions/stay-types').flush([]);
  }

});
