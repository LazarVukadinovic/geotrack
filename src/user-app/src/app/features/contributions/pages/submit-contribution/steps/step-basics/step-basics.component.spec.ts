import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';

import { StepBasicsComponent } from './step-basics.component';

describe('StepBasicsComponent', () => {
  let component: StepBasicsComponent;
  let fixture: ComponentFixture<StepBasicsComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StepBasicsComponent],
      providers: [provideHttpClient()],
    })
    .compileComponents();

    fixture = TestBed.createComponent(StepBasicsComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
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
    component.form.lat = 42.6507;
    component.form.lng = 18.0944;

    expect(component.isValid()).toBe(true);
    expect(component.hasConfirmedLocation()).toBe(true);
  });

  for (const placeType of ['Stay', 'Restaurant', 'Cafe']) {
    it(`requires proof upload for ${placeType}`, () => {
      component.form.placeType = placeType;

      expect(component.requiresOwnershipProof()).toBe(true);
    });
  }

  for (const placeType of ['Landmark', 'Nature', 'Beach']) {
    it(`does not require proof upload for ${placeType}`, () => {
      component.form.placeType = placeType;

      expect(component.requiresOwnershipProof()).toBe(false);
    });
  }

  for (const placeType of ['Stay', 'Restaurant', 'Cafe']) {
    it(`blocks continue without proof for ${placeType}`, () => {
      fillValidLocation();
      component.form.placeType = placeType;

      expect(component.isValid()).toBe(false);
    });
  }

  it('allows valid Restaurant proof and clears it when switching to an unrelated type', () => {
    fillValidLocation();
    component.selectPlaceType({ id: 2, name: 'Restaurant', description: null, isOwnable: true });
    component.form.ownershipProofFile = makePdfFile();

    expect(component.isValid()).toBe(true);

    component.selectPlaceType({ id: 1, name: 'Landmark', description: null, isOwnable: false });

    expect(component.requiresOwnershipProof()).toBe(false);
    expect(component.form.ownershipProofFile).toBeNull();
    expect(component.isValid()).toBe(true);
  });

  function fillValidLocation(): void {
    component.form.name = 'Validation Place';
    component.form.city = 'Dubrovnik';
    component.form.country = 'Croatia';
    component.form.lat = 42.6507;
    component.form.lng = 18.0944;
  }

  function makePdfFile(): File {
    return new File(['%PDF-1.4\n% proof\n'], 'ownership-proof.pdf', { type: 'application/pdf' });
  }

});
