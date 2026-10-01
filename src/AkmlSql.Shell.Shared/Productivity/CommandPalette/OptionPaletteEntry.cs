#nullable enable
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AkmlSql.Core.Models.Productivity;
using AkmlSql.Shell.Shared.Dialogs;

namespace AkmlSql.Shell.Shared.Productivity.CommandPalette
{
    /// <summary>
    /// Spec 040 (OPT-07, FR-053, research R7) — an Options setting listed in the Command Palette,
    /// under the <see cref="CategoryName"/> category. A toggle flips in place and shows its new
    /// <see cref="StateText"/> at once (the palette stays open); any other kind opens Options at
    /// that row. Its id is <c>opt:{pageKey}:{label}</c>, its name <c>‹page› › ‹label›</c>.
    /// </summary>
    internal sealed class OptionPaletteEntry : CommandEntry, INotifyPropertyChanged
    {
        /// <summary>Prefix of every option entry's <see cref="CommandEntry.Id"/>.</summary>
        internal const string IdPrefix = "opt:";

        /// <summary>The palette category the options are listed under.</summary>
        internal const string CategoryName = "Options";

        private bool _isOn;

        public OptionPaletteEntry(OptionsCatalogEntry option)
        {
            Option = option ?? throw new ArgumentNullException(nameof(option));
            Id = IdPrefix + option.PageKey + ":" + option.Label;
            Name = option.PageDisplay + " › " + option.Label;
            Category = CategoryName;
        }

        /// <summary>The Options row this entry stands for.</summary>
        internal OptionsCatalogEntry Option { get; }

        /// <summary>True when the entry flips in place (an on/off option).</summary>
        public bool IsToggle => Option.IsToggle;

        /// <summary>The toggle's current state; always false for other kinds.</summary>
        public bool IsOn
        {
            get => _isOn;
            set
            {
                if (_isOn == value) return;
                _isOn = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StateText));
            }
        }

        /// <summary><c>On</c> / <c>Off</c> for a toggle; empty for options that open the Options window.</summary>
        public string StateText => IsToggle ? (IsOn ? "On" : "Off") : string.Empty;

        /// <summary>True when <paramref name="commandId"/> names an option entry.</summary>
        internal static bool IsOptionId(string? commandId) =>
            commandId != null && commandId.StartsWith(IdPrefix, StringComparison.Ordinal);

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
