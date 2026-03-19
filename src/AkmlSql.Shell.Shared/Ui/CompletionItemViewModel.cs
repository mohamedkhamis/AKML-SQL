using System;
using System.ComponentModel;

namespace AkmlSql.Shell.Shared.Ui
{
    /// <summary>
    /// T087: View model for a single completion item in the popup list.
    /// Shell code: .NET Framework 4.7.2, C# 7.3 compatible.
    /// Implements INotifyPropertyChanged for WPF data binding.
    /// </summary>
    public class CompletionItemViewModel : INotifyPropertyChanged
    {
        private string _displayText = string.Empty;
        private string _insertText = string.Empty;
        private string _secondaryText = string.Empty;
        private string _iconKind = string.Empty;
        private bool _isSelected;
        private int _objectType;

        public string DisplayText
        {
            get { return _displayText; }
            set
            {
                if (_displayText != value)
                {
                    _displayText = value;
                    OnPropertyChanged(nameof(DisplayText));
                }
            }
        }

        public string InsertText
        {
            get { return _insertText; }
            set
            {
                if (_insertText != value)
                {
                    _insertText = value;
                    OnPropertyChanged(nameof(InsertText));
                }
            }
        }

        public string SecondaryText
        {
            get { return _secondaryText; }
            set
            {
                if (_secondaryText != value)
                {
                    _secondaryText = value;
                    OnPropertyChanged(nameof(SecondaryText));
                }
            }
        }

        /// <summary>
        /// Icon identifier based on CompletionObjectType (Table, View, Column, etc.)
        /// </summary>
        public string IconKind
        {
            get { return _iconKind; }
            set
            {
                if (_iconKind != value)
                {
                    _iconKind = value;
                    OnPropertyChanged(nameof(IconKind));
                }
            }
        }

        public int ObjectType
        {
            get { return _objectType; }
            set
            {
                if (_objectType != value)
                {
                    _objectType = value;
                    OnPropertyChanged(nameof(ObjectType));
                    IconKind = MapObjectTypeToIcon(value);
                }
            }
        }

        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private static string MapObjectTypeToIcon(int objectType)
        {
            // Maps CompletionObjectType enum values to icon identifiers
            switch (objectType)
            {
                case 0: return "Table";
                case 1: return "View";
                case 2: return "Column";
                case 3: return "Keyword";
                case 4: return "Snippet";
                case 5: return "Function";
                case 6: return "Procedure";
                case 7: return "Schema";
                case 8: return "Database";
                case 9: return "Variable";
                case 10: return "Alias";
                case 11: return "Parameter";
                default: return "Unknown";
            }
        }
    }
}
