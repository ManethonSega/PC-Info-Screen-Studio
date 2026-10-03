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
