import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  HostListener,
  inject,
  signal,
} from '@angular/core';

@Component({
  selector: 'ui-menu',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './ui-menu.component.html',
  styleUrl: './ui-menu.component.scss',
})
export class UiMenuComponent {
  readonly isOpen = signal(false);

  private readonly elRef = inject(ElementRef<HTMLElement>);

  toggle(): void {
    this.isOpen.update((v) => !v);
  }

  close(): void {
    this.isOpen.set(false);
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (!this.elRef.nativeElement.contains(event.target as Node)) {
      this.close();
    }
  }
}
