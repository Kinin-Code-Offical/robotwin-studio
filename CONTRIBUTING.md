# Contributing

Thanks for contributing to RobotWin Studio.

## Where to start

- Bugs and concrete work items: open an Issue.
- Questions and “how do I…?”: use Discussions.

## Development setup

- Follow the Windows setup guide: `docs/SETUP_WINDOWS.md`.
- .NET is pinned via `global.json`.

## Running tests

- CoreSim unit tests:

  `dotnet test CoreSim/CoreSim.sln`

## Tooling

- Repo snapshot (updates README tree + docs index files):

  `python tools/rt_tool.py update-repo-snapshot`

- Unity plugin sync:

  `python tools/rt_tool.py update-unity-plugins`

## Pull requests

- Keep PRs small and focused.
- Include a short test/verification note.
- Do not commit build outputs (`bin/`, `obj/`, `TestResults/`, Unity `Library/`, etc.).
