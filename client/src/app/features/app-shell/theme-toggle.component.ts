import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { LucideAngularModule, Sun, Moon } from 'lucide-angular';
import { ThemeService } from '../../core/theme/theme.service';

@Component({
  selector: 'app-theme-toggle',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LucideAngularModule],
  templateUrl: './theme-toggle.component.html',
  styleUrl: './theme-toggle.component.scss',
})
export class ThemeToggleComponent {
  private readonly themeService = inject(ThemeService);

  readonly SunIcon = Sun;
  readonly MoonIcon = Moon;

  readonly icon = computed(() =>
    this.themeService.theme() === 'light' ? this.MoonIcon : this.SunIcon,
  );

  readonly label = computed(() =>
    this.themeService.theme() === 'light' ? 'Switch to dark mode' : 'Switch to light mode',
  );

  toggle(): void {
    this.themeService.toggle();
  }
}
