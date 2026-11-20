# Security policy

Talk2Tia lets AI assistants modify industrial automation projects and download them to PLCs, so we take security
reports seriously.

## Supported versions

| Version | Supported |
|---|---|
| 0.9.x (beta) | ✅ |
| < 0.9 | ❌ |

## Reporting a vulnerability

**Please do not open a public issue.** Report vulnerabilities privately through
[GitHub Security Advisories](https://github.com/insideapp-srl/talk2tia/security/advisories/new).

Please include a description of the issue and its impact, the affected version, steps to reproduce and, if
possible, a suggested fix. We aim to acknowledge reports within 5 business days and will keep you informed
until the issue is resolved. We are happy to credit reporters in the release notes.

## Scope

In scope: the Talk2Tia server, installer, release pipeline and website in this repository — for example a way to
bypass the confirmation guards, to execute unintended Openness operations, or to tamper with signed releases.

Out of scope: vulnerabilities in Siemens TIA Portal or the Openness API (report them to
[Siemens ProductCERT](https://www.siemens.com/cert)), and in AI models or MCP clients (report them to their vendors).

## Verifying releases

Official binaries and installers are signed by **INSIDEAPP SOCIETÀ A RESPONSABILITÀ** (Certum code-signing
certificate) and listed with their SHA-256 checksums in `SHA256SUMS.txt` on each release. Do not run builds
from other sources in production environments.
