# Security Policy

## Supported Versions

PoshMcp is currently in pre-1.0 development. Only the latest 0.x minor release line receives security updates. Older minor versions are not patched — please upgrade to the current release before reporting issues.

| Version | Supported          |
| ------- | ------------------ |
| 0.11.x  | :white_check_mark: |
| < 0.11  | :x:                |

Once 1.0 ships, this policy will be revised to cover one or more stable release lines.

## Reporting a Vulnerability

Please **do not** report security vulnerabilities through public GitHub issues, discussions, or pull requests.

Report vulnerabilities privately through GitHub's built-in private vulnerability reporting:

1. Go to the [Security tab](https://github.com/usepowershell/poshmcp/security) of this repository.
2. Click **Report a vulnerability**.
3. Provide a clear description, reproduction steps, affected versions, and any suggested mitigation.

### What to expect

- **Acknowledgment:** We aim to acknowledge new reports within 3 business days.
- **Triage:** Within 7 business days we will confirm the issue, request additional information if needed, and assign a severity.
- **Fix and disclosure:** We follow coordinated disclosure. We will work with you on a fix timeline appropriate to severity, prepare a patched release, and publish an advisory once a fix is available.
- **Credit:** Reporters are credited in the published GitHub Security Advisory unless you request otherwise.

If GitHub's private vulnerability reporting is not available to you, open a minimal public issue asking maintainers to enable a private contact channel — **do not include vulnerability details in the public issue**.

## Repository Security Controls

The following GitHub-native security features are expected to be enabled on this repository:

- **Secret scanning** — automatically detects committed credentials, API keys, and tokens in the repository history and new pushes.
- **Push protection** — blocks pushes that contain detected secrets before they reach the remote, giving contributors a chance to remove or rotate them.
- **Dependabot alerts** — surfaces vulnerable dependencies in NuGet and GitHub Actions manifests.
- **CodeQL analysis** — static analysis for C# and GitHub Actions workflows runs on pull requests and on the default branch.

These controls are configured in **Settings → Code security** by repository administrators. Contributors who encounter a push protection block should rotate the affected secret rather than bypassing the check.
