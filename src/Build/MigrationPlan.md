# UnoDevelop core migration bootstrap

This folder captures the first migration wave from SharpDevelop to UnoDevelop.

## Wave 1 projects

- Main/Core/Project/ICSharpCode.Core.Uno.csproj
- Main/Base/Project/ICSharpCode.SharpDevelop.Uno.csproj
- Main/SharpDevelop/SharpDevelop.csproj

## Migration policy

- Reuse upstream source via `Compile Include` + `Link` whenever possible.
- Keep platform-specific behavior in `.uno.cs` files inside UnoDevelop.
- Avoid broad rewrites of upstream files in early waves.
- Expand links in small batches to keep build health stable.
