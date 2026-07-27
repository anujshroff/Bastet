---
name: import-commits
description: Replay commits from another clone of this repository onto a fresh local branch, recreating each one so it is authored, committed and signed by the local git identity. Use when work was done on a different machine (a VM, a second PC, a throwaway sandbox) and the resulting branch needs to land here as your own signed commits rather than being merged in as-is. Strips Co-Authored-By and generated-with trailers and restamps commit dates.
---

# Import commits from another clone as your own signed commits

Work done on another machine comes back as a full copy of this repository in a separate folder, with
a branch containing commits that are unsigned and stamped with whatever identity that machine had.
Merging or cherry-picking that branch directly preserves the wrong author and leaves the commits
unsigned. This skill instead **replays each commit locally**, so every one is authored, committed and
signed here.

## Invocation

```
/import-commits <source>
/import-commits <source> <branch>
```

| Argument | Required | Default |
|---|---|---|
| `source` | yes | Path to the repository copy, or any fetchable git remote |
| `branch` | no | The source repository's current HEAD branch |
| `target` | no | Same name as `branch` |
| `base` | no | `git merge-base` between local HEAD and the fetched branch |

Identity and signing are **read from local git config and never accepted as arguments** — passing an
identity in is how commits end up signed as the wrong person.

Defaults, applied unless the user says otherwise in their request:

- Strip `Co-Authored-By:` and `🤖 Generated with` trailers from every message.
- Reset author and committer dates to the time of import.
- Replay one-to-one. Do not squash — squashing is a merge-time decision.

## Step 0 — Pre-flight

Every check below is read-only. **If any fails, stop and report it. Do not proceed or work around it.**

```bash
git status --short                       # must be empty
git config user.name
git config user.email
git config commit.gpgsign                # must be true
git config user.signingkey               # must be set
git branch --list "<target>"             # must be empty
```

- **Dirty working tree** → stop. Replaying commits over uncommitted work risks losing it.
- **Signing not configured** → stop. Producing unsigned commits silently defeats the entire purpose
  of this skill. Say so plainly and let the user configure signing first.
- **Target branch already exists** → stop. Never append to, reset, or overwrite an existing branch.
  Tell the user it exists and let them delete it, rename it, or supply a different `target`.

Then inspect the source:

```bash
git -C "<source>" branch -vv
git -C "<source>" status --short         # report any uncommitted work left behind there
git -C "<source>" log --oneline <base>..<branch>
git -C "<source>" log --merges <base>..<branch>   # must be empty
git -C "<source>" merge-base HEAD <branch>
```

- **Merge base is not an ancestor of local HEAD** → the commit range is wrong. Stop and report;
  history diverged and the correct `base` must be established before anything is replayed.
- **Merge commits present in the range** → stop. `cherry-pick` needs `-m 1` for merges and picking a
  parent is a judgement call, not a default.

Check whether local HEAD carries commits the source does not have:

```bash
git rev-list --count <base>..HEAD
git log <base>..HEAD --oneline           # if the count is non-zero
```

If the count is non-zero, **say so before asking for confirmation**: the new branch will be rooted at
`<base>` and will **not** contain those commits. This is intentional — see below — but it must be
stated rather than assumed. Do not abort over it.

Finally, show the user the commits that will be imported and **wait for confirmation** before writing
anything:

```bash
git -C "<source>" log <base>..<branch> --pretty='%h | A:%an <%ae> | %s'
```

## Step 1 — Fetch

A local filesystem path is a valid git remote. Nothing is pushed; the source repository is never
modified.

```bash
git remote add import-src "<source>"
git fetch import-src
git log --oneline <base>..import-src/<branch>
```

## Step 2 — Create the branch

```bash
git checkout -b <target> <base>
```

### Why the merge base, and not local HEAD

Rooting here is what makes Step 4's verification exact: the replayed branch starts from the same
commit the source branched from, so its final tree can be compared directly against the source tip.

This means local commits made since that point are **not** carried onto the import branch. That is
usually what you want. The common case: repository tooling committed to the main branch, followed by
an import — the tooling commit is absent from the source's history, so the merge base is unchanged,
the import branch is rooted at the original point, and tree hashes still match exactly. The tooling
commit simply lives on its own branch.

If the user explicitly overrides `base` to sit on top of local-only commits, that is legitimate, but
it trades the exact tree check for the scoped one in Step 4.

