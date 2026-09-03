#!/usr/bin/env python3
"""Generate or verify the committed Fortuna OpenAPI document."""

import argparse
import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PROJECT = ROOT / "src/Presentation/ArturRios.Fortuna.WebApi/ArturRios.Fortuna.WebApi.csproj"
ASSEMBLY = PROJECT.parent / "bin/Release/net10.0/ArturRios.Fortuna.WebApi.dll"
DOCUMENT = ROOT / "docs/openapi/fortuna.json"


def normalized(path):
    with path.open(encoding="utf-8") as source:
        return json.dumps(json.load(source), indent=2, sort_keys=True) + "\n"


def generate(output):
    environment = {
        **os.environ,
        "FORTUNA_DATA_CONNECTIONSTRING": "Host=localhost;Database=fortuna;Username=postgres;Password=unused;Search Path=fortuna",
        "FORTUNA_DATA_DATABASETYPE": "PostgreSql",
        "FORTUNA_STORAGE_PROVIDER": "Filesystem",
        "FORTUNA_STORAGE_PATH": str(Path(tempfile.gettempdir()) / "fortuna-openapi-storage"),
        "FORTUNA_LOG_DIRECTORY": str(Path(tempfile.gettempdir()) / "fortuna-openapi-logs"),
        "FORTUNA_RUN_MIGRATIONS": "false",
        "FORTUNA_AUTH_TOKEN_SECRET": "fortuna-openapi-signing-key-with-enough-entropy",
        "FORTUNA_AUTH_TOKEN_ISSUER": "heimdall-openapi",
        "FORTUNA_AUTH_TOKEN_AUDIENCE": "fortuna-openapi",
        "FORTUNA_DEFAULT_DISPLAY_CURRENCY": "BRL",
        "FORTUNA_LOCALE": "pt-BR",
    }
    build = subprocess.run(
        [
            "dotnet",
            "build",
            str(PROJECT),
            "--configuration",
            "Release",
            "--disable-build-servers",
            "-m:1",
        ],
        cwd=ROOT,
        env=environment,
    )
    if build.returncode:
        return build.returncode

    result = subprocess.run(
        ["dotnet", "swagger", "tofile", "--output", str(output), str(ASSEMBLY), "v1"],
        cwd=ROOT,
        env=environment,
    )
    if result.returncode:
        return result.returncode

    output.write_text(normalized(output), encoding="utf-8")
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    arguments = parser.parse_args()

    with tempfile.TemporaryDirectory(prefix="fortuna-openapi-") as directory:
        generated = Path(directory) / "fortuna.json"
        if generate(generated):
            return 1

        if arguments.check:
            if not DOCUMENT.exists() or normalized(DOCUMENT) != normalized(generated):
                print("docs/openapi/fortuna.json is not current. Run scripts/openapi.py.", file=sys.stderr)
                return 1
            print("The committed OpenAPI document is current.")
            return 0

        DOCUMENT.parent.mkdir(parents=True, exist_ok=True)
        DOCUMENT.write_text(generated.read_text(encoding="utf-8"), encoding="utf-8")
        print(f"Wrote {DOCUMENT}")
        return 0


if __name__ == "__main__":
    sys.exit(main())
