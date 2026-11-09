using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Verse;

namespace BiotechPatch.MechsInColonistBar
{
    [StaticConstructorOnStartup]
    public static class CompatibilityPatch_ColoredMoodBar
    {
        // Colored Mood Bar is looked up by name because its assembly is CMColoredMoodBar on 1.6 and ColoredMoodBar17 on 1.7
        public static Type MoodCacheType;
        public static FieldInfo BreakThresholdExtremeField;
        public static FieldInfo MoodLevelField;
        public static FieldInfo MoodTextureField;
        public static FieldInfo MoodColorField;
        public static FieldInfo SettingsField;
        public static FieldInfo NeutralTexField;
        public static FieldInfo MinorBreakTexField;
        public static FieldInfo MajorBreakTexField;
        public static FieldInfo ExtremeBreakTexField;
        public static FieldInfo NeutralColorField;
        public static FieldInfo MinorColorField;
        public static FieldInfo MajorColorField;
        public static FieldInfo ExtremeColorField;
        public static object MoodLevelNeutral;
        public static object MoodLevelMinor;
        public static object MoodLevelMajor;
        public static object MoodLevelExtreme;

        static CompatibilityPatch_ColoredMoodBar()
        {
            Type type = AccessTools.TypeByName("ColoredMoodBar13.MoodCache");
            if (type != null && Init(type))
            {
                var harmony = new Harmony(BiotechPatchMod.PACKAGE_ID);
                harmony.Patch(type.Method("DoMood"), transpiler: typeof(CompatibilityPatch_ColoredMoodBar_DoMood).Method(nameof(CompatibilityPatch_ColoredMoodBar_DoMood.Transpiler)));
            }
        }

        private static bool Init(Type moodCacheType)
        {
            Type mainType = moodCacheType.Assembly.GetType("ColoredMoodBar13.Main");
            Type settingsType = moodCacheType.Assembly.GetType("ColoredMoodBar13.MoodPatchSettings");
            Type moodLevelType = moodCacheType.Assembly.GetType("ColoredMoodBar13.MoodLevel");
            if (mainType == null || settingsType == null || moodLevelType == null)
            {
                Log.Warning("[1trickPwnyta's Biotech Patch] Colored Mood Bar types not found; mech charge level will not be colored.");
                return false;
            }

            MoodCacheType = moodCacheType;
            BreakThresholdExtremeField = AccessTools.Field(moodCacheType, "BreakThresholdExtreme");
            MoodLevelField = AccessTools.Field(moodCacheType, "MoodLevel");
            MoodTextureField = AccessTools.Field(moodCacheType, "MoodTexture");
            MoodColorField = AccessTools.Field(moodCacheType, "MoodColor");
            SettingsField = AccessTools.Field(mainType, "Settings");
            NeutralTexField = AccessTools.Field(mainType, "neutralTex");
            MinorBreakTexField = AccessTools.Field(mainType, "minorBreakTex");
            MajorBreakTexField = AccessTools.Field(mainType, "majorBreakTex");
            ExtremeBreakTexField = AccessTools.Field(mainType, "extremeBreakTex");
            NeutralColorField = AccessTools.Field(settingsType, "Neutral");
            MinorColorField = AccessTools.Field(settingsType, "Minor");
            MajorColorField = AccessTools.Field(settingsType, "Major");
            ExtremeColorField = AccessTools.Field(settingsType, "Extreme");

            FieldInfo[] fields = { BreakThresholdExtremeField, MoodLevelField, MoodTextureField, MoodColorField, SettingsField, NeutralTexField, MinorBreakTexField, MajorBreakTexField, ExtremeBreakTexField, NeutralColorField, MinorColorField, MajorColorField, ExtremeColorField };
            string[] levels = { "Neutral", "Minor", "Major", "Extreme" };
            if (fields.Any(f => f == null) || levels.Any(l => !Enum.IsDefined(moodLevelType, l)))
            {
                Log.Warning("[1trickPwnyta's Biotech Patch] Colored Mood Bar members not found; mech charge level will not be colored.");
                return false;
            }

            MoodLevelNeutral = Enum.Parse(moodLevelType, "Neutral");
            MoodLevelMinor = Enum.Parse(moodLevelType, "Minor");
            MoodLevelMajor = Enum.Parse(moodLevelType, "Major");
            MoodLevelExtreme = Enum.Parse(moodLevelType, "Extreme");
            return true;
        }
    }

