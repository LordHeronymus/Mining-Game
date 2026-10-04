using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

public static class GpsCodec
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    public static Type ResolveType(string name) => Type.GetType(name ?? "") ?? AppDomain.CurrentDomain.GetAssemblies()
        .Select(assembly => assembly.GetType(name ?? "")).FirstOrDefault(type => type != null);
    public static FieldInfo Field(Type type, string name)
    {
        for (; type != null; type = type.BaseType)
        { var field = type.GetField(name, Flags | BindingFlags.DeclaredOnly); if (field != null) return field; }
        return null;
    }
    public static Type MemberType(Type type, string name) => Field(type, name)?.FieldType ?? type.GetProperty(name, Flags)?.PropertyType;
    public static object Get(object target, string name) => Field(target.GetType(), name) is FieldInfo field
        ? field.GetValue(target) : target.GetType().GetProperty(name, Flags)?.GetValue(target);
    public static void Set(object target, string name, object value)
    {
        if (Field(target.GetType(), name) is FieldInfo field) field.SetValue(target, value);
        else target.GetType().GetProperty(name, Flags)?.SetValue(target, value);
    }
    public static IEnumerable<FieldInfo> SerializableFields(Type type) => type.GetFields(Flags)
        .Where(field => !field.IsStatic && !field.IsInitOnly && !field.IsNotSerialized &&
            (field.IsPublic || field.IsDefined(typeof(SerializeField), true)));
    public static GpsValue Read(string name, Type type, object value, Func<Object, string> key)
    {
        var node = new GpsValue { name = name, type = type.AssemblyQualifiedName };
        if (typeof(Object).IsAssignableFrom(type)) { node.kind = GpsValueKind.Reference; node.text = key(value as Object); }
        else if (type.IsEnum) { node.kind = GpsValueKind.Enum; node.number = Convert.ToInt32(value); }
        else if (type == typeof(bool)) { node.kind = GpsValueKind.Boolean; node.flag = (bool)value; }
        else if (type == typeof(string)) { node.kind = GpsValueKind.Text; node.text = value as string ?? ""; }
        else if (type == typeof(AnimationCurve)) { node.kind = GpsValueKind.Curve; node.curve = GpsCurve.From(value as AnimationCurve); }
        else if (type == typeof(Color)) { node.kind = GpsValueKind.Color; node.color = (Color)value; }
        else if (type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4) || type == typeof(Vector2Int) || type == typeof(Vector3Int))
        {
            node.kind = GpsValueKind.Vector;
            node.vector = type == typeof(Vector2) ? (Vector4)(Vector2)value : type == typeof(Vector3) ? (Vector4)(Vector3)value :
                type == typeof(Vector4) ? (Vector4)value : type == typeof(Vector2Int) ? (Vector4)(Vector2)(Vector2Int)value : (Vector4)(Vector3)(Vector3Int)value;
        }
        else if (type.IsPrimitive || type == typeof(decimal))
        { node.kind = type == typeof(float) || type == typeof(double) || type == typeof(decimal) ? GpsValueKind.Number : GpsValueKind.Integer; node.number = Convert.ToDouble(value); }
        else if (type.IsArray || typeof(IList).IsAssignableFrom(type))
        {
            node.kind = GpsValueKind.Array;
            Type element = type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0];
            if (value is IEnumerable list) { int index = 0; foreach (var item in list) node.children.Add(Read((index++).ToString(), element, item, key)); }
        }
        else
        {
            node.kind = GpsValueKind.Object;
            value ??= Activator.CreateInstance(type);
            foreach (var field in SerializableFields(type)) node.children.Add(Read(field.Name, field.FieldType, field.GetValue(value), key));
        }
        return node;
    }
    public static object Write(GpsValue node, Func<string, Object> resolve)
    {
        Type type = ResolveType(node.type) ?? throw new InvalidOperationException("GPS-Typ fehlt: " + node.type);
        switch (node.kind)
        {
            case GpsValueKind.Reference: return string.IsNullOrEmpty(node.text) ? null : resolve(node.text) ?? throw new InvalidOperationException("GPS-Asset fehlt: " + node.text);
            case GpsValueKind.Number:
            case GpsValueKind.Integer: return Convert.ChangeType(node.number, type);
            case GpsValueKind.Boolean: return node.flag;
            case GpsValueKind.Text: return node.text ?? "";
            case GpsValueKind.Enum: return Enum.ToObject(type, (int)node.number);
            case GpsValueKind.Color: return node.color;
            case GpsValueKind.Curve: return node.curve?.ToCurve() ?? new AnimationCurve();
            case GpsValueKind.Vector:
                if (type == typeof(Vector2)) return (Vector2)node.vector;
                if (type == typeof(Vector3)) return (Vector3)node.vector;
                if (type == typeof(Vector2Int)) return new Vector2Int(Mathf.RoundToInt(node.vector.x), Mathf.RoundToInt(node.vector.y));
                if (type == typeof(Vector3Int)) return new Vector3Int(Mathf.RoundToInt(node.vector.x), Mathf.RoundToInt(node.vector.y), Mathf.RoundToInt(node.vector.z));
                return node.vector;
            case GpsValueKind.Array:
                Type element = type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0];
                if (type.IsArray) { var array = Array.CreateInstance(element, node.children.Count); for (int i = 0; i < array.Length; i++) array.SetValue(Write(node.children[i], resolve), i); return array; }
                var list = (IList)Activator.CreateInstance(type); foreach (var child in node.children) list.Add(Write(child, resolve)); return list;
            default:
                var result = Activator.CreateInstance(type);
                foreach (var child in node.children) if (MemberType(type, child.name) != null) Set(result, child.name, Write(child, resolve));
                return result;
        }
    }
    public static bool Validate(GpsValue node, out string error, int depth = 0)
    {
        error = null;
        if (node == null || depth > 12 || ResolveType(node.type) == null) { error = "Ungültiges GPS-Feld."; return false; }
        if (double.IsNaN(node.number) || double.IsInfinity(node.number)) { error = "Ungültiger Zahlenwert."; return false; }
        if (node.kind == GpsValueKind.Enum && !Enum.IsDefined(ResolveType(node.type), (int)node.number)) { error = "Ungültige Auswahl."; return false; }
        for (int i = 0; i < 4; i++) if (!Finite(node.vector[i]) || !Finite(node.color[i])) { error = "Ungültiger Farb- oder Vektorwert."; return false; }
        if (node.children == null || node.children.Count > 10000) { error = "Ungültige Liste."; return false; }
        if (node.kind == GpsValueKind.Curve && (node.curve == null || node.curve.keys == null || node.curve.keys.Count > 1000 ||
            node.curve.keys.Any(key => !Finite(key.time) || !Finite(key.value) || float.IsNaN(key.inTangent) || float.IsNaN(key.outTangent) ||
                !Finite(key.inWeight) || !Finite(key.outWeight) || key.weightedMode < 0 || key.weightedMode > 3)))
        { error = "Ungültige Kurve."; return false; }
        foreach (var child in node.children) if (!Validate(child, out error, depth + 1)) return false;
        return true;
    }
    public static bool ValidateShape(GpsValue node,Type expected,out string error)
    {
        error=null;
        if(expected==null || node?.type!=expected.AssemblyQualifiedName) { error="GPS-Feldtyp stimmt nicht überein."; return false; }
        var expectedKind=typeof(Object).IsAssignableFrom(expected) ? GpsValueKind.Reference : expected.IsEnum ? GpsValueKind.Enum :
            expected==typeof(bool) ? GpsValueKind.Boolean : expected==typeof(string) ? GpsValueKind.Text : expected==typeof(AnimationCurve) ? GpsValueKind.Curve :
            expected==typeof(Color) ? GpsValueKind.Color : expected==typeof(Vector2) || expected==typeof(Vector3) || expected==typeof(Vector4) || expected==typeof(Vector2Int) || expected==typeof(Vector3Int) ? GpsValueKind.Vector :
            expected.IsPrimitive || expected==typeof(decimal) ? (expected==typeof(float) || expected==typeof(double) || expected==typeof(decimal) ? GpsValueKind.Number : GpsValueKind.Integer) :
            expected.IsArray || typeof(IList).IsAssignableFrom(expected) ? GpsValueKind.Array : GpsValueKind.Object;
        if(node.kind!=expectedKind) { error="Ungültige GPS-Feldstruktur."; return false; }
        if(node.kind==GpsValueKind.Object)
        {
            var fields=SerializableFields(expected).ToArray();
            if(node.children.Count!=fields.Length || node.children.GroupBy(child=>child.name).Any(group=>group.Count()!=1)) { error="Unvollständige GPS-Feldstruktur."; return false; }
            foreach(var field in fields) if(!ValidateShape(node.children.Find(child=>child.name==field.Name),field.FieldType,out error)) return false;
        }
        else if(node.kind==GpsValueKind.Array)
        {
            Type element=expected.IsArray ? expected.GetElementType() : expected.GetGenericArguments()[0];
            foreach(var child in node.children) if(!ValidateShape(child,element,out error)) return false;
        }
        else if(node.children.Count!=0) { error="Ungültige GPS-Feldstruktur."; return false; }
        return true;
    }
    public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
