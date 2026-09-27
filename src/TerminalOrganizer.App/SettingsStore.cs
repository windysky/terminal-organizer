using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using TerminalOrganizer.Core.Assignment;
using TerminalOrganizer.Core.Overflow;

namespace TerminalOrganizer.App
{
    /// <summary>
    /// The loaded settings (REQ-SET-001, spec D-3): the hotkey string, the log path, and
    /// the manager choice riding the same file with SPEC-ZONE-005 ManagerStore semantics
    /// (ManagerWindowName keeps the legacy raw-title view; ManagerSelector is the B2
    /// canonical identity), the B1 onboarding flags (firstRunCompleted,
    /// startWithWindows — the latter retained for a later startup unit), and the C3
    /// schema-v2 overflow fields: the persisted OverflowPolicy (unknown text loads as
    /// Ask) and the CrossMonitorRedistribution mirror (maintained true exactly while the
    /// policy is Redistribute). MergeEnabled stays the A1 release gate and is NEVER
    /// turned on by the policy. Immutable; callers build a modified copy for the
    /// read-modify-write save.
    /// </summary>
    public sealed class AppSettings
    {
        /// <summary>The C3 settings schema version; a file without the field loads as this.</summary>
        public const int DefaultSchemaVersion = 2;

        private readonly string hotkey;
        private readonly string logPath;
        private readonly string managerWindowName;
        private readonly bool firstRunCompleted;
        private readonly bool startWithWindows;
        private readonly ManagerSelector managerSelector;
        private readonly PriorityOverride[] priorityOverrides;
        private readonly int schemaVersion;
        private readonly OverflowPolicy overflowPolicy;
        private readonly bool crossMonitorRedistribution;

        public AppSettings(string hotkey, string logPath, string managerWindowName)
            : this(hotkey, logPath, managerWindowName, false)
        {
        }

        public AppSettings(string hotkey, string logPath, string managerWindowName, bool mergeEnabled)
            : this(hotkey, logPath, managerWindowName, mergeEnabled, false, false)
        {
        }

        public AppSettings(string hotkey, string logPath, string managerWindowName, bool mergeEnabled,
            bool firstRunCompleted, bool startWithWindows)
            : this(hotkey, logPath, managerWindowName, mergeEnabled, firstRunCompleted, startWithWindows,
                null, null, DefaultSchemaVersion, OverflowPolicy.Ask, false)
        {
        }

        /// <summary>
        /// B2 canonical-manager form: the explicit selector is authoritative; callers
        /// keep ManagerWindowName coherent with it (the raw-title fallback, or null when
        /// empty) so the legacy ManagerStore view of the same file never disagrees.
        /// </summary>
        public AppSettings(string hotkey, string logPath, string managerWindowName, bool mergeEnabled,
            bool firstRunCompleted, bool startWithWindows, ManagerSelector managerSelector)
            : this(hotkey, logPath, managerWindowName, mergeEnabled, firstRunCompleted,
                startWithWindows, managerSelector, null)
        {
        }

        /// <summary>
        /// C1 form: also carries the manual priority overrides. Rows are held in the
        /// canonical sorted order (kind, then selector value ordinal, then rank); a null
        /// array means none.
        /// </summary>
        public AppSettings(string hotkey, string logPath, string managerWindowName, bool mergeEnabled,
            bool firstRunCompleted, bool startWithWindows, ManagerSelector managerSelector,
            PriorityOverride[] priorityOverrides)
            : this(hotkey, logPath, managerWindowName, mergeEnabled, firstRunCompleted, startWithWindows,
                managerSelector, priorityOverrides, DefaultSchemaVersion, OverflowPolicy.Ask, false)
        {
        }

        /// <summary>
        /// C3 schema-v2 form: also carries the settings schema version, the persisted
        /// overflow policy and the cross-monitor mirror. The legacy constructors default
        /// to schema 2 / Ask / false, so every pre-C3 read-modify-write keeps working.
        /// </summary>
        public AppSettings(string hotkey, string logPath, string managerWindowName, bool mergeEnabled,
            bool firstRunCompleted, bool startWithWindows, ManagerSelector managerSelector,
            PriorityOverride[] priorityOverrides, int schemaVersion, OverflowPolicy overflowPolicy,
            bool crossMonitorRedistribution)
        {
            this.MergeEnabled = mergeEnabled;
            this.hotkey = hotkey;
            this.logPath = logPath;
            this.managerWindowName = managerWindowName;
            this.firstRunCompleted = firstRunCompleted;
            this.startWithWindows = startWithWindows;
            this.managerSelector = managerSelector == null ? DeriveSelector(managerWindowName) : managerSelector;
            this.priorityOverrides = SettingsStore.NormalizeOverrides(priorityOverrides);
            this.schemaVersion = schemaVersion > 0 ? schemaVersion : DefaultSchemaVersion;
            this.overflowPolicy = overflowPolicy;
            this.crossMonitorRedistribution = crossMonitorRedistribution;
        }

