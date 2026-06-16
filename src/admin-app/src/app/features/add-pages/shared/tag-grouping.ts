export interface CategorizedTag {
  id: number;
  name: string;
  slug: string;
  tagTypeId?: number | null;
  tagTypeName?: string | null;
  tagTypeSlug?: string | null;
  tagTypeLabel?: string | null;
  tagTypeOrder?: number | null;
}

export interface SelectableTagItem<TTag extends CategorizedTag> {
  tag: TTag;
  checked: boolean;
}

export interface SelectableTagGroup<TTag extends CategorizedTag> {
  key: string;
  label: string;
  order: number;
  items: SelectableTagItem<TTag>[];
}

export function groupSelectableTags<TTag extends CategorizedTag>(
  items: readonly SelectableTagItem<TTag>[],
  rawQuery = '',
): SelectableTagGroup<TTag>[] {
  const query = normalize(rawQuery);
  const groups = new Map<string, SelectableTagGroup<TTag>>();

  for (const item of items) {
    const label = clean(item.tag.tagTypeLabel) || clean(item.tag.tagTypeName) || 'Other tags';
    const key = item.tag.tagTypeId != null
      ? `type-${item.tag.tagTypeId}`
      : clean(item.tag.tagTypeSlug) || 'uncategorized';
    const order = typeof item.tag.tagTypeOrder === 'number' && Number.isFinite(item.tag.tagTypeOrder)
      ? item.tag.tagTypeOrder
      : 1000;

    const categoryMatches = normalize(label).includes(query);
    const tagMatches = normalize(item.tag.name).includes(query) || normalize(item.tag.slug).includes(query);
    if (query && !categoryMatches && !tagMatches) continue;

    const existing = groups.get(key);
    if (existing) {
      existing.items.push(item);
      continue;
    }

    groups.set(key, {
      key,
      label,
      order,
      items: [item],
    });
  }

  return [...groups.values()]
    .map(group => ({
      ...group,
      items: [...group.items].sort((a, b) => a.tag.name.localeCompare(b.tag.name)),
    }))
    .sort((a, b) => a.order - b.order || a.label.localeCompare(b.label));
}

function clean(value: string | null | undefined): string {
  return value?.trim() ?? '';
}

function normalize(value: string | null | undefined): string {
  return clean(value).toLocaleLowerCase();
}
