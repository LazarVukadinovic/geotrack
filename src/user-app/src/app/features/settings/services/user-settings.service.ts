import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  AnalyticsPreferences,
  ChangePasswordRequest,
  ChangePasswordResponse,
  CreateSupportRequestRequest,
  CreateSupportRequestResponse,
  NotificationPreferences,
  SupportRequestsResponse,
} from '../models/user-settings.model';

@Injectable({ providedIn: 'root' })
export class UserSettingsService {
  private readonly http = inject(HttpClient);

  getNotificationPreferences(): Observable<NotificationPreferences> {
    return this.http.get<NotificationPreferences>('/api/user/me/notifications');
  }

  updateNotificationPreferences(preferences: NotificationPreferences): Observable<NotificationPreferences> {
    return this.http.put<NotificationPreferences>('/api/user/me/notifications', preferences);
  }

  getAnalyticsPreferences(): Observable<AnalyticsPreferences> {
    return this.http.get<AnalyticsPreferences>('/api/user/me/analytics');
  }

  updateAnalyticsPreferences(preferences: AnalyticsPreferences): Observable<AnalyticsPreferences> {
    return this.http.put<AnalyticsPreferences>('/api/user/me/analytics', preferences);
  }

  changePassword(payload: ChangePasswordRequest): Observable<ChangePasswordResponse> {
    return this.http.put<ChangePasswordResponse>('/api/user/me/password', payload);
  }

  getSupportRequests(): Observable<SupportRequestsResponse> {
    return this.http.get<SupportRequestsResponse>('/api/user/me/support-requests');
  }

  createSupportRequest(payload: CreateSupportRequestRequest): Observable<CreateSupportRequestResponse> {
    return this.http.post<CreateSupportRequestResponse>('/api/user/me/support-requests', payload);
  }
}
