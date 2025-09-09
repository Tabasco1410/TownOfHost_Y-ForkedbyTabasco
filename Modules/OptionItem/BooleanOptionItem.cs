
//元のコード
using System;
using TownOfHostY.Roles.Core;

namespace TownOfHostY
{
    public class BooleanOptionItem : OptionItem
    {
        private StringOptionItem innerOption;

        // コンストラクタ
        public BooleanOptionItem(int id, string name, bool defaultValue, TabGroup tab, bool isSingleValue)
            : base(id, name, defaultValue ? 1 : 0, tab, isSingleValue)
        {
            innerOption = new StringOptionItem(id, name, defaultValue ? 1 : 0, tab, isSingleValue, new string[] { "OFF", "ON" });
        }

        // Create メソッド
        public static BooleanOptionItem Create(int id, string name, bool defaultValue, TabGroup tab, bool isSingleValue)
        {
            return new BooleanOptionItem(id, name, defaultValue, tab, isSingleValue);
        }

        public static BooleanOptionItem Create(SimpleRoleInfo roleInfo, int idOffset, Enum name, bool defaultValue, bool isSingleValue, OptionItem parent = null)
        {
            var opt = new BooleanOptionItem(roleInfo.ConfigId + idOffset, name.ToString(), defaultValue, roleInfo.Tab, isSingleValue);
            opt.innerOption.SetParent(parent ?? roleInfo.RoleOption);
            return opt;
        }

        // Getter
        public override string GetString() => innerOption.GetString();
        public override int GetValue() => innerOption.GetValue();

        // Setter
        public override void SetValue(int value, bool doSync = true)
        {
            innerOption.SetValue(value, doSync);
            base.SetValue(innerOption.GetValue(), doSync);
        }

        public BooleanOptionItem SetParent(OptionItem parent)
        {
            innerOption.SetParent(parent);
            base.SetParent(parent);
            return this; 
        }
    }

}

