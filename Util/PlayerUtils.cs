using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CUCoreLib.Util;

public enum SkillType
{
    Strength = 0,
    Resilience = 1,
    Intelligence = 2
}

public enum LimbSlot
{
    Head = 0,
    Thorax = 1,
    Pelvis = 2
}

public static class PlayerUtils
{
    public static bool TryGetBody(out Body body)
    {
        body = null;
        if (PlayerCamera.main == null) return false;
        body = PlayerCamera.main.body;
        return body != null;
    }

    public static Body GetBody()
    {
        TryGetBody(out var body);
        return body;
    }

    public static bool TryGetCamera(out PlayerCamera camera)
    {
        camera = PlayerCamera.main;
        return camera;
    }

    public static PlayerCamera GetCamera()
    {
        TryGetCamera(out var camera);
        return camera;
    }

    public static bool TryGetItemInSlot(int slot, out Item item)
    {
        item = null;
        if (!GetBody()) return false;

        item = GetBody().GetItem(slot);
        return item != null;
    }

    public static bool HasItemInSlot(int slot)
    {
        return TryGetItemInSlot(slot, out _);
    }

    public static Vector2 GetMousePosition()
    {
        if (PlayerCamera.main != null)
            return PlayerCamera.main.body != null
                ? PlayerCamera.main.body.targetLookPos
                : (Vector2)Input.mousePosition;

        return Input.mousePosition;
    }

    public static void Alert(string text, bool important = false, float delay = 0f)
    {
        if (PlayerCamera.main == null || string.IsNullOrEmpty(text)) return;

        if (delay <= 0f)
            PlayerCamera.main.DoAlert(text, important);
        else
            CoroutineUtils.StartCoroutine(AlertDelayedRoutine(text, important, delay));
    }

    private static IEnumerator AlertDelayedRoutine(string text, bool important, float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        if (PlayerCamera.main != null)
            PlayerCamera.main.DoAlert(text, important);
    }

    public static void GiveItem(string id, int count)
    {
        if (!CheckUtils.IsInWorld() || string.IsNullOrWhiteSpace(id) || count <= 0) return;

        var body = PlayerCamera.main.body;
        if (body == null) return;

        var normalizedId = id.Trim();
        for (var i = 0; i < count; i++)
        {
            var spawned = Utils.Create(normalizedId, body.transform.position, 0f);
            var spawnedItem = spawned != null ? spawned.GetComponent<Item>() : null;
            if (spawnedItem == null)
            {
                if (spawned != null) Object.Destroy(spawned);

                return;
            }

            body.AutoPickUpItem(spawnedItem);
        }
    }

    public static void GiveItemInSlot(string id, int slot, int count)
    {
        if (!CheckUtils.IsInWorld() || string.IsNullOrWhiteSpace(id) || count <= 0) return;

        var body = PlayerCamera.main.body;
        if (body == null) return;

        var normalizedId = id.Trim();
        for (var i = 0; i < count; i++)
        {
            var spawned = Utils.Create(normalizedId, body.transform.position, 0f);
            var spawnedItem = spawned != null ? spawned.GetComponent<Item>() : null;
            if (spawnedItem == null)
            {
                if (spawned != null) Object.Destroy(spawned);

                return;
            }

            body.PickUpItem(spawnedItem, slot);
        }
    }

    public static void DoAmputate(Item item, Limb limb)
    {
        if (item == null || limb == null) return;

        Item.DoAmputate(item, limb);
    }

    public static AudioSource PlaySoundAt(AudioClip clip, Vector2? pos = null)
    {
        if (clip == null) return null;

        var playPos = pos ?? (PlayerCamera.main != null && PlayerCamera.main.body != null
            ? PlayerCamera.main.body.transform.position
            : Vector2.zero);

        return Sound.Play(clip, playPos);
    }

    public static Limb GetLimb(int index)
    {
        if (GetBody()?.limbs == null || index < 0 || index >= GetBody().limbs.Length) return null;
        return GetBody().limbs[index];
    }

    public static Limb GetLimb(LimbSlot slot)
    {
        return GetLimb((int)slot);
    }

    public static Limb GetLimbByName(string name)
    {
        return GetBody()?.LimbByName(name);
    }

    public static List<Limb> GetAllLimbs()
    {
        return GetBody()?.limbs != null ? [..GetBody().limbs] : [];
    }

    public static bool HasBrokenBone()
    {
        if (GetBody()?.limbs == null) return false;
        return GetBody().limbs.Any(limb => limb is { dismembered: false, broken: true });
    }

    public static bool HasDislocation()
    {
        return GetBody()?.limbs != null
               && GetBody().limbs.Any(limb => limb is { dismembered: false, dislocated: true });
    }

    public static bool HasInfection()
    {
        if (GetBody()?.limbs == null) return false;
        foreach (var limb in GetBody().limbs)
            if (limb is { dismembered: false, infected: true })
                return true;
        return false;
    }

    public static bool HasDismemberment()
    {
        return GetBody()?.limbs != null
               && GetBody().limbs.Any(limb => limb is { dismembered: true });
    }