## Step 3 — Replay each commit

Oldest first. For each commit: apply its changes without committing, rewrite the message, commit fresh.

```bash
AUTHOR="$(git config user.name) <$(git config user.email)>"
count=0
for sha in $(git rev-list --reverse <base>..import-src/<branch>); do
    git cherry-pick -n --allow-empty "$sha" >/dev/null || { echo "CHERRY-PICK FAILED at $sha"; break; }
    git log -1 --pretty=%B "$sha" \
        | grep -viE '^(Co-Authored-By:|🤖 Generated with)' > .git/IMPORT_MSG
    git commit --quiet --allow-empty --author="$AUTHOR" --date=now -F .git/IMPORT_MSG \
        || { echo "COMMIT FAILED at $sha"; break; }
    count=$((count+1))
done
rm -f .git/IMPORT_MSG
echo "replayed: $count"
```

Why each flag is there:

- **`-n`** stages the change without committing, so the message is entirely ours to write.
- **`--author` and `--date=now` set the author record explicitly.** Do **not** reach for
  `--reset-author` here — git rejects it outside `-C` / `-c` / `--amend` and the loop dies on the
  first commit with `fatal: --reset-author can be used only with -C, -c or --amend`. Setting the
  author explicitly is also robust either way: whether or not `cherry-pick -n` leaves a
  `CHERRY_PICK_HEAD` for `git commit` to inherit an author from (observed **absent** for a single
  `-n` pick, but do not depend on that), the identity written is the one read from local config.
- **Committer is always the local identity** — git takes it from config and it cannot be inherited
  from the source commit.
- **No `-S` needed** — `commit.gpgsign=true` signs every commit automatically. Step 0 already
  verified it.
- **`--allow-empty`** handles a commit whose changes are already present.

**Check the replayed count before moving on.** The loop `break`s on the first failure and leaves the
rest unimported; without the counter that looks indistinguishable from success. If it does not equal
the number of commits in the range, stop and diagnose rather than proceeding to Step 4.

On conflict, resolve, `git add`, run the two commands manually for that one commit, then resume the
loop for the remainder.

## Step 4 — Verify

The decisive check is that the replayed history yields a **byte-identical tree**:

```bash
git rev-parse HEAD^{tree}
git rev-parse import-src/<branch>^{tree}    # must be the SAME hash
git diff HEAD import-src/<branch>           # must be empty
```

Confirm the whole range arrived, then check identity and signature on every commit:

```bash
git rev-list --count <base>..HEAD                    # must equal the source range count
git log <base>..HEAD --pretty='%G? %an <%ae> | %cn' | sort | uniq -c
```

The second command collapses the range to one line per distinct signature/identity combination, so a
single stray commit is obvious. Expect one row: `G`, with the local identity as both author and
committer.

Expect `G` (good signature) and the local identity as both author and committer on every row. Confirm
no trailer survived:

```bash
git log <base>..HEAD --pretty=%B | grep -i "co-authored" || echo "clean"
```

**If the trees differ, stop and report it. Do not attempt a repair.** A mismatch means a commit failed
to apply cleanly; that needs diagnosing, and hand-patching the working tree to force the hashes to
match would hide a real problem.

### Fallback when the branch was rooted on top of local-only commits

Only when `base` was deliberately overridden so the branch sits above commits the source never had.
Exact tree equality cannot hold in that case, by construction — the local commits contribute files
the source tip does not contain. Verify with a scoped diff instead:

```bash
git log <base>..HEAD --name-only --pretty=format:   # derive the paths those commits touched
git diff HEAD import-src/<branch> -- ':(exclude)<those paths>'
```

Derive the exclusion list from that first command — **do not guess it**. An over-broad exclusion can
hide a file the import genuinely failed to bring across, which is the exact failure this step exists
to catch. If the scoped diff is not empty, treat it the same as a tree mismatch: stop and report.

## Step 5 — Clean up

```bash
git remote remove import-src
rm -f .git/IMPORT_MSG
```

Report to the user: how many commits landed, the branch they are on, that signatures verified, and
that **nothing has been pushed** — pushing and opening a PR remains their decision.

## Notes

- Never push. Never force-push. Never delete or reset the user's existing branches.
- The source repository is only ever read from.
- If the user asks to squash instead, that is a different operation — confirm the intended final
  message rather than inventing one.
