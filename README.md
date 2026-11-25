<h1 align="center">lmake</h1>

> **If you can support author:** [Place](https://www.roblox.com/games/104318394065422/donating)

[![Release](https://img.shields.io/github/v/release/TheExeDEV/lmake)](https://github.com/TheExeDEV/lmake/releases/latest)

[![CI](https://github.com/TheExeDEV/lmake/actions/workflows/ci.yml/badge.svg)](https://github.com/TheExeDEV/lmake/actions/workflows/ci.yml) [![Release](https://github.com/TheExeDEV/lmake/actions/workflows/release.yml/badge.svg)](https://github.com/TheExeDEV/lmake/actions/workflows/release.yml) [![Coverage](https://github.com/TheExeDEV/lmake/actions/workflows/coverage.yml/badge.svg)](https://github.com/TheExeDEV/lmake/actions/workflows/coverage.yml)
A build tool for Luau, written in C#: a preprocessor compatible with GPP and darklua-style require conversion in one
executable.

lmake (luau-make) takes a source folder and writes a ready-to-run copy of it. Every module goes through a built-in
[GPP 2.28](https://logological.org/gpp) preprocessor, so you get `--#define`, `--#ifdef`, macros with arguments and
expressions while the source stays valid Luau. Then every `require("@alias/...")` from `.luaurc` is rewritten into a
relative path, the way darklua's `convert_require` rule does, so the result loads in any runtime without alias support.

**The preprocessor**

- Directives that look like Luau lines starting with `--`: `--#define`, `--#if`, `--#ifdef`, `--#elif`, `--#include`, `--#eval` and the rest of GPP
- Macros with numbered (`#1`) and named arguments, nested calls, balanced parentheses in arguments
- Inline values with `--[[# ... ]]`: the expression is evaluated and the delimiters disappear
- Expressions with C operators, `defined(NAME)`, `length(text)`, text comparison and `=~` glob matching
- `--#mode` to change the syntax on the fly, protect strings or switch to cpp, TeX or HTML syntax
- Output that matches GPP 2.28 byte for byte, checked against the reference binary in the tests
- `lmake gpp` takes the GPP command line, so it replaces `gpp` in existing scripts

**Requires**

- `require("@alias/path")`, `require "@alias/path"` and `require([[@alias/path]])` become relative paths
- The Luau rules for `.luaurc`: the nearest file wins, parents fill in the rest, names are case-insensitive, aliases may point to other aliases
- `init.luau` modules and `@self` as in the Luau require semantics
- Packages outside the source folder are referenced at their real location from the output folder
- Unknown aliases, missing modules and ambiguous ones (`x.luau` next to `x/init.luau`) are reported, never guessed

**The build**

- One `lmake.json` with input and output folders, defines, include folders, prelude files and exclude globs
- Files that are not modules are copied, outputs that did not change are not rewritten
- Line endings kept as in the source or forced to LF or CRLF
- One self-contained executable for Windows, Linux and macOS, x64 and arm64, no .NET to install

---

## Contents

1. [Installation](#installation)
2. [Quick start](#quick-start)
3. [Preprocessor](#preprocessor)
4. [Directives](#directives)
5. [Expressions](#expressions)
6. [Requires and .luaurc](#requires-and-luaurc)
7. [lmake.json](#lmakejson)
8. [Command reference](#command-reference)
9. [Differences from GPP](#differences-from-gpp)
10. [Tests](#tests)
11. [Releasing](#releasing)
12. [Building from source](#building-from-source)
13. [Source layout](#source-layout)
14. [Limitations](#limitations)
15. [Acknowledgements](#acknowledgements)
16. [License](#license)

---

## Installation

One script for every system. It detects Windows, Linux or macOS and the processor (x64 or arm64) by itself.

**Windows** (PowerShell):

```powershell
irm https://raw.githubusercontent.com/TheExeDEV/lmake/master/installers/install | iex
```

**Linux, macOS, Git Bash:**

```bash
curl -fsSL https://raw.githubusercontent.com/TheExeDEV/lmake/master/installers/install | sh
```

Both lines run the same file. It downloads lmake from the latest release into `%LOCALAPPDATA%\lmake\bin` (Windows) or
`~/.lmake/bin` and adds that folder to `PATH`. Open a new terminal and run `lmake version`.

Options, set before the command: `LMAKE_VERSION=0.0.1` installs a specific version, `LMAKE_INSTALL_DIR` changes the
folder, `LMAKE_NO_PATH=1` leaves `PATH` alone, `LMAKE_REPOSITORY=owner/name` downloads from a fork and `LMAKE_BASE_URL`
from a mirror. In PowerShell: `$env:LMAKE_VERSION = "0.0.1"`.

**Manually:** download `lmake-<platform>` from the latest release, rename it to `lmake` (or `lmake.exe`) and put it on
your `PATH`.

| Platform | File |
| --- | --- |
| Windows x64 | `lmake-win-x64.exe` |
| Windows arm64 | `lmake-win-arm64.exe` |
| Linux x64 | `lmake-linux-x64` |
| Linux arm64 | `lmake-linux-arm64` |
| macOS Intel | `lmake-osx-x64` |
| macOS Apple Silicon | `lmake-osx-arm64` |

To update, run the install line again.

### Uninstall

Also one script for every system:

```powershell
irm https://raw.githubusercontent.com/TheExeDEV/lmake/master/installers/uninstall | iex
```

```bash
curl -fsSL https://raw.githubusercontent.com/TheExeDEV/lmake/master/installers/uninstall | sh
```

It removes the `lmake` command and its `PATH` entries.

---

## Quick start

```bash
lmake init
```

writes `lmake.json` with the defaults: sources in `src`, output in `out`. Take this project:

```text
.luaurc                 { "aliases": { "shared": "./src/shared", "pkg": "./Packages" } }
Packages/signal/init.luau
src/main.luau
src/shared/config.luau
```

`src/shared/config.luau`:

```luau
--#define MAX_PLAYERS 12
return {
	maxPlayers = MAX_PLAYERS,
	roundTime = --[[#--#eval 60 * 3
]],
--#ifdef DEBUG
	logLevel = "verbose",
--#else
	logLevel = "warn",
--#endif
}
```

`src/main.luau`:

```luau
local Config = require("@shared/config")
local Signal = require("@pkg/signal")

--#ifdef DEBUG
print("debug build, up to", Config.maxPlayers, "players")
--#endif

return { Config = Config, Signal = Signal }
```

Build it with `DEBUG` defined:

```bash
lmake build -DDEBUG
```

```text
built 2 modules, copied 0 files, 0 unchanged (96 ms)
```

`out/shared/config.luau`:

```luau
return {
	maxPlayers = 12,
	roundTime = 180,
	logLevel = "verbose",
}
```

`out/main.luau`:

```luau
local Config = require("./shared/config")
local Signal = require("../Packages/signal")

print("debug build, up to", Config.maxPlayers, "players")

return { Config = Config, Signal = Signal }
```

`./shared/config` is inside the output folder, `../Packages/signal` points from `out` to the real `Packages` folder.
A single file builds the same way and prints the result:

```bash
lmake build src/main.luau
```

---

## Preprocessor

The default syntax is this GPP user mode:

```text
-U "" "" "(" "," ")" "(" ")" "#" "" -M "--#" "\n" " " " " "\n" "(" ")" +cQQQ "--[[#" "]]"
```

| Part | Meaning |
| --- | --- |
| `-U "" "" "(" "," ")" "(" ")" "#" ""` | user macros are called as `NAME` or `NAME(a, b)`; `#1` to `#9` refer to arguments inside a macro body; there is no quote character |
| `-M "--#" "\n" " " " " "\n" "(" ")"` | directives start with `--#`, take arguments separated by spaces and end at the end of the line |
| `+cQQQ "--[[#" "]]"` | the text between `--[[#` and `]]` is evaluated and replaced by its result, without the delimiters |

`#t` outside a macro body stays the Luau length operator. Macros are expanded everywhere outside directives, including
inside Luau strings; to protect strings, declare them:

```luau
--#mode string "\"" "\""
```

A directive that is indented keeps its indentation in the output, exactly like GPP. Another syntax can be set in
`lmake.json` with `preprocess.syntax`, for example `["-C"]` for cpp-style `#define`.

---

## Directives

| Directive | What it does |
| --- | --- |
| `--#define NAME value` | defines a macro; without a value the macro is empty |
| `--#define NAME(a, b) value` | defines a macro with named arguments |
| `--#defeval NAME value` | defines a macro whose value is evaluated right away |
| `--#undef NAME` | removes a macro |
| `--#ifdef NAME`, `--#ifndef NAME` | starts a block kept when the macro is (not) defined |
| `--#if expr`, `--#elif expr` | starts a block kept when the expression is not `0` |
| `--#ifeq a b`, `--#ifneq a b` | starts a block kept when the evaluated values are (not) equal as text |
| `--#else`, `--#endif` | switches or ends the current block |
| `--#eval expr` | inserts the value of an expression |
| `--#include file` | inserts another file, searched next to the current file and then in the include folders |
| `--#sinclude file` | the same, silent when the file does not exist |
| `--#mode command ...` | changes the syntax: `quote`, `comment`, `string`, `nocomment`, `nostring`, `user`, `meta`, `save`, `restore`, `standard`, `preservelf`, `charset` |
| `--#error message` | stops the build with the message |
| `--#warning message` | prints a warning |
| `--#line`, `--#file` | inserts the current line number or file name |
| `--#date format` | inserts the current date in `strftime` format |
| `--#exec command` | inserts the output of a shell command, only with `preprocess.allowExec` |

---

## Expressions

`--#if`, `--#elif` and `--#eval` evaluate their argument after expanding macros:

| Operators | Notes |
| --- | --- |
| `\|\|`, `&&`, `!` | logic, `0` is false |
| `\|`, `^`, `&`, `~` | bitwise |
| `==`, `!=`, `<`, `<=`, `>`, `>=` | compare numbers, or text when a side is not a number |
| `=~` | glob match: `file.luau =~ *.luau` |
| `+`, `-`, `*`, `/`, `%` | 32-bit integer arithmetic |
| `defined(NAME)` | `1` when the macro is defined |
| `length(text)` | length of the text |

Numbers can be decimal, hex (`0x1F`) or octal (`017`). When the expression is not a number, `--#eval` inserts the
expanded text and `--#if` treats it as true.

---

## Requires and .luaurc

After preprocessing, lmake rewrites requires that start with an alias:

| Source | From `src/client/view.luau` | From `src/client/init.luau` |
| --- | --- | --- |
| `require("@shared/config")` | `require("../shared/config")` | `require("./shared/config")` |
| `require("@client/ui")` | `require("./ui")` | `require("@self/ui")` |
| `require("@pkg/signal")` | `require("../../Packages/signal")` | `require("../Packages/signal")` |

The table assumes the aliases point into `src` and `Packages` and the output keeps the same layout.

- Aliases come from the nearest `.luaurc` walking up from the requiring file; parent files fill in the aliases a nearer
  file does not define. Names are case-insensitive and values are relative to the `.luaurc` that defines them.
- An alias may point to another alias (`"utils": "@shared/utils"`); a cycle is an error.
- `./` and `../` follow the Luau require semantics: they are relative to the requiring module, and `init.luau` stands for
  its folder. Requires from an `init.luau` into its own folder use `@self/...`.
- Targets inside the input folder are referenced in the output folder; targets outside it are referenced at their real
  location.
- `require("./x")`, `require("@self/x")`, `obj.require(...)` and requires with a computed path are left alone.

Warnings, without stopping the build:

| Warning | Meaning |
| --- | --- |
| `unknown alias '@x'` | no `.luaurc` defines it; the require is left as it is |
| `does not resolve to a module` | no `x.luau`, `x.lua` or `x/init.luau` there |
| `is ambiguous` | more than one of them exists |
| `cannot express a relative path` | the target is on another drive |

---

## lmake.json

```json
{
  "input": "src",
  "output": "out",
  "extensions": [".luau", ".lua"],
  "exclude": ["**/*.spec.luau"],
  "copyOtherFiles": true,
  "lineEndings": "auto",
  "preprocess": {
    "enabled": true,
    "defines": { "DEBUG": true, "VERSION": "\"1.0.0\"", "LEVEL": 3 },
    "includeDirs": ["include"],
    "prelude": ["include/macros.luau"],
    "syntax": null,
    "allowExec": false
  },
  "requires": {
    "enabled": true
  }
}
```

| Setting | Default | Meaning |
| --- | --- | --- |
| `input` | `src` | folder with the sources |
| `output` | `out` | folder for the result; skipped when it is inside `input` |
| `extensions` | `.luau`, `.lua` | files that are preprocessed and converted |
| `exclude` | none | globs relative to `input`; `*` stays inside a folder, `**` crosses folders |
| `copyOtherFiles` | `true` | copy files with other extensions |
| `lineEndings` | `auto` | `auto` keeps CRLF when the source uses it; `lf` or `crlf` force one style |
| `preprocess.enabled` | `true` | run the preprocessor |
| `preprocess.defines` | none | macros; `true` defines `1`, `null` or `""` defines an empty macro |
| `preprocess.includeDirs` | none | folders searched by `--#include` after the folder of the current file |
| `preprocess.prelude` | none | files processed before every module, for shared macros |
| `preprocess.syntax` | lmake syntax | GPP mode switches, for example `["-C"]` |
| `preprocess.allowExec` | `false` | enable `--#exec` |
| `requires.enabled` | `true` | convert alias requires |

Unknown settings are errors, so a typo does not go unnoticed. Trailing commas are accepted.

---

## Command reference

| Command | What it does |
| --- | --- |
| `lmake build [path]` | builds the project in `path` (default `.`), or a single file to standard output |
| `lmake init [folder]` | writes `lmake.json` with the defaults into the folder (default `.`) |
| `lmake gpp [options] [file]` | the preprocessor with the GPP 2.28 command line, see below |
| `lmake version` | version |
| `lmake help` | usage |

Options for `build`:

| Option | Meaning |
| --- | --- |
| `-c, --config file` | settings file, default `<path>/lmake.json` |
| `-o, --output path` | output folder, or output file for a single source file |
| `-D<name>[=<value>]` | define a macro, applied after the ones from `lmake.json` |
| `-I<folder>` | add an include folder |
| `--no-preprocess` | skip the preprocessor |
| `--no-requires` | keep alias requires |
| `-q, --quiet` | print only errors and warnings |

`lmake gpp` accepts `-U`, `-M`, `-C`, `-T`, `-H`, `-X`, `-P`, `+c`, `+s`, `-c`, `-s`, `-D`, `-I`, `-o`, `-O`, `-x`, `-m`,
`-n`, `+n`, `-z`, `+z`, `--include`, `--nocurinc`, `--curdirinclast` and `--warninglevel`. Without a file it reads
standard input.

The exit code is `1` on errors, so both commands fit CI as they are.

---

## Differences from GPP

- Errors, warnings, `--#line` and `--#error` use the real line number; GPP reports one more because it reads ahead
- On an error nothing is written; GPP leaves the output produced up to that point
- `=~` always works; some GPP builds are compiled without glob support
- A nested `--#include` is searched next to the file that contains it, not next to the name it was included by
- `--includemarker` is not supported

---

## Tests

```bash
dotnet test --solution lmake.slnx
```

| Suite | Checks |
| --- | --- |
| `GppParityTests` | every file in [`tests/fixtures/gpp`](tests/fixtures/gpp) against the output of the reference GPP 2.28 |
| `GppProcessorTests` | directives, macros, includes, `--#exec`, warnings, errors and recursion limits |
| `GppOptionsTests` | the GPP command line, invalid switches and mode conflicts |
| `UtilTests` | expressions, globs, dates and syntax specifiers |
| `LuauTests` | the Luau lexer, `.luaurc` resolution and require conversion |
| `BuildTests` | whole builds against [`tests/fixtures/build/basic/expected`](tests/fixtures/build/basic/expected), settings and line endings |

The expected outputs in `tests/fixtures/gpp` are generated by the real GPP, so the tests need neither GPP nor internet.
After adding a fixture, regenerate them:

```bash
python scripts/update_gpp_fixtures.py --gpp path/to/gpp
```

`python scripts/check_rules.py` checks the repository rules: no comments, LF line endings, a version left to releases.
On GitHub the [CI](.github/workflows/ci.yml) workflow builds and tests on Windows, Linux and macOS on every push and pull
request.

---

## Releasing

Releases publish themselves. The version lives in one place, `<Version>` in `src/LMake/LMake.csproj`.

```bash
python scripts/release.py bump
```

Versions count up by `0.0.1` and carry over every ten, like an odometer: `0.0.1`, `0.0.2` … `0.0.9`, then `0.1.0`;
after `0.9.9` comes `1.0.0`. `bump` without arguments picks the next one; it also takes `major`, `minor`, `patch` or an
exact version like `0.3.0`. It writes the version into `LMake.csproj` and turns `## [Unreleased]` in `CHANGELOG.md` into
`## [0.0.2] - <date>`. Commit and push to `master`.

On every push to `master` the [Release](.github/workflows/release.yml) workflow checks whether the tag `v<version>`
exists. If it does not, it:

1. runs the tests and builds self-contained binaries for Windows, Linux and macOS, x64 and arm64,
2. creates the tag `v<version>` and publishes the release with the changelog section as notes and every binary attached.

Pushes that do not change the version release nothing. To rebuild any commit under a chosen version, run the workflow by
hand: Actions → Release → Run workflow. `python scripts/release.py notes 0.2.0` prints what the release notes will be.

### Branch and tag rules

The rules live in [`.github/rulesets`](.github/rulesets):

| Ruleset | Protects | Rules |
| --- | --- | --- |
| `master` | the default branch | no deletion, no force push, changes through pull requests, `build (windows-latest)`, `build (ubuntu-latest)` and `build (macos-latest)` must pass |
| `release tags` | tags `v*` | nobody moves, overwrites or deletes them |

Admins can bypass both. Apply or update them with

```bash
python scripts/apply_rulesets.py
```

GitHub offers rulesets for public repositories and on GitHub Pro and above.

---

## Building from source

Needs the .NET 10 SDK.

```bash
git clone https://github.com/TheExeDEV/lmake.git
```

```bash
dotnet build lmake.slnx -c Release
```

A self-contained single file for one platform:

```bash
python scripts/release.py publish win-x64 lmake.exe
```

Code style: tabs, Allman braces, `snake_case`, explicit types and no comments, see [`CONTRIBUTING.md`](CONTRIBUTING.md).
`dotnet format` checks the formatting with the root `.editorconfig`.

---

## Source layout

```
src/LMake/
├── Core.d.cs         every public type: the contract
├── Project.cs        project constants
├── Program.cs        entry point, command dispatch
├── Cli/              build, init and gpp commands, console output
├── Core/             Luau lexer, .luaurc resolution
├── Features/         one operation per file: build, require conversion
├── Gpp/              the GPP-compatible preprocessor: options, syntax, modes, expressions
├── Models/           settings and enum names
└── Util/             globs, dates
installers/           install and uninstall scripts
scripts/              release, ruleset, rule check and fixture scripts
tests/                test project and fixtures
```

---

## Limitations

- Requires are found by tokens, not by a full parse: a local variable named `require` is treated like the global one
- Requires inside interpolated strings are converted only in the `{...}` expressions, never in the text around them
- A macro called with arguments that run past the end of the file is treated as a call without arguments, like in GPP
- `--#exec` uses `cmd.exe` on Windows and `/bin/sh` elsewhere

---

## Acknowledgements

The preprocessor follows [GPP](https://logological.org/gpp) by Denis Auroux and Tristan Miller, its manual and its
behaviour. The require conversion follows the [`convert_require`](https://darklua.com/docs/rules/convert_require/) rule of
[darklua](https://github.com/seaofvoices/darklua) and the [Luau require RFCs](https://rfcs.luau.org/).

---

## License

MIT, see [LICENSE](LICENSE).
