import { Pipe, PipeTransform } from '@angular/core';
import { formatDescriptionRichHtml } from '../utils/description-formatting';

@Pipe({
  name: 'descriptionRichHtml',
  standalone: true,
  pure: true,
})
export class DescriptionRichHtmlPipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    return formatDescriptionRichHtml(value).replace(/\n/g, '<br>');
  }
}
