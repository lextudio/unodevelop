using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.SharpDevelop.Templates;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace UnoDevelop.Templates
{
    public sealed partial class NewProjectDialog : ContentDialog
    {
        readonly TemplateDiscoveryService _service;
        readonly string _defaultLocation;

        public IReadOnlyList<TemplateSummary> Templates { get; private set; }
            = Array.Empty<TemplateSummary>();

        public TemplateSummary? SelectedTemplate { get; private set; }

        public string ProjectName => NameBox.Text.Trim();

        public string Location => LocationBox.Text.Trim();

        public IReadOnlyDictionary<string, string> AdditionalParameters => ParseParameters(ParametersBox.Text);

        NewProjectDialog(TemplateDiscoveryService service, string defaultLocation)
        {
            InitializeComponent();
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _defaultLocation = defaultLocation ?? throw new ArgumentNullException(nameof(defaultLocation));
            LocationBox.Text = defaultLocation;
            StatusText.Text = "Loading templates...";
        }

        public static async Task<NewProjectDialog?> ShowAsync(
            TemplateDiscoveryService service,
            string defaultLocation)
        {
            var dialog = new NewProjectDialog(service, defaultLocation);
            dialog.XamlRoot = MainPage.Current?.XamlRoot;

            try
            {
                var templates = await service.GetInstalledTemplatesAsync(CancellationToken.None);

                // Filter to project-type templates (where tags include type=project).
                var projectTemplates = templates
                    .Where(t => !t.Tags.TryGetValue("type", out var type)
                        || !type.Equals("item", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                dialog.Templates = projectTemplates;
                dialog.TemplateList.ItemsSource = projectTemplates;

                if (projectTemplates.Length == 0)
                {
                    dialog.StatusText.Text = "No project templates found.";
                }
                else
                {
                    dialog.StatusText.Text = $"{projectTemplates.Length} template(s) available.";
                }
            }
            catch (Exception ex)
            {
                dialog.StatusText.Text = $"Failed to load templates: {ex.Message}";
            }

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary ? dialog : null;
        }

        void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectedTemplate = TemplateList.SelectedItem as TemplateSummary;
            UpdateCreateButton();
        }

        void OnNameChanged(object sender, TextChangedEventArgs e)
        {
            UpdateCreateButton();
        }

        void OnLocationChanged(object sender, TextChangedEventArgs e)
        {
            UpdateCreateButton();
        }

        void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            if (SelectedTemplate is null || string.IsNullOrWhiteSpace(ProjectName)
                || string.IsNullOrWhiteSpace(Location))
            {
                args.Cancel = true;
            }
        }

        void UpdateCreateButton()
        {
            IsPrimaryButtonEnabled = SelectedTemplate is not null
                && !string.IsNullOrWhiteSpace(ProjectName)
                && !string.IsNullOrWhiteSpace(Location);
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
