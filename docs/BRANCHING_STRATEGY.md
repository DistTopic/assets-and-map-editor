# Branching Strategy

This project follows a **GitFlow**-based branching model to ensure stability, traceability, and safe collaboration.

---

## Branch Overview

```
main            ← production-ready releases (protected, tagged)
  └── develop   ← integration branch for next release (protected)
       ├── feature/*   ← new features (from develop)
       ├── fix/*       ← bug fixes (from develop)
       └── docs/*      ← documentation changes (from develop)

release/*       ← release candidates (from develop → main)
hotfix/*        ← urgent fixes (from main → main + develop)
```

---

## Branch Roles

### `main`

- Represents the **latest stable release**.
- Every commit on `main` is a tagged release (e.g., `v1.0.0-preview`, `v1.0.0`).
- **Direct pushes are blocked.** All changes arrive via pull request only.
- Requires passing CI build and at least one approval.

### `develop`

- The **integration branch** for the next release.
- Feature branches merge here after review.
- **Direct pushes are blocked.** All changes arrive via pull request.
- Requires passing CI build.

### `feature/*`, `fix/*`, `docs/*`

- Short-lived branches created from `develop`.
- Naming convention: `feature/brush-system`, `fix/minimap-crash`, `docs/architecture`.
- Merged back into `develop` via pull request.
- Deleted after merge.

### `release/*`

- Created from `develop` when preparing a release.
- Naming convention: `release/v1.1.0`.
- Only bug fixes, version bumps, and documentation are allowed here.
- Merged into **both** `main` (tagged) and `develop`, then deleted.

### `hotfix/*`

- Created from `main` for critical production fixes.
- Naming convention: `hotfix/v1.0.1`.
- Merged into **both** `main` (tagged) and `develop`, then deleted.

---

## Workflow

### New Feature

```
1. git checkout develop
2. git pull origin develop
3. git checkout -b feature/my-feature
4. # work, commit, push
5. Open PR: feature/my-feature → develop
6. CI passes + review → merge
7. Delete feature/my-feature
```

### Preparing a Release

```
1. git checkout develop
2. git checkout -b release/v1.1.0
3. # version bump, final fixes, changelog update
4. Open PR: release/v1.1.0 → main
5. CI passes + review → merge
6. Tag: git tag v1.1.0 && git push origin v1.1.0
7. Merge release/v1.1.0 back into develop
8. Delete release/v1.1.0
```

### Hotfix

```
1. git checkout main
2. git checkout -b hotfix/v1.0.1
3. # apply fix, bump patch version
4. Open PR: hotfix/v1.0.1 → main
5. CI passes + review → merge
6. Tag: git tag v1.0.1 && git push origin v1.0.1
7. Merge hotfix/v1.0.1 back into develop
8. Delete hotfix/v1.0.1
```

---

## Branch Protection Rules

Both `main` and `develop` are protected with the following rules:

| Rule | `main` | `develop` |
|------|--------|-----------|
| Require pull request | Yes | Yes |
| Required approvals | 1 | 1 |
| Dismiss stale reviews on new push | Yes | Yes |
| Require status checks (CI build) | Yes | Yes |
| Require branches to be up to date | Yes | No |
| Block force pushes | Yes | Yes |
| Block branch deletion | Yes | Yes |
| Restrict who can push to matching branches | Maintainers only | Maintainers only |

---

## Commit Convention

All commits must follow [Conventional Commits](https://www.conventionalcommits.org/):

```
<type>(<scope>): <description>
```

See [CONTRIBUTING.md](../CONTRIBUTING.md) for the full list of types and scopes.
