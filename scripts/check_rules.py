import argparse
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
PROJECT = "src/LMake/LMake.csproj"
VERSION_PATTERN = re.compile(r"<Version>([^<]+)</Version>")
COMMENT_PATTERNS = {
	".cs": re.compile(r"^\s*(//|/\*)"),
	".py": re.compile(r"^\s*#"),
	".yml": re.compile(r"^\s*#"),
	".toml": re.compile(r"^\s*#"),
	".editorconfig": re.compile(r"^\s*[#;]"),
	".luau": re.compile(r"(^|[^-])--(?![#!-]|\[\[#)"),
	".expected": re.compile(r"(^|[^-])--(?![#!-]|\[\[#)"),
}
JSON_COMMENT = re.compile(r'^\s*//|,\s*//|\{\s*//')


def get_tracked_files():
	output = subprocess.check_output(["git", "ls-files", "--cached", "--others", "--exclude-standard"], cwd=ROOT, text=True)
	return [ROOT / line for line in output.splitlines() if (ROOT / line).is_file()]


def get_suffix(path):
	return path.name if path.name.startswith(".") else path.suffix


def get_comment_problems(files):
	problems = []

	for path in files:
		pattern = COMMENT_PATTERNS.get(get_suffix(path))

		if pattern is None and path.suffix != ".json":
			continue

		for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
			if (pattern and pattern.search(line)) or (path.suffix == ".json" and JSON_COMMENT.search(line)):
				problems.append(f"{path.relative_to(ROOT).as_posix()}:{number}: comment: {line.strip()}")

	return problems


def get_line_ending_problems(files):
	problems = []

	for path in files:
		data = path.read_bytes()

		if b"\0" not in data and b"\r" in data:
			problems.append(f"{path.relative_to(ROOT).as_posix()}: CRLF line endings")

	return problems


def get_version_problems(base):
	if not base:
		return []

	previous = subprocess.run(["git", "show", f"{base}:{PROJECT}"], cwd=ROOT, capture_output=True, text=True)

	if previous.returncode != 0:
		return []

	before = VERSION_PATTERN.search(previous.stdout)
	after = VERSION_PATTERN.search((ROOT / PROJECT).read_text(encoding="utf-8"))

	if before and after and before.group(1) != after.group(1):
		return [f"{PROJECT}: the version is raised by scripts/release.py bump, not by a pull request"]

	return []


def main():
	parser = argparse.ArgumentParser(description="Check repository rules: no comments, LF line endings, untouched version")
	parser.add_argument("--base", help="base commit of a pull request, enables the version check")
	arguments = parser.parse_args()

	files = get_tracked_files()
	problems = get_comment_problems(files) + get_line_ending_problems(files) + get_version_problems(arguments.base)

	for problem in problems:
		print(problem)

	return 1 if problems else 0


if __name__ == "__main__":
	sys.exit(main())
