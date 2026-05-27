import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormsModule } from '@angular/forms';
import { of } from 'rxjs';
import { vi } from 'vitest';

import { SupportTicketsComponent } from './support-tickets.component';
import { AdminDashboardService } from '../../../../core/services/administrator/admin-dashboard.service';
import { AdminSupportTicketsService } from '../../../../core/services/administrator/admin-support-tickets.service';
import {
  AdminSupportTicketDetail,
  AdminSupportTicketListItem,
  AdminSupportTicketPageRequest,
  AdminSupportTicketsPage,
} from '../../../../core/models/administrator/admin-support-ticket.model';

@Component({
  selector: 'app-sidebar',
  standalone: true,
  template: '<aside>{{ supportReportsCount }}</aside>',
})
class SidebarStubComponent {
  @Input() supportReportsCount: number | null = null;
}

@Component({
  selector: 'app-topbar',
  standalone: true,
  template: '<header>{{ title }}</header>',
})
class TopbarStubComponent {
  @Input() title = '';
  @Input() showBack = false;
  @Input() backRoute = '';
}

describe('SupportTicketsComponent', () => {
  let fixture: ComponentFixture<SupportTicketsComponent>;
  let component: SupportTicketsComponent;
  let ticketsService: {
    getPage$: ReturnType<typeof vi.fn>;
    getDetail$: ReturnType<typeof vi.fn>;
    markInProgress$: ReturnType<typeof vi.fn>;
    resolve$: ReturnType<typeof vi.fn>;
    dismiss$: ReturnType<typeof vi.fn>;
    reopen$: ReturnType<typeof vi.fn>;
    saveNote$: ReturnType<typeof vi.fn>;
    formatDate: ReturnType<typeof vi.fn>;
    formatDatetime: ReturnType<typeof vi.fn>;
  };

  beforeEach(async () => {
    ticketsService = {
      getPage$: vi.fn((req: AdminSupportTicketPageRequest) => of(pageForRequest(req))),
      getDetail$: vi.fn((ticketId: number) => of(detailTicket(ticketId))),
      markInProgress$: vi.fn((ticketId: number, note?: string | null) => of({ ...detailTicket(ticketId), status: 'InProgress', internalNote: note ?? null })),
      resolve$: vi.fn((ticketId: number, note?: string | null) => of({ ...detailTicket(ticketId), status: 'Resolved', internalNote: note ?? null })),
      dismiss$: vi.fn((ticketId: number, note?: string | null) => of({ ...detailTicket(ticketId), status: 'Dismissed', internalNote: note ?? null })),
      reopen$: vi.fn((ticketId: number, note?: string | null) => of({ ...detailTicket(ticketId), status: 'Open', internalNote: note ?? null })),
      saveNote$: vi.fn((ticketId: number, note: string) => of({ ...detailTicket(ticketId), internalNote: note })),
      formatDate: vi.fn((value: string | null | undefined) => value ?? ''),
      formatDatetime: vi.fn((value: string | null | undefined) => value ?? ''),
    };

    await TestBed.configureTestingModule({
      imports: [SupportTicketsComponent],
      providers: [
        { provide: AdminSupportTicketsService, useValue: ticketsService },
        { provide: AdminDashboardService, useValue: { loadDashboard: vi.fn(() => of({ badges: { supportReportsCount: 1 } })) } },
      ],
    })
      .overrideComponent(SupportTicketsComponent, {
        set: { imports: [CommonModule, FormsModule, SidebarStubComponent, TopbarStubComponent] },
      })
      .compileComponents();

    fixture = TestBed.createComponent(SupportTicketsComponent);
    component = fixture.componentInstance;
  });

  it('loads the initial support ticket page with take 25', () => {
    fixture.detectChanges();

    expect(ticketsService.getPage$).toHaveBeenCalledWith(expect.objectContaining({
      status: 'open',
      skip: 0,
      take: 25,
    }));
    expect(normalizedText()).toContain('Map issue');
  });

  it('appends tickets when Show More is clicked', () => {
    fixture.detectChanges();

    clickButtonContaining('Show More');

    expect(ticketsService.getPage$).toHaveBeenLastCalledWith(expect.objectContaining({
      skip: 25,
      take: 25,
    }));
    expect(normalizedText()).toContain('Second issue');
  });

  it('opens detail and resolves with the internal note', () => {
    fixture.detectChanges();

    component.openDetail(ticketItem(1));
    fixture.detectChanges();

    expect(ticketsService.getDetail$).toHaveBeenCalledWith(1);
    component.detailInternalNote = 'Handled internally.';
    component.resolveTicket(component.detailTicket!);

    expect(ticketsService.resolve$).toHaveBeenCalledWith(1, 'Handled internally.');
  });

  function clickButtonContaining(label: string): void {
    const buttons = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>);
    const button = buttons.find(item => item.textContent?.includes(label));
    expect(button).toBeTruthy();
    button!.click();
    fixture.detectChanges();
  }

  function normalizedText(): string {
    return (fixture.nativeElement.textContent as string).replace(/\s+/g, ' ').trim();
  }
});

function pageForRequest(req: AdminSupportTicketPageRequest): AdminSupportTicketsPage {
  const item = req.skip === 0 ? ticketItem(1, 'Map issue') : ticketItem(2, 'Second issue');

  return {
    items: [item],
    totalCount: 2,
    total: 2,
    skip: req.skip,
    take: req.take,
    nextSkip: req.skip === 0 ? 25 : null,
    hasMore: req.skip === 0,
    counts: {
      openCount: 1,
      inProgressCount: 0,
      resolvedCount: 0,
      dismissedCount: 0,
      allCount: 2,
    },
  };
}

function ticketItem(ticketId: number, subject = 'Map issue'): AdminSupportTicketListItem {
  return {
    ticketId,
    referenceCode: `GT-${String(ticketId).padStart(6, '0')}`,
    reporter: {
      userId: 12,
      displayName: 'Support Reporter',
      email: 'reporter@example.com',
      initials: 'SR',
      isAnonymous: false,
      avatarColor: '#2563eb',
    },
    category: 'location-and-maps',
    categoryLabel: 'Location and maps',
    subject,
    messagePreview: 'The map stayed in the wrong place.',
    status: 'Open',
    priority: 'Normal',
    pageUrl: '/settings/contact-support',
    appRoute: '/settings/contact-support',
    createdAt: '2026-05-26T20:00:00Z',
    hasAttachment: false,
    permissions: {
      canView: true,
      canMarkInProgress: true,
      canResolve: true,
      canDismiss: true,
      canReopen: false,
      canAddNote: true,
    },
  };
}

function detailTicket(ticketId: number): AdminSupportTicketDetail {
  return {
    ...ticketItem(ticketId),
    message: 'The map stayed in the wrong place.',
    statusHistory: [],
    internalNote: null,
    resolutionMessage: null,
  };
}
