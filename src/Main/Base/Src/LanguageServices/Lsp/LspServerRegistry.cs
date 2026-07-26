using System;
using System.Collections.Generic;
using System.IO;

namespace ICSharpCode.SharpDevelop.LanguageServices.Lsp
{
    public sealed class LspServerLaunchSpec
    {
        public LspServerLaunchSpec(string languageId, string command, params string[] arguments)
        {
            LanguageId = languageId ?? throw new ArgumentNullException(nameof(languageId));
            Command = command ?? throw new ArgumentNullException(nameof(command));
            Arguments = arguments ?? Array.Empty<string>();
        }

        public string LanguageId { get; }

        public string Command { get; }

        public IReadOnlyList<string> Arguments { get; }
    }

    public sealed class LspServerRegistry
    {
        readonly Dictionary<string, LspServerLaunchSpec> _specsByExtension =
            new(StringComparer.OrdinalIgnoreCase);

        public void Register(string extension, LspServerLaunchSpec spec)
        {
            if (spec is null)
                throw new ArgumentNullException(nameof(spec));

            _specsByExtension[NormalizeExtension(extension)] = spec;
        }

        public bool TryGetLaunchSpec(string extension, out LspServerLaunchSpec spec)
        {
            return _specsByExtension.TryGetValue(NormalizeExtension(extension), out spec!);
        }

        /// <summary>
        /// Pilot server mapping (docs/language-services.md slices 5-6): TypeScript/JavaScript
        /// via `typescript-language-server`, and Python via `pylsp`, both launched from PATH.
        /// The second language was added purely as config here — no change was needed to
        /// <see cref="LspLanguageService"/> — confirming the registry is genuinely
        /// language-agnostic rather than TypeScript-specific. Adding another language remains
        /// a config entry, not a code change.
        /// </summary>
        public static LspServerRegistry CreateDefault()
        {
            var registry = new LspServerRegistry();
            var typeScript = new LspServerLaunchSpec("typescript", "typescript-language-server", "--stdio");
            registry.Register(".ts", typeScript);
            registry.Register(".tsx", typeScript);
            registry.Register(".js", typeScript);
            registry.Register(".jsx", typeScript);
            var python = new LspServerLaunchSpec("python", "pylsp");
            registry.Register(".py", python);
            var fsharp = new LspServerLaunchSpec(
                "fsharp", "dotnet", "tool", "run", "fsautocomplete", "--");
            registry.Register(".fs", fsharp);
            registry.Register(".fsi", fsharp);

            return registry;
        }

        static string NormalizeExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
                throw new ArgumentException("An extension is required.", nameof(extension));

            return extension[0] == '.' ? extension : "." + extension;
        }
    }
}
