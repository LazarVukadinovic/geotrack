import { Component, DestroyRef, inject, signal } from '@angular/core';
import { CommonModule, Location as BrowserLocation } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { combineLatest } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

interface DetailParentLink {
  label: string;
  name: string;
  route: string;
}

interface DetailSectionPreview {
  sectionKey?: string;
  sectionTitle?: string;
  title?: string;
  name?: string;
  places?: unknown[];
  events?: unknown[];
  locations?: unknown[];
}

interface HierarchyDetail {
  id: string;
  title: string;
  typeLabel: string;
  description?: string;
  latitude?: number | null;
  longitude?: number | null;
  rating?: number | null;
  reviewsCount?: number | null;
  parent?: DetailParentLink | null;
  sections: DetailSectionPreview[];
}

type EntityType = 'location' | 'destination' | 'region' | 'territory';
type LoadState = 'loading' | 'loaded' | 'notFound' | 'error';



@Component({
  selector: 'app-entity-hierarchy-detail-page',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './entity-hierarchy-detail-page.component.html',
  styleUrl: './entity-hierarchy-detail-page.component.scss',
})
export class EntityHierarchyDetailPageComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  private readonly browserLocation = inject(BrowserLocation);

  protected readonly detail = signal<HierarchyDetail | null>(null);
  protected readonly loadState = signal<LoadState>('loading');

  protected readonly expandedSectionIndex = signal<number | null>(null);

  protected toggleSection(index: number): void {
    this.expandedSectionIndex.update(current => current === index ? null : index);
  }

  protected sectionCount(section: DetailSectionPreview): number {
    return (section.places?.length ?? 0) +
          (section.events?.length ?? 0) +
          (section.locations?.length ?? 0);
  }

  protected sectionItems(section: DetailSectionPreview): { name: string; route?: string }[] {
    const all = [
    ...(section.places ?? []),
    ...(section.events ?? []),
    ...(section.locations ?? []),
  ] as Record<string, unknown>[];
    return all.map(item => ({
    name: String(item['name'] ?? item['title'] ?? 'Item'),
    route: item['route'] ? String(item['route']) : undefined,
  }));
  }

  protected readonly fullExpandedSections = signal<Set<number>>(new Set());

  protected toggleFullExpand(index: number, event: Event): void {
    event.stopPropagation();
    this.fullExpandedSections.update(current => {
      const next = new Set(current);
      if (next.has(index)) {
        next.delete(index);
      } else {
        next.add(index);
      }
      return next;
    });
  }

  protected isFullExpanded(index: number): boolean {
    return this.fullExpandedSections().has(index);
  }

  constructor() {
    combineLatest([this.route.paramMap, this.route.data])
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(([params, data]) => {
        const id = params.get('id') ?? '';
        const type = data['entityType'] as EntityType | undefined;
        if (!type || !id || Number.isNaN(Number(id))) {
          this.loadState.set('notFound');
          this.detail.set(null);
          return;
        }

        this.load(type, id);
      });
  }

  protected goBack(): void {
    this.browserLocation.back();
  }

  private load(type: EntityType, id: string): void {
    this.loadState.set('loading');
    this.detail.set(null);

    this.http.get<Record<string, unknown>>(this.endpoint(type, id)).subscribe({
      next: (dto) => {
        this.detail.set(this.mapDto(type, dto));
        this.loadState.set('loaded');
      },
      error: (err) => {
        this.detail.set(null);
        this.loadState.set(err?.status === 404 ? 'notFound' : 'error');
      },
    });
  }

  private endpoint(type: EntityType, id: string): string {
    switch (type) {
      case 'location':
        return `/api/locations/${id}`;
      case 'destination':
        return `/api/destinations/${id}`;
      case 'region':
        return `/api/regions/${id}`;
      case 'territory':
        return `/api/Territory/${id}`;
    }
  }

  private mapDto(type: EntityType, dto: Record<string, unknown>): HierarchyDetail {
  const id = this.toStringValue(dto['id']);
  const title = this.toStringValue(dto['name']) || 'Untitled';

  switch (type) {
    case 'location': {
      const destinationId = this.toNullableString(dto['destinationId']);
      const destinationName = this.toNullableString(dto['destinationName']);
      return {
        id,
        title,
        typeLabel: 'Location',
        description: this.toNullableString(dto['description']) ?? undefined,
        latitude: this.toNullableNumber(dto['latitude']),
        longitude: this.toNullableNumber(dto['longitude']),
        rating: this.toNullableNumber(dto['averageRating']),
        reviewsCount: this.toNullableNumber(dto['reviewsCount']),
        parent: destinationId
          ? { label: 'Destination', name: destinationName ?? `Destination #${destinationId}`, route: `/destinations/${destinationId}` }
          : null,
        sections: this.mapSectionsWithRoutes(dto),
      };
    }
    case 'destination': {
      const regionId = this.toNullableString(dto['regionId']);
      const regionName = this.toNullableString(dto['regionName']);
      return {
        id,
        title,
        typeLabel: 'Destination',
        description: this.toNullableString(dto['description']) ?? undefined,
        latitude: this.toNullableNumber(dto['latitude']),
        longitude: this.toNullableNumber(dto['longitude']),
        rating: this.toNullableNumber(dto['averageRating']),
        reviewsCount: this.toNullableNumber(dto['reviewsCount']),
        parent: regionId
          ? { label: 'Region', name: regionName ?? `Region #${regionId}`, route: `/regions/${regionId}` }
          : null,
        sections: this.mapSectionsWithRoutes(dto),
      };
    }
    case 'region': {
      const territoryId = this.toNullableString(dto['territoryId']);
      const territoryName = this.toNullableString(dto['territoryName']);
      const destinations = Array.isArray(dto['destinations']) ? dto['destinations'] as Record<string, unknown>[] : [];
      return {
        id,
        title,
        typeLabel: 'Region',
        parent: territoryId
          ? { label: 'Territory', name: territoryName ?? `Territory #${territoryId}`, route: `/territories/${territoryId}` }
          : null,
        sections: destinations.length > 0 ? [{
          sectionTitle: 'Destinations',
          locations: destinations.map(d => ({
            id: d['id'],
            name: d['name'],
            route: `/destinations/${d['id']}`,
          })) as unknown[],
        }] : [],
      };
    }
    case 'territory': {
      const regions = Array.isArray(dto['regions']) ? dto['regions'] as Record<string, unknown>[] : [];
      return {
        id,
        title,
        typeLabel: 'Territory',
        parent: null,
        sections: regions.length > 0 ? [{
          sectionTitle: 'Regions',
          locations: regions.map(r => ({
            id: r['id'],
            name: r['name'],
            route: `/regions/${r['id']}`,
          })) as unknown[],
        }] : [],
      };
    }
  }
}

