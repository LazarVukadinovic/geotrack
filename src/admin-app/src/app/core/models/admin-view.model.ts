import { AdminRoleCode, UserRole } from './user.model';

export interface AdminView {
  roleCode: AdminRoleCode;
  label: UserRole;
  dashboardRoute: string;
  settingsRoute: string;
}

export const ADMIN_VIEW_DEFINITIONS: readonly AdminView[] = [
  {
    roleCode: 'Admin',
    label: 'Administrator',
    dashboardRoute: '/admin-dashboard',
    settingsRoute: '/settings',
  },
  {
    roleCode: 'ContentModerator',
    label: 'Content Moderator',
    dashboardRoute: '/content-moderator/dashboard',
    settingsRoute: '/settings',
  },
  {
    roleCode: 'TerritoryManager',
    label: 'Territory Manager',
    dashboardRoute: '/territory/overview',
    settingsRoute: '/settings',
  },
  {
    roleCode: 'TerritoryAssociate',
    label: 'Territory Associate',
    dashboardRoute: '/territory/overview',
    settingsRoute: '/settings',
  },
  {
    roleCode: 'RegionalManager',
    label: 'Regional Manager',
    dashboardRoute: '/dashboard',
    settingsRoute: '/settings',
  },
] as const;

export function normalizeAdminRoleCode(role: string | null | undefined): AdminRoleCode | null {
  const normalized = (role ?? '').replace(/[\s_-]/g, '').toLowerCase();

  switch (normalized) {
    case 'admin':
    case 'administrator':
    case 'superadmin':
    case 'sysadmin':
      return 'Admin';
    case 'contentmoderator':
    case 'moderator':
      return 'ContentModerator';
    case 'nationalmanager':
    case 'territorymanager':
      return 'TerritoryManager';
    case 'territoryassociate':
      return 'TerritoryAssociate';
    case 'regionalmanager':
      return 'RegionalManager';
    default:
      return null;
  }
}

export function getAdminViewByCode(roleCode: AdminRoleCode | null | undefined): AdminView | null {
  return ADMIN_VIEW_DEFINITIONS.find(view => view.roleCode === roleCode) ?? null;
}

export function getAdminViewByRoleLabel(role: UserRole | null | undefined): AdminView | null {
  const roleCode = normalizeAdminRoleCode(role);
  return getAdminViewByCode(roleCode);
}

export function getAdminViewsForRoles(roles: readonly string[] | null | undefined): AdminView[] {
  const roleCodes = new Set<AdminRoleCode>(
    (roles ?? [])
      .map(role => normalizeAdminRoleCode(role))
      .filter((roleCode): roleCode is AdminRoleCode => roleCode !== null),
  );

  return ADMIN_VIEW_DEFINITIONS.filter(view => roleCodes.has(view.roleCode));
}

export function resolveAdminViewForRoles(
  roles: readonly string[] | null | undefined,
  preferredRoleCode?: AdminRoleCode | null,
): AdminView | null {
  const views = getAdminViewsForRoles(roles);
  if (views.length === 0) {
    return null;
  }

  if (preferredRoleCode) {
    const preferred = views.find(view => view.roleCode === preferredRoleCode);
    if (preferred) {
      return preferred;
    }
  }

  return views[0];
}
