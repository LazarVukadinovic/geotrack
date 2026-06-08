const BLOCK_TAGS = 'p|div|section|article|header|footer|aside|main|h[1-6]|ul|ol|li|blockquote|pre|table|thead|tbody|tfoot|tr';
const INLINE_TAGS = 'strong|b|em|i|u';

export function formatDescriptionPlainText(value: string | null | undefined): string {
  return normalizeDescriptionLines(decodeHtmlEntities(value ?? '')
    .replace(/<\s*(script|style)\b[^>]*>.*?<\s*\/\s*\1\s*>/gis, ' ')
    .replace(/<br\s*\/?>/gi, '\n')
    .replace(new RegExp(`<\\s*\\/\\s*(${BLOCK_TAGS})\\s*>`, 'gi'), '\n')
    .replace(new RegExp(`<\\s*(${BLOCK_TAGS})\\b[^>]*>`, 'gi'), '')
    .replace(/<[^>]*>/g, ''));
}

export function formatDescriptionRichHtml(value: string | null | undefined): string {
  const cleaned = decodeHtmlEntities(value ?? '')
    .replace(/<\s*(script|style)\b[^>]*>.*?<\s*\/\s*\1\s*>/gis, ' ')
    .replace(/<br\s*\/?>/gi, '\n')
    .replace(new RegExp(`<\\s*\\/\\s*(${BLOCK_TAGS})\\s*>`, 'gi'), '\n')
    .replace(new RegExp(`<\\s*(${BLOCK_TAGS})\\b[^>]*>`, 'gi'), '')
    .replace(/<\s*span\b[^>]*\bstyle=(["'])(.*?)\1[^>]*>(.*?)<\s*\/\s*span\s*>/gis, (_match, _quote: string, style: string, content: string) =>
      applyStyledSpanTags(style, content))
    .replace(new RegExp(`<\\s*(${INLINE_TAGS})\\b[^>]*>`, 'gi'), (_match, tag: string) => `<${normalizeInlineTag(tag)}>`)
    .replace(new RegExp(`<\\s*\\/\\s*(${INLINE_TAGS})\\s*>`, 'gi'), (_match, tag: string) => `</${normalizeInlineTag(tag)}>`)
    .replace(/<(?!\/?(?:strong|em|u)\b)[^>]*>/gi, '');

  return normalizeDescriptionLines(cleaned, line => stripHtml(line).trim().length > 0);
}

export function isLongDescription(value: string | null | undefined, options: { maxChars?: number; maxLines?: number } = {}): boolean {
  const formatted = formatDescriptionPlainText(value);
  const plainText = formatted.replace(/[ \t\f\v]+/g, ' ').trim();
  return plainText.length > (options.maxChars ?? 260) || formatted.split('\n').length > (options.maxLines ?? 5);
}

export function normalizeEditorDescriptionHtml(value: string | null | undefined): string {
  const html = formatDescriptionRichHtml(value);
  return html.replace(/\n/g, '<br>');
}

function normalizeDescriptionLines(value: string, keepLine: (line: string) => boolean = line => line.trim().length > 0): string {
  const lines = value
    .replace(/\r\n/g, '\n')
    .replace(/\r/g, '\n')
    .split('\n')
    .map(line => line.replace(/[ \t\f\v]+/g, ' ').trim());

  const result: string[] = [];
  let consecutiveEmpty = 0;

  for (const line of lines) {
    if (!keepLine(line)) {
      consecutiveEmpty++;
      continue;
    }
    if (result.length > 0 && consecutiveEmpty > 0) {
      result.push('');
    }
    consecutiveEmpty = 0;
    result.push(line);
  }

  return result.join('\n').trim();
}

function normalizeInlineTag(tag: string): string {
  const normalized = tag.toLowerCase();
  if (normalized === 'b') return 'strong';
  if (normalized === 'i') return 'em';
  return normalized;
}

function stripHtml(value: string): string {
  return value.replace(/<[^>]*>/g, ' ');
}

function applyStyledSpanTags(style: string, content: string): string {
  let html = content;
  const normalizedStyle = style.toLowerCase();

  if (/font-weight\s*:\s*(?:bold|[6-9]00)/.test(normalizedStyle)) {
    html = `<strong>${html}</strong>`;
  }

  if (/font-style\s*:\s*italic/.test(normalizedStyle)) {
    html = `<em>${html}</em>`;
  }

  if (/text-decoration(?:-line)?\s*:[^;]*underline/.test(normalizedStyle)) {
    html = `<u>${html}</u>`;
  }

  return html;
}

function decodeHtmlEntities(value: string): string {
  return value
    .replace(/&nbsp;/gi, ' ')
    .replace(/&amp;/gi, '&')
    .replace(/&lt;/gi, '<')
    .replace(/&gt;/gi, '>')
    .replace(/&quot;/gi, '"')
    .replace(/&#39;|&apos;/gi, "'")
    .replace(/&#(\d+);/g, (_match, code: string) => String.fromCharCode(Number(code)))
    .replace(/&#x([0-9a-f]+);/gi, (_match, code: string) => String.fromCharCode(Number.parseInt(code, 16)));
}
