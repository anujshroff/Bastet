# Bastet — Deferred Findings

Findings that are real but whose correct fix is structural work a discovery/reconcile round must not
smuggle in. Each is transplanted whole (all fields, including repro) so the effort that closes it has
the full context. A deferred finding is terminal for the round that filed it and carries a `deferred`
ledger row; it gets a second ledger row when finally closed.

---

## L2-remainder (round 23) — Details CIDR modal carries a free-standing client IP engine duplicating IpUtilityService `[x1]`

**Split from:** round-23 L2. The wizard half (client re-deriving which VNet prefix each Azure subnet
belongs to) was fixed in round 23 by stamping `BulkAzureSubnetViewModel.ContainingPrefixes`
server-side and grouping the client by it, deleting `ipToInt`/`prefixContainsCidr` from
`_BulkScripts.cshtml`. This is the structural remainder.

**Where:** src/Bastet/Views/Subnet/Details/_SubnetCalculationScripts.cshtml:174-287 (approx; re-cite
at pickup).

**Breaks:** §5 (IpUtilityService is the only code that manipulates addresses as integers; a second
implementation is a real finding and the fix is to delete it). The Details "Add Child Subnet" CIDR
modal carries a free-standing JavaScript IP engine — masks, subnet boundaries, overlap detection, and
an optimal-CIDR search — that duplicates `IpUtilityService.IsSubnetContainedInParent`,
`CalculateUnallocatedRanges`, and the mask/boundary arithmetic. It agrees with the server today, but
drift would recommend a network/CIDR the `Subnet/Create` POST then rejects, or mis-detect overlap, so
the modal would suggest an allocation the app refuses — an unactionable error the operator cannot
resolve from that screen.

**Repro:** Verifier (round 23 finder) drove it live (Playwright): Details on 10.99.0.0/16, the modal
recommended a /17; a debug trace showed the client engine computing boundaries and overlap live,
entirely client-side, with no server round-trip. `git log -S`: the Details engine traces to 46b3e69
(#17) — original feature code, not audit residue.

**Why deferred, not fixed:** unlike the wizard half, the Details modal has NO server counterpart to
expose. Closing it requires a restructure: either a new server endpoint that returns the
CIDR/network suggestion (and overlap verdict) for a requested size within a parent, or precomputed
per-range suggestions carried in `SubnetDetailsViewModel`, then deleting the client engine and
binding the modal to the server result. That is a component reshape, not a narrow deletion, and must
be done deliberately (with its own tests and review) rather than smuggled into a reconcile fix.

**Proposed fix (at pickup):** add the server suggestion surface (endpoint or precomputed view-model
field) computed via `IpUtilityService` only; replace the client engine's recommendation/overlap
computation with the server result; delete the free-standing arithmetic in
`_SubnetCalculationScripts.cshtml`. Keep the modal's pure presentation (input handling, display).

**Confidence:** confirmed (duplication and live client computation both reproduced; the "would drift"
consequence is latent, not a live defect).

**Residue of:** none (original feature code, #17).
