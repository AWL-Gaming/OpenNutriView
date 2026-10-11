# OpenNutriView native regression checks

Set `ECO_NATIVE_DIR` to an immutable Eco 1099 runtime DLL copy. Set `ECO_NATIVE_DEPENDENCY_DIR` to its complete dependency copy if needed. Runtime DLLs must match `afb6b026c14454495459d823fb4bf26e21563370`; reference assemblies cannot execute this suite.

Run from the repository root on Windows:

```powershell
dotnet run --project tools\NativeFoodRegression\NativeFoodRegression.csproj -c Release
```

The woven mod runs against owned native users and stomachs for cache and registration checks. Food-source scanning and storage failure checks compile the actual method and constructor bodies with native Roslyn against owned fixture objects. They do not access a server, save, database or live shop. Client tooltips, cold world scanning and dinner-party calculations still need native server acceptance.

For CI, configure BUILD_RUNNER_LABELS as a JSON array selecting a Windows runner. Supply the runtime directories through the ECO_NATIVE_DIR and ECO_NATIVE_DEPENDENCY_DIR repository secrets.
