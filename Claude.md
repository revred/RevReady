# Claude Code — Work Instructions

**Rules**
- Keep native kernels stateless; all parallelism in C#.
- No exceptions across FFI; use int error codes + rev_last_error().
- Never change C ABI layouts without bumping REVREADY_API_VERSION.

**Tasks**
1) Implement Zstd/LZ4 behind existing C ABI; keep RAW path.
2) Add Merkle proof builder per WPS/04_Proofs_Ledger/proof_schema.json.
3) Extend C# demo to emit proof bundle and verify round-trip.

