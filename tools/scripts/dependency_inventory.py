"""What RobotWin Studio ships and builds on, with each licence: the NuGet packages of every project (their licence read
from the package's own .nuspec in the NuGet cache), and the native components and runtimes the installer carries.

Writes docs/DEPENDENCIES.md. With --check (CI) nothing is written: it fails when a package's licence is not on the
allowed list, or when docs/DEPENDENCIES.md is not what the projects resolve to now (a package was added or updated
without the inventory). Run after `dotnet restore`.

    python tools/scripts/dependency_inventory.py [--check]
"""
import json
import os
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = ROOT / "docs" / "DEPENDENCIES.md"

# What goes into the installer / the headless server, and what only builds and tests it.
SHIPPED = [
    "Studio/RobotWin.Studio/RobotWin.Studio.csproj",
    "CoreSim/src/RobotTwin.CoreSim/RobotTwin.CoreSim.csproj",
    "tools/RobotWin.Headless/RobotWin.Headless.csproj",
]
BUILD_ONLY = [
    "CoreSim/tests/RobotTwin.CoreSim.Tests/RobotTwin.CoreSim.Tests.csproj",
    "Studio/RobotWin.Studio.Tests/RobotWin.Studio.Tests.csproj",
]

# Licences a dependency may have without a word from the maintainers (SPDX ids).
ALLOWED = {"MIT", "Apache-2.0", "BSD-2-Clause", "BSD-3-Clause", "MS-PL", "Zlib", "ISC", "Unlicense", "0BSD"}

# Older packages name a URL instead of an expression.
LICENCE_URLS = {
    "https://licenses.nuget.org/MIT": "MIT",
    "https://github.com/dotnet/corefx/blob/master/LICENSE.TXT": "MIT",
    "https://github.com/dotnet/runtime/blob/main/LICENSE.TXT": "MIT",
    "https://licenses.nuget.org/Apache-2.0": "Apache-2.0",
    "https://raw.githubusercontent.com/xunit/xunit/master/license.txt": "Apache-2.0",
}

# Carried by the installer or the app beside the NuGet packages (see THIRD_PARTY_NOTICES.md for the texts).
BUNDLED = [
    (".NET Desktop Runtime 10.0 (Microsoft.NETCore.App, Microsoft.WindowsDesktop.App, hostfxr)", "the build machine's 10.0.x", "MIT", "installer component `dotnet`"),
    ("Open CASCADE Technology", "8.0.2", "LGPL-2.1 with the Open CASCADE exception", "installer component `occt` (`occt\\`)"),
    ("Microsoft Visual C++ runtime (msvcp140, vcruntime140 …)", "the build machine's", "Visual Studio redistribution terms", "`occt\\`, beside the bridge"),
    ("arduino-cli", "latest at build time", "GPL-3.0 (a separate program, not linked)", "installer component `arduino`"),
    ("Arduino AVR core, avr-gcc, Binutils, AVR Libc, AVRDUDE", "AVR core 1.8.6", "LGPL-2.1 / GPL-3.0 with the GCC runtime exception / BSD / GPL-2.0", "installer component `arduino`"),
    ("Adafruit CAD Parts (3D models)", "converted STEP files", "MIT", "embedded in the app"),
]


def nuget_root():
    return pathlib.Path(os.environ.get("NUGET_PACKAGES") or pathlib.Path.home() / ".nuget" / "packages")


def licence(package, version):
    spec = nuget_root() / package.lower() / version.lower() / (package.lower() + ".nuspec")
    if not spec.exists():
        return "unknown (not in the NuGet cache: run dotnet restore)"
    text = spec.read_text(encoding="utf-8", errors="replace")
    m = re.search(r'<license\s+type="expression"\s*>([^<]+)</license>', text)
    if m:
        return m.group(1).strip()
    if re.search(r'<license\s+type="file"', text):
        return "file in the package"
    m = re.search(r"<licenseUrl>([^<]+)</licenseUrl>", text)
    if m:
        url = m.group(1).strip()
        return LICENCE_URLS.get(url, url)
    return "unknown"


