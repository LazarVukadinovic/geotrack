import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { vi } from 'vitest';

import { NewEventComponent } from './add-event.component';

@Component({
  standalone: true,
  template: '',
})
class EventDetailRouteStubComponent {}

describe('NewEventComponent', () => {
  let component: NewEventComponent;
  let fixture: ComponentFixture<NewEventComponent>;
  let http: HttpTestingController;
  let router: Router;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [NewEventComponent],
      providers: [
        provideRouter([{ path: 'events/detail/:id', component: EventDetailRouteStubComponent }]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(NewEventComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => {
    http.verify();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('saves edited event fields and navigates to refreshed detail page', () => {
    const navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    component.isEditMode = true;
    component.editEventId = '42';
    component.activeRegionId = 7;
    component.eventTitle = 'Updated title';
    component.summary = 'Updated summary';
    component.eventTypeId = 3;
    component.cardImageUrl = '/media/seed/events/card.webp';
    component.coverImageUrl = '/media/seed/events/cover.webp';
    component.venueType = 'location';
    component.selectedLocationId = 11;
    component.selectedDestinationId = 5;
    component.startDate = '2026-06-10';
    component.startTime = '14:30';
    component.endDate = '2026-06-10';
    component.endTime = '16:00';
    component.customVenueName = 'Updated venue';
    component.recurring = true;
    component.freeEvent = false;
    component.ticketPrice = '25';
    component.externalTicketLink = 'https://tickets.example.com/event';
    component.organizerName = 'Updated organizer';
    component.contactEmail = 'events@example.com';
    component.attendanceLimit = '150';
    component.ageRestriction = '16+';

    component.publish();

    const request = http.expectOne('/api/admin/regional/events/42?regionId=7');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual(expect.objectContaining({
      parentEntityType: 'Location',
      destinationId: 5,
      locationId: 11,
      placeId: null,
      eventTypeId: 3,
      name: 'Updated title',
      summary: 'Updated summary',
      venueName: 'Updated venue',
      isRecurring: true,
      isFree: false,
      ticketPrice: '25',
      externalTicketLink: 'https://tickets.example.com/event',
      organizerName: 'Updated organizer',
      contactEmail: 'events@example.com',
      attendanceLimit: 150,
      ageRestriction: '16+',
    }));

    request.flush({
      id: 42,
      name: 'Updated title',
      summary: 'Updated summary',
      description: '',
      status: 'Published',
      parentEntityType: 'Location',
      regionId: 7,
      regionDisplayName: 'Region',
      destinationId: 5,
      destinationName: 'Destination',
      locationId: 11,
      locationName: 'Location',
      eventTypeId: 3,
      eventType: 'Festival',
      category: 'Festival',
      startsAt: '2026-06-10T14:30:00Z',
      endsAt: '2026-06-10T16:00:00Z',
      isAllDay: false,
      isRecurring: true,
      isFree: false,
      ticketPrice: '25',
      externalTicketLink: 'https://tickets.example.com/event',
      hostedBy: 'Updated organizer',
      contactEmail: 'events@example.com',
      attendanceLimit: 150,
      ageRestriction: '16+',
      createdAt: '2026-06-01T00:00:00Z',
      lastUpdatedAt: '2026-06-04T00:00:00Z',
      tags: [],
      images: [],
    });

    expect(navigateSpy).toHaveBeenCalledWith(['/events/detail', 42], {
      queryParams: { regionId: 7 },
      state: { regionId: 7, feedbackMessage: 'Event updated.' },
    });
  });

  it('uploads changed edit images before saving the event image set', () => {
    component.isEditMode = true;
    component.editEventId = '42';
    component.activeRegionId = 7;
    component.eventTitle = 'Updated title';
    component.summary = 'Updated summary';
    component.eventTypeId = 3;
    component.cardImageUrl = 'data:image/png;base64,card';
    component.cardImageName = 'new-card.png';
    component.coverImageUrl = '/media/seed/events/cover.webp';
    component.coverImageName = 'Existing cover image';
    component.venueType = 'location';
    component.selectedLocationId = 11;
    component.selectedDestinationId = 5;
    component.startDate = '2026-06-10';
    component.startTime = '14:30';
    component.endDate = '2026-06-10';
    component.endTime = '16:00';
    (component as any).cardImageFile = new File(['card'], 'new-card.png', { type: 'image/png' });
    (component as any).existingImages = [
      { url: '/media/seed/events/card.webp', isMain: true, sortOrder: 0 },
      { url: '/media/seed/events/cover.webp', isMain: false, sortOrder: 1 },
      { url: '/media/seed/events/gallery.webp', isMain: false, sortOrder: 2 },
    ];
    component.galleryImages = [{ url: '/media/seed/events/gallery.webp', name: 'Existing gallery' }];

    component.publish();

    const uploadRequest = http.expectOne('/api/admin/regional/media/images?regionId=7');
    expect(uploadRequest.request.method).toBe('POST');
    expect(uploadRequest.request.body instanceof FormData).toBe(true);
    uploadRequest.flush({ images: [{ url: '/media/uploads/regional/new-card.png' }] });

    const updateRequest = http.expectOne('/api/admin/regional/events/42?regionId=7');
    expect(updateRequest.request.method).toBe('PUT');
    expect(updateRequest.request.body.images).toEqual([
      {
        url: '/media/uploads/regional/new-card.png',
        isMain: true,
        sortOrder: 0,
        altText: 'Updated title',
        caption: null,
      },
      {
        url: '/media/seed/events/cover.webp',
        isMain: false,
        sortOrder: 1,
        altText: 'Updated title',
        caption: null,
      },
      {
        url: '/media/seed/events/gallery.webp',
        isMain: false,
        sortOrder: 2,
        altText: null,
        caption: null,
      },
    ]);
    updateRequest.flush({
      id: 42,
      name: 'Updated title',
      summary: 'Updated summary',
      description: '',
      status: 'Published',
      parentEntityType: 'Location',
      regionId: 7,
      regionDisplayName: 'Region',
      destinationId: 5,
      destinationName: 'Destination',
      locationId: 11,
      locationName: 'Location',
      eventTypeId: 3,
      eventType: 'Festival',
      category: 'Festival',
      startsAt: '2026-06-10T14:30:00Z',
      endsAt: '2026-06-10T16:00:00Z',
      isAllDay: false,
      isRecurring: false,
      isFree: true,
      createdAt: '2026-06-01T00:00:00Z',
      lastUpdatedAt: '2026-06-04T00:00:00Z',
      tags: [],
      images: [],
    });
  });
});
