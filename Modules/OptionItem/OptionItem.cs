using System;
using System.Collections.Generic;
using System.Linq;
using TownOfHostY.Modules;
using UnityEngine;

namespace TownOfHostY
{
    public abstract class OptionItem
    {
        #region static
        public static IReadOnlyList<OptionItem> AllOptions => _allOptions;
        private static readonly List<OptionItem> _allOptions = new(1024);
        public static IReadOnlyDictionary<int, OptionItem> FastOptions => _fastOptions;
        private static readonly Dictionary<int, OptionItem> _fastOptions = new(1024);
        public static int CurrentPreset { get; set; }
        public static bool IdDuplicated { get; private set; } = false;
        #endregion

        // 必須情報
        public int Id { get; }
        public string Name { get; }
        public int DefaultValue { get; }
        public TabGroup Tab { get; }
        public bool IsSingleValue { get; }

        // 任意情報
        public Color NameColor { get; protected set; }
        public OptionFormat ValueFormat { get; protected set; }
        public CustomGameMode GameMode { get; protected set; }
        public bool IsHeader { get; protected set; }
        public string IsHeaderName { get; protected set; }
        public bool IsHidden { get; protected set; }
        public bool IsFixValue { get; protected set; }
        public bool IsText { get; protected set; }

        public Dictionary<string, string> ReplacementDictionary
        {
            get => _replacementDictionary;
            set
            {
                if (value == null) _replacementDictionary?.Clear();
                else _replacementDictionary = value;
            }
        }
        private Dictionary<string, string> _replacementDictionary;

        // 設定値
        public int[] AllValues { get; private set; } = new int[NumPresets];
        public int CurrentValue
        {
            get => GetValue();
            set => SetValue(value);
        }
        public int SingleValue { get; private set; }

        // 親子情報
        public OptionItem Parent { get; private set; }
        public List<OptionItem> Children;

        public OptionBehaviour OptionBehaviour;

        // イベント
        public event EventHandler<UpdateValueEventArgs> UpdateValueEvent;

        // コンストラクタ
        public OptionItem(int id, string name, int defaultValue, TabGroup tab, bool isSingleValue)
        {
            Id = id;
            Name = name;
            DefaultValue = defaultValue;
            Tab = tab;
            IsSingleValue = isSingleValue;

            // 任意情報の初期値
            NameColor = Color.white;
            ValueFormat = OptionFormat.None;
            GameMode = CustomGameMode.Standard;
            IsHeader = false;
            IsHeaderName = "";
            IsHidden = false;
            IsFixValue = false;
            IsText = false;

            Children = new();

            // デフォルト値
            if (Id == PresetId)
            {
                SingleValue = DefaultValue;
                CurrentPreset = SingleValue;
            }
            else if (IsSingleValue)
            {
                SingleValue = DefaultValue;
            }
            else
            {
                for (int i = 0; i < NumPresets; i++)
                    AllValues[i] = DefaultValue;
            }

            // ID 登録
            if (_fastOptions.TryAdd(id, this))
            {
                _allOptions.Add(this);
            }
            else
            {
#if DEBUG
                IdDuplicated = true;
#endif
                var existing = _fastOptions[id];
                Logger.Error(
                    $"ID:{id} が重複しています " +
                    $"(既存: Name={existing.Name}, Tab={existing.Tab}, Default={existing.DefaultValue}; " +
                    $"新規: Name={Name}, Tab={Tab}, Default={DefaultValue})",
                    "OptionItem"
                );
            }
        }

        // Setter chain
        public OptionItem Do(Action<OptionItem> action)
        {
            action(this);
            return this;
        }

        public OptionItem SetColor(Color value) => Do(i => i.NameColor = value);
        public OptionItem SetValueFormat(OptionFormat value) => Do(i => i.ValueFormat = value);
        public OptionItem SetGameMode(CustomGameMode value) => Do(i => i.GameMode = value);
        public OptionItem SetHeader(bool value, string str = "") => Do(i => { i.IsHeader = value; if (str != "") i.IsHeaderName = str; });
        public OptionItem SetHidden(bool value) => Do(i => i.IsHidden = value);
        public OptionItem SetFixValue(bool value) => Do(i => i.IsFixValue = value);
        public OptionItem SetText(bool value) => Do(i => i.IsText = value);
        public OptionItem SetReplacementDictionary(Dictionary<string, string> value) => Do(i => i.ReplacementDictionary = value);

        public OptionItem SetParent(OptionItem parent) => Do(i =>
        {
            i.Parent = parent;
            parent.SetChild(i);
        });
        public OptionItem SetChild(OptionItem child) => Do(i => i.Children.Add(child));
        public OptionItem RegisterUpdateValueEvent(EventHandler<UpdateValueEventArgs> handler)
            => Do(i => UpdateValueEvent += handler);

