# Contributing to LuxEngine

Thanks for helping make LuxEngine better!

## Reporting bugs

Open an [issue](https://github.com/vothalvino/LuxEngine/issues) and include:

- what you did, what you expected and what happened
- your Windows version and DMX interface
- `%LOCALAPPDATA%\LuxEngine\errors.log` if it exists

## Missing fixtures

The best fix for everyone is to add the fixture to the [Open Fixture Library](https://open-fixture-library.org/fixture-editor); it will be included the next time the library is refreshed. In the meantime, use **Fixtures → Create new fixture type** in the app. Your fixtures are saved as JSON in `Documents\LuxEngine\Fixtures` and can be shared with **Fixtures → Import fixture file**.

## Code

- Keep logic in `LuxEngine.Core` / `LuxEngine.Output` where it can be unit-tested; the app project should mostly be UI.
- Add or update tests in `tests/` for engine changes and run `dotnet test` on both test projects.
- Match the style of the surrounding code (`.editorconfig` defaults, nullable enabled).
- One topic per pull request, with a short description of what and why.