def allowed(expr):
    expr = expr.strip().strip("()")
    if " OR " in expr:
        return any(allowed(x) for x in expr.split(" OR "))
    if " AND " in expr:
        return all(allowed(x) for x in expr.split(" AND "))
    return expr in ALLOWED


def packages(project):
    out = subprocess.run(["dotnet", "list", str(ROOT / project), "package", "--include-transitive", "--format", "json"],
                         capture_output=True, text=True, encoding="utf-8", errors="replace", cwd=ROOT)
    if out.returncode != 0:
        sys.exit(f"dotnet list failed for {project}:\n{out.stdout}\n{out.stderr}")
    data = json.loads(out.stdout)
    found = {}
    for p in data.get("projects", []):
        for fw in p.get("frameworks", []):
            for kind in ("topLevelPackages", "transitivePackages"):
                for pkg in fw.get(kind, []):
                    found[(pkg["id"], pkg["resolvedVersion"])] = True
    return found


def inventory():
    rows = {}
    for group, projects in (("shipped", SHIPPED), ("build and test only", BUILD_ONLY)):
        for project in projects:
            name = pathlib.Path(project).stem
            for (pid, ver) in packages(project):
                row = rows.setdefault((pid, ver), {"licence": licence(pid, ver), "projects": set(), "group": group})
                row["projects"].add(name)
                if group == "shipped":
                    row["group"] = "shipped"
    return rows


def render(rows):
    lines = [
        "# Dependencies",
        "",
        "What RobotWin Studio ships and builds on, with each licence. Generated by `tools/scripts/dependency_inventory.py` "
        "from what the projects resolve (`dotnet list package --include-transitive`) and the packages' own `.nuspec`; "
        "CI fails when it is out of date or a licence is not on the allowed list "
        f"({', '.join(sorted(ALLOWED))}). The licence texts of the bundled components are in `THIRD_PARTY_NOTICES.md`.",
        "",
        "Türkçe: uygulamanın dağıttığı ve derlemede kullandığı bileşenler ve lisansları; CI denetler.",
        "",
    ]
    for group in ("shipped", "build and test only"):
        part = sorted((k, v) for k, v in rows.items() if v["group"] == group)
        lines += [f"## NuGet packages: {group}", "", "| Package | Version | Licence | Used by |", "| --- | --- | --- | --- |"]
        for (pid, ver), v in part:
            lines.append(f"| {pid} | {ver} | {v['licence']} | {', '.join(sorted(v['projects']))} |")
        lines.append("")
    lines += ["## Bundled components and runtimes", "", "| Component | Version | Licence | Where |", "| --- | --- | --- | --- |"]
    for c, ver, lic, where in BUNDLED:
        lines.append(f"| {c} | {ver} | {lic} | {where} |")
    lines.append("")
    return "\n".join(lines)


def main():
    check = "--check" in sys.argv[1:]
    rows = inventory()
    bad = [(pid, ver, v["licence"]) for (pid, ver), v in rows.items() if not allowed(v["licence"])]
    text = render(rows)
    if check:
        failed = False
        for pid, ver, lic in sorted(bad):
            print(f"::error::{pid} {ver}: licence '{lic}' is not on the allowed list (tools/scripts/dependency_inventory.py)")
            failed = True
        current = OUT.read_text(encoding="utf-8").replace("\r\n", "\n") if OUT.exists() else ""
        if current != text:
            print("::error::docs/DEPENDENCIES.md is out of date: run python tools/scripts/dependency_inventory.py and commit it")
            failed = True
        print(f"{len(rows)} packages, {len(bad)} with a licence off the list")
        sys.exit(1 if failed else 0)
    OUT.write_text(text, encoding="utf-8", newline="\n")
    print(f"{OUT.relative_to(ROOT)}: {len(rows)} packages")
    for pid, ver, lic in sorted(bad):
        print(f"  not on the allowed list: {pid} {ver} ({lic})")


if __name__ == "__main__":
    main()
