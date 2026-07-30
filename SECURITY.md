# Security Policy

## Supported Versions

| Version | Supported |
| --- | --- |
| `1.0.x` | Yes |
| `< 1.0` | No |

The `main` branch receives development fixes. Stable security fixes are
documented in the changelog and released with an appropriate SemVer version.

## Reporting a Vulnerability

Do not include vulnerability details, credentials, browser artifacts, or
captured page content in a public issue.

If GitHub's private
[Report a vulnerability](https://github.com/bockuden/resilient-browser-automation/security/advisories/new)
form is available, use it and include:

- the affected version or commit;
- the security impact and prerequisites;
- minimal reproduction steps;
- whether credentials, traces, screenshots, HTML, or SQLite data are involved;
  and
- a suggested mitigation, if known.

The maintainer will acknowledge the report, validate its scope, and coordinate
disclosure through the private advisory. This portfolio project does not claim
a production response-time SLA.

If private reporting is not available, open a
[security contact request](https://github.com/bockuden/resilient-browser-automation/issues/new?template=security_contact.yml).
That issue is public: select only the affected area and do not include the
vulnerability, reproduction, credentials, logs, or artifacts. The maintainer
can then establish a private reporting channel.

## Security Boundaries

The repository demonstrates automation against its pinned deterministic local
Test Stand. It does not implement CAPTCHA bypass, anti-bot evasion, credential
harvesting, proxy rotation, or automation against third-party services.

Screenshots, HTML, traces, JSON metadata, and SQLite files can contain sensitive
target data in a real deployment. Keep them in restricted storage, configure
retention, and review redaction for the target domain.

Implementation details for secrets, evidence, CI permissions, and containers
are documented in [docs/security.md](docs/security.md).
