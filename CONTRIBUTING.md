# Contributing to lmake

Thanks for taking the time to improve lmake! Bug reports, fixes and new features are welcome.

## Getting started

1. Install the .NET 10 SDK and Python 3.
2. Clone the repository and run the tests:

   ```bash
   dotnet test --solution lmake.slnx
   ```

3. Run the built tool from source:

   ```bash
   dotnet run --project src/LMake -- build path/to/project
   ```

## Project layout

| Path | Contents |
| --- | --- |
| `src/LMake/Core.d.cs` | every public type and member; implementations do not add public members |
| `src/LMake/Project.cs` | project constants |
| `src/LMake/Program.cs` | entry point and command dispatch |
| `src/LMake/Cli/` | console commands |
| `src/LMake/Core/` | Luau lexer and `.luaurc` resolution |
| `src/LMake/Features/` | build and require conversion |
| `src/LMake/Gpp/` | the GPP-compatible preprocessor |
| `src/LMake/Models/` | settings and enum names |
| `src/LMake/Util/` | globs and date formatting |
| `tests/` | tests and offline fixtures |
| `scripts/` | release, ruleset and fixture helpers |

## Code style

The rules are checked in pull requests by `dotnet format` and `scripts/check_rules.py`:

- no comments anywhere, including test data; names and structure explain the code;
- `snake_case` members, lowercase type names, methods start with a verb (`get_`, `is_`, `add_`, `make_`, ...);
- tabs with a width of 4, Allman braces, braces on every block, a blank line between logical blocks;
- explicit types instead of `var`, at most 140 characters per line, LF line endings;
- operations that can fail return `result<T>`; string and enum dispatch uses `relevant_*` tables.

## Preprocessor changes

The preprocessor must keep matching GPP 2.28. Add a fixture to `tests/fixtures/gpp` for every behaviour you touch and
regenerate the expected output with a reference GPP binary:

```bash
python scripts/update_gpp_fixtures.py --gpp path/to/gpp
```

## Pull requests

- Keep each pull request focused on one change and add tests for it.
- Add an entry under `## [Unreleased]` in `CHANGELOG.md`.
- Do not change the version in `LMake.csproj`; releases raise it with `python scripts/release.py bump`.
- Write commit messages as short imperative sentences starting with a capital letter, for example `Add exclude globs`.
