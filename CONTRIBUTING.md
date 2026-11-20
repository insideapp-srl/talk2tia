# Contributing to Talk2Tia

Thanks for your interest in Talk2Tia! Bug reports, ideas and pull requests are welcome.

## Reporting issues

- Search [existing issues](https://github.com/insideapp-srl/talk2tia/issues) first.
- Include the Talk2Tia version (`talk2tia --version`), the TIA Portal version and update, the MCP client you use,
  the tool that failed with its arguments, and the error message.
- **Never attach customer projects, PLC programs or any confidential data.** Reduce the problem to a minimal
  test project.
- Security vulnerabilities must not be reported in public issues: see [SECURITY.md](SECURITY.md).

## Development setup

Requirements: Windows x64, TIA Portal V21 with Openness, your user in the **Siemens TIA Openness** group,
.NET SDK 8 or later (it builds the .NET Framework 4.8 projects), Node.js LTS for the website.

```powershell
dotnet build -c Release
dotnet test -c Release
```

The Siemens Openness assemblies are referenced from your TIA Portal installation. **Never commit or redistribute
`Siemens.Engineering*.dll`.**

Website:

```powershell
cd site
npm install
npm run dev
```

## Pull requests

1. Fork the repository and create a branch from `main` (`feat/…`, `fix/…`, `docs/…`).
2. Keep changes focused; add or update tests in `tests/Talk2Tia.Tests`.
3. Use [Conventional Commits](https://www.conventionalcommits.org/) for commit messages
   (e.g. `feat: add watch table tools`, `fix: handle know-how protected blocks`).
4. Add an entry under **Unreleased** in [CHANGELOG.md](CHANGELOG.md) for user-visible changes.
5. Make sure `dotnet build` and `dotnet test` pass, then open a pull request against `main`.

Pull requests from forks are built by a maintainer: CI for the server needs a machine with TIA Portal and does
not run automatically on fork pull requests.

### Adding a tool

- Put it in the matching class under `src/Talk2Tia.Core/Tools` and register new classes in `ServerHost.Toolsets`.
- Name it `<area>_<verb>_<object>` in snake_case, describe the tool and every parameter.
- Run all Openness calls through `TiaSession.RunAsync`.
- Destructive operations must require `confirm=true` (or `overwrite=true` for code replacement); the catalog
  tests enforce this.
- Regenerate `docs/tools.md` when the catalog changes.

## Releases

Maintainers release by pushing a `vX.Y.Z` tag on `main`. The release workflow builds, tests, signs and publishes
the installers. `0.x` versions are published as pre-releases.

## License

By contributing you agree that your contributions are licensed under the [MIT License](LICENSE).
