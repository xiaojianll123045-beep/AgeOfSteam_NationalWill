using MissionLibrary.HotKey;
using MissionSharedLibrary.Config.HotKey;
using MissionSharedLibrary.HotKey;
using MissionSharedLibrary.Repository;
using System;
using System.Collections.Generic;
using TaleWorlds.InputSystem;

namespace RTSCamera.CommandSystem.Config.HotKey
{
    public enum GameKeyEnum
    {
        SelectFormation,
        KeepMovementOrder,
        FormationLockMovement,
        SelectTargetForCommand,
        CommandQueue,
        KeepFormationWidth,
        AutoVolley,
        ManualVolley,
        VolleyFire,
        NumberOfGameKeyEnums
    }
    public class CommandSystemGameKeyCategory
    {
        public const string CategoryId = "RTSCameraCommandSystemHotKey";

        public static AGameKeyCategory Category
        {
            get
            {
                try
                {
                    var mgr = AGameKeyCategoryManager.Get();
                    if (mgr == null) return null;
                    var cat = mgr.GetItem(CategoryId);
                    if (cat == null)
                    {
                        // 懒注册: 原版注册发生在共享库管理器就绪之前(被 ?. 静默跳过),
                        // 导致 GetKey(...) 返回 null, 后续 .IsKeyDownInOrder() 空引用崩溃。
                        RegisterGameKeyCategory();
                        cat = mgr.GetItem(CategoryId);
                    }
                    return cat;
                }
                catch { return null; }
            }
        }

        public static void RegisterGameKeyCategory()
        {
            AGameKeyCategoryManager.Get()?.RegisterGameKeyCategory(CreateCategory, CategoryId, new Version(1, 0));
        }
        public static GameKeyCategory CreateCategory()
        {
            var result = new GameKeyCategory(CategoryId,
                (int)GameKeyEnum.NumberOfGameKeyEnums, CommandSystemGameKeyConfig.Get());
            result.AddGameKeySequence(new GameKeySequence((int) GameKeyEnum.SelectFormation,
                nameof(GameKeyEnum.SelectFormation),
                CategoryId, new List<GameKeySequenceAlternative>()
                {
                    new GameKeySequenceAlternative
                    (
                        new List<InputKey> () {
                            InputKey.MiddleMouseButton
                        }
                    )
                }));
            result.AddGameKeySequence(new GameKeySequence((int)GameKeyEnum.KeepMovementOrder,
                nameof(GameKeyEnum.KeepMovementOrder),
                CategoryId, new List<GameKeySequenceAlternative>()
                {
                    new GameKeySequenceAlternative(
                        new List<InputKey>()
                        {
                            InputKey.LeftAlt
                        }),
                    new GameKeySequenceAlternative(
                        new List<InputKey>()
                        {
                            InputKey.RightAlt
                        })
                }));
            result.AddGameKeySequence(new GameKeySequence((int)GameKeyEnum.FormationLockMovement,
                nameof(GameKeyEnum.FormationLockMovement),
                CategoryId, new List<GameKeySequenceAlternative>()
                {
                    new GameKeySequenceAlternative(
                        new List<InputKey>()
                        {
                            InputKey.LeftAlt
                        }),
                    new GameKeySequenceAlternative(
                        new List<InputKey>()
                        {
                            InputKey.RightAlt
                        })
                }));
            result.AddGameKeySequence(new GameKeySequence((int)GameKeyEnum.SelectTargetForCommand,
                nameof(GameKeyEnum.SelectTargetForCommand),
                CategoryId, new List<GameKeySequenceAlternative>()
                {
                    new GameKeySequenceAlternative(
                        new List<InputKey>()
                        {
                            InputKey.LeftAlt
                        }),
                    new GameKeySequenceAlternative(
                        new List<InputKey>()
                        {
                            InputKey.RightAlt
                        })
                }));
            result.AddGameKeySequence(new GameKeySequence((int)GameKeyEnum.CommandQueue,
                nameof(GameKeyEnum.CommandQueue),
                CategoryId, new List<GameKeySequenceAlternative>()
                {
                    new GameKeySequenceAlternative(
                        new List<InputKey>()
                        {
                            InputKey.LeftShift
                        }),
                    new GameKeySequenceAlternative(
                        new List<InputKey>()
                        {
                            InputKey.RightShift
                        })
                }));
            result.AddGameKeySequence(new GameKeySequence((int)GameKeyEnum.KeepFormationWidth,
                nameof(GameKeyEnum.KeepFormationWidth),
                CategoryId, new List<GameKeySequenceAlternative>()
                {
                    new GameKeySequenceAlternative(
                        new List<InputKey>()
                        {
                            InputKey.LeftControl
                        }),
                    new GameKeySequenceAlternative(
                        new List<InputKey>()
                        {
                            InputKey.RightControl
                        })
                }));
            result.AddGameKeySequence(new GameKeySequence((int)GameKeyEnum.AutoVolley,
                nameof(GameKeyEnum.AutoVolley),
                CategoryId, new List<GameKeySequenceAlternative>()
                {
                    new GameKeySequenceAlternative(
                        new List<InputKey>()
                        {
                            InputKey.H
                        })
                }));
            result.AddGameKeySequence(new GameKeySequence((int)GameKeyEnum.ManualVolley,
                nameof(GameKeyEnum.ManualVolley),
                CategoryId, new List<GameKeySequenceAlternative>()
                {
                    new GameKeySequenceAlternative(
                        new List<InputKey>()
                        {
                            InputKey.J
                        })
                }));
            result.AddGameKeySequence(new GameKeySequence((int)GameKeyEnum.VolleyFire,
                nameof(GameKeyEnum.VolleyFire),
                CategoryId, new List<GameKeySequenceAlternative>()
                {
                    new GameKeySequenceAlternative(
                        new List<InputKey>()
                        {
                            InputKey.K
                        })
                }));
            return result;
        }

        public static IGameKeySequence GetKey(GameKeyEnum key)
        {
            return Category?.GetGameKeySequence((int)key);
        }
    }
}
