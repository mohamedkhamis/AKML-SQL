namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) — M1 scaffold (T028). FR-005 (theme).
/// Real implementation (OS preference detection, IndexedDB persistence) lands at T034
/// in Phase 3 (M2).
/// </summary>
public interface IThemeService
{
    ThemeMode Current { get; }
    event Action? Changed;
    void Set(ThemeMode mode);
}

public enum ThemeMode { System, Light, Dark, HighContrast }

internal sealed class StubThemeService : IThemeService
{
    public ThemeMode Current { get; private set; } = ThemeMode.System;
    public event Action? Changed;

    public void Set(ThemeMode mode)
    {
        if (mode == Current) return;
        Current = mode;
        Changed?.Invoke();
    }
}
