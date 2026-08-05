# PhotoRoute

PhotoRoute is a .NET application contained in this repository. This README provides quick instructions to build, run, test, and contribute to the project.

## Repository

- Solution: PhotoRoute.slnx
- Repository root: F:\repos\PhotoRoute
- Remote: https://github.com/mingfei80/PhotoRoute (origin)
- Active branch: features/getdata (local workspace context)

## Requirements

- .NET 10 SDK (install from https://dotnet.microsoft.com)
- Visual Studio 2026 / Visual Studio Code or another editor that supports .NET 10

## Build

From the repository root run:

dotnet build PhotoRoute.slnx

Or open the solution in Visual Studio (File → Open → Project/Solution) and build from the IDE.

## Run

Identify the executable project you want to run (for example a web/API or console project) and run:

dotnet run --project <path-to-project.csproj>

Or use Visual Studio's Run/Debug controls.

## Tests

If the solution contains test projects, run:

dotnet test PhotoRoute.slnx

## Contributing

- Create feature branches (e.g., `features/your-feature`).
- Follow the existing code style and patterns.
- Open a pull request against the main repository remote when your change is ready.

## Troubleshooting

- If you see SDK errors, confirm the installed .NET SDK version with `dotnet --info` and install .NET 10.
- Restore packages with `dotnet restore` and try rebuilding.

## License

This repository does not include a license file. Add a LICENSE file to clarify the project license.

## Contact

For repository-specific questions, use the project's issue tracker or contact the repository owner listed on the remote URL.
