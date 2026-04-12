import { Component, input, output, signal } from '@angular/core';
import type { SuggestionPill } from '../../models/agent.model';

@Component({
  selector: 'app-agent-suggestion-pills',
  standalone: true,
  imports: [],
  templateUrl: './agent-suggestion-pills.component.html',
  styleUrl: './agent-suggestion-pills.component.scss',
})
export class AgentSuggestionPillsComponent {
  readonly pills = input.required<SuggestionPill[]>();
  readonly pillSelected = output<SuggestionPill>();

  /** Tracks the label of the last pressed pill so a selected style can be applied. */
  protected readonly selectedLabel = signal<string | null>(null);

  protected onPill(pill: SuggestionPill): void {
    this.selectedLabel.set(pill.label);
    this.pillSelected.emit(pill);
  }
}
