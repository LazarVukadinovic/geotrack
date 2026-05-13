import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AdminProfile,
  AdminProfileAvatarResponse,
  UpdateAdminProfileRequest,
} from '../models/admin-profile.model';

@Injectable({ providedIn: 'root' })
export class AdminProfileService {
  constructor(private readonly http: HttpClient) {}

  getProfile(): Observable<AdminProfile> {
    return this.http.get<AdminProfile>('/api/admin/profile');
  }

  updateProfile(request: UpdateAdminProfileRequest): Observable<AdminProfile> {
    return this.http.put<AdminProfile>('/api/admin/profile', request);
  }

  uploadAvatar(file: File): Observable<AdminProfileAvatarResponse> {
    const formData = new FormData();
    formData.append('avatar', file);

    return this.http.post<AdminProfileAvatarResponse>('/api/admin/profile/avatar', formData);
  }

  removeAvatar(): Observable<AdminProfileAvatarResponse> {
    return this.http.delete<AdminProfileAvatarResponse>('/api/admin/profile/avatar');
  }
}
