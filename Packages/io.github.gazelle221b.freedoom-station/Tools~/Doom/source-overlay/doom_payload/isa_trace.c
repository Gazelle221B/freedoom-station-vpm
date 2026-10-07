/* Test harness only: execute unchanged src/ and retain trap/exit evidence. */
#define main stock_rvc_main
#include "../src/main.c"
#undef main

typedef struct {
    uint pc, instruction, privilege, mcause, mtval, scause, stval, satp;
} snapshot;

static snapshot state(uint pc, uint instruction) {
    snapshot s = {pc, instruction, cpu.csr.privilege,
        cpu.csr.data[CSR_MCAUSE], cpu.csr.data[CSR_MTVAL],
        cpu.csr.data[CSR_SCAUSE], cpu.csr.data[CSR_STVAL], read_csr_raw(&cpu, CSR_SATP)};
    return s;
}

static void print_state(const char *tag, snapshot s) {
    printf("%s pc=0x%08x instruction=0x%08x privilege=%u mcause=0x%08x "
           "mtval=0x%08x scause=0x%08x stval=0x%08x satp=0x%08x\n",
           tag, s.pc, s.instruction, s.privilege, s.mcause, s.mtval,
           s.scause, s.stval, s.satp);
}

int main(int argc, char **argv) {
    if (argc < 2 || argc > 4) {
        fprintf(stderr, "Usage: isa_trace ELF [max_steps=2000000] [tohost=0]\n");
        return 2;
    }
    uint limit = argc > 2 ? strtoul(argv[2], NULL, 0) : 2000000;
    if (!limit) return 2;
    uint tohost = argc > 3 ? strtoul(argv[3], NULL, 0) : 0;
    uint8_t *ram = calloc(1, MEM_SIZE);
    if (!ram || load_elf(argv[1], strlen(argv[1])+1, ram, MEM_SIZE, false)) return 2;
    cpu = cpu_init(ram, NULL, NULL, 0);
    /* Observe the executed exit instead of allowing the core to exit this host. */
    allow_ecall_exit = false;
    snapshot ring[64] = {{0}}, last_fault = {0};
    uint count = 0, faults = 0, user_ecalls = 0, exit_raw = 0, exit_priv = 0;
    bool exited = false;
    for (; count < limit; count++) {
        uint pc = cpu.pc, priv = cpu.csr.privilege;
        ins_ret fetch = ins_ret_noop(&cpu);
        uint physical = mmu_translate(&fetch, pc, MMU_ACCESS_FETCH);
        uint word = fetch.trap.en ? 0 : mem_get_word(&cpu, physical);
        bool final_exit = word == 0x73 && cpu.xreg[17] == 93 &&
                          (tohost == 0 || priv != PRIV_USER);
        uint raw = cpu.xreg[10];
        ring[count%64] = state(pc, word);
        cpu_tick(&cpu);
        if (word == 0x73 && priv == PRIV_USER) user_ecalls++;
        if (cpu.pc == (cpu.csr.data[CSR_STVEC] & ~3u) && cpu.csr.privilege == PRIV_SUPERVISOR) {
            uint cause = cpu.csr.data[CSR_SCAUSE];
            if (cause == 12 || cause == 13 || cause == 15) {
                last_fault = state(pc, word);
                faults++;
                print_state("PAGE_FAULT", last_fault);
            }
        }
        /* Service the original env/v console HTIF; termination uses ecall. */
        if (tohost >= 0x80000000 && tohost+8 < 0x80000000u+(uint)MEM_SIZE) {
            uint64_t value;
            memcpy(&value, ram+(tohost-0x80000000), 8);
            if ((value >> 48) == 0x0101) {
                fprintf(stderr, "%c", (char)value);
                memset(ram+(tohost-0x80000000), 0, 8);
            }
        }
        if (final_exit) {
            exited = true; exit_raw = raw; exit_priv = priv; count++;
            break;
        }
    }
    for (uint n = count > 64 ? count-64 : 0; n < count; n++) print_state("TRACE", ring[n%64]);
    print_state("FINAL", state(cpu.pc, ring[(count-1)%64].instruction));
    print_state("LAST_PAGE_FAULT", last_fault);
    bool pass = exited && exit_raw == 0;
    printf("RESULT {\"passed\":%s,\"exited\":%s,\"steps\":%u,\"rawExit\":%u,"
           "\"exitPrivilege\":%u,\"userEcalls\":%u,\"pageFaults\":%u,\"satp\":%u}\n",
           pass ? "true" : "false", exited ? "true" : "false", count, exit_raw,
           exit_priv, user_ecalls, faults, read_csr_raw(&cpu, CSR_SATP));
    return pass ? 0 : 1;
}
