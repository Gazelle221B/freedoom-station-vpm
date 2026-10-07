/* Sv32 permission matrix against the production C MMU, not a copied MMU.
 * Oracle: RISC-V Privileged Architecture, Memory Privilege / Sv32 algorithm.
 * stdout: JSON per case; stderr: summary. Exit 1 on mismatch.
 * Optional argument: numeric case id for a deterministic reproducer.
 */
#define main stock_rvc_main
#include "../src/main.c"
#undef main

enum { TWO_LEVEL, INVALID_V, BOTTOM_NONLEAF, SUPER_ALIGNED, SUPER_MISALIGNED };
static const uint probe_va = 0x00401234u;
typedef struct { uint pa, fault, cause, tval; } outcome;

/* Independent truth table: no production MMU helpers used. */
static outcome expected(uint current, uint mprv, uint mpp, uint mode,
                        uint flags, uint sum, uint mxr, uint layout) {
    static const uint fault_causes[] = {12, 13, 15};
    uint effective = mode != 0 && mprv ? mpp : current;
    outcome ok = {effective == 3 ? probe_va :
                  (layout == SUPER_ALIGNED ? 0x80001234u : 0x80004234u), 0, 0, 0};
    outcome bad = {0, 1, fault_causes[mode], probe_va};
    if (effective == 3) return ok;
    uint r = (flags >> 1) & 1, w = (flags >> 2) & 1;
    uint x = (flags >> 3) & 1, u = (flags >> 4) & 1;
    uint a = (flags >> 6) & 1, d = (flags >> 7) & 1;
    if (layout == INVALID_V || layout == BOTTOM_NONLEAF ||
        layout == SUPER_MISALIGNED || (!r && w) || (!r && !x)) return bad;
    /* Columns: fetch/load/store. Rows: effective U/S, page U=0/1. */
    uint permission[2][2][3] = {
        {{0, 0, 0}, {1, 1, 1}},
        {{1, 1, 1}, {0, sum, sum}}
    };
    uint access[] = {x, r || (mxr && x), w};
    if (!permission[effective == 1][u][mode] || !access[mode] ||
        !a || (mode == 2 && !d)) return bad;
    return ok;
}

static uint flags_from_bits(uint bits) {
    return 1u | ((bits & 15u) << 1) | ((bits & 48u) << 2);
}

static outcome observed(uint current, uint mprv, uint mpp, uint mode,
                        uint flags, uint sum, uint mxr, uint layout) {
    uint root = ((0x80002000u >> 12) << 10) | 1u;
    uint leaf = ((0x80004000u >> 12) << 10) | flags;
    if (layout == INVALID_V) leaf &= ~1u;
    if (layout == BOTTOM_NONLEAF) leaf &= ~14u;
    if (layout >= SUPER_ALIGNED) {
        uint physical = layout == SUPER_ALIGNED ? 0x80000000u : 0x80004000u;
        root = ((physical >> 12) << 10) | flags;
    }
    /* Both VPN indices are one. No cache or CSR implementation changed. */
    memcpy(cpu.mem + 0x1004, &root, sizeof(root));
    memcpy(cpu.mem + 0x2004, &leaf, sizeof(leaf));
    cpu.csr.privilege = current;
    write_csr_raw(&cpu, CSR_MSTATUS,
                  (mprv << 17) | (mpp << 11) | (sum << 18) | (mxr << 19));
    mmu_update(0x80080001u);
    ins_ret ret = ins_ret_noop(&cpu);
    uint pa = mmu_translate(&ret, probe_va, mode);
    outcome result = {pa, ret.trap.en, ret.trap.en ? ret.trap.type : 0,
                      ret.trap.en ? ret.trap.value : 0};
    return result;
}

int main(int argc, char **argv) {
    static const uint privileges[] = {0, 1, 3};
    uint8_t *ram = calloc(1, MEM_SIZE);
    if (!ram) return 2;
    cpu = cpu_init(ram, NULL, NULL, 0);
    long selected = argc == 2 ? strtol(argv[1], NULL, 10) : -1;
    uint id = 0, total = 0, failed = 0, sum_failed = 0, mprv_failed = 0, other = 0;
    uint data_failed = 0, no_mprv_failed = 0;
    for (uint p = 0; p < 3; p++)
    for (uint mprv = 0; mprv < 2; mprv++)
    for (uint pp = 0; pp < 3; pp++)
    for (uint mode = 0; mode < 3; mode++)
    for (uint bits = 0; bits < 64; bits++)
    for (uint sum = 0; sum < 2; sum++)
    for (uint mxr = 0; mxr < 2; mxr++)
    for (uint layout = 0; layout < 5; layout++, id++) {
        if (selected >= 0 && id != (uint)selected) continue;
        uint current = privileges[p], mpp = privileges[pp];
        uint flags = flags_from_bits(bits);
        outcome want = expected(current, mprv, mpp, mode, flags, sum, mxr, layout);
        outcome got = observed(current, mprv, mpp, mode, flags, sum, mxr, layout);
        bool pass = got.pa == want.pa && got.fault == want.fault &&
                    got.cause == want.cause && got.tval == want.tval;
        const char *category = "pass";
        total++;
        if (!pass) {
            failed++;
            if (mode != 0) data_failed++;
            if (!mprv) no_mprv_failed++;
            if (mode == 0 && mprv && mpp != current && current != 3) {
                category = "mprv_fetch"; mprv_failed++;
            } else if (mode == 0 && current == 1 && sum && (flags & 16u)) {
                category = "sum_fetch"; sum_failed++;
            } else { category = "other"; other++; }
        }
        printf("{\"id\":%u,\"priv\":%u,\"mprv\":%u,\"mpp\":%u,\"access\":%u,"
               "\"pteFlags\":%u,\"sum\":%u,\"mxr\":%u,\"layout\":%u,"
               "\"expected\":{\"pa\":%u,\"fault\":%u,\"cause\":%u,\"tval\":%u},"
               "\"actual\":{\"pa\":%u,\"fault\":%u,\"cause\":%u,\"tval\":%u},"
               "\"pass\":%s,\"category\":\"%s\"}\n",
               id, current, mprv, mpp, mode, flags, sum, mxr, layout,
               want.pa, want.fault, want.cause, want.tval,
               got.pa, got.fault, got.cause, got.tval, pass ? "true" : "false", category);
    }
    fprintf(stderr, "{\"total\":%u,\"passed\":%u,\"failed\":%u,\"sumFetch\":%u,"
            "\"mprvFetch\":%u,\"other\":%u,\"dataFailures\":%u,\"mprvOffFailures\":%u}\n",
            total, total - failed, failed, sum_failed, mprv_failed, other,
            data_failed, no_mprv_failed);
    free(ram);
    return !total ? 2 : failed ? 1 : 0;
}
