    using System;
    using TownOfHostY.Roles.Core;

    namespace TownOfHostY
    {
        public class BooleanOptionItem : OptionItem
        {

            // 必須情報
            public IntegerValueRule Rule;
            public string[] Selections = new[] { "OFF", "ON" };

            // コンストラクタ
            public BooleanOptionItem(int id, string name, bool defaultValue, TabGroup tab, bool isSingleValue)
                : base(id, name, defaultValue ? 1 : 0, tab, isSingleValue)
            {
                Rule = (0, 1, 1); // OFF(0) / ON(1)
            }

            // Create メソッド (ID + 名前)
            public static BooleanOptionItem Create(int id, string name, bool defaultValue, TabGroup tab, bool isSingleValue)
            {
                return new BooleanOptionItem(id, name, defaultValue, tab, isSingleValue);
            }

            // Create メソッド (Enum 名前)
            public static BooleanOptionItem Create(int id, Enum name, bool defaultValue, TabGroup tab, bool isSingleValue)
            {
                return new BooleanOptionItem(id, name.ToString(), defaultValue, tab, isSingleValue);
            }

            // Create メソッド (RoleInfo)
            public static BooleanOptionItem Create(SimpleRoleInfo roleInfo, int idOffset, Enum name, bool defaultValue, bool isSingleValue, OptionItem parent = null)
            {
                var opt = new BooleanOptionItem(
                    roleInfo.ConfigId + idOffset, name.ToString(), defaultValue, roleInfo.Tab, isSingleValue
                );
                opt.SetParent(parent ?? roleInfo.RoleOption);
                return opt;
            }

            // --- Getter ---
            public override int GetInt() => Rule.GetValueByIndex(CurrentValue);
            public override float GetFloat() => Rule.GetValueByIndex(CurrentValue);

            public override string GetString()
            {
                int index = Rule.GetValueByIndex(CurrentValue);
                if (index < 0 || index >= Selections.Length) index = 0;
                return Translator.GetString(Selections[index]);
            }

            public bool Bool => GetValue() == 1;

            public override int GetValue()
            {
                return Rule.RepeatIndex(base.GetValue());
            }

            // --- Setter ---
            public override void SetValue(int value, bool doSync = true)
            {
                base.SetValue(Rule.RepeatIndex(value), doSync);
            }

            // --- ユーティリティ ---
            public BooleanOptionItem Toggle(bool doSync = true)
            {
                SetValue(1 - GetValue(), doSync);
                return this;
            }
        }
    }
