import argparse
import pathlib
import re
import shutil
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
FIXTURES = ROOT / "tests" / "fixtures" / "gpp"
SYNTAX = [
	"-U", "", "", "(", ",", ")", "(", ")", "#", "",
	"-M", "--#", "\\n", " ", " ", "\\n", "(", ")",
	"+cQQQ", "--[[#", "]]",
]
ERROR_PATTERN = re.compile(r"^[^\n]*?:\d+: error: (.*)$", re.M)


def get_gpp(requested):
	path = requested or shutil.which("gpp")

	if not path:
		raise SystemExit("gpp was not found; pass --gpp with the path to a GPP 2.28 binary")

	return path


def write_text(path, text):
	path.write_text(text, encoding="utf-8", newline="\n")


def update(gpp, source):
	result = subprocess.run([gpp, *SYNTAX, source.name], cwd=source.parent, capture_output=True)
	expected = source.with_suffix(".expected")
	error = source.with_suffix(".error")

	if result.returncode == 0:
		write_text(expected, result.stdout.decode("utf-8").replace("\r", ""))
		error.unlink(missing_ok=True)
		return "ok"

	match = ERROR_PATTERN.search(result.stderr.decode("utf-8"))

	if not match:
		raise SystemExit(f"{source.name}: gpp failed without an error message")

	write_text(error, match.group(1).strip() + "\n")
	expected.unlink(missing_ok=True)
	return "error"


def main():
	parser = argparse.ArgumentParser(description="Regenerate tests/fixtures/gpp expectations with a reference gpp binary")
	parser.add_argument("--gpp", help="path to gpp, defaults to the one on PATH")
	arguments = parser.parse_args()

	gpp = get_gpp(arguments.gpp)

	for source in sorted(FIXTURES.glob("*.luau")):
		print(f"{update(gpp, source):5} {source.name}")

	return 0


if __name__ == "__main__":
	sys.exit(main())
