# Miscellaneous Scripts, Prototypes, and Experiments in C#

### Prerequisites
* .NET 6 or newer
* Requires the [dotnet-script global tool](https://github.com/dotnet-script/dotnet-script). Install it with `dotnet tool install -g dotnet-script`.

### Usage
Run with any of these:
* `./script.csx [arguments]`
* `dotnet script script.csx [-- arguments]`
* `dotnet-script script.csx [-- arguments]`

### Troubleshooting
* If you get a package manager error while installing `dotnet-script`, then add `--ignore-failed-sources`. See [this issue](https://github.com/dotnet/core/issues/1962#issuecomment-740324062) for details.
* If VS Code can't find the "System" namespace, then restart OmniSharp. Open the command pallette (`ctrl + shift + p`) and enter `Restart OmniSharp`.
* If VS Code shows a "!!MISSING: command!!" warning in your code, then restart OmniSharp as described above. See [this issue](https://github.com/OmniSharp/omnisharp-vscode/issues/3003) for details.
* Rider does not yet support CSX files. They will be treated as plain text.