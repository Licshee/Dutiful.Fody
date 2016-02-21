﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

using static Mono.Cecil.MethodAttributes;
using System.IO;
using System.Text;

public class ModuleWeaver
{
    // Will log an informational message to MSBuild
    public Action<string> LogInfo { get; set; }

    // An instance of Mono.Cecil.ModuleDefinition for processing
    public XElement Config { get; set; }
    public ModuleDefinition ModuleDefinition { get; set; }
    private string methodNameFormat;
    private Regex StopWordForDeclaringType;
    private Regex StopWordForMethodName;
    private Regex StopWordForReturnType;

    TypeSystem typeSystem;

    // Init logging delegates to make testing easier
    public ModuleWeaver()
    {
        LogInfo = m => { };
    }

    private static void LoadArguments(ILProcessor processor, ushort count)
    {
        processor.Emit(OpCodes.Ldarg_0);
        if (count < 1) return;

        for (ushort i = 1; i < count; i++)
            processor.Emit(OpCodes.Ldarg, i);

        processor.Emit(OpCodes.Ldarg, count);
    }
    private MethodDefinition MakeDutifulVariant(MethodDefinition method)
    {
        var name = string.Format(methodNameFormat, method.Name);
        LogInfo($"Weaving method \"{name}\"...");

        var attributes = method.Attributes & (MemberAccessMask | HideBySig | Static);
        var dutiful = new MethodDefinition(name, attributes, method.DeclaringType);

        var customAttributes = dutiful.CustomAttributes;
        foreach (var attr in method.CustomAttributes)
            customAttributes.Add(attr);

        var parameters = dutiful.Parameters;
        foreach (var param in method.Parameters)
            parameters.Add(param);

        var processor = dutiful.Body.GetILProcessor();
        LoadArguments(processor, (ushort)parameters.Count);

        processor.Emit(OpCodes.Callvirt, method);
        if (method.ReturnType != typeSystem.Void)
            processor.Emit(OpCodes.Pop);

        processor.Emit(OpCodes.Ldarg_0);
        processor.Emit(OpCodes.Ret);
        return dutiful;
    }

    private void AddDutifulMethods(TypeDefinition type)
    {
        LogInfo($"Processing type \"{type.FullName}\"...");

        foreach (var method in type.Methods.Where(m => (m.IsPublic || m.IsFamily)
            && !(m.IsStatic || m.IsConstructor) && m.SemanticsAttributes == MethodSemanticsAttributes.None).ToArray())
        {
            var returnType = method.ReturnType;
            if (returnType == type)
                continue;

            if (StopWordForDeclaringType.IsMatch(method.GetOriginalBaseMethod().DeclaringType.FullName))
                continue;

            if (StopWordForMethodName != null)
            {
                if (StopWordForMethodName.IsMatch(method.Name))
                    continue;
            }

            if (StopWordForReturnType != null)
            {
                if (StopWordForReturnType.IsMatch(returnType.FullName))
                    continue;
            }

            type.Methods.Add(MakeDutifulVariant(method));
        }
    }

    public void Execute()
    {
        typeSystem = ModuleDefinition.TypeSystem;

        SetupFromConfig();

        foreach (var type in ModuleDefinition.Types.Where(t => t.IsPublic && !(t.IsEnum || t.IsInterface)))
            AddDutifulMethods(type);
    }

    private void SetupStopWordForDeclaringType()
    {
        string tmp;
        IEnumerable<string> lines = null;

        tmp = Config?.Element("StopWordForDeclaringType")?.Value;
        if (!tmp.IsNullOrWhiteSpace())
        {
            using (var sr = new StringReader(tmp))
                lines = sr.NonWhiteSpaceLines().ToArray();
        }

        tmp = Config?.Attribute("StopWordForDeclaringType")?.Value;
        if (tmp == null)
            tmp = typeSystem.Object.FullName;

        if (lines == null)
            lines = new[] { tmp };
        else
            lines = new[] { tmp }.Concat(lines);

        if (lines == null) return;

        lines = lines.Select(s =>
        {
            if (s[0] == '@')
                return Regex.Escape(s.Substring(1).Trim());
            new Regex(s);
            return s;
        }).Where(s => s != "").Distinct();

        var sb = new StringBuilder();
        sb.Append("^(?:");
        foreach (var line in lines)
        {
            sb.Append(line);
            sb.Append('|');
        }
        sb.Length--;
        sb.Append(")$");

        StopWordForDeclaringType = new Regex(sb.ToString());
    }
    private void SetupStopWordForMethodName()
    {
        if (Config == null) return;

        string tmp;
        IEnumerable<string> lines = null;

        tmp = Config.Element("StopWordForMethodName")?.Value;
        if (!tmp.IsNullOrWhiteSpace())
        {
            using (var sr = new StringReader(tmp))
                lines = sr.NonWhiteSpaceLines()
                    .Select(s => s.Trim()).Where(s => s != "").ToArray();
        }

        tmp = Config.Attribute("StopWordForMethodName")?.Value;
        if (!tmp.IsNullOrWhiteSpace())
        {
            if (lines == null)
                lines = new[] { tmp };
            else
                lines = new[] { tmp }.Concat(lines);
        }

        if (lines == null) return;

        lines = lines.Select(s =>
        {
            if (s[0] == '@')
                return Regex.Escape(s.Substring(1).TrimStart());
            new Regex(s);
            return s;
        }).Where(s => s != "").Distinct();

        var sb = new StringBuilder();
        sb.Append("^(?:");
        foreach (var line in lines)
        {
            sb.Append(line);
            sb.Append('|');
        }
        sb.Length--;
        sb.Append(")$");

        StopWordForMethodName = new Regex(sb.ToString());
    }
    private void SetupStopWordForReturnType()
    {
        if (Config == null) return;

        string tmp;
        IEnumerable<string> lines = null;

        tmp = Config.Element("StopWordForReturnType")?.Value;
        if (!tmp.IsNullOrWhiteSpace())
        {
            using (var sr = new StringReader(tmp))
                lines = sr.NonWhiteSpaceLines()
                    .Select(s => s.Trim()).Where(s => s != "").ToArray();
        }

        tmp = Config.Attribute("StopWordForReturnType")?.Value;
        if (!tmp.IsNullOrWhiteSpace())
        {
            if (lines == null)
                lines = new[] { tmp };
            else
                lines = new[] { tmp }.Concat(lines);
        }

        if (lines == null) return;

        lines = lines.Select(s =>
        {
            if (s[0] == '@')
                return Regex.Escape(s.Substring(1).TrimStart());
            new Regex(s);
            return s;
        }).Where(s => s != "").Distinct();

        var sb = new StringBuilder();
        sb.Append("^(?:");
        foreach (var line in lines)
        {
            sb.Append(line);
            sb.Append('|');
        }
        sb.Length--;
        sb.Append(")$");

        StopWordForReturnType = new Regex(sb.ToString());
    }
    private void SetupFromConfig()
    {
        const string nameAttr = "NameFormat";
        methodNameFormat = (Config?.Attribute(nameAttr)?.Value) ?? "Dutiful{0}";
        if (!methodNameFormat.Contains("{0}"))
            throw new ArgumentException(nameAttr);

        SetupStopWordForDeclaringType();
        SetupStopWordForMethodName();
        SetupStopWordForReturnType();
    }
}
