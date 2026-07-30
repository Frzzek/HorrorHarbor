# Development environment and throughput

This guide records the smallest useful toolset and safe throughput improvements
for HorrorHarbor. It does not weaken the authoritative release gate.

## Model and context

There is no repository or Visual Studio Code setting that increases an AI
provider's context window or output-token limit. Those limits are selected by the
active provider and model. Keep the highest-capability model available for
security and release work; reduce unnecessary context by excluding generated
trees rather than hiding authored source.

Do not automate profile creation or launch another editor window during release
work. Extension profiles are user state and should be changed manually only when
the user requests a profile change.

## Core toolset

The project requires:

- .NET SDK 10.
- Git and GitHub CLI.
- Python 3.13 through either `python` or the Windows `py -3` launcher.
- Windows PowerShell 5.1 or newer.

Useful Visual Studio Code extensions are:

- C# Dev Kit, C#, and the .NET runtime installer.
- PowerShell.
- Python, Pylance, and the Python debugger.
- Markdownlint.
- GitHub Actions.
- Zoo Code for agent-assisted work.

GitLens and Rainbow CSV are optional inspection aids. Node.js, Docker, CMake,
Ninja, and a separate Markdown CLI are not required by the current build, so
installing them solely for this repository adds maintenance without improving
the release gate.

## Safe local editor optimization

The shared `.vscode/settings.json` excludes `bin`, `obj`, `artifacts`, and
`TestResults` from file watching and ordinary search. These directories contain
generated output; excluding them reduces indexing work without suppressing
compiler analyzers or omitting authored files from verification. All other
`.vscode` content remains ignored as machine-local editor state.

These settings apply when the repository itself is opened as the Visual Studio
Code workspace. They contain no profile identifiers, credentials, absolute
paths, or other machine-specific values.

## Fast and authoritative loops

During implementation, run the narrowest relevant test project first. Before a
candidate is published or merged, run the authoritative gate:

```powershell
./eng/build.ps1 -Configuration Release
```

The GitHub quality workflow still executes the full gate for pull requests to
`main`. Pushes to topic branches no longer start a duplicate push workflow, and
superseded workflow runs are cancelled. A push to `main` and the scheduled
CodeQL scan remain enabled.
