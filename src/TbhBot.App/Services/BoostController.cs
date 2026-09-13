using TbhBot.Core.Game;

namespace TbhBot.App.Services;

/// <summary>
/// Controlador persistente dos multiplicadores do Boost Lab.
/// Stats já suportados pelo editor principal continuam usando Engine.WantStats/AutomationLoop.
/// XP usa o StatType 47 (IncreaseExpAmount), observado como 1.0 no build validado.
/// </summary>
public sealed class BoostController : IDisposable
{
    public const int XpStatType = 47;

    private readonly EngineService _svc;
    private readonly object _sync = new();
    private readonly Timer _timer;

    private int _pid;
    private RawStatAccessor? _raw;

    private readonly Dictionary<string, double> _namedBaseline = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _namedFactors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _namedTargets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double?> _previousOverrides = new(StringComparer.Ordinal);

    private double? _xpBaseline;
    private double _xpFactor = 1.0;
    private int _tickBusy;
    private bool _disposed;

    public BoostController(EngineService svc)
    {
        _svc = svc;
        _svc.StateChanged += OnStateChanged;
        _timer = new Timer(_ => Tick(), null, 500, 500);
    }

    public double GetNamedFactor(string stat)
    {
        lock (_sync) return _namedFactors.TryGetValue(stat, out double v) ? v : 1.0;
    }

    public double XpFactor
    {
        get { lock (_sync) return _xpFactor; }
    }

    public bool HasActiveBoosts
    {
        get { lock (_sync) return _namedFactors.Count > 0 || _xpFactor != 1.0; }
    }

    public Task<bool> SetNamedFactorAsync(string stat, double factor)
        => Task.Run(() => SetNamedFactor(stat, factor));

    public Task<bool> SetXpFactorAsync(double factor)
        => Task.Run(() => SetXpFactor(factor));

