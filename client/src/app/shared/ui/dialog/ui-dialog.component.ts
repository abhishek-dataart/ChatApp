import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  HostListener,
  OnDestroy,
  OutputEmitterRef,
  effect,
  inject,
  input,
  output,
  viewChild,
} from '@angular/core';
import { FocusTrap, FocusTrapFactory } from '@angular/cdk/a11y';

@Component({
  selector: 'ui-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './ui-dialog.component.html',
  styleUrl: './ui-dialog.component.scss',
})
export class UiDialogComponent implements OnDestroy {
  readonly open = input<boolean>(false);
  readonly closed: OutputEmitterRef<void> = output<void>();

  private readonly panelRef = viewChild<ElementRef<HTMLElement>>('dialogPanel');
  private readonly focusTrapFactory = inject(FocusTrapFactory);
  private focusTrap: FocusTrap | null = null;

  constructor() {
    effect(() => {
      const isOpen = this.open();
      if (isOpen) {
        setTimeout(() => this.trapFocus());
      } else {
        this.destroyTrap();
      }
    });
  }

  private trapFocus(): void {
    const panel = this.panelRef()?.nativeElement;
    if (!panel) return;
    this.destroyTrap();
    this.focusTrap = this.focusTrapFactory.create(panel);
    this.focusTrap.focusInitialElementWhenReady();
  }

  private destroyTrap(): void {
    this.focusTrap?.destroy();
    this.focusTrap = null;
  }

  ngOnDestroy(): void {
    this.destroyTrap();
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.open()) {
      this.closed.emit();
    }
  }

  onBackdropClick(): void {
    this.closed.emit();
  }

  onPanelClick(event: MouseEvent): void {
    event.stopPropagation();
  }
}
