# Azure import / reconcile — manual test walkthrough

Work top to bottom. Each step is either something you **run** in PowerShell or something you **do**
in the app, followed by what you should **see**. State builds as you go, so don't skip ahead.

Legend:

| | Meaning |
|---|---|
| ▶ **RUN** | Paste into PowerShell |
| 🖱 **DO** | Click through the app at <https://localhost:5139> |
| ✅ **EXPECT** | What proves the step passed |
| ⛔ **STOP** | If this fails, something is genuinely broken — don't continue |

---

# Setup

## Step 0 — Sign in and create the resource group

▶ **RUN**

```powershell
$RG  = "bastet_vnet_testing"
$LOC = "eastus"

az login
az account set --subscription "<your-subscription-id>"
az account show --query "{name:name, id:id}" -o table

az group create -n $RG -l $LOC
```

## Step 1 — Start the app

▶ **RUN**

```powershell
dotnet run --project src/Bastet --launch-profile http-dev
```

🖱 **DO** — open <https://localhost:5139>

✅ **EXPECT** — **Bulk Azure Import** and **Azure Reconcile** both appear in the top nav.

> Leave this running. Open a second PowerShell window for every `az` command below.

---

# Part 1 — The ordinary import

## Step 2 — Create a simple VNet

▶ **RUN**

```powershell
az network vnet create -g $RG -n vnet-simple -l $LOC --address-prefixes 10.10.0.0/16
az network vnet subnet create -g $RG --vnet-name vnet-simple -n snet-app  --address-prefixes 10.10.1.0/24
az network vnet subnet create -g $RG --vnet-name vnet-simple -n snet-data --address-prefixes 10.10.2.0/24
```

## Step 3 — Import it

🖱 **DO**

1. **Bulk Azure Import** → pick your subscription → **Next**
2. Tick `vnet-simple` **10.10.0.0/16** and both subnets
3. **Next: Preview** → **Next: Commit** → **Confirm Import**

✅ **EXPECT**

- Preview shows badge **New top-level**, creating `vnet-simple` (10.10.0.0/16)
- Two children listed: `snet-app` 10.10.1.0/24, `snet-data` 10.10.2.0/24
- After commit: the subnet tree shows `vnet-simple` with 2 children

## Step 4 — Re-open the import (the already-imported check)

🖱 **DO** — **Bulk Azure Import** → same subscription → **Next**

✅ **EXPECT**

- `snet-app` and `snet-data` are **greyed out**, badge **Already imported**
- The **10.10.0.0/16** prefix is greyed out, badge **Cannot import**, reason *"already has child subnets"*
- Ticking **Select all** selects **nothing** from this VNet

## Step 5 — Prove Select all is safe

🖱 **DO** — click **Select all**, then **Next: Preview**

⛔ **EXPECT** — the preview shows **no global errors**. If you see *"already exists in BASTET"*, disabled
rows are being submitted and the fix is broken.

## Step 6 — Try the hide toggle

🖱 **DO** — go **Back to Selection** → switch on **Hide already imported**

✅ **EXPECT** — `vnet-simple` disappears entirely. Switch it off and it comes back.

---

# Part 2 — The fully-encompassing VNet

> A VNet whose only subnet covers its whole prefix. This should import as **one** BASTET subnet, not two.

## Step 7 — Create it

▶ **RUN**

```powershell
az network vnet create -g $RG -n vnet-encompass -l $LOC `
  --address-prefixes 10.11.0.0/24 `
  --subnet-name snet-all --subnet-prefixes 10.11.0.0/24
```

## Step 8 — Import it

🖱 **DO** — **Bulk Azure Import** → subscription → tick `vnet-encompass` **10.11.0.0/24** *and* `snet-all` → Preview → Commit

✅ **EXPECT**

- Preview says **New top-level** and *"will be marked **fully allocated** (Azure subnet `snet-all` encompasses entire address space)"*
- Commit creates **exactly one** subnet: `vnet-encompass` 10.11.0.0/24
- **No** child called `snet-all` — the tree shows one row, marked fully allocated

## Step 9 — Re-open the import (the encompassing trap)

🖱 **DO** — **Bulk Azure Import** → subscription → **Next**

✅ **EXPECT**

- `snet-all` is **NOT** badged "Already imported" — it was never created as a subnet
- The **10.11.0.0/24** prefix is greyed, badge **Cannot import**, reason *"is marked as fully allocated"*

---

# Part 3 — Multiple prefixes on one VNet

## Step 10 — Create it

▶ **RUN**

```powershell
az network vnet create -g $RG -n vnet-multiprefix -l $LOC --address-prefixes 10.12.0.0/16 10.13.0.0/16
az network vnet subnet create -g $RG --vnet-name vnet-multiprefix -n snet-a --address-prefixes 10.12.1.0/24
az network vnet subnet create -g $RG --vnet-name vnet-multiprefix -n snet-b --address-prefixes 10.13.1.0/24
```

## Step 11 — Import both prefixes

