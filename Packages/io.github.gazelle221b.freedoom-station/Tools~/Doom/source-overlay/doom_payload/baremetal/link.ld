ENTRY(_start)

MEMORY
{
  RAM (rwx) : ORIGIN = 0x80000000, LENGTH = 128M
}

SECTIONS
{
  .text : {
    *(.text.startup)
    *(.text*)
  } > RAM

  .rodata : {
    *(.rodata*)
    *(.srodata*)
  } > RAM

  .data : {
    *(.data*)
    *(.sdata*)
  } > RAM

  .bss : {
    . = ALIGN(4);
    _bss_start = .;
    *(.bss*)
    *(.sbss*)
    *(COMMON)
    . = ALIGN(4);
    _bss_end = .;
  } > RAM

  _end = .;

  PROVIDE(_heap_start = .);
  PROVIDE(_heap_end   = 0x86000000);
  PROVIDE(_stack_top  = 0x87F00000);
  PROVIDE(_framebuffer_addr = 0x86000000);

  /* Global pointer: 0x800 bytes after start of .sdata */
  PROVIDE(__global_pointer$ = . + 0x800);
  ASSERT(_end < 0x86000000, "Payload overlaps framebuffer")
}
