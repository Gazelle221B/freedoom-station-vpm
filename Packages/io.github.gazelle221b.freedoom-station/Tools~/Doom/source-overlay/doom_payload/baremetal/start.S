# Bare-metal RISC-V M-mode startup for Doomgeneric payload
# rv32ima, no C extension, no F extension
# Entry point at 0x80000000

.section .text.startup, "ax"
.globl _start
_start:
    # Initialize global pointer
    .option push
    .option norelax
    la      gp, __global_pointer$
    .option pop

    # Set up stack pointer
    la      sp, _stack_top

    # Clear BSS
    la      t0, _bss_start
    la      t1, _bss_end
1:
    bgeu    t0, t1, 2f
    sw      zero, 0(t0)
    addi    t0, t0, 4
    j       1b
2:

    # Set up trap vector (mtvec = direct mode, trap_handler)
    la      t0, trap_handler
    csrw    mtvec, t0

    # Jump to main
    call    main

    # If main returns, exit via ecall
    li      a7, 93        # SYS_exit
    li      a0, 0
    ecall
    j       .

# Trap handler: print mcause+mepc, then exit
.section .text, "ax"
.globl trap_handler
trap_handler:
    # Save registers
    addi    sp, sp, -64
    sw      ra, 0(sp)
    sw      t0, 4(sp)
    sw      t1, 8(sp)
    sw      t2, 12(sp)
    sw      a0, 16(sp)
    sw      a1, 20(sp)
    sw      a2, 24(sp)
    sw      a7, 28(sp)

    # Read mcause and mepc
    csrr    t0, mcause
    csrr    t1, mepc
    sw      t0, 32(sp)
    sw      t1, 36(sp)
    csrr    t0, mtval
    sw      t0, 40(sp)

    # Print trap message
    la      a0, trap_msg
    call    uart_puts_asm

    # Print mcause as hex
    lw      a0, 32(sp)
    call    print_hex_asm

    # Print " PC="
    la      a0, pc_msg
    call    uart_puts_asm

    # Print mepc as hex
    lw      a0, 36(sp)
    call    print_hex_asm

    la      a0, val_msg
    call    uart_puts_asm
    lw      a0, 40(sp)
    call    print_hex_asm

    # Newline
    li      a0, 0x0a
    call    uart_putc_asm

    # Exit via ecall
    li      a7, 93
    li      a0, 1
    ecall
    j       .

# --- UART helpers (byte-addressed 16550 at 0x10000000) ---
# LSR at 0x10000005, THR at 0x10000000
# Wait for THR empty (LSR bit 5 = 0x20), then write byte

uart_putc_asm:
    # a0 = byte to send
    li      t0, 0x10000000
    li      t1, 0x10000005
1:
    lbu     t2, 0(t1)    # read LSR
    andi    t2, t2, 0x20 # check THR empty
    beqz    t2, 1b
    sb      a0, 0(t0)    # write THR
    ret

uart_puts_asm:
    # a0 = pointer to null-terminated string
    mv      t3, a0
1:
    lbu     a0, 0(t3)
    beqz    a0, 2f
    addi    t3, t3, 1
    # save t3, call uart_putc
    addi    sp, sp, -4
    sw      ra, 0(sp)
    call    uart_putc_asm
    lw      ra, 0(sp)
    addi    sp, sp, 4
    j       1b
2:
    ret

print_hex_asm:
    # a0 = 32-bit value to print as 8 hex digits
    addi    sp, sp, -20
    sw      ra, 0(sp)
    sw      s0, 4(sp)
    sw      s1, 8(sp)

    mv      s0, a0
    li      s1, 8          # 8 hex digits

1:
    addi    s1, s1, -1
    slli    t0, s1, 2     # shift amount = s1 * 4
    srl     t0, s0, t0    # get nibble
    andi    t0, t0, 0xf
    li      t1, 0x39
    addi    t0, t0, 0x30  # '0'
    bge     t1, t0, 2f
    addi    t0, t0, 0x27  # 'a' - '0' - 10 = 0x27
2:
    mv      a0, t0
    call    uart_putc_asm
    bnez    s1, 1b

    lw      ra, 0(sp)
    lw      s0, 4(sp)
    lw      s1, 8(sp)
    addi    sp, sp, 20
    ret

.section .rodata
trap_msg:
    .string "TRAP mcause="
pc_msg:
    .string " PC="
val_msg:
    .string " MTVAL="
