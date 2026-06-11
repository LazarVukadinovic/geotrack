export const OPEN_24_HOURS_LABEL = 'Open 24 hours';

export interface OperatingHourLike {
  dayOfWeek?: string;
  name?: string;
  isOpen?: boolean;
  open?: boolean;
  openTime?: string | null;
  closeTime?: string | null;
}

export function isOpen24HoursText(value: string | null | undefined): boolean {
  return /^open\s+24\s*(?:hours?|h)$/i.test((value ?? '').trim());
}

export function isOpen24HoursSchedule(hours: readonly OperatingHourLike[]): boolean {
  if (hours.length < 7) return false;

  return hours.every(hour => {
    const isOpen = hour.isOpen ?? hour.open;
    return isOpen === true && hour.openTime === '00:00' && hour.closeTime === '00:00';
  });
}

export function hasDisplayableOperatingHours(hours: readonly OperatingHourLike[]): boolean {
  return hours.length > 0;
}
