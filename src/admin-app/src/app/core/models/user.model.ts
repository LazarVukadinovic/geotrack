export type UserRole = 'National Manager' | 'Regional Manager';

export interface User {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  role: UserRole;
  region: string;
  memberSince: string;
  bio: string;
  language: string;
  timezone: string;
  notifyRequests: boolean;
  notifyNewReview: boolean;
  avatarUrl?: string;
}