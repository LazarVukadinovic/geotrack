export interface Country {
  code: string;
  name: string;
  continent: string;
  flag: string;
}

export interface Territory {
  id: string;
  name: string;
  countries: string[];
  assignedManagerId: string | null;
  assignedManagerName: string | null;
  assignedManagerEmail: string | null;
  status: 'Active' | 'Unassigned';
  createdAt: string;
  updatedAt: string;
}

export interface TerritoryManager {
  id: string;
  name: string;
  email: string;
  initials: string;
  avatarColor: string;
  assignedTerritoryIds: string[];
  status: 'Active' | 'Inactive';
}

export interface CreateTerritoryRequest {
  name: string;
  countries: string[];
  assignedManagerId: string | null;
}

export interface TerritoryPage {
  items: Territory[];
  total: number;
}
