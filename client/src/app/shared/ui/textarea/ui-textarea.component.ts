import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  forwardRef,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

@Component({
  selector: 'ui-textarea',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './ui-textarea.component.html',
  styleUrl: './ui-textarea.component.scss',
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => UiTextareaComponent),
      multi: true,
    },
  ],
})
export class UiTextareaComponent implements ControlValueAccessor {
  readonly placeholder = input<string | undefined>(undefined);
  readonly error = input<string | undefined>(undefined);
  readonly rows = input<number>(3);

  readonly value = signal<string>('');
  readonly isDisabled = signal<boolean>(false);

  private readonly textareaRef = viewChild<ElementRef<HTMLTextAreaElement>>('textareaEl');

  private onChange: (val: string) => void = () => {};
  private onTouched: () => void = () => {};

  handleInput(event: Event): void {
    const el = event.target as HTMLTextAreaElement;
    this.value.set(el.value);
    this.onChange(el.value);
    this.autoGrow(el);
  }

  handleBlur(): void {
    this.onTouched();
  }

  private autoGrow(el: HTMLTextAreaElement): void {
    el.style.height = 'auto';
    el.style.height = `${el.scrollHeight}px`;
  }

  writeValue(val: string): void {
    this.value.set(val ?? '');
  }

  registerOnChange(fn: (val: string) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.isDisabled.set(isDisabled);
  }
}
