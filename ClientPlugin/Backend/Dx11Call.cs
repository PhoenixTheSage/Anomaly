using System;
using System.Collections.Generic;
using System.Reflection;

namespace ClientPlugin.Backend;

static class Dx11Call
{
    static Assembly dx11;

    public static bool Ready => dx11 != null;
    internal static object BindingIdentity => dx11;
    internal static bool Supports(string typeName,string method,int argumentCount)
    {
        var type=dx11?.GetType(typeName);if(type==null)return false;
        foreach(var candidate in type.GetMethods(BindingFlags.Public|BindingFlags.Static))
            if(candidate.Name==method && candidate.GetParameters().Length==argumentCount)return true;
        return false;
    }

    public static void Bind(Assembly assembly) { dx11 = assembly; lock (Named) Named.Clear(); }
    // Per (type, name): the public static methods with that name. Overloads still resolve per call from the
    // argument types (Match); only the assembly type lookup and the full method-array scan are cached.
    static readonly Dictionary<(string, string), MethodInfo[]> Named = new();
    static MethodInfo[] Candidates(string typeName, string method)
    {
        lock (Named)
        {
            if (Named.TryGetValue((typeName, method), out var cached)) return cached;
            var type = dx11?.GetType(typeName);
            if (type == null) return null;
            var found = new List<MethodInfo>();
            foreach (var candidate in type.GetMethods(BindingFlags.Public | BindingFlags.Static)) if (candidate.Name == method) found.Add(candidate);
            return Named[(typeName, method)] = found.ToArray();
        }
    }

    public static bool Call(string typeName, string method, params object[] args)
    {
        return Try(typeName, method, args, out _);
    }

    public static bool Try(string typeName, string method, object[] args, out object result)
    {
        result = null;
        if (dx11 == null)
            return false;
        var candidates = Candidates(typeName, method);
        if (candidates == null)
            return false;
        var found = Match(candidates, method, args);
        if (found == null)
            return false;
        result = found.Invoke(null, args ?? Array.Empty<object>());
        return true;
    }

    public static object Get(string typeName, string property)
    {
        if (dx11 == null)
            return null;
        var type = dx11.GetType(typeName);
        var info = type?.GetProperty(property, BindingFlags.Public | BindingFlags.Static);
        return info?.GetValue(null);
    }

    public static void Set(string typeName, string property, object value)
    {
        if (dx11 == null)
            return;
        var type = dx11.GetType(typeName);
        var info = type?.GetProperty(property, BindingFlags.Public | BindingFlags.Static);
        info?.SetValue(null, value);
    }

    public static object CallInstance(object target, string method, params object[] args)
    {
        if (target == null)
            return null;
        var found = Match(target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance), method, args);
        return found?.Invoke(target, args ?? Array.Empty<object>());
    }

    static MethodInfo Match(IEnumerable<MethodInfo> methods, string name, object[] args)
    {
        int count = args?.Length ?? 0;
        MethodInfo fallback = null;
        foreach (var method in methods)
        {
            if (method.Name != name)
                continue;
            var parameters = method.GetParameters();
            if (parameters.Length != count)
                continue;
            bool ok = true;
            for (int i = 0; i < count; i++)
            {
                if (parameters[i].ParameterType.IsByRef)
                    continue;
                if (args[i] == null)
                    continue;
                if (!parameters[i].ParameterType.IsInstanceOfType(args[i]))
                {
                    ok = false;
                    break;
                }
            }

            if (!ok)
                continue;
            if (fallback == null)
                fallback = method;
            else
                return method;
        }

        return fallback;
    }
}
