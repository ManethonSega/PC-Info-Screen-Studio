# Contributing

Contributions are welcome while the project is under active development.

## Development setup

1. Install the .NET 10 SDK on Windows.
2. Clone the repository.
3. Run `dotnet restore PCInfoScreenStudio.slnx`.
4. Build with `dotnet build PCInfoScreenStudio.slnx`.

Keep hardware protocol changes isolated in `src/Tedd.TuringScreen` when possible. New UI/data/media work belongs in `src/PCInfoScreenStudio.App`.

## Pull requests

Prefer focused pull requests. Include a short description of the user-facing behavior, testing performed, and any hardware model used for device testing.

Do not commit copyrighted theme media or fonts unless redistribution is clearly permitted.

## Licensing contributions

Original contributions to PC Info Screen Studio are accepted under
`GPL-3.0-or-later`, unless an explicit component-specific license applies.
Contributors retain their copyright; this policy does not assign ownership.

For changes to third-party components (including the MIT-licensed
`src/Tedd.TuringScreen` library), preserve the component's existing license and
copyright notices.

Before adapting code from another project, check the exact files' license,
preserve required notices, and identify the upstream URL, revision and modified
files in the pull request. A GPL license on this repository does not make every
upstream license compatible. If imported code is GPL-3.0-only, respect that
restriction when licensing and distributing the combined work.
