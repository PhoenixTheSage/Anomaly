using System;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RichHudFramework.UI.Client;
using VRage.Utils;

namespace ClientPlugin.RichHud;

/// <summary>
/// Master only exposes <c>Add(subcategory)</c> on the Root. Nested
/// <see cref="TerminalPageCategory"/> trees are the same TreeBox entries;
/// Anomaly attaches them to the parent category and recurses
/// <c>SelectedPage</c> so the right-hand panel still resolves.
/// </summary>
static class TerminalCategoryNest
{
    const string MasterCategoryType = "RichHudFramework.UI.Server.TerminalPageCategoryBase";
    static readonly ConditionalWeakTable<object, object> Owned = new();
    static readonly Dictionary<Type, FieldInfo> Trees = new();
    static readonly Dictionary<Type, PropertyInfo> Values = new(), Pages = new();
    static readonly object CacheGate = new();
    static bool patched;
    static Harmony harmony;

    public static void EnsurePatched()
    {
        if (patched)
            return;

        var getter = FindSelectedPageGetter();
        if (getter == null)
            return;

        harmony = new Harmony("Anomaly.TerminalCategoryNest");
        harmony.Patch(getter, postfix: new HarmonyMethod(typeof(TerminalCategoryNest), nameof(SelectedPagePostfix)));
        patched = true;
        MyLog.Default.WriteLine("Anomaly: Rich HUD nested folder SelectedPage recurse is live");
    }

    public static bool TryAttach(TerminalPageCategory parent, TerminalPageCategory child)
    {
        if (parent == null || child == null)
            return false;
        var attached = TryAddToMasterTree(parent.ID, child.ID);
        if (attached)
        {
            Owned.GetValue(parent.ID, _ => new object());
            Owned.GetValue(child.ID, _ => new object());
        }
        return attached;
    }

    static bool TryAddToMasterTree(object parentMaster, object childMaster)
    {
        if (parentMaster == null || childMaster == null)
            return false;

        var treeBox = ReadTreeBox(parentMaster);
        if (treeBox == null)
            return false;

        var add = FindContainerAdd(treeBox.GetType(), childMaster.GetType());
        if (add == null)
            return false;

        try
        {
            add.Invoke(treeBox, new[] { childMaster });
            return true;
        }
        catch (Exception e)
        {
            MyLog.Default.Warning("Anomaly: nest folder attach failed: " + e.Message);
            return false;
        }
    }

    static void SelectedPagePostfix(object __instance, ref object __result)
    {
        if (__result != null || __instance == null || !Owned.TryGetValue(__instance, out _))
            return;

        var treeBox = ReadTreeBox(__instance);
        if (treeBox == null)
            return;

        object selected;
        try
        {
            selected = CachedProperty(Values, treeBox.GetType(), "Value")
                ?.GetValue(treeBox);
        }
        catch
        {
            return;
        }

        if (selected == null || ReferenceEquals(selected, __instance))
            return;
        if (!Owned.TryGetValue(selected, out _) || !IsMasterCategory(selected.GetType()))
            return;

        try
        {
            __result = CachedProperty(Pages, selected.GetType(), "SelectedPage")
                ?.GetValue(selected);
        }
        catch
        {
            // Leave the original null selection.
        }
    }

    static MethodInfo FindSelectedPageGetter()
    {
        var type = FindMasterCategoryType();
        return type?.GetProperty("SelectedPage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            ?.GetGetMethod();
    }

    static Type FindMasterCategoryType()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type;
            try
            {
                type = assembly.GetType(MasterCategoryType, false, false);
            }
            catch
            {
                continue;
            }

            if (type != null)
                return type;
        }

        return null;
    }

    static bool IsMasterCategory(Type type)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            if (t.FullName == MasterCategoryType)
                return true;
        }

        return false;
    }

    static object ReadTreeBox(object masterCategory)
    {
        var type = masterCategory.GetType();
        FieldInfo field;
        lock (CacheGate)
        {
            if (!Trees.TryGetValue(type, out field))
            {
                for (var t = type; t != null && field == null; t = t.BaseType)
                    field = t.GetField("treeBox", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                Trees[type] = field;
            }
        }
        return field?.GetValue(masterCategory);
    }

    static PropertyInfo CachedProperty(Dictionary<Type, PropertyInfo> cache, Type type, string name)
    {
        lock (CacheGate)
        {
            if (!cache.TryGetValue(type, out var property))
                cache[type] = property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            return property;
        }
    }

    static MethodInfo FindContainerAdd(Type treeBoxType, Type childType)
    {
        foreach (var method in treeBoxType.GetMethods(BindingFlags.Instance | BindingFlags.Public))
        {
            if (method.Name != "Add" || method.IsGenericMethod)
                continue;
            var args = method.GetParameters();
            if (args.Length != 1)
                continue;
            if (args[0].ParameterType.IsAssignableFrom(childType))
                return method;
        }

        return null;
    }
}