        // 置き換え辞書
        public OptionItem AddReplacement((string key, string value) kvp)
            => Do(i =>
            {
                ReplacementDictionary ??= new();
                ReplacementDictionary.Add(kvp.key, kvp.value);
            });
        public OptionItem RemoveReplacement(string key)
            => Do(i => ReplacementDictionary?.Remove(key));

        // Getter
        public virtual string GetName(bool disableColor = false, bool colorLighter = false)
        {
            return disableColor ?
                Translator.GetString(Name, ReplacementDictionary) :
                Utils.ColorString(colorLighter ? NameColor.ShadeColor(-0.3f) : NameColor, Translator.GetString(Name, ReplacementDictionary));
        }
        public virtual bool GetBool() => CurrentValue != 0 && (Parent == null || Parent.GetBool());
        public virtual int GetInt() => CurrentValue;
        public virtual float GetFloat() => CurrentValue;
        public virtual string GetString() => ApplyFormat(CurrentValue.ToString());
        public virtual int GetValue() => IsSingleValue ? SingleValue : AllValues[CurrentPreset];

        // Hidden判定
        public virtual bool IsHiddenOn(CustomGameMode mode)
        {
            return IsHidden || (GameMode != CustomGameMode.All && GameMode != mode);
        }

        public string ApplyFormat(string value)
        {
            if (ValueFormat == OptionFormat.None) return value;
            return string.Format(Translator.GetString("Format." + ValueFormat), value);
        }

        // 外部からの操作
        public virtual void Refresh()
        {
            if (OptionBehaviour is not null and StringOption opt)
            {
                opt.TitleText.text = GetName();
                opt.ValueText.text = GetString();
                opt.oldValue = opt.Value = CurrentValue;
            }
        }
        public virtual void SetValue(int afterValue, bool doSave, bool doSync = true)
        {
            int beforeValue = CurrentValue;
            if (IsSingleValue)
                SingleValue = afterValue;
            else
                AllValues[CurrentPreset] = afterValue;

            CallUpdateValueEvent(beforeValue, afterValue);
            Refresh();
            if (doSync)
                SyncAllOptions();
            if (doSave)
                OptionSaver.Save();
        }
        public virtual void SetValue(int afterValue, bool doSync = true) => SetValue(afterValue, true, doSync);

        public void SetAllValues(int[] values) => AllValues = values; // プリセット読み込み専用

        // 演算子オーバーロード
        public static OptionItem operator ++(OptionItem item)
            => item.Do(i => i.SetValue(i.CurrentValue + 1));
        public static OptionItem operator --(OptionItem item)
            => item.Do(i => i.SetValue(i.CurrentValue - 1));

        // 全体操作
        public static void SwitchPreset(int newPreset)
        {
            CurrentPreset = Math.Clamp(newPreset, 0, NumPresets - 1);
            foreach (var op in AllOptions)
                op.Refresh();
            SyncAllOptions();
        }
        public static void SyncAllOptions()
        {
            if (
                Main.AllPlayerControls.Count() <= 1 ||
                AmongUsClient.Instance.AmHost == false ||
                PlayerControl.LocalPlayer == null
            ) return;

            RPC.SyncCustomSettingsRPC();
        }

        // EventArgs
        private void CallUpdateValueEvent(int beforeValue, int currentValue)
        {
            if (UpdateValueEvent == null) return;
            try
            {
                UpdateValueEvent(this, new UpdateValueEventArgs(beforeValue, currentValue));
            }
            catch (Exception ex)
            {
                Logger.Error($"[{Name}] UpdateValueEventの呼び出し時に例外が発生しました", "OptionItem.UpdateValueEvent");
                Logger.Exception(ex, "OptionItem.UpdateValueEvent");
            }
        }

        public class UpdateValueEventArgs : EventArgs
        {
            public int CurrentValue { get; set; }
            public int BeforeValue { get; set; }
            public UpdateValueEventArgs(int beforeValue, int currentValue)
            {
                CurrentValue = currentValue;
                BeforeValue = beforeValue;
            }
        }

        public const int NumPresets = 5;
        public const int PresetId = 0;
    }

    public enum TabGroup
    {
        ModMainSettings,
        ImpostorRoles,
        MadmateRoles,
        CrewmateRoles,
        NeutralRoles,
        UnitRoles,
        Addons,
    }

    public enum OptionFormat
    {
        None,
        Players,
        Seconds,
        Percent,
        Times,
        Multiplier,
        Votes,
        Pieces,
        Pair,
        Turns,
    }
}
