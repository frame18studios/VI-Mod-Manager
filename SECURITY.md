# Security and private reporting

## Supported versions

Only the latest `v*` release receives fixes. `v0.2` is an early, experimental release.

## Reporting a vulnerability

Please report security issues privately so they can be addressed before public disclosure:

- Use GitHub's **Report a vulnerability** option on the repository's **Security** tab (private vulnerability reporting is enabled), or
- Contact the maintainer privately through [GitHub](https://github.com/frame18studios) and ask for a private channel.

Do not open a public issue for an unpatched vulnerability. Include the affected version or build, reproduction steps, and the impact you observed. You should receive an acknowledgement within seven days, and maintainers will coordinate disclosure with you.

## Reporting infringing or unlawful material

Use the same private channel for takedown requests. Do not post personal contact details in a public issue. Include:

- The material you believe is infringing and its location.
- The right you believe is infringed.
- A statement that you are authorized to act for the rights holder.
- Your contact details, sent privately through the reporting channel.

See [DISCLAIMER.md](DISCLAIMER.md) for the project's scope and responsible-use terms.

## Scope

The shipped application makes no network calls at runtime, executes no package payloads, and does not request administrator privileges. Reports about path traversal, unsafe writes, backup integrity, or hash verification are especially welcome. Build-time tooling and its notices are documented in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
