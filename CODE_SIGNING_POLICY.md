# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by
[SignPath Foundation](https://signpath.org).

This policy covers the Windows installers and executables of the DockStudio apps built from this repository:
ScreenLove, DockSpace, DockPDF and the DockStudio launcher.

## What gets signed

- Only files built by the release workflows in `.github/workflows/` (GitHub Actions) from a tagged commit of the
  `main` branch of this repository.
- Nothing is signed from a developer machine, and no third-party binaries are signed with this certificate
  except the open source components bundled in our own installers (see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)).

## Team roles

| Role | Members |
|---|---|
| Committers and reviewers | [RETC3](https://github.com/RETC3) |
| Approvers | [RETC3](https://github.com/RETC3) |

Every signing request is approved manually by an approver. All team members use multi-factor authentication
on GitHub and on SignPath.

## Privacy policy

These programs do not transfer any information to other networked systems unless specifically requested by the
user or the person installing or operating them, with these exceptions, all described in the app and on the website:

- **Update check**: at most once a day the app reads the latest version number from the project website
  (getdockstudio.it or screenlove.it, with GitHub as fallback). It can be turned off in the settings.
- **Anonymous usage statistics** and **error reports**: sent only if the user opts in, at installation or on first
  start. Both boxes start unchecked. Statistics contain a random identifier, the app version, the Windows version
  and usage time; error reports contain the error type and the code location, without file names or personal data.

Captures, documents and other user files never leave the computer.

## Contact

Issues: https://github.com/RETC3/DockStudio/issues
