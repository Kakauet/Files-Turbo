# Contributing to Files Turbo
Files Turbo is a performance fork of [Files](https://github.com/files-community/Files). It keeps the upstream
feature set and only changes how work is done, so the most useful contributions are bug reports, measurements
and performance fixes. All community interactions must abide by the [Code of Conduct](./CODE_OF_CONDUCT.md).

## Where to report
* **Bugs in Files Turbo**: open an issue in this repository. Check first whether it also happens in the official
  Files app; if it does, it is an upstream bug and belongs in the
  [Files issues](https://github.com/files-community/Files/issues).
* **Feature requests**: Files Turbo does not add features. Please suggest them to
  [Files](https://github.com/files-community/Files/discussions), where they can benefit everyone.
* **Performance problems**: open an issue with the folder size, the view (details, grid, ...), the time you
  observed and, if possible, the same measurement in Files.

## Contributions we accept
* **DO** keep behaviour visible to users identical to upstream Files, unless the change fixes a bug.
* **DO** explain what was slow, why, and how you measured it before and after the change. Numbers without a
  method are hard to review; include the machine, the folder and the number of runs.
* **DO** add or update tests in `tests/Files.App.Turbo.UnitTests` whenever the logic can run without the UI.
* **DO** follow the [Coding and Style](https://files.community/docs/contributing/code-style) guidelines of
  Files and keep changes as small as possible, so they can also be offered upstream.
* **DO NOT** merge unrelated changes into one pull request.
* **DO NOT** change storage formats or settings files; Files Turbo must stay compatible with Files.

## Making changes to the code
Build and test instructions are in the [README](../README.md#build). Before opening a pull request, run:
```powershell
dotnet test --project tests/Files.App.Turbo.UnitTests -c Release -p:Platform=x64
```
CI runs the same tests, builds the app and checks the formatting of changed XAML files.

If your change is complex, please clean up the branch history before submitting a pull request.
You can use [git rebase](https://docs.microsoft.com/en-us/azure/devops/repos/git/rebase#squash-local-commits)
to group your changes into a small number of commits which can be reviewed one at a time.

## Running interaction tests (optional)
The tests drive the app through [Appium](https://appium.io), which automates it using WinAppDriver.
Install WinAppDriver, and Node.js to run Appium on:
```powershell
winget install Microsoft.WindowsApplicationDriver
winget install OpenJS.NodeJS
```
Then install Appium and the Windows driver it automates through:
```powershell
npm install --global appium
appium driver install --source=npm appium-windows-driver
```
The tests launch Appium themselves when it is not already running, so keep it on your `PATH`.

Finally, you will need to run the Files.Package at least once to have the debug version be registered on your machine.
Once you have the debug version registered, you can run the tests from the test explorer in Visual Studio.

## Upstream changes
Improvements that are useful to everyone are best contributed to Files directly. If a Files Turbo change could
be accepted upstream, mention it in the pull request so it can be offered to Files as well.
