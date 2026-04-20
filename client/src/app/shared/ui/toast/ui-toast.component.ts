import {
  ChangeDetectionStrategy,
  Component,
  HostBinding,
  input,
  output,
  OutputEmitterRef,
} from '@angular/core';
import { Toast } from '../../../core/notifications/toast.service';

@Component({
  selector: 'ui-toast',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './ui-toast.component.html',
  styleUrl: './ui-toast.component.scss',
})
export class UiToastComponent {
  readonly toast = input.required<Toast>();
  readonly dismiss: OutputEmitterRef<string> = output<string>();

  @HostBinding('class')
  get hostClass(): string {
    return `severity-${this.toast().severity}`;
  }

  close(): void {
    this.dismiss.emit(this.toast().id);
  }
}
