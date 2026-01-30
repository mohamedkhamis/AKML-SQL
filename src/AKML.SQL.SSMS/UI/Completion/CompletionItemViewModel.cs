using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AKML.SQL.Shared.Contracts;

namespace AKML.SQL.SSMS.UI.Completion
{
    /// <summary>
    /// View model for a completion item in the suggestion popup.
    /// </summary>
    public class CompletionItemViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;
        private string _highlightedLabel;

        public CompletionItemViewModel(CompletionItem item)
        {
            Item = item ?? throw new ArgumentNullException(nameof(item));
            _highlightedLabel = item.Label;
        }

        /// <summary>
        /// Gets the underlying completion item.
        /// </summary>
        public CompletionItem Item { get; }

        /// <summary>
        /// Gets the display label for the completion item.
        /// </summary>
        public string Label => Item.Label;

        /// <summary>
        /// Gets the highlighted label with matched characters marked.
        /// </summary>
        public string HighlightedLabel
        {
            get => _highlightedLabel;
            set
            {
                if (_highlightedLabel != value)
                {
                    _highlightedLabel = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets the detail/description text.
        /// </summary>
        public string Detail => Item.Detail;

        /// <summary>
        /// Gets the full documentation.
        /// </summary>
        public string Documentation => Item.Documentation;

        /// <summary>
        /// Gets the text to insert when committed.
        /// </summary>
        public string InsertText => string.IsNullOrEmpty(Item.InsertText) ? Item.Label : Item.InsertText;

        /// <summary>
        /// Gets the filter text for matching.
        /// </summary>
        public string FilterText => string.IsNullOrEmpty(Item.FilterText) ? Item.Label : Item.FilterText;

        /// <summary>
        /// Gets the sort text for ordering.
        /// </summary>
        public string SortText => string.IsNullOrEmpty(Item.SortText) ? Item.Label : Item.SortText;

        /// <summary>
        /// Gets the relevance score (0-100).
        /// </summary>
        public int RelevanceScore => Item.RelevanceScore;

        /// <summary>
        /// Gets or sets whether this item is selected in the list.
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets whether this item should be preselected.
        /// </summary>
        public bool Preselect => Item.Preselect;

        /// <summary>
        /// Gets the completion item kind.
        /// </summary>
        public CompletionItemKind Kind => Item.Kind;

        /// <summary>
        /// Gets the icon for this completion item based on its kind.
        /// </summary>
        public ImageSource Icon => GetIconForKind(Item.Kind);

        /// <summary>
        /// Gets the kind display name for accessibility.
        /// </summary>
        public string KindDisplayName => GetKindDisplayName(Item.Kind);

        /// <summary>
        /// Gets whether this is a Pro-only feature item.
        /// </summary>
        public bool IsProFeature => Item.Tags.Contains("pro");

        private static ImageSource GetIconForKind(CompletionItemKind kind)
        {
            // Return appropriate icon based on kind
            // Icons are loaded from embedded resources
            var iconName = kind switch
            {
                CompletionItemKind.Keyword => "Keyword",
                CompletionItemKind.Table => "Table",
                CompletionItemKind.View => "View",
                CompletionItemKind.Column => "Column",
                CompletionItemKind.Function => "Function",
                CompletionItemKind.Procedure => "Procedure",
                CompletionItemKind.Schema => "Schema",
                CompletionItemKind.Database => "Database",
                CompletionItemKind.Snippet => "Snippet",
                CompletionItemKind.Alias => "Alias",
                CompletionItemKind.Parameter => "Parameter",
                CompletionItemKind.Variable => "Variable",
                CompletionItemKind.Index => "Index",
                CompletionItemKind.Trigger => "Trigger",
                CompletionItemKind.User => "User",
                CompletionItemKind.Constraint => "Constraint",
                _ => "Generic"
            };

            try
            {
                var uri = new Uri($"pack://application:,,,/AKML.SQL.SSMS;component/Resources/Icons/{iconName}.png", UriKind.Absolute);
                return new BitmapImage(uri);
            }
            catch
            {
                // Return null if icon not found - UI will handle missing icons
                return null;
            }
        }

        private static string GetKindDisplayName(CompletionItemKind kind)
        {
            return kind switch
            {
                CompletionItemKind.Keyword => "Keyword",
                CompletionItemKind.Table => "Table",
                CompletionItemKind.View => "View",
                CompletionItemKind.Column => "Column",
                CompletionItemKind.Function => "Function",
                CompletionItemKind.Procedure => "Stored Procedure",
                CompletionItemKind.Schema => "Schema",
                CompletionItemKind.Database => "Database",
                CompletionItemKind.Snippet => "Snippet",
                CompletionItemKind.Alias => "Alias",
                CompletionItemKind.Parameter => "Parameter",
                CompletionItemKind.Variable => "Variable",
                CompletionItemKind.Index => "Index",
                CompletionItemKind.Trigger => "Trigger",
                CompletionItemKind.User => "User",
                CompletionItemKind.Constraint => "Constraint",
                _ => "Item"
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
