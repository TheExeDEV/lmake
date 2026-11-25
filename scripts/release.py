import argparse
import datetime
import pathlib
import re
import shutil
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
PROJECT = ROOT / "src" / "LMake" / "LMake.csproj"
CHANGELOG = ROOT / "CHANGELOG.md"
UNRELEASED = "## [Unreleased]"
VERSION_PATTERN = re.compile(r"^(\d+)\.(\d+)\.(\d+)(-[0-9A-Za-z.-]+)?$")
PROJECT_PATTERN = re.compile(r"(<Version>)([^<]+)(</Version>)")
PUBLISH = ROOT / "publish"
ODOMETER_BASE = 10
BUMP_KINDS = ("next", "major", "minor", "patch")


def get_version():
	match = PROJECT_PATTERN.search(PROJECT.read_text(encoding="utf-8"))

	if not match:
		raise SystemExit("<Version> not found in LMake.csproj")

	return match.group(2)


def parse(version):
	match = VERSION_PATTERN.match(version)

	if not match:
		raise SystemExit(f"invalid version: {version}")

	return tuple(int(part) for part in match.groups()[:3])


def get_next(kind):
	major, minor, patch = parse(get_version())

	if kind == "major":
		return f"{major + 1}.0.0"

	if kind == "minor":
		return f"{major}.{minor + 1}.0"

	if kind == "patch":
		return f"{major}.{minor}.{patch + 1}"

	patch += 1

	if patch == ODOMETER_BASE:
		patch = 0
		minor += 1

	if minor == ODOMETER_BASE:
		minor = 0
		major += 1

	return f"{major}.{minor}.{patch}"


def write_version(version):
	text = PROJECT.read_text(encoding="utf-8")
	PROJECT.write_text(PROJECT_PATTERN.sub(lambda match: match.group(1) + version + match.group(3), text), encoding="utf-8", newline="\n")


def move_unreleased(version):
	lines = CHANGELOG.read_text(encoding="utf-8").splitlines()

	if UNRELEASED not in lines:
		raise SystemExit("CHANGELOG.md has no [Unreleased] section")

	if any(line.startswith(f"## [{version}]") for line in lines):
		raise SystemExit(f"CHANGELOG.md already has {version}")

	index = lines.index(UNRELEASED)
	date = datetime.date.today().isoformat()
	lines[index:index + 1] = [UNRELEASED, "", f"## [{version}] - {date}"]
	CHANGELOG.write_text("\n".join(lines).rstrip("\n") + "\n", encoding="utf-8", newline="\n")


def get_notes(version):
	lines = CHANGELOG.read_text(encoding="utf-8").splitlines()

	for header in (f"## [{version}]", UNRELEASED):
		start = next((i for i, line in enumerate(lines) if line.startswith(header)), None)

		if start is None:
			continue

		end = next((i for i in range(start + 1, len(lines)) if lines[i].startswith("## ")), len(lines))
		body = "\n".join(lines[start + 1:end]).strip()

		if body:
			return body

	return f"lmake {version}"


def command_publish(arguments):
	output = PUBLISH / arguments.runtime
	version = arguments.version or get_version()
	subprocess.run(
		[
			"dotnet", "publish", str(PROJECT), "-c", "Release", "-r", arguments.runtime, "--self-contained",
			"-p:PublishSingleFile=true", "-p:PublishTrimmed=true", f"-p:Version={version}", "-o", str(output),
		],
		check=True,
	)
	binary = output / ("lmake.exe" if arguments.runtime.startswith("win") else "lmake")
	shutil.copyfile(binary, ROOT / arguments.asset)
	print(arguments.asset)


def command_version(arguments):
	print(get_version())


def command_bump(arguments):
	target = get_next(arguments.version) if arguments.version in BUMP_KINDS else arguments.version

	if parse(target) <= parse(get_version()) and "-" not in target:
		raise SystemExit(f"{target} is not newer than {get_version()}")

	write_version(target)
	move_unreleased(target)
	print(target)


def command_notes(arguments):
	print(get_notes(arguments.version or get_version()))


def main():
	parser = argparse.ArgumentParser(description="lmake release helper")
	commands = parser.add_subparsers(dest="command", required=True)

	commands.add_parser("version", help="print the current version").set_defaults(handler=command_version)

	bump = commands.add_parser("bump", help="set a new version: next (default, 0.0.9 -> 0.1.0), X.Y.Z, major, minor or patch")
	bump.add_argument("version", nargs="?", default="next")
	bump.set_defaults(handler=command_bump)

	publish = commands.add_parser("publish", help="build a self-contained single-file binary for a runtime identifier")
	publish.add_argument("runtime", help="runtime identifier, for example win-x64 or osx-arm64")
	publish.add_argument("asset", help="file name of the produced binary")
	publish.add_argument("--version", help="version to stamp, defaults to LMake.csproj")
	publish.set_defaults(handler=command_publish)

	notes = commands.add_parser("notes", help="print release notes from CHANGELOG.md")
	notes.add_argument("version", nargs="?")
	notes.set_defaults(handler=command_notes)

	arguments = parser.parse_args()
	arguments.handler(arguments)
	return 0


if __name__ == "__main__":
	sys.exit(main())