🖱 **DO** — **Bulk Azure Import** → tick **both** prefixes and **both** subnets → Preview → Commit

✅ **EXPECT** — **two** separate top-level subnets (10.12.0.0/16 and 10.13.0.0/16), one child each.
Both carry the same VNet resource ID.

---

# Part 4 — Edge cases

## Step 12 — IPv4/IPv6 filtering

▶ **RUN**

```powershell
az network vnet create -g $RG -n vnet-dualstack -l $LOC --address-prefixes 10.15.0.0/16 fd00:15::/48
az network vnet subnet create -g $RG --vnet-name vnet-dualstack -n snet-dual `
  --address-prefixes 10.15.1.0/24 fd00:15:0:1::/64
az network vnet subnet create -g $RG --vnet-name vnet-dualstack -n snet-v6only `
  --address-prefixes fd00:15:0:2::/64
```

🖱 **DO** — **Bulk Azure Import** → look at `vnet-dualstack`

✅ **EXPECT**

- Only **10.15.0.0/16** is listed — `fd00:15::/48` never appears
- Only `snet-dual` is listed, showing **10.15.1.0/24** (not its IPv6 prefix)
- `snet-v6only` does **not** appear at all

## Step 13 — A VNet with no subnets

▶ **RUN**

```powershell
az network vnet create -g $RG -n vnet-empty -l $LOC --address-prefixes 10.14.0.0/16
```

🖱 **DO** — import `vnet-empty` 10.14.0.0/16 → Preview → Commit

✅ **EXPECT** — preview says *"No child subnets selected."*; one subnet created, not fully allocated.

## Step 14 — Nesting under an existing subnet

▶ **RUN**

```powershell
az network vnet create -g $RG -n vnet-deep -l $LOC --address-prefixes 10.20.0.0/16
az network vnet subnet create -g $RG --vnet-name vnet-deep -n snet-deep --address-prefixes 10.20.1.0/24
```

🖱 **DO**

1. In BASTET, create a subnet by hand: name `Corp`, network `10.0.0.0`, CIDR `8`
2. **Bulk Azure Import** → tick `vnet-deep` **10.20.0.0/16** + `snet-deep` → Preview

✅ **EXPECT** — badge **New child**, *"create `vnet-deep` (10.20.0.0/16) under `Corp`"* → Commit and
confirm it lands under `Corp` in the tree.

## Step 15 — Overlapping VNets are rejected

▶ **RUN**

```powershell
az network vnet create -g $RG -n vnet-overlap-a -l $LOC --address-prefixes 10.30.0.0/16 `
  --subnet-name snet-a --subnet-prefixes 10.30.1.0/24
az network vnet create -g $RG -n vnet-overlap-b -l $LOC --address-prefixes 10.30.0.0/16 `
  --subnet-name snet-b --subnet-prefixes 10.30.2.0/24
```

🖱 **DO** — tick **both** `vnet-overlap-a` and `vnet-overlap-b` (both 10.30.0.0/16) → **Next: Preview**

✅ **EXPECT** — red global error *"overlaps with"*, and **Next: Commit** stays disabled.

🖱 **DO** — go back, untick `vnet-overlap-b`, leave only `vnet-overlap-a` → Preview → Commit

✅ **EXPECT** — imports cleanly.

---

# Part 5 — Reconcile

> From here on you're deleting things in Azure and checking BASTET notices.

## Step 16 — Baseline: nothing has changed

🖱 **DO** — **Azure Reconcile** → pick your subscription → **Next: Scan**

✅ **EXPECT**

- *"Everything imported from this subscription still exists in Azure."*
- Your hand-made `Corp` (10.0.0.0/8) is **not** listed — it never came from Azure

## Step 17 — Fail closed ⛔ (the most important step)

▶ **RUN**

```powershell
az logout
```

🖱 **DO** — **Azure Reconcile** → subscription → **Next: Scan**

⛔ **EXPECT**

- Red banner **"Nothing was checked"** with the connection error
- **Zero** deletable rows, no checkboxes, no delete button

If it lists *anything* as deleted, **stop** — BASTET is treating "couldn't reach Azure" as "everything
is gone", and the next click would archive your whole tree.

▶ **RUN**

```powershell
az login
az account set --subscription "<your-subscription-id>"
```

## Step 18 — Delete one subnet → *Subnet deleted*

▶ **RUN**

```powershell
az network vnet subnet delete -g $RG --vnet-name vnet-multiprefix -n snet-a
```

🖱 **DO** — **Azure Reconcile** → scan

✅ **EXPECT** — only **10.12.1.0/24** listed, status **Subnet deleted**. Both 10.12/10.13 targets stay live.

## Step 19 — Re-address a subnet → *Prefix changed*

> Azure resource IDs are name-based, so deleting and recreating under the same name keeps the ID and
> changes only the address.

▶ **RUN**

```powershell
az network vnet subnet delete -g $RG --vnet-name vnet-multiprefix -n snet-b
az network vnet subnet create -g $RG --vnet-name vnet-multiprefix -n snet-b --address-prefixes 10.13.9.0/24
```