    public Task<bool> ApplyFarmPresetAsync()
        => Task.Run(() => ApplyPreset(new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["Attack Damage"] = 1.50,
            ["Attack Speed"] = 1.25,
            ["Movement Speed"] = 1.25,
        }, xpFactor: 1.50, "FARM"));

    public Task<bool> ApplyBossSafePresetAsync()
        => Task.Run(() => ApplyPreset(new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["Attack Damage"] = 1.25,
            ["Attack Speed"] = 1.15,
            ["Max Hp"] = 1.50,
            ["Armor"] = 1.50,
        }, xpFactor: 1.0, "BOSS SAFE"));

    public Task RestoreAllAsync()
        => Task.Run(() =>
        {
            lock (_sync)
            {
                if (!EnsureSessionLocked())
                {
                    ClearStateLocked();
                    return;
                }

                RestoreNamedLocked();
                RestoreXpLocked();
                _svc.RaiseLog("BOOST LAB: multiplicadores restaurados para o baseline");
            }
        });

    private bool ApplyPreset(Dictionary<string, double> factors, double xpFactor, string name)
    {
        lock (_sync)
        {
            if (!EnsureSessionLocked()) return false;

            RestoreNamedLocked();
            RestoreXpLocked();

            foreach (var (stat, factor) in factors)
            {
                if (!SetNamedFactorLocked(stat, factor))
                {
                    RestoreNamedLocked();
                    RestoreXpLocked();
                    return false;
                }
            }

            if (!SetXpFactorLocked(xpFactor))
            {
                RestoreNamedLocked();
                RestoreXpLocked();
                return false;
            }

            _svc.RaiseLog($"BOOST LAB: preset {name} aplicado");
            return true;
        }
    }

    private bool SetNamedFactor(string stat, double factor)
    {
        lock (_sync)
        {
            if (!EnsureSessionLocked()) return false;
            bool ok = SetNamedFactorLocked(stat, factor);
            if (ok)
                _svc.RaiseLog($"BOOST LAB: {stat} x{NormalizeFactor(factor):0.##}");
            return ok;
        }
    }

    private bool SetNamedFactorLocked(string stat, double factor)
    {
        factor = NormalizeFactor(factor);

        if (factor == 1.0)
        {
            RestoreOneNamedLocked(stat);
            return true;
        }

        if (!_namedBaseline.TryGetValue(stat, out double baseline))
        {
            Dictionary<string, double> current;
            try { current = _svc.Engine.Stats.ReadStats(); }
            catch { return false; }

            if (!current.TryGetValue(stat, out baseline) || !double.IsFinite(baseline)) return false;
            _namedBaseline[stat] = baseline;
        }

        if (!_previousOverrides.ContainsKey(stat))
        {
            var currentOverrides = _svc.Engine.WantStats;
            _previousOverrides[stat] = currentOverrides.TryGetValue(stat, out double old) ? old : null;
        }

        double target = baseline * factor;
        if (!double.IsFinite(target)) return false;

        if (StatSafety.TryGet(stat, out var limit))
            target = Math.Clamp(target, limit.Min, limit.Max);

        _namedFactors[stat] = factor;
        _namedTargets[stat] = target;
        ApplyNamedTargetsLocked();
        return true;
    }

    private bool SetXpFactor(double factor)
    {
        lock (_sync)
        {
            if (!EnsureSessionLocked()) return false;
            bool ok = SetXpFactorLocked(factor);
            if (ok) _svc.RaiseLog($"BOOST LAB: XP Gain x{NormalizeFactor(factor):0.##}");
            return ok;
        }
    }

    private bool SetXpFactorLocked(double factor)
    {
        factor = NormalizeFactor(factor);
        if (_raw is null) return false;

        if (_xpBaseline is null)
        {
            double? raw = _raw.Read(XpStatType);
            if (raw is null || !double.IsFinite(raw.Value) || raw.Value < 0) return false;
            _xpBaseline = raw.Value;
        }

        if (factor == 1.0)
        {
            RestoreXpLocked();
            return true;
        }

        double target = _xpBaseline.Value * factor;
        if (!double.IsFinite(target)) return false;
        if (!_raw.Write(XpStatType, target)) return false;

        _xpFactor = factor;
        return true;
    }

    private static double NormalizeFactor(double factor)
    {
        if (!double.IsFinite(factor)) return 1.0;
        factor = Math.Clamp(factor, 1.0, 3.0);
        return Math.Abs(factor - 1.0) < 0.0001 ? 1.0 : factor;
    }

    private void ApplyNamedTargetsLocked()
    {
        if (_namedTargets.Count == 0) return;
        var merged = new Dictionary<string, double>(_svc.Engine.WantStats, StringComparer.Ordinal);
        foreach (var (stat, target) in _namedTargets) merged[stat] = target;
        _svc.Engine.WantStats = merged;
    }

    private void RestoreOneNamedLocked(string stat)
    {
        if (!_namedFactors.ContainsKey(stat) && !_previousOverrides.ContainsKey(stat)) return;

        var merged = new Dictionary<string, double>(_svc.Engine.WantStats, StringComparer.Ordinal);
        if (_previousOverrides.TryGetValue(stat, out double? previous) && previous is double old)
            merged[stat] = old;
        else
            merged.Remove(stat);

        _svc.Engine.WantStats = merged;
        _namedFactors.Remove(stat);
        _namedTargets.Remove(stat);
        _previousOverrides.Remove(stat);
    }

    private void RestoreNamedLocked()
    {
        if (_namedFactors.Count == 0 && _previousOverrides.Count == 0) return;

        var merged = new Dictionary<string, double>(_svc.Engine.WantStats, StringComparer.Ordinal);
        foreach (var stat in _previousOverrides.Keys.ToArray())
        {
            if (_previousOverrides[stat] is double old) merged[stat] = old;
            else merged.Remove(stat);
        }

        _svc.Engine.WantStats = merged;
        _namedFactors.Clear();
        _namedTargets.Clear();
        _previousOverrides.Clear();
    }

    private void RestoreXpLocked()
    {
        if (_raw is not null && _xpBaseline is double baseline)
        {
            try { _raw.Write(XpStatType, baseline); } catch { }
        }
        _xpFactor = 1.0;
    }

    private bool EnsureSessionLocked()
    {
        if (!_svc.IsAttached) return false;
        int pid = _svc.Engine.Target.ProcessId;
        if (pid <= 0) return false;

        if (_pid != pid || _raw is null)
        {
            _pid = pid;
            _raw = new RawStatAccessor(_svc.Engine.Memory, _svc.Engine.Scanner);
            _namedBaseline.Clear();
            _namedFactors.Clear();
            _namedTargets.Clear();
            _previousOverrides.Clear();
            _xpBaseline = null;
            _xpFactor = 1.0;
        }
        return true;
    }

    private void OnStateChanged()
    {
        lock (_sync)
        {
            if (!_svc.IsAttached)
            {
                _pid = 0;
                _raw = null;
                ClearStateLocked();
            }
            else
            {
                EnsureSessionLocked();
            }
        }
    }

    private void Tick()
    {
        if (_disposed || Interlocked.Exchange(ref _tickBusy, 1) != 0) return;
        try
        {
            lock (_sync)
            {
                if (!EnsureSessionLocked()) return;

                // Named stats usam o AutomationLoop existente. Aqui só garantimos que outro painel
                // não removeu acidentalmente os alvos enquanto o Boost Lab continua ativo.
                if (_namedTargets.Count > 0) ApplyNamedTargetsLocked();

                // XP ainda não faz parte do editor de 25 stats; reaplica somente esse float direto.
                if (_xpFactor != 1.0 && _xpBaseline is double baseline && _raw is not null)
                {
                    double target = baseline * _xpFactor;
                    _raw.Write(XpStatType, target);
                }
            }
        }
        catch { }
        finally { Volatile.Write(ref _tickBusy, 0); }
    }

    private void ClearStateLocked()
    {
        _namedBaseline.Clear();
        _namedFactors.Clear();
        _namedTargets.Clear();
        _previousOverrides.Clear();
        _xpBaseline = null;
        _xpFactor = 1.0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _svc.StateChanged -= OnStateChanged;
        _timer.Dispose();
    }
}
