import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import type { AgentRecommendationCard } from '../../models/agent.model';

@Component({
  selector: 'app-agent-recommendation-card',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './agent-recommendation-card.component.html',
  styleUrl: './agent-recommendation-card.component.scss',
})
export class AgentRecommendationCardComponent {
  readonly card = input.required<AgentRecommendationCard>();
}