private mapSectionsWithRoutes(dto: Record<string, unknown>): DetailSectionPreview[] {
  const sections = Array.isArray(dto['sections']) ? dto['sections'] as Record<string, unknown>[] : [];
  return sections.map(section => ({
    sectionKey: this.toStringValue(section['key']),
    sectionTitle: this.toStringValue(section['title']),
    locations: (Array.isArray(section['locations']) ? section['locations'] as Record<string, unknown>[] : [])
      .map(l => ({ id: l['id'], name: l['name'], route: `/locations/${l['id']}` })),
    places: (Array.isArray(section['places']) ? section['places'] as Record<string, unknown>[] : [])
      .map(p => ({ id: p['id'], name: p['name'], route: `/places/${p['id']}` })),
  }));
}

  private toStringValue(value: unknown): string {
    return value == null ? '' : String(value);
  }

  private toNullableString(value: unknown): string | null {
    if (value == null) return null;
    const text = String(value).trim();
    return text.length > 0 ? text : null;
  }

  private toNullableNumber(value: unknown): number | null {
    if (typeof value === 'number' && Number.isFinite(value)) return value;
    if (typeof value === 'string' && value.trim().length > 0) {
      const parsed = Number(value);
      return Number.isFinite(parsed) ? parsed : null;
    }
    return null;
  }

  private toSections(value: unknown): DetailSectionPreview[] {
    return Array.isArray(value) ? value as DetailSectionPreview[] : [];
  }
}
