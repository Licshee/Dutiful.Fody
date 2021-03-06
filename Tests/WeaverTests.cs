﻿using System;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.CSharp.RuntimeBinder;
using Mono.Cecil;
using NUnit.Framework;

[TestFixture]
public class WeaverTests
{
    Assembly assembly;
    string newAssemblyPath;
    string assemblyPath;
    Type targetClass;
    Type targetStruct;

    [TestFixtureSetUp]
    public void Setup()
    {
        var projectPath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, @"..\..\..\AssemblyToProcess\AssemblyToProcess.csproj"));
        assemblyPath = Path.Combine(Path.GetDirectoryName(projectPath), @"bin\Debug\AssemblyToProcess.dll");
#if (!DEBUG)
        assemblyPath = assemblyPath.Replace("Debug", "Release");
#endif

        newAssemblyPath = assemblyPath.Replace(".dll", "2.dll");
        File.Copy(assemblyPath, newAssemblyPath, true);

        var config = XElement.Parse(@"<Dutiful NameFormat=""Careless"" SyncNameFormat=""Sync"" TargetTypeLevel=""Struct""/>");
        config.SetAttributeValue("StopWordForReturnType", @".+\.UIntPtr");
        config.Add(new XElement("StopWordForReturnType") { Value = @"
            @System.IntPtr
            .+\.StringBuilder
        " });
        config.SetAttributeValue("StopWordForSignature", "@System.Object TargetStruct::DontWrapThis(System.Object)");
        config.SetAttributeValue("StopWordForMethodName", ".+NoDutiful");
        config.Add(new XElement("StopWordForMethodName") { Value = @"
            @NoDutiful
            No_+.+
        " });
        var moduleDefinition = ModuleDefinition.ReadModule(newAssemblyPath);
        var weavingTask = new ModuleWeaver
        {
            Config = config,
            ModuleDefinition = moduleDefinition,
        };

        weavingTask.Execute();
        moduleDefinition.Write(newAssemblyPath);

        assembly = Assembly.LoadFile(newAssemblyPath);
        targetClass = assembly.GetType("TargetClass");
        targetStruct = assembly.GetType("TargetStruct");
    }

    [Test]
    public void TestGeneric()
    {
        dynamic instance = Activator.CreateInstance(targetClass);

        Type type;

        instance.TypeOf<object>(out type);
        Assert.AreEqual(typeof(object), type);

        instance.TypeOfCareless<object>(out type);
        Assert.AreEqual(typeof(object), type);
    }

    public class StaticMembersDynamicWrapper : DynamicObject
    {
        private Type _type;
        public StaticMembersDynamicWrapper(Type type) { _type = type; }

        // Handle static properties
        public override bool TryGetMember(GetMemberBinder binder, out object result)
        {
            PropertyInfo prop = _type.GetProperty(binder.Name, BindingFlags.FlattenHierarchy | BindingFlags.Static | BindingFlags.Public);
            if (prop == null)
            {
                result = null;
                return false;
            }

            result = prop.GetValue(null, null);
            return true;
        }

        // Handle static methods
        public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result)
        {
            MethodInfo method = _type.GetMethod(binder.Name, BindingFlags.FlattenHierarchy | BindingFlags.Static | BindingFlags.Public);
            if (method == null)
            {
                result = null;
                return false;
            }

            result = method.Invoke(null, args);
            return true;
        }
    }

    [Test]
    public void TestSync()
    {
        dynamic type = new StaticMembersDynamicWrapper(targetClass);
        type.GetTaskStaticSync();

        dynamic instance = Activator.CreateInstance(targetClass);

        instance.GetTaskSync();
        instance.GetTaskSync<object>();

        Assert.AreSame(instance, instance.GetTaskSyncCareless());
        Assert.AreSame(instance, instance.GetTaskSyncCareless<object>());
    }

    [Test]
    public void ValidateJustMe()
    {
        dynamic instance = Activator.CreateInstance(targetClass);

        Assert.AreEqual(instance, instance.JustMe());
        Assert.IsInstanceOf(targetClass, instance.GetDerived());

        Assert.Throws<RuntimeBinderException>(() => instance.JustMeCareless());
        Assert.Throws<RuntimeBinderException>(() => instance.GetDerivedCareless());
    }

    [Test]
    public void ValidateStructStopWords()
    {
        dynamic instance = Activator.CreateInstance(targetStruct);

        instance.NOOP();
        Assert.Throws<NotImplementedException>(() => instance.NOOPCareless());

        instance.No();
        Assert.Throws<NotImplementedException>(() => instance.NoCareless(instance));
        Assert.AreEqual(instance, instance.NoCareless());

        var obj = new object();
        Assert.AreEqual(obj, instance.DontWrapThis(obj));
        instance.No_Thanks();
        instance.NoDutiful();
        instance.NoopNoDutiful();

        Assert.Throws<RuntimeBinderException>(() => instance.DontWrapThisCareless(obj));
        Assert.Throws<RuntimeBinderException>(() => instance.No_ThanksCareless());
        Assert.Throws<RuntimeBinderException>(() => instance.NoDutifulCareless());
        Assert.Throws<RuntimeBinderException>(() => instance.NoopNoDutifulCareless());

        Assert.Throws<RuntimeBinderException>(() => instance.EqualsCareless(null));
        Assert.Throws<RuntimeBinderException>(() => instance.ToStringCareless());
    }

    [Test]
    public void ValidateClassStopWords()
    {
        dynamic instance = Activator.CreateInstance(targetClass);

        Assert.AreEqual(instance, instance.SpawnStopwatchCareless());

        Assert.IsInstanceOf<IntPtr>(instance.GetIntPtr());
        Assert.IsInstanceOf<UIntPtr>(instance.GetUIntPtr());
        Assert.Null(instance.GetStringBuilder());

        Assert.Throws<RuntimeBinderException>(() => instance.GetIntPtrCareless());
        Assert.Throws<RuntimeBinderException>(() => instance.GetUIntPtrCareless());
        Assert.Throws<RuntimeBinderException>(() => instance.GetStringBuilderCareless());
    }

    [Test]
    public void ValidateClassArguments()
    {
        dynamic instance = Activator.CreateInstance(targetClass);

        const string format = "{2}:\t{0}{3}{1}@{4}";
        var expected = $"233:\t{{{targetClass.FullName}}}@{IntPtr.Zero}";

        {
            string output;
            var result = instance.TryMakeString(format, 233, IntPtr.Zero, out output);
            Assert.True(result);
            Assert.AreEqual(expected, output);
        }
        {
            string output;
            var result = instance.TryMakeStringCareless(format, 233, IntPtr.Zero, out output);
            Assert.AreEqual(instance, result);
            Assert.AreEqual(expected, output);
        }
    }

    [Test]
    public void ValidateStructArguments()
    {
        dynamic instance = Activator.CreateInstance(targetStruct);

        const string format = "{2}:\t{0}{3}{1}@{4}";
        var expected = $"233:\t{{{targetStruct.FullName}}}@{IntPtr.Zero}";

        {
            string output;
            var result = instance.TryMakeString(format, 233, IntPtr.Zero, out output);
            Assert.True(result);
            Assert.AreEqual(expected, output);
        }
        {
            string output;
            var result = instance.TryMakeStringCareless(format, 233, IntPtr.Zero, out output);
            Assert.AreEqual(instance, result);
            Assert.AreEqual(expected, output);
        }
    }

#if (DEBUG)
    [Test]
    public void PeVerify()
    {
        Verifier.Verify(assemblyPath, newAssemblyPath);
    }
#endif
}