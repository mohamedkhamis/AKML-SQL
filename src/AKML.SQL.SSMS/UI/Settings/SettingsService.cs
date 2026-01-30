using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AKML.SQL.Shared;
using Newtonsoft.Json;
using Serilog;

namespace AKML.SQL.SSMS.UI.Settings
{
    /// <summary>
    /// Service for loading, saving, and managing AKML-SQL settings.
    /// Implements Story 3.3: Settings Framework & Options Page.
    /// </summary>
    public class SettingsService : IDisposable
    {
        private static readonly ILogger _logger = Log.Logger;
        private static readonly object _lock = new object();
        private static SettingsService? _instance;

        private readonly string _settingsFilePath;
        private AkmlSettings _settings;
        private bool _isDirty;
        private Timer? _autoSaveTimer;
        private bool _disposed;

        /// <summary>
        /// Gets the singleton instance of the settings service.
        /// </summary>
        public static SettingsService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new SettingsService();
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Gets the current settings.
        /// </summary>
        public AkmlSettings Settings => _settings;

        /// <summary>
        /// Event raised when settings are loaded.
        /// </summary>
        public event EventHandler? SettingsLoaded;

        /// <summary>
        /// Event raised when settings are saved.
        /// </summary>
        public event EventHandler? SettingsSaved;

        /// <summary>
        /// Event raised when a setting changes.
        /// </summary>
        public event EventHandler<SettingChangedEventArgs>? SettingChanged;

        private SettingsService()
        {
            _settingsFilePath = GetSettingsFilePath();
            _settings = new AkmlSettings();

            // Subscribe to setting changes
            _settings.SettingChanged += OnSettingChanged;

            // Load settings
            LoadSettings();

            // Setup auto-save timer (save dirty settings every 5 seconds)
            _autoSaveTimer = new Timer(AutoSaveCallback, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

            _logger.Debug("[SettingsService] Initialized with settings file: {Path}", _settingsFilePath);
        }

        /// <summary>
        /// Loads settings from disk.
        /// </summary>
        public void LoadSettings()
        {
            var correlationId = Guid.NewGuid().ToString("N").Substring(0, 8);
            _logger.Debug("[{CorrelationId}] Loading settings from: {Path}", correlationId, _settingsFilePath);

            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    var loadedSettings = JsonConvert.DeserializeObject<AkmlSettings>(json);

                    if (loadedSettings != null)
                    {
                        // Unsubscribe from old settings
                        _settings.SettingChanged -= OnSettingChanged;

                        _settings = loadedSettings;

                        // Subscribe to new settings
                        _settings.SettingChanged += OnSettingChanged;

                        _logger.Information("[{CorrelationId}] Settings loaded successfully", correlationId);
                    }
                    else
                    {
                        _logger.Warning("[{CorrelationId}] Settings file was empty or invalid, using defaults", correlationId);
                    }
                }
                else
                {
                    _logger.Information("[{CorrelationId}] Settings file not found, using defaults", correlationId);
                    SaveSettings(); // Create initial settings file
                }

                SettingsLoaded?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[{CorrelationId}] Error loading settings, using defaults", correlationId);
                _settings = new AkmlSettings();
                _settings.SettingChanged += OnSettingChanged;
            }
        }

        /// <summary>
        /// Saves settings to disk.
        /// </summary>
        public void SaveSettings()
        {
            var correlationId = Guid.NewGuid().ToString("N").Substring(0, 8);
            _logger.Debug("[{CorrelationId}] Saving settings to: {Path}", correlationId, _settingsFilePath);

            try
            {
                // Ensure directory exists
                var directory = Path.GetDirectoryName(_settingsFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    _logger.Debug("[{CorrelationId}] Created settings directory: {Directory}", correlationId, directory);
                }

                var json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
                File.WriteAllText(_settingsFilePath, json);

                _isDirty = false;
                _logger.Information("[{CorrelationId}] Settings saved successfully", correlationId);

                SettingsSaved?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[{CorrelationId}] Error saving settings", correlationId);
            }
        }

