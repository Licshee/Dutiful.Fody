﻿using System;
using System.Diagnostics;

public class TargetClass
{
    public void NOOP() { }

    public TargetClass JustMe() => this;

    [Obsolete]
    public virtual Stopwatch SpawnStopwatch()
        => Stopwatch.StartNew();

    public bool TryMakeString(string format, decimal arg2, IntPtr arg3, out string result)
    {
        result = string.Format(format, "{", "}", arg2, this, arg3);
        Debug.WriteLine(result);
        return true;
    }

    protected Stopwatch FamilySpawnStopwatch()
        => SpawnStopwatch();
    protected internal Stopwatch FamilyOrAssemblySpawnStopwatch()
        => SpawnStopwatch();

    private void PrivateNOOP() { }
    internal void AssemblyNOOP() { }
}

