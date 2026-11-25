# Security Policy

## Supported versions

Only the latest release of lmake receives security fixes.

## Reporting a vulnerability

Please do not open a public issue for security problems. Report them privately through
[GitHub security advisories](https://github.com/TheExeDEV/lmake/security/advisories/new) with a description, the steps
to reproduce and the lmake version. You will get an answer within a week.

## Things to keep in mind

- `--#exec` runs shell commands from the processed sources. It is disabled unless `preprocess.allowExec` is set in
  `lmake.json` or `-x` is passed to `lmake gpp`. Only enable it for sources you trust.
- `--#include` can read any file the current user can read. Treat preprocessed sources like build scripts.
