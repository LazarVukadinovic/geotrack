import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface TerritoryRegionItem {
  id: number;
  name: string;
}

export interface TerritoryDetail {
  id: number;
  name: string;
  regions: TerritoryRegionItem[];
}

@Injectable({ providedIn: 'root' })
export class TerritoriesService {
  constructor(private readonly http: HttpClient) {}

  getDetail$(id: string | number): Observable<TerritoryDetail> {
    return this.http.get<TerritoryDetail>(`/api/Territory/${id}`);
  }
}