🖱 **DO** — scan again

✅ **EXPECT** — **10.13.1.0/24** listed as **Prefix changed**, reason naming the new `10.13.9.0/24`.

## Step 20 — Remove a VNet address prefix → *Prefix removed*

▶ **RUN**

```powershell
# Azure won't drop an address space that still has a subnet in it
az network vnet subnet delete -g $RG --vnet-name vnet-multiprefix -n snet-b
az network vnet update -g $RG -n vnet-multiprefix --remove addressSpace.addressPrefixes 1
az network vnet show -g $RG -n vnet-multiprefix --query addressSpace.addressPrefixes -o json
```

🖱 **DO** — scan again

✅ **EXPECT** — the **10.13.0.0/16** target listed as **Prefix removed** (*not* "VNet deleted"). The
10.12.0.0/16 target stays live.

## Step 21 — Delete the encompassing subnet → *Needs review*

> The VNet and its prefix survive, so there's nothing to delete — but the fully-allocated flag no
> longer has anything behind it.

▶ **RUN**

```powershell
az network vnet subnet delete -g $RG --vnet-name vnet-encompass -n snet-all
```

🖱 **DO** — scan

✅ **EXPECT**

- **10.11.0.0/24** appears under **Needs review** — with **no checkbox**
- Reason: *"Marked fully allocated, but no Azure subnet ... covers 10.11.0.0/24 any more"*
- If it's the only finding, the delete button stays disabled

## Step 22 — Now delete that whole VNet → *VNet deleted*

▶ **RUN**

```powershell
az network vnet delete -g $RG -n vnet-encompass
```

🖱 **DO** — scan

✅ **EXPECT** — **10.11.0.0/24** moves out of "Needs review" into the **deletable** list, status **VNet deleted**.

## Step 23 — Cascade warning over hand-made data

🖱 **DO** — in BASTET, add a child under `vnet-deep` (10.20.0.0/16) by hand: network `10.20.9.0`,
CIDR `24`. Then add a host IP to it.

▶ **RUN**

```powershell
az network vnet delete -g $RG -n vnet-deep
```

🖱 **DO** — scan

✅ **EXPECT** — the **10.20.0.0/16** row shows a red cascade warning counting **your hand-made child
and its host IP**. This is the case where reconcile takes live data with it — check the number before
approving.

## Step 24 — The confirmation gate

🖱 **DO** — tick a row → **Next: Confirm deletion** → type `yes` in the box

✅ **EXPECT** — the **Delete Stale Subnets** button stays disabled.

▶ **RUN** — the server must reject it independently, not just the UI

```powershell
Invoke-WebRequest -Uri "https://localhost:5139/Subnet/BulkDeleteStaleAzureSubnets" `
  -Method POST -ContentType "application/json" -Body '{"confirmation":"yes"}' `
  -SkipCertificateCheck -SkipHttpErrorCheck | Select-Object StatusCode, Content
```

✅ **EXPECT** — `400` and *"You must type 'approved' to confirm deletion."*

## Step 25 — Actually delete

🖱 **DO** — type `approved` → **Delete Stale Subnets**

✅ **EXPECT**

- Success message with counts, then a redirect to the subnet tree
- The deleted subnets are gone from the tree
- They appear under **Subnets → Deleted Subnets** (archived, not destroyed)
- Cascaded children and host IPs went with them

## Step 26 — A freed prefix can be re-imported

▶ **RUN**

```powershell
az network vnet delete -g $RG -n vnet-simple
```

🖱 **DO** — **Azure Reconcile** → scan → select the `vnet-simple` rows → `approved` → delete

▶ **RUN**

```powershell
az network vnet create -g $RG -n vnet-simple -l $LOC --address-prefixes 10.10.0.0/16 `
  --subnet-name snet-app --subnet-prefixes 10.10.1.0/24
```

🖱 **DO** — **Bulk Azure Import** → tick `vnet-simple` + `snet-app` → Preview → Commit

✅ **EXPECT** — imports cleanly. Before the delete this was blocked by the unique
`{NetworkAddress, Cidr}` constraint.

---

# Teardown

▶ **RUN**

```powershell
az group delete -n bastet_vnet_testing --yes --no-wait
```

---

# Known limitation worth confirming

**Bulk import cannot add newly-created Azure subnets to a VNet you've already imported.** Once a
target has children, the planner refuses it (*"already has child subnets"*), so its prefix — and
therefore everything under it — is greyed out.

To see it: after Step 3, run

```powershell
az network vnet subnet create -g $RG --vnet-name vnet-simple -n snet-new --address-prefixes 10.10.3.0/24
```

then re-open Bulk Azure Import. `snet-new` is new to BASTET, but its **10.10.0.0/16** prefix is
blocked, so it can't be imported. The only route today is to delete the BASTET children and re-import
the VNet. This is pre-existing planner behaviour, not a regression — the annotation just makes it
visible in step 2 instead of at Preview.