        /// <summary>
        /// Saves settings asynchronously.
        /// </summary>
        public async Task SaveSettingsAsync()
        {
            await Task.Run(() => SaveSettings());
        }

        /// <summary>
        /// Resets all settings to defaults.
        /// </summary>
        public void ResetToDefaults()
        {
            var correlationId = Guid.NewGuid().ToString("N").Substring(0, 8);
            _logger.Information("[{CorrelationId}] Resetting settings to defaults", correlationId);

            _settings.ResetToDefaults();
            SaveSettings();
        }

        /// <summary>
        /// Imports settings from a file.
        /// </summary>
        public bool ImportSettings(string filePath)
        {
            var correlationId = Guid.NewGuid().ToString("N").Substring(0, 8);
            _logger.Information("[{CorrelationId}] Importing settings from: {Path}", correlationId, filePath);

            try
            {
                if (!File.Exists(filePath))
                {
                    _logger.Warning("[{CorrelationId}] Import file not found: {Path}", correlationId, filePath);
                    return false;
                }

                var json = File.ReadAllText(filePath);
                var importedSettings = JsonConvert.DeserializeObject<AkmlSettings>(json);

                if (importedSettings == null)
                {
                    _logger.Warning("[{CorrelationId}] Invalid settings file format", correlationId);
                    return false;
                }

                // Unsubscribe from old settings
                _settings.SettingChanged -= OnSettingChanged;

                _settings = importedSettings;

                // Subscribe to new settings
                _settings.SettingChanged += OnSettingChanged;

                SaveSettings();

                _logger.Information("[{CorrelationId}] Settings imported successfully", correlationId);
                SettingsLoaded?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[{CorrelationId}] Error importing settings", correlationId);
                return false;
            }
        }

        /// <summary>
        /// Exports settings to a file.
        /// </summary>
        public bool ExportSettings(string filePath)
        {
            var correlationId = Guid.NewGuid().ToString("N").Substring(0, 8);
            _logger.Information("[{CorrelationId}] Exporting settings to: {Path}", correlationId, filePath);

            try
            {
                var json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
                File.WriteAllText(filePath, json);

                _logger.Information("[{CorrelationId}] Settings exported successfully", correlationId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[{CorrelationId}] Error exporting settings", correlationId);
                return false;
            }
        }

        /// <summary>
        /// Gets a setting value by name using reflection.
        /// </summary>
        public T? GetSetting<T>(string settingName)
        {
            var property = typeof(AkmlSettings).GetProperty(settingName);
            if (property == null) return default;

            var value = property.GetValue(_settings);
            return value is T typedValue ? typedValue : default;
        }

        /// <summary>
        /// Sets a setting value by name using reflection.
        /// </summary>
        public bool SetSetting<T>(string settingName, T value)
        {
            var property = typeof(AkmlSettings).GetProperty(settingName);
            if (property == null || !property.CanWrite) return false;

            try
            {
                property.SetValue(_settings, value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
        {
            _isDirty = true;
            _logger.Debug("[SettingsService] Setting changed: {Name} = {NewValue} (was {OldValue})",
                e.SettingName, e.NewValue, e.OldValue);

            SettingChanged?.Invoke(this, e);
        }

        private void AutoSaveCallback(object? state)
        {
            if (_isDirty && !_disposed)
            {
                SaveSettings();
            }
        }

        private static string GetSettingsFilePath()
        {
            return Path.Combine(AkmlConstants.Paths.AppDataFolder, "settings.json");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _logger.Debug("[SettingsService] Disposing");

            // Save any pending changes
            if (_isDirty)
            {
                SaveSettings();
            }

            _autoSaveTimer?.Dispose();
            _autoSaveTimer = null;

            _settings.SettingChanged -= OnSettingChanged;
        }
    }
}