        public string Hotkey { get { return hotkey; } }
        public bool MergeEnabled { get; private set; }
        public string LogPath { get { return logPath; } }

        /// <summary>The persisted manager choice (exact ordinal window name); null for no choice.</summary>
        public string ManagerWindowName { get { return managerWindowName; } }

        /// <summary>The B2 canonical manager choice; never null.</summary>
        public ManagerSelector ManagerSelector { get { return managerSelector; } }

        /// <summary>B1 onboarding: false until the first-run dialog has been acknowledged and saved.</summary>
        public bool FirstRunCompleted { get { return firstRunCompleted; } }

        /// <summary>B1 onboarding: the retained startup checkbox value; no Run key is ever written for it.</summary>
        public bool StartWithWindows { get { return startWithWindows; } }

        /// <summary>The C1 manual priority overrides in canonical sorted order; never null.</summary>
        public PriorityOverride[] PriorityOverrides
        {
            get { return (PriorityOverride[])priorityOverrides.Clone(); }
        }

        /// <summary>The C3 settings schema version (a legacy file without the field loads as 2).</summary>
        public int SchemaVersion { get { return schemaVersion; } }

        /// <summary>The C3 persisted overflow policy; the default and every unknown text load as Ask.</summary>
        public OverflowPolicy OverflowPolicy { get { return overflowPolicy; } }

        /// <summary>The C3 cross-monitor mirror: true exactly while the policy is Redistribute.</summary>
        public bool CrossMonitorRedistribution { get { return crossMonitorRedistribution; } }

        private static ManagerSelector DeriveSelector(string managerWindowName)
        {
            return string.IsNullOrEmpty(managerWindowName)
                ? ManagerSelector.None()
                : new ManagerSelector(ManagerSelectorKind.RawTitle, managerWindowName, managerWindowName);
        }

        public override string ToString()
        {
            return string.Format("hotkey={0}|log={1}|manager={2}", hotkey, logPath, managerWindowName == null ? "-" : managerWindowName);
        }
    }

    /// <summary>
    /// One persisted manual priority override (C1): the manager-style selector it keys on
    /// and the pinned manual rank. Valid ranks are 0..999 (lower = higher priority); the
    /// store ignores out-of-range rows with a diagnostic. Immutable; rows serialize in the
    /// canonical sorted order (selector kind, then value ordinal, then rank).
    /// </summary>
    public sealed class PriorityOverride
    {
        private readonly ManagerSelector selector;
        private readonly int rank;

        public PriorityOverride(ManagerSelector selector, int rank)
        {
            this.selector = selector;
            this.rank = rank;
        }

        public ManagerSelector Selector { get { return selector; } }
        public int Rank { get { return rank; } }
    }

    /// <summary>
    /// Loads and saves the single settings file (REQ-SET-001, spec D-3):
    /// %LOCALAPPDATA%\TerminalOrganizer\settings.json holding hotkey / logPath /
    /// managerWindowName as one JSON shape. Sole-writer rule: ALL persistence goes through
    /// this store's read-modify-write Save — the Core ManagerStore only ever READS this
    /// file, so no field is ever clobbered. Tolerant by contract: a missing or malformed
    /// file loads as the defaults and never throws; a field absent from the file (a legacy
    /// ManagerStore-shaped {"managerWindowName":...}) keeps its default. Save is
    /// best-effort: a failure is reported to the optional diagnostic sink and swallowed.
    /// </summary>
    public sealed class SettingsStore
    {
        /// <summary>The D-3 default hotkey string.</summary>
        public const string DefaultHotkey = "Ctrl+Alt+O";

        private readonly string path;
        private readonly Action<string> log;

        public SettingsStore(string path)
            : this(path, null)
        {
        }

        /// <param name="log">Optional diagnostic sink for degraded loads and failed saves (advisory: surface the drop, never throw).</param>
        public SettingsStore(string path, Action<string> log)
        {
            this.path = path;
            this.log = log;
        }

        /// <summary>%LOCALAPPDATA%\TerminalOrganizer (spec D-3).</summary>
        public static string DefaultDirectory()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TerminalOrganizer");
        }

        /// <summary>The D-3 settings file path.</summary>
        public static string DefaultPath()
        {
            return Path.Combine(DefaultDirectory(), "settings.json");
        }

