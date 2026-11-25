# lmake

lmake (luau-make) is a build tool for Luau. It runs every module through a built-in preprocessor that is compatible with
[GPP 2.28](https://logological.org/gpp), then rewrites `require` calls that use `.luaurc` aliases into relative paths, the
way darklua's `convert_require` rule does. The result is plain Luau that any runtime can load without alias support.

lmake is a single self-contained executable written in C# (.NET 10). It does not need `gpp` or `darklua` installed.

## Install

Download the binary for your platform from the [releases](https://github.com/TheExeDEV/lmake/releases) page and put it on
your `PATH`:

| Platform | File |
| --- | --- |
| Windows x64 | `lmake-win-x64.exe` |
| Linux x64 | `lmake-linux-x64` |
| macOS Intel | `lmake-osx-x64` |
| macOS Apple Silicon | `lmake-osx-arm64` |

To build from source you need the .NET 10 SDK:

```bash
dotnet publish src/LMake -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=true -o publish
```

## Quick start

```bash
lmake init
lmake build
```

`lmake init` writes `lmake.json` with the default settings. `lmake build` reads every file in `src`, preprocesses `.luau`
and `.lua` modules, converts alias requires and writes the result to `out` with the same folder layout. Other files are
copied as they are. Files whose output did not change are not rewritten.

A single file can be built too. Without `-o` the result goes to standard output:

```bash
lmake build src/main.luau
lmake build src/main.luau -o out/main.luau -DDEBUG
```

## Preprocessor

The default syntax is the GPP user mode

```text
-U "" "" "(" "," ")" "(" ")" "#" "" -M "--#" "\n" " " " " "\n" "(" ")" +cQQQ "--[[#" "]]"
```

which means:

- directives (meta-macros) start with `--#` and end at the end of the line: `--#define NAME value`. To Luau tools they
  look like ordinary lines starting with `--`, so the source stays valid Luau;
- user macros are called as `NAME` or `NAME(a, b)`; inside a macro body `#1` to `#9` refer to the arguments, while `#t`
  stays the Luau length operator;
- `--[[# ... ]]` is evaluated and replaced by its result, without the delimiters, so a value can be placed in the middle
  of an expression: `local size = --[[# SIZE * 2 ]]`.

```luau
--#define DEBUG
--#define VERSION "1.4.0"
--#define CLAMP(x, lo, hi) math.clamp(x, lo, hi)

local Settings = {
	version = VERSION,
	volume = CLAMP(0.5, 0, 1),
	retries = --[[# --#eval 2 * 3
	]],
}

--#ifdef DEBUG
print("debug build", Settings.version)
--#else
Settings.version ..= "-release"
--#endif

return Settings
```

Supported directives:

| Directive | Meaning |
| --- | --- |
| `--#define NAME value`, `--#define NAME(a, b) value` | define a macro, optionally with named arguments |
| `--#defeval NAME value` | define a macro whose value is evaluated right away |
| `--#undef NAME` | remove a macro |
| `--#ifdef NAME`, `--#ifndef NAME` | conditional block on whether a macro is defined |
| `--#if expr`, `--#elif expr`, `--#else`, `--#endif` | conditional blocks on an expression |
| `--#ifeq a b`, `--#ifneq a b` | compare two evaluated values as text |
| `--#eval expr` | insert the value of an expression |
| `--#include file`, `--#sinclude file` | insert another file (`sinclude` ignores missing files) |
| `--#mode ...` | change the syntax: `quote`, `comment`, `string`, `nocomment`, `user`, `meta`, `save`, `restore`, `standard`, `preservelf`, `charset` |
| `--#error msg`, `--#warning msg` | stop the build, or print a warning |
| `--#line`, `--#file`, `--#date fmt` | current line, file name, formatted date |
| `--#exec command` | insert the output of a shell command (only with `allowExec`) |

Expressions support the C operators except shifts, `?:` and assignments, plus `defined(NAME)`, `length(text)` and
`text =~ glob`. When a side of a comparison is not a number, the comparison is done on text.

Macros are expanded everywhere outside directives, including inside Luau strings. To protect strings, declare them:

```luau
--#mode string "\"" "\""
```

Directives that are indented keep their indentation in the output, exactly like GPP.

## Require conversion

After preprocessing, every `require("@alias/path")`, `require "@alias/path"` and `require([[@alias/path]])` is rewritten
into a relative path. Aliases follow the Luau rules:

- aliases come from the nearest `.luaurc` walking up from the requiring file; parent files fill in the aliases that a
  nearer file does not define;
- alias names are case-insensitive and are resolved relative to the `.luaurc` that defines them;
- an alias may point to another alias (`"utils": "@shared/utils"`); cycles are reported;
- `@self` is left untouched.

The generated paths follow the Luau require semantics: `./` and `../` are relative to the requiring module, and
`init.luau` stands for its folder, so from `pkg/init.luau` the path `./x` means a sibling of `pkg`. Requires from an
`init.luau` to modules inside its own folder are written as `@self/...`. Targets inside the input folder are referenced
in the output folder; targets outside it (for example a `Packages` folder next to `src`) are referenced at their real
location.

Unknown aliases, paths that do not resolve to a module and ambiguous modules (`x.luau` next to `x.lua` or `x/init.luau`)
are reported as warnings. Unknown aliases are left as they are.

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
| `exclude` | none | glob patterns relative to `input`; `*` stays inside a folder, `**` crosses folders |
| `copyOtherFiles` | `true` | copy files with other extensions to the output |
| `lineEndings` | `auto` | `auto` keeps CRLF when the source uses it, `lf` or `crlf` force one style |
| `preprocess.defines` | none | macros; `true` defines `1`, `null` or `""` defines an empty macro |
| `preprocess.includeDirs` | none | folders searched by `--#include` after the folder of the current file |
| `preprocess.prelude` | none | files processed before every module, useful for shared macros |
| `preprocess.syntax` | lmake syntax | GPP mode switches as a list, for example `["-C"]` for cpp syntax |
| `preprocess.allowExec` | `false` | enable `--#exec` |
| `requires.enabled` | `true` | convert alias requires |

Comments and trailing commas are accepted in `lmake.json` and `.luaurc`.

## Command line

```text
lmake build [path] [options]   build a project folder or a single file
lmake init [directory]         create lmake.json with default settings
lmake gpp [gpp options] [file] run the built-in preprocessor with a gpp-compatible command line
lmake version
lmake help
```

| Build option | Meaning |
| --- | --- |
| `-c, --config <file>` | settings file, default `<path>/lmake.json` |
| `-o, --output <path>` | output folder, or output file for a single source file |
| `-D<name>[=<value>]` | define a macro, added after the ones from `lmake.json` |
| `-I<directory>` | add an include folder |
| `--no-preprocess` | skip the preprocessor |
| `--no-requires` | keep alias requires |
| `-q, --quiet` | print only errors and warnings |

`lmake gpp` accepts the GPP 2.28 switches (`-U`, `-M`, `-C`, `-T`, `-H`, `-X`, `-P`, `+c`, `+s`, `-c`, `-s`, `-D`, `-I`,
`-o`, `-O`, `-x`, `-m`, `-n`, `+n`, `-z`, `+z`, `--include`, `--nocurinc`, `--curdirinclast`, `--warninglevel`), so it can
replace `gpp` in existing scripts.

## Differences from GPP 2.28

The output of the preprocessor matches GPP byte for byte on the fixtures in `tests/fixtures/gpp`. Known differences:

- error and warning messages, `#line` and `#error` use the real line number; GPP reports one more because it reads ahead;
- on an error nothing is written, while GPP leaves the output produced up to that point;
- `=~` glob matching always works; some GPP builds are compiled without it;
- a nested `#include` is searched relative to the file that contains it, not relative to the name it was included by;
- `--includemarker` is not supported.

## Development

```bash
dotnet test --solution lmake.slnx
python scripts/check_rules.py
python scripts/update_gpp_fixtures.py --gpp path/to/gpp
python scripts/apply_rulesets.py
python scripts/release.py bump minor
```

`update_gpp_fixtures.py` regenerates the expected outputs in `tests/fixtures/gpp` with a reference GPP binary, so the
parity tests run offline. Releases are published by the `Release` workflow when the version in `src/LMake/LMake.csproj`
has no release yet.

## License

[MIT](LICENSE)
