import {
  ChangeDetectionStrategy,
  Component,
  inject,
} from '@angular/core';
import { ToastService } from '../../../core/notifications/toast.service';
import { UiToastComponent } from './ui-toast.component';

@Component({
  selector: 'ui-toast-container',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [UiToastComponent],
  templateUrl: './ui-toast-container.component.html',
  styleUrl: './ui-toast-container.component.scss',
})
export class UiToastContainerComponent {
  private readonly toastService = inject(ToastService);

  readonly toasts = this.toastService.toasts;

  dismiss(id: string): void {
    this.toastService.dismiss(id);
  }
}