        /// <summary>The D-3 default log path.</summary>
        public static string DefaultLogPath()
        {
            return Path.Combine(DefaultDirectory(), "terminal-organizer.log");
        }

        /// <summary>The D-3 defaults: hotkey "Ctrl+Alt+O", default log path, no manager.</summary>
        public static AppSettings Defaults()
        {
            return new AppSettings(DefaultHotkey, DefaultLogPath(), null);
        }

        /// <summary>Loads the settings; tolerant — never throws for file content.</summary>
        public AppSettings Load()
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return Defaults();
                }
                string json = File.ReadAllText(path);
                SettingsDto dto = new JavaScriptSerializer().Deserialize<SettingsDto>(json);
                if (dto == null)
                {
                    Degrade("settings file content was not a settings object; loading defaults");
                    return Defaults();
                }
                string hotkey = string.IsNullOrEmpty(dto.hotkey) ? DefaultHotkey : dto.hotkey;
                string logPath = string.IsNullOrEmpty(dto.logPath) ? DefaultLogPath() : dto.logPath;
                string manager = string.IsNullOrEmpty(dto.managerWindowName) ? null : dto.managerWindowName;
                // C3: unknown policy text loads as Ask; the cross-monitor mirror is kept
                // coherent with the loaded policy (true only while Redistribute).
                OverflowPolicy policy = OverflowPolicyParser.Parse(dto.overflowPolicy);
                return new AppSettings(hotkey, logPath, manager, dto.mergeEnabled,
                    dto.firstRunCompleted, dto.startWithWindows, ParseSelector(dto.managerSelector),
                    ParseOverrides(dto.priorityOverrides), dto.schemaVersion, policy,
                    policy == OverflowPolicy.Redistribute && dto.crossMonitorRedistribution);
            }
            catch (Exception ex)
            {
                // Tolerant load (REQ-SET-001): malformed content degrades to defaults, never throws.
                Degrade("settings load degraded to defaults: " + ex.Message);
                return Defaults();
            }
        }

        /// <summary>Saves all three fields as one JSON document; best-effort, never throws.</summary>
        public void Save(AppSettings settings)
        {
            try
            {
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }
                AppSettings values = settings == null ? Defaults() : settings;
                SettingsDto dto = new SettingsDto();
                dto.hotkey = values.Hotkey;
                dto.logPath = values.LogPath;
                dto.managerWindowName = values.ManagerWindowName;
                dto.mergeEnabled = values.MergeEnabled;
                dto.firstRunCompleted = values.FirstRunCompleted;
                dto.startWithWindows = values.StartWithWindows;
                ManagerSelector selector = values.ManagerSelector;
                dto.managerSelector = new ManagerSelectorDto();
                dto.managerSelector.kind = selector.Kind.ToString();
                dto.managerSelector.value = selector.Value;
                dto.managerSelector.rawTitleFallback = selector.RawTitleFallback;
                dto.priorityOverrides = SerializeOverrides(values.PriorityOverrides);
                dto.schemaVersion = values.SchemaVersion;
                dto.overflowPolicy = OverflowPolicyParser.Format(values.OverflowPolicy);
                dto.crossMonitorRedistribution = values.CrossMonitorRedistribution;
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(path, new JavaScriptSerializer().Serialize(dto));
            }
            catch (Exception ex)
            {
                // Best-effort save: surface the failure on the diagnostic sink, never throw.
                Degrade("settings save failed: " + ex.Message);
            }
        }

        private void Degrade(string message)
        {
            Action<string> sink = log;
            if (sink != null)
            {
                sink(message);
            }
        }

        /// <summary>
        /// Maps the persisted B2 selector object; null when the file carries none (the
        /// legacy managerWindowName field then applies through the AppSettings
        /// constructor). Tolerant by contract: an unknown kind or a valueless selector
        /// degrades to the raw-title fallback when one is present, never throws.
        /// </summary>
        private static ManagerSelector ParseSelector(ManagerSelectorDto dto)
        {
            if (dto == null)
            {
                return null;
            }
            ManagerSelectorKind kind;
            if (!Enum.TryParse<ManagerSelectorKind>(dto.kind, true, out kind))
            {
                return DegradeToRawTitle(dto.rawTitleFallback);
            }
            if (kind == ManagerSelectorKind.None)
            {
                return ManagerSelector.None();
            }
            if (kind != ManagerSelectorKind.RawTitle && string.IsNullOrEmpty(dto.value))
            {
                return DegradeToRawTitle(dto.rawTitleFallback);
            }
            return new ManagerSelector(kind, dto.value, dto.rawTitleFallback);
        }

        private static ManagerSelector DegradeToRawTitle(string rawTitleFallback)
        {
            return string.IsNullOrEmpty(rawTitleFallback)
                ? ManagerSelector.None()
                : new ManagerSelector(ManagerSelectorKind.RawTitle, rawTitleFallback, rawTitleFallback);
        }

        /// <summary>
        /// Maps the persisted C1 override rows, dropping every invalid one with a
        /// diagnostic: an unknown selector kind, a valueless selector, or a rank outside
        /// 0..999 (the manual rank bounds). Surviving rows return in canonical order.
        /// </summary>
        private PriorityOverride[] ParseOverrides(PriorityOverrideDto[] overrides)
        {
            List<PriorityOverride> rows = new List<PriorityOverride>();
            if (overrides != null)
            {
                foreach (PriorityOverrideDto dto in overrides)
                {
                    if (dto == null)
                    {
                        continue;
                    }
                    ManagerSelectorKind kind;
                    if (!Enum.TryParse<ManagerSelectorKind>(dto.kind, true, out kind)
                        || kind == ManagerSelectorKind.None)
                    {
                        Degrade("priority override ignored: unknown selector kind '" + dto.kind + "'");
                        continue;
                    }
                    if (dto.rank < PriorityResolver.ManualRankMin || dto.rank > PriorityResolver.ManualRankMax)
                    {
                        Degrade("priority override ignored: rank " + dto.rank + " outside 0..999");
                        continue;
                    }
                    ManagerSelector selector = new ManagerSelector(kind, dto.value, dto.rawTitleFallback);
                    if (selector.IsEmpty)
                    {
                        Degrade("priority override ignored: selector has no value");
                        continue;
                    }
                    rows.Add(new PriorityOverride(selector, dto.rank));
                }
            }
            return NormalizeOverrides(rows.ToArray());
        }

        /// <summary>The DTO rows for one save, in the canonical sorted order.</summary>
        private static PriorityOverrideDto[] SerializeOverrides(PriorityOverride[] overrides)
        {
            PriorityOverride[] sorted = NormalizeOverrides(overrides);
            PriorityOverrideDto[] dtos = new PriorityOverrideDto[sorted.Length];
            for (int i = 0; i < sorted.Length; i++)
            {
                dtos[i] = new PriorityOverrideDto();
                dtos[i].kind = sorted[i].Selector.Kind.ToString();
                dtos[i].value = sorted[i].Selector.Value;
                dtos[i].rawTitleFallback = sorted[i].Selector.RawTitleFallback;
                dtos[i].rank = sorted[i].Rank;
            }
            return dtos;
        }

        /// <summary>The in-memory canonical form: sorted, no null/empty rows, never null.</summary>
        public static PriorityOverride[] NormalizeOverrides(PriorityOverride[] overrides)
        {
            List<PriorityOverride> rows = new List<PriorityOverride>();
            if (overrides != null)
            {
                foreach (PriorityOverride row in overrides)
                {
                    if (row != null && row.Selector != null && !row.Selector.IsEmpty)
                    {
                        rows.Add(row);
                    }
                }
            }
            rows.Sort(CompareOverrides);
            return rows.ToArray();
        }

        /// <summary>The canonical override order: selector kind, then value ordinal, then rank.</summary>
        private static int CompareOverrides(PriorityOverride x, PriorityOverride y)
        {
            int byKind = x.Selector.Kind.CompareTo(y.Selector.Kind);
            if (byKind != 0)
            {
                return byKind;
            }
            int byValue = string.CompareOrdinal(x.Selector.Value, y.Selector.Value);
            if (byValue != 0)
            {
                return byValue;
            }
            return x.Rank.CompareTo(y.Rank);
        }

        /// <summary>The settings JSON shape; public fields so JavaScriptSerializer maps them.</summary>
        private sealed class SettingsDto
        {
            public string hotkey;
            public string logPath;
            public string managerWindowName;
            public bool mergeEnabled;
            public bool firstRunCompleted;
            public bool startWithWindows;
            public ManagerSelectorDto managerSelector;
            public PriorityOverrideDto[] priorityOverrides;
            public int schemaVersion;
            public string overflowPolicy;
            public bool crossMonitorRedistribution;
        }

        /// <summary>The persisted B2 canonical manager selector shape; public fields so JavaScriptSerializer maps them.</summary>
        private sealed class ManagerSelectorDto
        {
            public string kind;
            public string value;
            public string rawTitleFallback;
        }

        /// <summary>One persisted C1 priority override row; public fields so JavaScriptSerializer maps them.</summary>
        private sealed class PriorityOverrideDto
        {
            public string kind;
            public string value;
            public string rawTitleFallback;
            public int rank;
        }
    }
}
