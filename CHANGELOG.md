# Changelog

All notable changes to lmake are documented in this file. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project follows [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- `lmake build` builds a project folder or a single file: preprocesses Luau modules, converts `.luaurc` alias requires
  into relative paths and copies other files, skipping outputs that did not change.
- Built-in preprocessor compatible with GPP 2.28, with the default syntax
  `-U "" "" "(" "," ")" "(" ")" "#" "" -M "--#" "\n" " " " " "\n" "(" ")" +cQQQ "--[[#" "]]"`.
- `lmake gpp` runs the preprocessor with a gpp-compatible command line.
- `lmake init` creates `lmake.json` with the default settings.
- Require conversion follows the Luau require semantics: case-insensitive aliases, inherited and chained `.luaurc`
  aliases, `init.luau` modules and `@self`.
- Settings in `lmake.json`: input and output folders, extensions, exclude globs, copying of other files, line endings,
  defines, include folders, prelude files, custom preprocessor syntax and `--#exec`.
