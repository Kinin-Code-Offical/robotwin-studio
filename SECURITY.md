# 🔒 Security Policy

## Supported Versions

We actively maintain security updates for the following versions:

| Version | Supported  | Notes               |
| ------- | ---------- | ------------------- |
| main    | ✅ Yes     | Active development  |
| 1.x.x   | ⚠️ Limited | Critical fixes only |
| < 1.0   | ❌ No      | No longer supported |

**Note**: Only the `main` branch receives regular security updates. We recommend always using the latest stable release.

## Reporting a Vulnerability

### 🚨 Security Issues

If you discover a security vulnerability in RobotWin Studio, please help us protect our users by following responsible disclosure practices:

### ✅ DO:

1. **Use GitHub Security Advisories** (preferred method):

   - Navigate to the [Security tab](https://github.com/Kinin-Code-Offical/robotwin-studio/security)
   - Click "Report a vulnerability"
   - Fill out the advisory form with details

2. **Contact maintainers privately** if Security Advisories are unavailable:

   - Open a minimal issue titled "Security Concern - Private Discussion Required"
   - Do NOT include exploit details in the public issue
   - Wait for maintainer response to establish secure communication

3. **Provide detailed information**:
   - Description of the vulnerability
   - Steps to reproduce
   - Potential impact assessment
   - Suggested fix (if available)
   - Your contact information for follow-up

### ❌ DON'T:

- **Do NOT open public issues** with detailed exploit information
- **Do NOT share vulnerabilities** on social media or public forums
- **Do NOT exploit vulnerabilities** beyond proof-of-concept testing
- **Do NOT demand compensation** (we don't have a bug bounty program)

## Our Commitment

### Response Timeline

- **Initial response**: Within 48 hours of report
- **Severity assessment**: Within 5 business days
- **Fix deployment**: Varies by severity (see below)
- **Public disclosure**: Coordinated with reporter after fix

### Severity Levels

| Severity | Response Time | Example                            |
| -------- | ------------- | ---------------------------------- |
| Critical | 1-3 days      | Remote code execution, data breach |
| High     | 1-2 weeks     | Privilege escalation, DoS          |
| Medium   | 2-4 weeks     | Information disclosure             |
| Low      | Next release  | Minor issues with limited impact   |

## Security Best Practices

### For Users

- **Keep software updated** to the latest stable version
- **Review dependencies** regularly using `dotnet list package --vulnerable`
- **Use strong authentication** for any external integrations
- **Limit permissions** to minimum required for operation
- **Monitor logs** for unusual activity

### For Contributors

- **Never commit secrets** (API keys, passwords, tokens) to the repository
- **Use `.gitignore`** to prevent accidental exposure of sensitive files
- **Validate all inputs** from external sources (files, network, user input)
- **Follow secure coding practices** (see `docs/CONTRIBUTING.md`)
- **Run static analysis** tools before submitting PRs

## Known Security Considerations

### Named Pipe Communication

RobotWin Studio uses named pipes for IPC between components. Be aware:

- Named pipes are accessible to any process on the local machine
- Do not transmit sensitive data through pipes without encryption
- Validate all data received from pipes

### Native Code

The C++ engines (NativeEngine, FirmwareEngine) run with Unity's permissions:

- Buffer overflows could affect the entire Unity process
- Ensure proper bounds checking in all native code
- Use AddressSanitizer during development

### Unity Integration

Unity plugins have full access to the Unity runtime:

- Validate all calls from managed to native code
- Use safe marshalling for string and pointer data
- Handle native exceptions gracefully

## Security Updates

Security fixes are announced through:

- **GitHub Security Advisories** (subscribers notified automatically)
- **Release notes** with `[SECURITY]` prefix
- **Repository README** security notice section

## Questions?

For security-related questions that are not sensitive:

- Open a [Discussion](https://github.com/Kinin-Code-Offical/robotwin-studio/discussions)
- Tag with `security` label

For sensitive security concerns, always use private reporting methods described above.

---

**Thank you for helping keep RobotWin Studio secure!**