    public static float GetMaxInfection()
    {
        if (GetBody()?.limbs == null) return 0f;
        var max = 0f;
        foreach (var limb in GetBody().limbs)
            if (limb is { dismembered: false } && limb.infectionAmount > max)
                max = limb.infectionAmount;
        return max;
    }

    public static float GetAveragePain()
    {
        return GetBody()?.averagePain ?? 0f;
    }

    public static float GetTotalBleedSpeed()
    {
        return GetBody()?.totalBleedSpeed ?? 0f;
    }

    public static void HealLimb(Limb limb)
    {
        if (limb == null || limb.dismembered) return;
        limb.skinHealth = limb.muscleHealth = 100f;
        limb.bleedAmount = limb.pain = limb.infectionAmount = 0f;
        limb.infected = false;
        limb.shrapnel = 0;
        if (limb.broken) limb.MendBone();
        if (limb.dislocated) limb.UnDislocate();
    }

    public static void HealLimb(int index)
    {
        HealLimb(GetLimb(index));
    }

    public static void DamageSkin(Limb limb, float value)
    {
        if (limb != null) limb.skinHealth = Mathf.Clamp(limb.skinHealth - value, 0f, 100f);
    }

    public static void DamageMuscle(Limb limb, float value)
    {
        if (limb != null) limb.muscleHealth = Mathf.Clamp(limb.muscleHealth - value, 0f, 100f);
    }

    public static void SetSkinHealthRaw(Limb limb, float value)
    {
        if (limb != null) limb.skinHealth = Mathf.Clamp(value, 0f, 100f);
    }

    public static void SetMuscleHealthRaw(Limb limb, float value)
    {
        if (limb != null) limb.muscleHealth = Mathf.Clamp(value, 0f, 100f);
    }

    public static void SetBleedRaw(Limb limb, float value)
    {
        if (limb != null) limb.bleedAmount = Mathf.Clamp(value, 0f, 100f);
    }

    public static void SetPainRaw(Limb limb, float value)
    {
        if (limb != null) limb.pain = Mathf.Clamp(value, 0f, 100f);
    }

    public static void SetInfectionRaw(Limb limb, float value)
    {
        if (limb == null) return;
        limb.infectionAmount = Mathf.Clamp(value, 0f, 100f);
        limb.infected = value > 0f;
    }
    
    public static float XpMultiplier
    {
        get => Skills.xpGainMult;
        set
        {
            if (WorldGeneration.runSettings != null) WorldGeneration.runSettings["xpgain"] = Mathf.Max(0f, value);
        }
    }

    private static Skills GetSkills()
    {
        return GetBody().skills;
    }

    public static int GetLevel(SkillType skillType)
    {
        return GetSkills() is { } skill
            ? skillType switch { SkillType.Strength => skill.STR, SkillType.Resilience => skill.RES, _ => skill.INT }
            : 0;
    }

    public static float GetExperience(SkillType skillType)
    {
        return GetSkills() is { } skill
            ? skillType switch
            {
                SkillType.Strength => skill.expSTR, SkillType.Resilience => skill.expRES, _ => skill.expINT
            }
            : 0f;
    }

    public static float GetProgress(SkillType skillType)
    {
        return GetSkills() is { } skill
            ? skillType switch
            {
                SkillType.Strength => skill.ToNextNormalized(skill.expSTR, skill.minSTR, skill.maxSTR),
                SkillType.Resilience => skill.ToNextNormalized(skill.expRES, skill.minRES, skill.maxRES),
                _ => skill.ToNextNormalized(skill.expINT, skill.minINT, skill.maxINT)
            }
            : 0f;
    }

    public static float GetExperienceInLevel(SkillType skillType)
    {
        return GetSkills() is { } skill
            ? skillType switch
            {
                SkillType.Strength => skill.expSTR - skill.minSTR, SkillType.Resilience => skill.expRES - skill.minRES,
                _ => skill.expINT - skill.minINT
            }
            : 0f;
    }

    public static float GetExperienceForNextLevel(SkillType skillType)
    {
        return GetSkills() is { } skill
            ? skillType switch
            {
                SkillType.Strength => skill.maxSTR - skill.minSTR, SkillType.Resilience => skill.maxRES - skill.minRES,
                _ => skill.maxINT - skill.minINT
            }
            : 0f;
    }

    public static int GetExperienceForLevel(int targetLevel)
    {
        return Skills.GetExperienceForLevel(targetLevel);
    }

    public static void AddExperience(SkillType skillType, float xp)
    {
        GetSkills()?.AddExp((int)skillType, xp);
    }

    public static void SetLevelRaw(SkillType skillType, int level)
    {
        if (GetSkills() is not { } skill) return;
        level = Mathf.Max(0, level);
        switch (skillType)
        {
            case SkillType.Strength: skill.STR = level; break;
            case SkillType.Resilience: skill.RES = level; break;
            case SkillType.Intelligence:
            default: skill.INT = level; break;
        }

        skill.UpdateExpBoundaries();
        switch (skillType)
        {
            case SkillType.Strength: skill.expSTR = skill.minSTR; break;
            case SkillType.Resilience: skill.expRES = skill.minRES; break;
            case SkillType.Intelligence:
            default: skill.expINT = skill.minINT; break;
        }
    }
}