using AmongUs.GameOptions;

namespace TownOfHostY.Modules
{
    public class NormalGameOptionsSender : GameOptionsSender
    {
        public override IGameOptions BasedGameOptions =>
            GameOptionsManager.Instance.CurrentGameOptions;
        public override bool IsDirty
        {
            get
            {
                if (_logicOptions == null || !GameManager.Instance.LogicComponents.Contains(_logicOptions))
                {
                    _logicOptions = null;
                    foreach (var glc in GameManager.Instance.LogicComponents)
                    {
                        if (glc.TryCast<LogicOptions>(out var lo))
                        {
                            _logicOptions = lo;
                            break;
                        }
                    }
                }
                return _logicOptions?.IsDirty ?? false; // nullならfalse
            }
            protected set
            {
                _logicOptions?.ClearDirtyFlag(); // nullなら何もしない
            }
        }

        private LogicOptions _logicOptions;

        public override IGameOptions BuildGameOptions()
            => BasedGameOptions;
    }
}