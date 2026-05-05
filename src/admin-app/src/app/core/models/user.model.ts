export interface User {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  role: string;
  region: string;
  memberSince: string;
  bio: string;
  language: string;
  timezone: string;
  notifyRequests: boolean;
  notifyNewReview: boolean;
  avatarUrl?: string | null;
}