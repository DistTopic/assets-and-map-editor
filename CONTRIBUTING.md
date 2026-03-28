# Contributing to Assets And Map Editor

Thank you for your interest in contributing! This document provides guidelines and information to help you get started.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [How Can I Contribute?](#how-can-i-contribute)
- [Development Setup](#development-setup)
- [Pull Request Process](#pull-request-process)
- [Commit Convention](#commit-convention)
- [Style Guidelines](#style-guidelines)

## Code of Conduct

This project follows a [Code of Conduct](CODE_OF_CONDUCT.md). By participating, you agree to uphold its standards. Please report unacceptable behavior via the channels listed in the Code of Conduct.

## How Can I Contribute?

### Reporting Bugs

Before submitting a bug report, please check the [existing issues](https://github.com/DistTopic/assets-and-map-editor/issues) to avoid duplicates.

When filing a bug report, include:

- **Summary** — A clear, concise description of the problem.
- **Steps to Reproduce** — Specific steps to trigger the issue.
- **Expected Behavior** — What you expected to happen.
- **Actual Behavior** — What actually happened.
- **Environment** — OS, architecture, and application version.
- **Files** — If the issue relates to specific DAT/SPR/OTB/OTBM files, mention the format version and file size.

### Suggesting Features

Feature requests are welcome. Open an issue with the **Feature Request** template and describe:

- The problem your feature would solve.
- Your proposed solution.
- Any alternatives you've considered.

### Pull Requests

We welcome pull requests for bug fixes, improvements, and new features. For non-trivial changes, please open an issue first to discuss the approach before investing significant effort.

## Development Setup

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- A C# editor (Visual Studio, VS Code with C# Dev Kit, or JetBrains Rider)

### Building

```bash
git clone https://github.com/DistTopic/assets-and-map-editor.git
cd assets-and-map-editor
dotnet build src/App/AssetsAndMapEditor.App.csproj
```

### Running

```bash
dotnet run --project src/App/AssetsAndMapEditor.App.csproj
```

### Project Structure

- `src/App/` — Main Avalonia application (views, view models, controls, models)
- `src/OTB/` — OTB file format parsing library
- `.github/workflows/` — CI/CD pipeline definitions
- `docs/` — Extended documentation

## Pull Request Process

1. **Fork** the repository and create a feature branch from `main`.
2. **Write clear commit messages** following the [Commit Convention](#commit-convention).
3. **Test your changes** — ensure the application builds and runs correctly on your platform.
4. **Update documentation** if your change affects user-facing behavior.
5. **Open a pull request** targeting the `main` branch with a clear description of what and why.

A maintainer will review your PR and may request changes. Once approved, it will be merged.

## Commit Convention

This project follows [Conventional Commits](https://www.conventionalcommits.org/):

```
<type>(<scope>): <description>
```

### Types

| Type       | Usage                                      |
|------------|---------------------------------------------|
| `feat`     | New feature                                 |
| `fix`      | Bug fix                                     |
| `docs`     | Documentation only                          |
| `refactor` | Code change that neither fixes nor adds     |
| `perf`     | Performance improvement                     |
| `test`     | Adding or updating tests                    |
| `ci`       | CI/CD configuration changes                 |
| `chore`    | Maintenance tasks (dependencies, tooling)   |

### Scopes (optional)

Common scopes: `map`, `brush`, `sprite`, `otb`, `dat`, `session`, `minimap`, `ui`.

### Examples

```
feat(brush): add carpet brush type support
fix(minimap): prevent crash during render pass
docs: update README with build instructions
```

## Style Guidelines

### C# Code

- Follow the default .NET coding conventions.
- Use `CommunityToolkit.Mvvm` attributes (`[ObservableProperty]`, `[RelayCommand]`) for view model properties and commands.
- Prefer async/await for I/O-bound operations.
- Keep view models in `ViewModels/`, models in `Models/`, and custom controls in `Controls/`.

### AXAML (Avalonia XAML)

- Use data binding with compiled bindings where possible.
- Follow the existing theme conventions (Catppuccin Mocha dark theme).

### General

- Keep pull requests focused — one logical change per PR.
- Avoid unrelated formatting or refactoring in the same PR.
- Write self-explanatory code; add comments only where the intent is non-obvious.

---

Thank you for contributing!
