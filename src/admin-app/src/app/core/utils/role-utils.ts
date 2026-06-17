/**
 * Strips trailing digits (and surrounding whitespace) from a role string.
 *
 * Examples:
 *   "TerritoryManager5"   → "TerritoryManager"
 *   "TERRITORY MANAGER 3" → "TERRITORY MANAGER"
 *   "RegionalManager_12"  → "RegionalManager_12"  (underscore kept; digits only at end after space)
 *
 * The regex removes any sequence of digits that appears at the very end of the string,
 * optionally preceded by a space or underscore separator.
 */
export function cleanRoleName(role: string | null | undefined): string {
  if (!role) return '';
  return role.replace(/[\s_]?\d+$/, '').trim();
}
