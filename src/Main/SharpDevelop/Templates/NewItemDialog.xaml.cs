using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.SharpDevelop.Templates;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace UnoDevelop.Templates
{
    public sealed partial class NewItemDialog : ContentDialog
    {
        const string BundledTextTemplateIdentity = "UnoDevelop.Templates.TextTemplate.Item";

        readonly TemplateDiscoveryService _service;
        readonly string _targetDirectory;

        public IReadOnlyList<TemplateSummary> Templates { get; private set; }
            = Array.Empty<TemplateSummary>();

        public TemplateSummary? SelectedTemplate { get; private set; }

        public string ItemName => NameBox.Text.Trim();

        public IReadOnlyDictionary<string, string> AdditionalParameters => ParseParameters(ParametersBox.Text);

        NewItemDialog(TemplateDiscoveryService service, string targetDirectory)
        {
            InitializeComponent();
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _targetDirectory = targetDirectory ?? throw new ArgumentNullException(nameof(targetDirectory));
            StatusText.Text = "Loading templates...";
        }

        public static async Task<NewItemDialog?> ShowAsync(
            TemplateDiscoveryService service,
            string targetDirectory)
        {
            var dialog = new NewItemDialog(service, targetDirectory);
            dialog.XamlRoot = MainPage.Current?.XamlRoot;

            try
            {
                var templates = await service.GetInstalledTemplatesAsync(CancellationToken.None);
                templates = await EnsureBundledTextTemplateInstalledAsync(service, templates);

                // Filter to item-type templates (where tags include type=item).
                // Project templates are not relevant for "New Item" into an existing project.
                var itemTemplates = templates
                    .Where(t => t.Tags.TryGetValue("type", out var type)
                        && type.Equals("item", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                dialog.Templates = itemTemplates;
                dialog.TemplateList.ItemsSource = itemTemplates;

                if (itemTemplates.Length == 0)
                {
                    dialog.StatusText.Text = "No item templates found.";
                }
                else
                {
                    dialog.StatusText.Text = $"{itemTemplates.Length} template(s) available.";
                }
            }
            catch (Exception ex)
            {
                dialog.StatusText.Text = $"Failed to load templates: {ex.Message}";
            }

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary ? dialog : null;
        }

        static async Task<IReadOnlyList<TemplateSummary>> EnsureBundledTextTemplateInstalledAsync(
            TemplateDiscoveryService service,
            IReadOnlyList<TemplateSummary> templates)
        {
            if (templates.Any(t => string.Equals(t.Identity, BundledTextTemplateIdentity, StringComparison.Ordinal)))
                return templates;

            if (!TryResolveBundledTextTemplatePath(out var packagePath))
                return templates;

            try
            {
                var installed = await service.InstallTemplatePackageAsync(packagePath, CancellationToken.None);
                if (!installed)
                    return templates;

                return await service.GetInstalledTemplatesAsync(CancellationToken.None);
            }
            catch
            {
                return templates;
            }
        }

        static bool TryResolveBundledTextTemplatePath(out string packagePath)
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Templates", "Bundled", "TextTemplate"),
                Path.GetFullPath(Path.Combine(
                    AppContext.BaseDirectory,
                    "..",
                    "..",
                    "..",
                    "..",
                    "Main",
                    "SharpDevelop",
                    "Templates",
                    "Bundled",
                    "TextTemplate"))
            };

            foreach (var candidate in candidates)
            {
                if (!Directory.Exists(candidate))
                    continue;

                var configPath = Path.Combine(candidate, ".template.config", "template.json");
                if (!File.Exists(configPath))
                    continue;

                packagePath = candidate;
                return true;
            }

            packagePath = string.Empty;
            return false;
        }

        void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectedTemplate = TemplateList.SelectedItem as TemplateSummary;
            UpdateAddButton();
        }

        void OnNameChanged(object sender, TextChangedEventArgs e)
        {
            UpdateAddButton();
        }

        void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            if (SelectedTemplate is null || string.IsNullOrWhiteSpace(ItemName))
            {
                args.Cancel = true;
            }
        }

        void UpdateAddButton()
        {
            IsPrimaryButtonEnabled = SelectedTemplate is not null
                && !string.IsNullOrWhiteSpace(ItemName);
        }

        static IReadOnlyDictionary<string, string> ParseParameters(string text)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(text))
                return values;

            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                var separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0)
                    continue;

                var key = line.Substring(0, separatorIndex).Trim();
                var value = line.Substring(separatorIndex + 1).Trim();
                if (key.Length == 0)
                    continue;

                values[key] = value;
            }

            return values;
        }
    }
}