    public static class CompatibilityPatch_ColoredMoodBar_DoMood
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            List<CodeInstruction> instructionsList = instructions.ToList();

            Label trueLabel = il.DefineLabel();
            instructionsList.InsertRange(3, new[]
            {
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Call, typeof(Pawn).PropertyGetter(nameof(Pawn.IsColonyMech))),
                new CodeInstruction(OpCodes.Brtrue_S, trueLabel)
            });

            int index = instructionsList.FindIndex(i => i.Calls(typeof(Need).PropertyGetter(nameof(Need.CurLevel))));
            instructionsList[index - 6].labels.Add(trueLabel);
            instructionsList[index - 1] = new CodeInstruction(OpCodes.Call, typeof(CompatibilityPatch_ColoredMoodBar_DoMood).Method(nameof(GetMoodNeed)));

            index = instructionsList.FindIndex(i => i.Calls(typeof(Need).PropertyGetter(nameof(Need.CurLevelPercentage))));
            instructionsList[index - 1] = new CodeInstruction(OpCodes.Call, typeof(CompatibilityPatch_ColoredMoodBar_DoMood).Method(nameof(GetMoodNeed)));

            Label nonMechLabel = il.DefineLabel();
            index = instructionsList.FindLastIndex(i => i.LoadsField(CompatibilityPatch_ColoredMoodBar.BreakThresholdExtremeField));
            instructionsList[index].labels.Add(nonMechLabel);
            instructionsList.InsertRange(index, new[]
            {
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, typeof(CompatibilityPatch_ColoredMoodBar_DoMood).Method(nameof(SetParametersForEnergy))),
                new CodeInstruction(OpCodes.Brfalse_S, nonMechLabel),
                new CodeInstruction(OpCodes.Pop),
                new CodeInstruction(OpCodes.Pop),
                new CodeInstruction(OpCodes.Ret)
            });

            return instructionsList;
        }

        private static Need GetMoodNeed(Pawn_NeedsTracker needs) => needs.mood as Need ?? needs.energy;

        private static bool SetParametersForEnergy(Pawn pawn, object cache)
        {
            if (pawn.IsColonyMech)
            {
                object level;
                FieldInfo texField;
                FieldInfo colorField;
                MechanitorControlGroup group = pawn.GetMechControlGroup();
                if (group == null || pawn.needs.energy.CurLevelPercentage > group.mechRechargeThresholds.min)
                {
                    level = CompatibilityPatch_ColoredMoodBar.MoodLevelNeutral;
                    texField = CompatibilityPatch_ColoredMoodBar.NeutralTexField;
                    colorField = CompatibilityPatch_ColoredMoodBar.NeutralColorField;
                }
                else if (pawn.needs.energy.CurLevelPercentage > group.mechRechargeThresholds.min / 2)
                {
                    level = CompatibilityPatch_ColoredMoodBar.MoodLevelMinor;
                    texField = CompatibilityPatch_ColoredMoodBar.MinorBreakTexField;
                    colorField = CompatibilityPatch_ColoredMoodBar.MinorColorField;
                }
                else if (pawn.needs.energy.CurLevelPercentage > 0f)
                {
                    level = CompatibilityPatch_ColoredMoodBar.MoodLevelMajor;
                    texField = CompatibilityPatch_ColoredMoodBar.MajorBreakTexField;
                    colorField = CompatibilityPatch_ColoredMoodBar.MajorColorField;
                }
                else
                {
                    level = CompatibilityPatch_ColoredMoodBar.MoodLevelExtreme;
                    texField = CompatibilityPatch_ColoredMoodBar.ExtremeBreakTexField;
                    colorField = CompatibilityPatch_ColoredMoodBar.ExtremeColorField;
                }
                CompatibilityPatch_ColoredMoodBar.MoodLevelField.SetValue(cache, level);
                CompatibilityPatch_ColoredMoodBar.MoodTextureField.SetValue(cache, texField.GetValue(null));
                CompatibilityPatch_ColoredMoodBar.MoodColorField.SetValue(cache, colorField.GetValue(CompatibilityPatch_ColoredMoodBar.SettingsField.GetValue(null)));
                return true;
            }
            return false;
        }
    }
}
